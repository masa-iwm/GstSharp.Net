using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Rtsp;

/// <content>
/// The parser of a transport header, whose result is a structure the caller
/// provides in C.
/// </content>
/// <remarks>
/// <c>gst_rtsp_transport_parse</c> writes into a <c>GstRTSPTransport</c> the
/// caller declares, which no generated out parameter can produce: the record is
/// opaque here and has no boxed free to allocate against. The storage comes
/// from <c>gst_rtsp_transport_new</c> instead, which allocates and initialises
/// one, and goes back through <c>gst_rtsp_transport_free</c>. That free is not
/// interchangeable with <c>g_free</c>: it re-initialises the transport first,
/// which is what releases the strings the parser duplicated into it.
/// </remarks>
public sealed partial class RTSPTransport
{
    /// <summary>
    /// Parses an RTSP transport header into a transport.
    /// </summary>
    /// <param name="transport">The header text, as it appears on the wire.</param>
    /// <param name="result">
    /// The parsed transport on success, which the caller owns and releases with
    /// <see cref="Free"/>; <see langword="null"/> when the text was not a
    /// transport header, or when the allocation itself failed.
    /// </param>
    /// <returns>
    /// <see cref="RTSPResult.Ok"/> when <paramref name="transport"/> was
    /// parsed, <see cref="RTSPResult.Einval"/> when it is not a transport
    /// header, or whatever the allocation answered.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_transport_parse</c>. The binding allocates the
    /// storage with <c>gst_rtsp_transport_new</c>, which is what
    /// <see cref="New(out Gst.Rtsp.RTSPTransport)"/> does as well, so the
    /// ownership of a parsed transport is the ownership of a new one: it is a
    /// bare pointer holder with no finalizer, and <see cref="Free"/> is the
    /// release.
    /// </para>
    /// <para>
    /// A parse that fails releases the storage here rather than handing back a
    /// half filled transport, which is why the failure answer is
    /// <see langword="null"/> and not an empty instance the caller would have
    /// to free.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="transport"/> is <see langword="null"/>.
    /// </exception>
    public static unsafe RTSPResult Parse(string transport, out RTSPTransport? result)
    {
        ArgumentNullException.ThrowIfNull(transport);

        nint storage = 0;
        RTSPResult allocated = (RTSPResult)GstRtspTransportNewStorage(&storage);
        if (allocated != RTSPResult.Ok || storage == 0)
        {
            result = null;
            return allocated;
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(transport, buffer);

        RTSPResult parsed = (RTSPResult)GstRtspTransportParse(scope.Pointer, storage);
        if (parsed != RTSPResult.Ok)
        {
            // The parser fills the transport as it goes and gives up part way
            // through, so the storage is released the only way it may be: the
            // free re-initialises the transport and drops the strings that were
            // duplicated into it.
            _ = GstRtspTransportFreeStorage(storage);
            result = null;
            return parsed;
        }

        result = FromNative(storage);
        return parsed;
    }

    /// <summary>The <c>gst_rtsp_transport_parse</c> entry point.</summary>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_transport_parse")]
    private static unsafe partial int GstRtspTransportParse(byte* str, nint transport);

    /// <summary>
    /// The <c>gst_rtsp_transport_new</c> entry point, which allocates the
    /// storage the parse above fills.
    /// </summary>
    /// <param name="transport">Receives the new transport.</param>
    /// <returns>A <c>GstRTSPResult</c>.</returns>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_transport_new")]
    private static unsafe partial int GstRtspTransportNewStorage(nint* transport);

    /// <summary>
    /// The <c>gst_rtsp_transport_free</c> entry point, which releases the
    /// storage above along with the strings the parser put in it.
    /// </summary>
    /// <param name="transport">The transport to release.</param>
    /// <returns>A <c>GstRTSPResult</c>.</returns>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_transport_free")]
    private static partial int GstRtspTransportFreeStorage(nint transport);
}
