using System.Runtime.InteropServices;

namespace Gst.RtspServer;

/// <content>
/// The one producer of a <see cref="RTSPThread"/>, which the generator skips
/// because the thread it hands out is released by a stop rather than by an
/// unreference.
/// </content>
public unsafe partial class RTSPThreadPool
{
    /// <summary>
    /// Takes a thread of the pool for a purpose.
    /// </summary>
    /// <param name="type">What the thread is taken for.</param>
    /// <returns>
    /// The thread, which owes one stop, or <see langword="null"/> when the pool
    /// allows none.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <remarks>
    /// <para>
    /// The answer carries one reference and one <c>reused</c> count
    /// (rtsp-thread-pool.c:455-463, :474), and both leave together when the
    /// wrapper is disposed or <see cref="RTSPThread.Stop"/> is called. A
    /// thread the pool recycles is shared: the count is what says how many
    /// holders it has, and the loop only quits once the last of them has let
    /// go.
    /// </para>
    /// <para>
    /// <see langword="null"/> is a normal answer rather than a failure: a pool
    /// whose maximum is zero allows no thread of its own and expects the caller
    /// to run the work on the thread it is already on
    /// (rtsp-thread-pool.c:449-452).
    /// </para>
    /// <para>
    /// The <c>GstRTSPContext</c> the C takes beside the type is not offered.
    /// The default implementation reads nothing of it and only hands it to the
    /// <c>configure_thread</c> hook of a subclass (rtsp-thread-pool.c:429-430),
    /// which this binding does not let an application override, and the
    /// context itself is projected as a value rather than as a wrapper, so
    /// there is no live one to hand over. The null the call is given is what
    /// every in tree caller that has no context passes.
    /// </para>
    /// </remarks>
    public Gst.RtspServer.RTSPThread? GetThread(Gst.RtspServer.RTSPThreadType type)
    {
        nint handle = GstRtspThreadPoolGetThread(Handle, (int)type, 0);
        Gst.RtspServer.RTSPThread? result = handle == 0 ? null : Gst.RtspServer.RTSPThread.FromPool(handle);
        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>The <c>gst_rtsp_thread_pool_get_thread</c> entry point.</summary>
    [LibraryImport("GstRtspServer", EntryPoint = "gst_rtsp_thread_pool_get_thread")]
    private static partial nint GstRtspThreadPoolGetThread(nint pool, int type, nint ctx);
}
