using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The failure path of the adopt in place shape, on both wrappers that carry
/// it: the C function consumed what it was given and answered nothing, so the
/// wrapper has to end up disposed and the call has to raise rather than hand
/// back a wrapper that stands for nothing.
/// </summary>
/// <remarks>
/// <para>
/// No GStreamer installation is needed to pin it, and none could produce it on
/// demand either: the one call that answers zero is
/// <c>gst_memory_make_writable</c> on an allocator whose <c>mem_copy</c> fails,
/// which no allocator that ships does. What can be exercised is the pair of
/// runtime primitives every <c>MakeWritable</c> is written out of —
/// <c>BeginMakeWritable</c> reads the handle, the call runs, and
/// <c>AdoptWritable</c> takes its answer — because nothing native happens
/// between them.
/// </para>
/// <para>
/// The wrapper being left disposed rather than holding zero and throwing later
/// is the whole point: the reference is gone either way, and the release the
/// finalizer would otherwise attempt has nothing left to release.
/// </para>
/// </remarks>
public class AdoptWritableTests
{
    /// <summary>
    /// A handle that stands for a native object without being one. Nothing in
    /// the path under test dereferences it.
    /// </summary>
    private static readonly nint Sentinel = 0x1000;

    [Fact]
    public void AMiniObjectWrapperIsLeftDisposedWhenTheCopyCouldNotBeMade()
    {
        ProbeMiniObject probe = new(Sentinel);

        Assert.Equal(Sentinel, probe.Begin());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => probe.Adopt(nint.Zero));

        Assert.Contains("could not be made writable", error.Message, StringComparison.Ordinal);
        Assert.True(probe.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => probe.Handle);
    }

    [Fact]
    public void AMiniObjectWrapperFollowsTheObjectTheCallAnswered()
    {
        using ProbeMiniObject probe = new(Sentinel);
        nint replacement = Sentinel + 0x40;

        probe.Adopt(replacement);

        Assert.False(probe.IsDisposed);
        Assert.Equal(replacement, probe.Handle);
    }

    [Fact]
    public void ABoxedWrapperIsLeftDisposedWhenTheCopyCouldNotBeMade()
    {
        ProbeBoxed probe = new(Sentinel);

        Assert.Equal(Sentinel, probe.Begin());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => probe.Adopt(nint.Zero));

        Assert.Contains("could not be made writable", error.Message, StringComparison.Ordinal);
        Assert.True(probe.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => probe.Handle);
    }

    /// <summary>
    /// A mini object wrapper over a handle that is not one. The reference count
    /// is never touched: the constructor adopts, and the release is overridden
    /// away so that neither disposing nor the finalizer reaches native code.
    /// </summary>
    /// <param name="handle">The sentinel to hold.</param>
    private sealed class ProbeMiniObject(nint handle) : MiniObject(handle, Transfer.Full)
    {
        /// <summary>Gives the handle up the way a generated member does.</summary>
        /// <returns>The handle the call would be given.</returns>
        internal nint Begin() => BeginMakeWritable();

        /// <summary>Adopts what the call would have answered.</summary>
        /// <param name="writable">The answer.</param>
        internal void Adopt(nint writable) => AdoptWritable(writable);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // There is nothing to unreference: the handle is a sentinel.
        }
    }

    /// <summary>
    /// A boxed wrapper over a handle that is not a value. Nothing is copied and
    /// nothing is freed, for the same reason.
    /// </summary>
    /// <param name="handle">The sentinel to hold.</param>
    private sealed class ProbeBoxed(nint handle) : Boxed(handle, GType.Boxed, Transfer.Full)
    {
        /// <summary>Gives the handle up the way a generated member does.</summary>
        /// <returns>The handle the call would be given.</returns>
        internal nint Begin() => BeginMakeWritable();

        /// <summary>Adopts what the call would have answered.</summary>
        /// <param name="writable">The answer.</param>
        internal void Adopt(nint writable) => AdoptWritable(writable);

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            // There is nothing to free: the handle is a sentinel.
        }
    }
}
