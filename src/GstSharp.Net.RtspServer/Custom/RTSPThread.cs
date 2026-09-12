using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.RtspServer;

/// <content>
/// The stop based lifetime of a thread the pool hands out.
/// </content>
/// <remarks>
/// <para>
/// A pooled thread is not released by an unreference. One
/// <c>gst_rtsp_thread_pool_get_thread</c> hands out one reference and one
/// <c>reused</c> count (rtsp-thread-pool.c:455-463, :474), and exactly one
/// <c>gst_rtsp_thread_stop</c> releases both: the last stop attaches an idle
/// source that quits the loop and unreferences the thread from its destroy
/// notification, and every other stop unreferences it straight away
/// (rtsp-thread-pool.c:174-190). Unreferencing without stopping therefore
/// leaves the OS thread running for good, and stopping twice on one reference
/// releases one that was never taken.
/// </para>
/// <para>
/// The wrapper <see cref="RTSPThreadPool.GetThread"/> mints owes exactly one
/// stop, which <see cref="Stop"/> and disposal perform. A wrapper that was
/// handed a thread some other way - the lent one of a virtual method - owes
/// none and is released the ordinary way.
/// </para>
/// </remarks>
public sealed unsafe partial class RTSPThread
{
    private bool _stopOnDispose;

    /// <summary>
    /// Gets the main context the thread runs its sources on.
    /// </summary>
    /// <value>
    /// A wrapper of the <c>GMainContext</c> of the thread, which the caller
    /// disposes.
    /// </value>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <remarks>
    /// <para>
    /// The context is written when the thread is created and never replaced,
    /// and it lives as long as the thread does. Every read hands out a wrapper
    /// of its own that holds a reference of its own, the way
    /// <see cref="Gst.GLib.MainContext.ThreadDefault"/> does, so the caller
    /// disposes what it reads.
    /// </para>
    /// <para>
    /// The loop beside it is deliberately not offered. Quitting it from
    /// outside leaves the idle source that a stop attached with nothing to run
    /// it, so the reference that source carries is never released and the
    /// thread is never collected.
    /// </para>
    /// </remarks>
    public Gst.GLib.MainContext Context
    {
        get
        {
            nint context = ((RTSPThreadRaw*)Handle)->Context;
            Gst.GLib.MainContext value = new(context, Transfer.None);
            System.GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this wrapper owes the thread a stop.
    /// </summary>
    internal bool OwesStop => _stopOnDispose;

    /// <summary>
    /// Releases this reference of the thread, together with the <c>reused</c>
    /// count that came with it.
    /// </summary>
    /// <remarks>
    /// This is <c>gst_rtsp_thread_stop</c> under the name the library uses.
    /// Disposing the wrapper does the same thing, and a second call is a no
    /// operation. The OS thread behind it stays alive while any other holder -
    /// a media that was prepared on it, the recycling of the pool itself -
    /// still counts a use of it.
    /// </remarks>
    public void Stop() => Dispose();

    /// <summary>
    /// Wraps a thread the pool has just handed out, which owes one stop.
    /// </summary>
    /// <param name="handle">The thread, with the reference the pool minted.</param>
    /// <returns>The wrapper, which performs the stop when it is disposed.</returns>
    /// <remarks>
    /// <see cref="Gst.Interop.Transfer.Full"/> adopts the reference without
    /// taking one of its own (Custom/MiniObject.cs:82-87), so the wrapper owns
    /// exactly what <c>gst_rtsp_thread_pool_get_thread</c> handed over.
    /// </remarks>
    internal static RTSPThread FromPool(nint handle) => new(handle, Transfer.Full) { _stopOnDispose = true };

    /// <summary>
    /// Hands the reference of this wrapper over to a call that consumes it.
    /// </summary>
    /// <remarks>
    /// Nothing is released: the callee has taken the reference and the
    /// <c>reused</c> count with it, so the wrapper only detaches itself and
    /// every member of it throws afterwards.
    /// </remarks>
    internal void Consumed()
    {
        _stopOnDispose = false;
        _ = HandOver();
    }

    /// <summary>
    /// Performs the stop this wrapper owes and then releases its reference.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <see cref="Gst.MiniObject.Dispose()"/>,
    /// <see langword="false"/> when it comes from the finalizer.
    /// </param>
    /// <remarks>
    /// The stop is handed a reference of its own, because it consumes one on
    /// every path, and the base class then releases the one the wrapper owns:
    /// one reference and one <c>reused</c> count leave together, which is
    /// exactly what the <c>get_thread</c> that minted this wrapper added. The
    /// finalizer thread may perform the stop as well: it decrements the reuse
    /// count atomically and either attaches the source that quits the loop to
    /// the context of the thread or unreferences it, and neither needs the
    /// thread the loop runs on (rtsp-thread-pool.c:182-189).
    /// </remarks>
    protected override void Dispose(bool disposing)
    {
        if (_stopOnDispose && !IsDisposed)
        {
            _stopOnDispose = false;
            nint thread = Handle;
            _ = Gst.GstNative.MiniObjectRef(thread);
            GstRtspThreadStop(thread);
        }

        base.Dispose(disposing);
    }

    /// <summary>The <c>gst_rtsp_thread_stop</c> entry point.</summary>
    [LibraryImport("GstRtspServer", EntryPoint = "gst_rtsp_thread_stop")]
    private static partial void GstRtspThreadStop(nint thread);
}
