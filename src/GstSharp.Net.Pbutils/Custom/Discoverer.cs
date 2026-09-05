using Gst.GLib;
using Gst.Interop;

namespace Gst.Pbutils;

/// <content>
/// The non throwing face of <c>gst_discoverer_discover_uri</c>, which is the
/// one call of the binding whose C hands back a result and an error together.
/// </content>
public unsafe partial class Discoverer
{
    /// <summary>
    /// Discovers the given URI, answering what was found and what went wrong
    /// instead of raising.
    /// </summary>
    /// <param name="uri">The URI to discover.</param>
    /// <param name="error">
    /// What the discovery reported, or <see langword="null"/> when it reported
    /// nothing.
    /// </param>
    /// <returns>
    /// What was discovered, or <see langword="null"/> when the library
    /// answered nothing at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>gst_discoverer_discover_uri</c> sets its error and returns its
    /// information object independently of one another
    /// (gstdiscoverer.c:2643-2689), and the case where both arrive is the
    /// interesting one: a stream whose media type no installed plugin can
    /// handle comes back as an information object whose
    /// <see cref="DiscovererInfo.GetResult"/> is
    /// <see cref="DiscovererResult.MissingPlugins"/>, carrying the installer
    /// details of what is missing, together with an error that says as much.
    /// <see cref="DiscoverUri"/> raises in that case, so the information is out
    /// of reach; this overload hands both out.
    /// </para>
    /// <para>
    /// The information object is the caller's to dispose, as it is for
    /// <see cref="DiscoverUri"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="uri"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public DiscovererInfo? TryDiscoverUri(string uri, out GException? error)
    {
        ArgumentNullException.ThrowIfNull(uri);

        Span<byte> uriBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope uriScope = GMarshal.StackUtf8(uri, uriBuffer);

        nint errorNative = 0;
        nint nativeResult = GstDiscovererDiscoverUri(Handle, uriScope.Pointer, &errorNative);
        GC.KeepAlive(this);

        // The GError of this call is a copy the caller owns
        // (gstdiscoverer.c:2675), unlike the borrowed one a signal is handed,
        // so it is read into a managed exception and released here.
        error = GException.FromBorrowed(errorNative);
        if (errorNative != 0)
        {
            GLibNative.ErrorFree(errorNative);
        }

        return Gst.GObject.Object.FromNative<DiscovererInfo>(nativeResult, Transfer.Full);
    }
}
