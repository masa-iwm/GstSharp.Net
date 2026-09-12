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
    /// The answer carries one reference and one <c>reused</c> count - a thread
    /// that is made for the call starts at both (rtsp-thread-pool.c:107,
    /// :119-131 through :415), and a thread the pool recycles is reused, which
    /// adds one of each (rtsp-thread-pool.c:151-153, reached from :455-463) -
    /// and both leave together when the wrapper is disposed or
    /// <see cref="RTSPThread.Stop"/> is called. The reference the worker of the
    /// pool holds beside them (rtsp-thread-pool.c:474, :486) is not the
    /// caller's and is released by the loop itself. A thread the pool recycles
    /// is shared: the count is what says how many holders it has, and the loop
    /// only quits once the last of them has let go.
    /// </para>
    /// <para>
    /// <see langword="null"/> is a normal answer rather than a failure. A pool
    /// whose maximum is zero allows no <see cref="RTSPThreadType.Client"/>
    /// thread of its own and expects the caller to run the work on the thread
    /// it is already on (rtsp-thread-pool.c:447-452); a
    /// <see cref="RTSPThreadType.Media"/> thread is made whatever the maximum
    /// is (rtsp-thread-pool.c:483-489). A type the pool does not know
    /// (rtsp-thread-pool.c:490-491) and a thread the pool could not push to its
    /// worker pool (rtsp-thread-pool.c:497-505) answer <see langword="null"/>
    /// as well.
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
