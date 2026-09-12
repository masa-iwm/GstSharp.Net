using System.Runtime.InteropServices;

namespace Gst.RtspServer;

/// <content>
/// <c>gst_rtsp_media_prepare</c>, which consumes the thread it is handed on
/// every path and cannot be generated for it.
/// </content>
public unsafe partial class RTSPMedia
{
    /// <summary>
    /// Prepares the media for streaming, running its bus watch on a thread.
    /// </summary>
    /// <param name="thread">
    /// The thread to run the bus watch on, which a
    /// <see cref="RTSPThreadPool.GetThread"/> minted, or
    /// <see langword="null"/> for the default main context of the process.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the media is prepared.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="thread"/> owes no stop, so it was not handed out by
    /// <see cref="RTSPThreadPool.GetThread"/> and cannot be consumed here.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="thread"/> was disposed.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The thread is consumed whether the call succeeds or not.</b> Every
    /// path of the C stops it - the four failure labels and the success
    /// (rtsp-media.c:4257, :4265, :4274, :4284, :4294, :4386-4388) - so the
    /// wrapper is detached when this returns and using it afterwards throws.
    /// Nothing else releases it: a caller neither stops nor disposes what it
    /// handed over.
    /// </para>
    /// <para>
    /// <see langword="null"/> is accepted and is what the shipped shape of this
    /// member could only ever be given, but it is rarely what an application
    /// wants: the bus watch is then attached to the default main context and
    /// the call blocks for up to twenty seconds waiting for the preroll that
    /// only an iteration of that context can deliver (rtsp-media.c:3128-3136).
    /// A thread of the pool is the shape the library itself uses
    /// (rtsp-client.c:1065-1071, :3473-3479).
    /// </para>
    /// <para>
    /// A wrapper that owes no stop is refused before the call rather than
    /// consumed: the media would release a <c>reused</c> count that the
    /// wrapper never took, which stops a thread somebody else is still using.
    /// </para>
    /// </remarks>
    public bool Prepare(Gst.RtspServer.RTSPThread? thread)
    {
        // The handle of the thread is read first, so that a wrapper that was
        // disposed - or consumed by an earlier call - throws before anything
        // is handed over.
        nint threadHandle = thread is null ? 0 : thread.Handle;
        if (thread is not null && !thread.OwesStop)
        {
            throw new ArgumentException(
                "The thread was not handed out by RTSPThreadPool.GetThread, so it owes no stop and preparing " +
                "a media with it would release a use count it never took.",
                nameof(thread));
        }

        nint media = Handle;

        // The reference of the wrapper is what the media takes over, so
        // nothing is minted for the call.
        int prepared = GstRtspMediaPrepare(media, threadHandle);
        thread?.Consumed();
        bool result = prepared != 0;
        System.GC.KeepAlive(this);
        return result;
    }

    /// <summary>The <c>gst_rtsp_media_prepare</c> entry point.</summary>
    [LibraryImport("GstRtspServer", EntryPoint = "gst_rtsp_media_prepare")]
    private static partial int GstRtspMediaPrepare(nint media, nint thread);
}
