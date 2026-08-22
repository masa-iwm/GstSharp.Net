using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The zero copy members that lend memory the caller owns to GStreamer:
/// <c>gst_buffer_new_wrapped_full</c> and <c>gst_memory_new_wrapped</c>. Both
/// carry a notification whose managed state the binding releases through the
/// invocation itself, and both validate the range before they allocate
/// anything.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class WrappedMemoryTests
{
    /// <summary>How long a notification is given to run after the last reference is gone.</summary>
    private static readonly TimeSpan NotifyTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public unsafe void ANewWrappedBufferMapsTheVeryMemoryItWasGiven()
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
        GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        using ManualResetEventSlim released = new(initialState: false);

        try
        {
            nint data = pin.AddrOfPinnedObject();
            using (Buffer buffer = Buffer.NewWrappedFull(
                MemoryFlags.Readonly,
                data,
                (nuint)payload.Length,
                offset: 2,
                size: 4,
                released.Set))
            {
                using Buffer.MapScope scope = buffer.Map(MapFlags.Read);

                // Zero copy: the mapping is the block that was passed in, at
                // the offset that was asked for, not a copy of it.
                Assert.Equal((nuint)4, scope.Size);
                Assert.Equal(data + 2, (nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(scope.Span)));
                Assert.Equal(new byte[] { 3, 4, 5, 6 }, scope.Span.ToArray());
                Assert.False(released.IsSet);
            }

            Assert.True(released.Wait(NotifyTimeout), "the notification never ran");
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void NewWrappedMemoryRunsItsNotificationWhenTheLastReferenceIsGone()
    {
        byte[] payload = [9, 9, 9, 9];
        GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
        using ManualResetEventSlim released = new(initialState: false);

        try
        {
            Memory? memory = Memory.NewWrapped(
                MemoryFlags.Readonly,
                pin.AddrOfPinnedObject(),
                (nuint)payload.Length,
                offset: 0,
                size: (nuint)payload.Length,
                released.Set);

            Assert.NotNull(memory);
            Assert.False(released.IsSet);
            memory.Dispose();

            Assert.True(released.Wait(NotifyTimeout), "the notification never ran");
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// The ranges the C functions answer with a critical warning and a null
    /// pointer: a null block, a range that runs past the end of the block, and
    /// an offset past the end of it. The parameters are widened, because xunit
    /// cannot build an <c>nint</c> out of an <c>[InlineData]</c> literal and
    /// drops such a theory without discovering it.
    /// </summary>
    /// <param name="data">The address of the block.</param>
    /// <param name="maxsize">How many bytes the block holds.</param>
    /// <param name="offset">Where the valid data starts.</param>
    /// <param name="size">How many valid bytes there are.</param>
    [Theory]
    [InlineData(0L, 16UL, 0UL, 4UL)]
    [InlineData(1L, 16UL, 8UL, 16UL)]
    [InlineData(1L, 16UL, 17UL, 0UL)]
    public void ABadRangeIsRefusedBeforeAnythingIsAllocated(
        long data,
        ulong maxsize,
        ulong offset,
        ulong size)
    {
        nint block = (nint)data;
        nuint max = (nuint)maxsize;
        nuint start = (nuint)offset;
        nuint length = (nuint)size;

        int notified = 0;
        Action notify = () => Interlocked.Increment(ref notified);

        Assert.Throws<ArgumentException>(
            () => Buffer.NewWrappedFull(MemoryFlags.Readonly, block, max, start, length, notify));
        Assert.Throws<ArgumentException>(
            () => Memory.NewWrapped(MemoryFlags.Readonly, block, max, start, length, notify));

        // The guard runs before the handle is allocated, so nothing was
        // handed over and nothing can run the notification.
        Assert.Equal(0, Volatile.Read(ref notified));
    }

    [Fact]
    public void AWrappedBufferWithoutANotificationIsAllowed()
    {
        byte[] payload = [7, 7];
        GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);

        try
        {
            using Buffer buffer = Buffer.NewWrappedFull(
                MemoryFlags.Readonly,
                pin.AddrOfPinnedObject(),
                (nuint)payload.Length,
                offset: 0,
                size: (nuint)payload.Length,
                notify: null);

            using Buffer.MapScope scope = buffer.Map(MapFlags.Read);
            Assert.Equal(payload, scope.Span.ToArray());
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void TheStateOfANotificationIsReleasedByTheInvocation()
    {
        byte[] payload = [4, 2];
        GCHandle pin = GCHandle.Alloc(payload, GCHandleType.Pinned);

        try
        {
            WeakReference weak = Wrap(pin.AddrOfPinnedObject(), (nuint)payload.Length);

            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < NotifyTimeout)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (!weak.IsAlive)
                {
                    return;
                }

                Thread.Sleep(20);
            }

            Assert.Fail("the notification state was still reachable, so its handle was never freed");
        }
        finally
        {
            pin.Free();
        }
    }

    /// <summary>
    /// Creates and releases a wrapped buffer from a frame of its own, so that
    /// no local of the test keeps the notification alive.
    /// </summary>
    /// <param name="data">The block to wrap.</param>
    /// <param name="length">Its length.</param>
    /// <returns>A weak reference to the notification.</returns>
    private static WeakReference Wrap(nint data, nuint length)
    {
        int ran = 0;

        // The delegate has to capture something: a lambda that captures nothing
        // is cached in a static field by the compiler and is never collectable.
        Action notify = () => Interlocked.Increment(ref ran);
        WeakReference weak = new(notify);

        using (Buffer buffer = Buffer.NewWrappedFull(
            MemoryFlags.Readonly,
            data,
            length,
            offset: 0,
            size: length,
            notify))
        {
            Assert.Equal(0, Volatile.Read(ref ran));
        }

        Assert.Equal(1, Volatile.Read(ref ran));
        return weak;
    }
}
