using System.Runtime.InteropServices;

namespace Gst.RtspServer;

/// <content>
/// The other half of <see cref="RTSPServer.Attach"/>: the call that takes the
/// source back off the context again, which the C library leaves to
/// <c>glib</c> and this binding therefore has to spell itself.
/// </content>
public partial class RTSPServer
{
    /// <summary>
    /// Detaches the source that <see cref="Attach"/> put on a main context,
    /// which stops the server from accepting connections.
    /// </summary>
    /// <param name="sourceId">
    /// The identifier <see cref="Attach"/> answered. <see cref="Attach"/>
    /// answers 0 on failure, and 0 is never a valid source identifier.
    /// </param>
    /// <param name="context">
    /// <b>The very context that was passed to <see cref="Attach"/>.</b>
    /// <see langword="null"/> stands for the default context and matches an
    /// <c>Attach(null)</c>; a private
    /// <see cref="Gst.GLib.MainContext"/> has to be named here as well.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a source with that identifier was found on
    /// that context and destroyed, <see langword="false"/> when there was none
    /// — because it was already detached, because the identifier is not this
    /// server's, or because the wrong context was named.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Naming the wrong context is not caught.</b> For the default context
    /// this is <c>g_source_remove</c>, which searches the default context only
    /// and logs a GLib critical when it finds nothing; for a private context it
    /// is <c>g_main_context_find_source_by_id</c> followed by
    /// <c>g_source_destroy</c>, which answers <see langword="false"/> quietly.
    /// So detaching a source that was attached to a private context while
    /// passing <see langword="null"/> here produces a critical and a
    /// <see langword="false"/>, and leaves the server listening. Keep the
    /// identifier and the context together for as long as the server is
    /// attached.
    /// </para>
    /// <para>
    /// This is the first step of the shutdown order, which the whole of is:
    /// detach the source, then <see cref="ClientFilter"/> with
    /// <see cref="RTSPFilterResult.Remove"/> to close every connection, then
    /// <see cref="RTSPSessionPool.Filter"/> with the same answer — a closing
    /// client does not remove its session, and
    /// <see cref="RTSPSessionPool.Cleanup"/> only expires the timed out ones —
    /// then poll <c>ClientFilter(null)</c> until it is empty, because the close
    /// of a client completes asynchronously on its own thread, and only then,
    /// if at all, <see cref="RTSPThreadPool.Cleanup"/>, which joins every pool
    /// thread and blocks forever when it is called before the clients are gone.
    /// See
    /// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#rtsp-server">RTSP server</see>.
    /// </para>
    /// <para>
    /// Disposing the server instead of detaching does not stop it. The attached
    /// source holds a reference of its own on the native server and every
    /// managed client holds another, so
    /// <see cref="Gst.GObject.Object.Dispose()"/> while attached only gives up
    /// this wrapper's part: it strips the handlers this wrapper connected and
    /// leaves the server serving, with no handle left to detach it by. That is
    /// safe, and it is never what was meant.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="context"/> was disposed.
    /// </exception>
    public bool Detach(uint sourceId, Gst.GLib.MainContext? context = null)
    {
        if (context is null)
        {
            return GSourceRemove(sourceId) != 0;
        }

        // The source is borrowed: find_source_by_id does not reference it, and
        // destroying it is all that is asked of the caller.
        nint source = GMainContextFindSourceById(context.Handle, sourceId);

        System.GC.KeepAlive(context);

        if (source == 0)
        {
            return false;
        }

        GSourceDestroy(source);
        return true;
    }

    /// <summary>The <c>g_source_remove</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_source_remove")]
    private static partial int GSourceRemove(uint tag);

    /// <summary>The <c>g_main_context_find_source_by_id</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_main_context_find_source_by_id")]
    private static partial nint GMainContextFindSourceById(nint context, uint sourceId);

    /// <summary>The <c>g_source_destroy</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_source_destroy")]
    private static partial void GSourceDestroy(nint source);
}
