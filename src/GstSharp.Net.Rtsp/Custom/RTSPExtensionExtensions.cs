using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Rtsp;

/// <content>
/// The transport reader of an RTSP extension, whose result the C function
/// hands back through a pointer the caller provides.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_rtsp_extension_get_transports</c> takes a <c>gchar**</c> and the gir
/// declares it as a plain string with no direction on it, so the generated
/// member takes a <see cref="string"/>: it copies that string into a temporary
/// UTF-8 buffer and hands the library the address of the buffer, where an
/// extension writes a freshly allocated transport header the member then
/// discards. The result is lost and leaked, which is why the generated overload
/// is <see cref="ObsoleteAttribute">obsolete</see> and this one exists beside
/// it. The published signature stays because the surface of a <c>1.28.x</c>
/// package only ever grows; the generated member takes the out parameter in
/// <c>1.30</c>, in the shape this overload already has, and this hand written
/// sibling goes away with it.
/// </para>
/// </remarks>
public static unsafe partial class RTSPExtensionExtensions
{
    /// <summary>
    /// Asks an extension for the transport header to offer for a set of lower
    /// transports.
    /// </summary>
    /// <param name="ext">The extension to ask.</param>
    /// <param name="protocols">The lower transports the caller is willing to use.</param>
    /// <param name="transport">
    /// The transport header the extension wants, which the caller owns, or
    /// <see langword="null"/> when the extension named none. An extension that
    /// does not implement the call, and an implementation that has nothing to
    /// say for the transports it was offered, both leave it
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see cref="RTSPResult.Ok"/> from an extension that answered and from one
    /// that does not implement the call at all, or whatever the implementation
    /// returned.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_extension_get_transports</c>. The C function is a
    /// vfunc dispatcher: it answers <c>GST_RTSP_OK</c> and leaves the
    /// destination exactly as it found it when the extension implements no
    /// <c>get_transports</c>, which is why a caller in C initialises the
    /// destination to <c>NULL</c> before the call and why a
    /// <see langword="null"/> here is a normal answer rather than a failure.
    /// The destination is written before this returns in either case.
    /// </para>
    /// <para>
    /// What an implementation writes is a string it allocated, which the caller
    /// releases; the copy is made here and the native string is freed with it,
    /// so nothing of the call outlives this member.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="ext"/> is <see langword="null"/>.
    /// </exception>
    public static RTSPResult GetTransports(
        this IRTSPExtension ext,
        RTSPLowerTrans protocols,
        out string? transport)
    {
        ArgumentNullException.ThrowIfNull(ext);

        // The dispatcher does not touch the destination when the extension
        // implements nothing, so the zero is the answer of that path rather
        // than a precaution.
        nint storage = 0;
        int nativeResult = GstRtspExtensionGetTransportsStorage(ext.Handle, (int)protocols, &storage);
        GC.KeepAlive(ext);
        transport = GMarshal.PtrToStringUtf8AndFree(storage);
        return (RTSPResult)nativeResult;
    }

    /// <summary>
    /// The <c>gst_rtsp_extension_get_transports</c> entry point, declared with
    /// the pointer to a string the C function really takes.
    /// </summary>
    /// <param name="ext">The extension to ask.</param>
    /// <param name="protocols">The lower transports, as the C enumeration.</param>
    /// <param name="transport">Where the allocated transport header goes.</param>
    /// <returns>The <c>GstRTSPResult</c> of the call.</returns>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_extension_get_transports")]
    private static partial int GstRtspExtensionGetTransportsStorage(nint ext, int protocols, nint* transport);
}
