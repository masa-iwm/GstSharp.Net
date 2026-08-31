using Gst;
using Gst.Allocators;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstAllocators</c> binding against the library that is installed: the
/// allocators are built, a real file descriptor travels into a
/// <see cref="Gst.Memory"/> and back out again, and the calls whose answer is
/// platform dependent are asserted per platform rather than skipped.
/// </summary>
/// <remarks>
/// <para>
/// The module exports every one of its entry points on every platform, so
/// nothing here is gated on the operating system: what changes across platforms
/// is the answer, not the availability. A test that only ran on Linux would
/// leave the Windows contract — an allocator that is built successfully and then
/// allocates <see langword="null"/> — unmeasured, and that contract is the one
/// an application is most likely to be surprised by.
/// </para>
/// <para>
/// Nothing here allocates through a DRM device or a udmabuf, because no leg of
/// the matrix has either. Those two are measured for the shape of their failure,
/// which is what the documentation of the module promises: a
/// <see langword="null"/> rather than a throw.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AllocatorsTests
{
    /// <summary>The size of the backing file and of every allocation below.</summary>
    private const int MemorySize = 4096;

    /// <summary>
    /// The fd allocator is a plain GObject and is built on every platform, the
    /// ones without <c>mmap</c> included. The wrapper that arrives is the exact
    /// type rather than a bare <see cref="Gst.Allocator"/>, which is what says
    /// that the module handed its types to the registry.
    /// </summary>
    [Fact]
    public void TheFdAllocatorIsBuiltOnEveryPlatform()
    {
        using Gst.Allocator allocator = FdAllocator.New();

        Assert.NotNull(allocator);
        Assert.IsType<FdAllocator>(allocator);
    }

    /// <summary>
    /// The dmabuf allocator derives from the fd allocator and is built the same
    /// way, and its wrapper is the derived one rather than the base.
    /// </summary>
    [Fact]
    public void TheDmaBufAllocatorIsBuiltOnEveryPlatformAndWrapsAsItself()
    {
        using Gst.Allocator allocator = DmaBufAllocator.New();

        Assert.NotNull(allocator);
        Assert.IsType<DmaBufAllocator>(allocator);
    }

    /// <summary>
    /// A descriptor of a real file becomes a <see cref="Gst.Memory"/> that maps
    /// — everywhere the library has <c>mmap</c>, and <see langword="null"/> on
    /// Windows, where it does not.
    /// </summary>
    /// <remarks>
    /// <see cref="FdMemoryFlags.DontClose"/> is what keeps the descriptor with
    /// the <see cref="SafeFileHandle"/> that owns it: without it the memory
    /// closes the descriptor when it is released and the handle closes it a
    /// second time. The file is grown to the allocation size first, because a
    /// mapping that reaches past the end of its file faults on the access rather
    /// than on the map.
    /// </remarks>
    [Fact]
    public void AFileDescriptorBecomesMappableMemoryWhereTheLibraryHasMmap()
    {
        string path = Path.Combine(Path.GetTempPath(), "gstsharp-fd-" + Guid.NewGuid().ToString("N") + ".bin");

        try
        {
            File.WriteAllBytes(path, new byte[MemorySize]);

            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);
            int fd = (int)handle.DangerousGetHandle();

            using Gst.Allocator allocator = FdAllocator.New();
            using Gst.Memory? memory = FdAllocator.Alloc(allocator, fd, MemorySize, FdMemoryFlags.DontClose);

            if (OperatingSystem.IsWindows())
            {
                // No mmap: the allocation answers null and never touched the
                // descriptor, so the handle above is still its only owner.
                Assert.Null(memory);
                return;
            }

            Assert.NotNull(memory);
            Assert.True(AllocatorsGlobal.IsFdMemory(memory));
            Assert.Equal(fd, AllocatorsGlobal.FdMemoryGetFd(memory));
            Assert.Equal((nuint)MemorySize, memory.Size);

            Assert.True(memory.Map(out MapInfo info, MapFlags.Read));
            try
            {
                Assert.Equal((nuint)MemorySize, info.Size);
            }
            finally
            {
                memory.Unmap(info);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The whole life of the shared memory allocator, in one test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gst_shm_allocator_init_once</c> registers a process global singleton
    /// and there is no way to unregister it, so the state before the first call
    /// can be observed exactly once in a process. xunit orders nothing across
    /// facts, which means the observation and the call that destroys it have to
    /// sit in the same one, and <b>no other test of this assembly may name
    /// <see cref="ShmAllocator"/> at all</b>.
    /// </para>
    /// <para>
    /// <c>InitOnce</c> is not the only caller of it in the process, which is why
    /// the rule above reaches past this binding: <c>gst_unix_fd_sink_class_init</c>
    /// registers the singleton, and <c>gst_element_register</c> class-refs every
    /// element it registers, so loading the <c>unixfd</c> plugin in this process
    /// would register it too; constructing a Wayland display does the same. No
    /// test of this assembly instantiates either, and the registry scan that
    /// would load the plugins runs in <c>gst-plugin-scanner</c> rather than here,
    /// so the null above is the state of a process that has not asked. <b>A test
    /// that instantiates <c>unixfdsink</c> or <c>waylandsink</c> — or that turns
    /// off the scanner with <c>GST_REGISTRY_FORK=no</c> — would take that state
    /// away.</b>
    /// </para>
    /// <para>
    /// The registration itself is unconditional, so it succeeds on Windows too;
    /// what Windows does not have is the <c>memfd_create</c> or <c>shm_open</c>
    /// that the allocation needs, and that is where the answer diverges.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheSharedMemoryAllocatorAppearsWithInitOnceAndAllocatesWhereItCan()
    {
        Assert.Null(ShmAllocator.Get());

        ShmAllocator.InitOnce();

        Gst.Allocator? allocator = ShmAllocator.Get();
        Assert.NotNull(allocator);
        Assert.IsType<ShmAllocator>(allocator);

        // Calling it again is documented to be a no-op, so the singleton — and
        // therefore the interned wrapper of it — is the same object.
        ShmAllocator.InitOnce();
        Assert.Same(allocator, ShmAllocator.Get());

        using Gst.Memory? memory = allocator.Alloc(MemorySize, null);

        if (OperatingSystem.IsWindows())
        {
            Assert.Null(memory);
            return;
        }

        Assert.NotNull(memory);
        Assert.True(AllocatorsGlobal.IsFdMemory(memory));
        Assert.Equal((nuint)MemorySize, memory.Size);
    }

    /// <summary>
    /// The udmabuf allocator arrived in 1.28 and registers itself on the first
    /// <see cref="UdmabufAllocator.Get"/> rather than asking for a separate
    /// initialisation. No leg of the matrix has <c>/dev/udmabuf</c>, so what is
    /// measured is that asking is safe: the answer is a
    /// <see langword="null"/> and not an exception.
    /// </summary>
    /// <remarks>
    /// The whole subject postdates the 1.24 floor of the matrix, so the fact is
    /// skipped rather than branched: a call into
    /// <c>gst_udmabuf_allocator_get</c> on the floor throws
    /// <see cref="EntryPointNotFoundException"/>, which is the documented
    /// behaviour of the binding and not something to assert here.
    /// </remarks>
    [RequiresGStreamerFact(28)]
    public void TheUdmabufAllocatorIsAskedForWithoutThrowing()
    {
        Gst.Allocator? allocator = UdmabufAllocator.Get();

        // A machine with the kernel module loaded answers non-null, and that is
        // just as correct; the point is that neither answer is an exception.
        if (allocator is not null)
        {
            Assert.IsType<UdmabufAllocator>(allocator);
        }
    }

    /// <summary>
    /// Opening a DRM device that is not there answers <see langword="null"/>.
    /// A build without libdrm answers <see langword="null"/> too, which is why
    /// this is not gated on the platform.
    /// </summary>
    [Fact]
    public void ADrmDumbAllocatorOnAMissingDeviceAnswersNull()
    {
        Assert.Null(DRMDumbAllocator.NewWithDevicePath("/dev/dri/card-gstsharp-does-not-exist"));

        // -1 is the unset value of the drm-fd property, so a build with libdrm
        // rejects it in the same place a bad device path is rejected.
        Assert.Null(DRMDumbAllocator.NewWithFd(-1));
    }

    /// <summary>
    /// The four predicates answer <see langword="false"/> for the ordinary
    /// system memory of a buffer, which is the case that has to be safe: they
    /// are what a caller asks before reaching for a descriptor.
    /// </summary>
    [Fact]
    public void TheMemoryPredicatesAnswerFalseForSystemMemory()
    {
        using Gst.Memory memory = SystemMemory();

        Assert.False(AllocatorsGlobal.IsFdMemory(memory));
        Assert.False(AllocatorsGlobal.IsDmabufMemory(memory));
        Assert.False(AllocatorsGlobal.IsDrmDumbMemory(memory));
        Assert.False(AllocatorsGlobal.IsPhysMemory(memory));
    }

    /// <summary>
    /// The physical address of memory that has none is zero rather than a
    /// failure, and the call says so on the console.
    /// </summary>
    /// <remarks>
    /// <c>gst_phys_memory_get_phys_addr</c> opens with a
    /// <c>g_return_val_if_fail (gst_is_phys_memory (mem), 0)</c>, so it belongs
    /// with the two descriptor accessors rather than with the predicates: the
    /// answer for a memory of the wrong kind is a <c>0</c> and a
    /// <c>g_critical</c>, not a quiet <c>0</c>. That is a message on the console
    /// and nothing more here, because nothing in this suite makes criticals
    /// fatal.
    /// </remarks>
    [Fact]
    public void SystemMemoryHasNoPhysicalAddress()
    {
        using Gst.Memory memory = SystemMemory();

        Assert.Equal((nuint)0, AllocatorsGlobal.PhysMemoryGetPhysAddr(memory));
    }

    /// <summary>
    /// The DRM handle of memory that is not DRM dumb is zero and, unlike the two
    /// descriptor accessors, says nothing about it. This is the one accessor a
    /// caller may reach for without asking first.
    /// </summary>
    [Fact]
    public void TheDrmHandleOfSystemMemoryIsZero()
    {
        using Gst.Memory memory = SystemMemory();

        Assert.Equal(0u, AllocatorsGlobal.DrmDumbMemoryGetHandle(memory));
    }

    /// <summary>
    /// The fd memory flags cross as the bit field the header declares. They are
    /// the one argument of this module that carries meaning rather than a
    /// pointer, and <see cref="FdMemoryFlags.DontClose"/> is the one the
    /// ownership contract turns on.
    /// </summary>
    [Fact]
    public void TheFdMemoryFlagsMatchTheHeader()
    {
        Assert.Equal(0, (int)FdMemoryFlags.None);
        Assert.Equal(1, (int)FdMemoryFlags.KeepMapped);
        Assert.Equal(2, (int)FdMemoryFlags.MapPrivate);
        Assert.Equal(4, (int)FdMemoryFlags.DontClose);
    }

    /// <summary>
    /// One block of ordinary system memory, taken out of a buffer the default
    /// allocator filled.
    /// </summary>
    /// <returns>The memory, which the caller disposes.</returns>
    private static Gst.Memory SystemMemory()
    {
        using Gst.Buffer? buffer = Gst.Buffer.NewAllocate(null, MemorySize, null);
        Assert.NotNull(buffer);

        Gst.Memory? memory = buffer.PeekMemory(0);
        Assert.NotNull(memory);
        return memory;
    }
}
