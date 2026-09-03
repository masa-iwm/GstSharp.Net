namespace Gst.Allocators;

/// <summary>
/// The entry point of the <c>GstAllocators</c> binding: it initialises
/// GstSharp.Net and makes sure that the types of this assembly are in the type
/// registry.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers its types from a module initialiser, and
/// the runtime runs a module initialiser before the first <em>call</em> into
/// that assembly, not before the first type of it is merely named. What this
/// module hands to the registry is the five allocator classes —
/// <see cref="FdAllocator"/> and the three that derive from it,
/// <see cref="DmaBufAllocator"/>, <see cref="ShmAllocator"/> and
/// <see cref="UdmabufAllocator"/>, plus <see cref="DRMDumbAllocator"/>. Nothing
/// else of the module carries an entry: the memory an allocator hands out is a
/// plain <see cref="Gst.Memory"/> whatever allocated it, so the nine functions
/// that read a descriptor, a DRM handle or a physical address out of one are
/// static calls on <see cref="AllocatorsGlobal"/> rather than members of a
/// memory type of their own. <see cref="IPhysMemoryAllocator"/> carries no
/// entry either: it is a marker the library declares for allocators that live
/// outside GStreamer, none of the five implements it, and what asks the
/// question for any memory is
/// <see cref="AllocatorsGlobal.IsPhysMemory"/>.
/// </para>
/// <para>
/// An application that only names one of the five and leaves every call to
/// another binding assembly therefore never executes a line of this one: the
/// registry has no entry to build their wrappers from, and what arrives is the
/// closest type it does know — the failure described under
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry">The GType registry</see>.
/// That is the shape to watch for here, because an allocator an element hands
/// out — through an allocation query, say — is exactly such a handle: without
/// this module it wraps as a bare <see cref="Gst.Allocator"/> and the cast to
/// <see cref="DmaBufAllocator"/> is <see langword="null"/>.
/// </para>
/// <para>
/// Calling <see cref="Initialize"/> instead of <c>GstSharp.Initialize</c> is a
/// call into this assembly and closes that hole. The registry is rebuilt on the
/// next lookup after a module is added, so the order of the two does not
/// matter; what matters is that the module initialiser runs at all.
/// </para>
/// <para>
/// <c>GstSharp.Initialize</c> also sweeps the assemblies that are loaded and
/// runs their module initialisers, and it keeps doing so for assemblies that
/// are loaded later, so an application that never names this class is covered
/// as well. Calling this one is the deterministic way to say it. The sweep
/// reaches assemblies, not wrappers: a wrapper the registry built before this
/// assembly was loaded keeps the type it was created with.
/// </para>
/// <para>
/// <b>What the module promises on which operating system.</b> Every entry point
/// of <c>libgstallocators</c> is exported on every platform the binding
/// supports, so nothing here is marked as unavailable and no call fails to
/// bind. What differs is the answer: the library is built without the system
/// calls the allocators need, and the calls that would use them return
/// <see langword="null"/>. The nullable returns of the generated surface are
/// where that is stated, and this is what they mean.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// On Windows <see cref="FdAllocator.New"/> and
/// <see cref="DmaBufAllocator.New"/> succeed — the allocator objects are
/// real — but every allocation through them answers <see langword="null"/>
/// without a message, because the library has no <c>mmap</c>. The file
/// descriptor that was passed in is untouched and stays the caller's to close.
/// </description>
/// </item>
/// <item>
/// <description>
/// When an allocation does succeed, ownership of the file descriptor moves to
/// the memory: releasing the last reference of the <see cref="Gst.Memory"/>
/// closes it. Pass <see cref="FdMemoryFlags.DontClose"/> to keep it, which is
/// what a descriptor the caller goes on using — one owned by a
/// <see cref="System.Runtime.InteropServices.SafeHandle"/>, say — needs.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="ShmAllocator.Get"/> answers <see langword="null"/> until the
/// singleton has been registered, and non-null afterwards on every platform
/// including Windows: that registration is unconditional.
/// <see cref="ShmAllocator.InitOnce"/> is the way to ask for it, and it is not
/// the only caller — the class initialisation of <c>unixfdsink</c> registers it
/// as the plugin is loaded, and so does the construction of a Wayland display —
/// so a process that touched either finds it already there without having
/// asked. Allocating through the allocator it returns is the platform
/// dependent part —
/// <c>memfd_create</c> on Linux, <c>shm_open</c> on macOS, and
/// <see langword="null"/> on Windows. <c>InitOnce</c> is process global and
/// cannot be undone.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="UdmabufAllocator.Get"/> registers the allocator itself on the
/// first call rather than asking for a separate <c>InitOnce</c>, and answers
/// <see langword="null"/> when the kernel has no <c>/dev/udmabuf</c> — which is
/// every platform other than Linux and most Linux machines. The type arrived in
/// GStreamer 1.28, so an older installation does not export its
/// <c>get_type</c>; the registry leaves it out of the table instead of failing
/// to freeze, and a call into one of its members throws
/// <see cref="System.EntryPointNotFoundException"/> there.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="DRMDumbAllocator.NewWithDevicePath"/> and
/// <see cref="DRMDumbAllocator.NewWithFd"/> answer <see langword="null"/>
/// unless the library was built with libdrm and the path or descriptor names a
/// DRM device that supports dumb allocation, so a machine without a GPU answers
/// <see langword="null"/> on Linux too.
/// <see cref="DRMDumbAllocator.Alloc"/> answers <see langword="null"/> whenever
/// the ioctl behind it fails, and leaves its <c>outPitch</c> at zero when the
/// driver reports no pitch, which is an answer rather than a failure.
/// </description>
/// </item>
/// </list>
/// <para>
/// <b>The accessors of <see cref="AllocatorsGlobal"/> that read a descriptor
/// out of a memory disagree about what they do with a memory of the wrong
/// kind.</b> <see cref="AllocatorsGlobal.FdMemoryGetFd"/> and
/// <see cref="AllocatorsGlobal.DmabufMemoryGetFd"/> answer <c>-1</c>, and
/// <see cref="AllocatorsGlobal.PhysMemoryGetPhysAddr"/> answers <c>0</c>; all
/// three raise a <c>g_critical</c> on the way, which is a message on the
/// console and nothing more.
/// <see cref="AllocatorsGlobal.DrmDumbMemoryGetHandle"/> answers <c>0</c>
/// silently. <see cref="AllocatorsGlobal.DrmDumbMemoryExportDmabuf"/>
/// checks nothing at all and reads the fields of whatever it is handed, so ask
/// <see cref="AllocatorsGlobal.IsDrmDumbMemory"/> before calling it. The four
/// <c>Is…Memory</c> predicates are the only members here that are quiet on a
/// memory of any kind: they answer <see langword="false"/> for one that was not
/// allocated by the allocator in question, which is what makes them the
/// question to ask before reaching for a descriptor or an address.
/// </para>
/// <para>
/// <b>Writing an allocator in C# is not supported yet.</b> The classes of this
/// module wrap the allocators the library implements; deriving from
/// <see cref="Gst.Allocator"/> to have GStreamer call a managed allocation
/// method needs the virtual method plumbing that
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>
/// describes for elements, and no allocator virtual method is bound.
/// </para>
/// </remarks>
public static class GstAllocators
{
    /// <summary>
    /// Loads the native libraries, initialises GStreamer and puts the types of
    /// this assembly into the type registry.
    /// </summary>
    /// <param name="options">
    /// Where the native libraries are and how GStreamer should be initialised,
    /// or <see langword="null"/> for the defaults.
    /// </param>
    /// <remarks>
    /// This forwards to <c>GstSharp.Initialize</c> and is idempotent in the
    /// same way: after the first call, a call with <see langword="null"/>
    /// options does nothing but register this module, and options that
    /// contradict the first call are refused.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The options conflict with the ones of the first call.
    /// </exception>
    /// <exception cref="Gst.Interop.GstNativeLoadException">
    /// The native libraries could not be found.
    /// </exception>
    /// <exception cref="Gst.GLib.GException">GStreamer refused to initialise.</exception>
    public static void Initialize(GstSharpOptions? options = null) =>
        global::GstSharp.Initialize(options);
}
