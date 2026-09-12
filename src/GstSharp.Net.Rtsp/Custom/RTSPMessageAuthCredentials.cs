using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gst.Rtsp;

/// <content>
/// The authentication credentials a message carries, which the C hands out as
/// a block that only one free function releases.
/// </content>
/// <remarks>
/// <c>gst_rtsp_message_parse_auth_credentials</c> answers a NULL terminated
/// <c>GstRTSPAuthCredential**</c> and the gir states neither a length nor a
/// zero termination on it, so the planner has no shape for the return; the
/// block is also released by <c>gst_rtsp_auth_credentials_free</c> alone,
/// which walks it to the terminator and frees every credential with a free
/// that is static in C and reachable only through the boxed type. The member
/// below is therefore the whole binding of both symbols: it copies what it
/// reads and frees the block once.
/// </remarks>
public sealed partial class RTSPMessage
{
    /// <summary>
    /// Reads the authentication credentials of one header of this message.
    /// </summary>
    /// <param name="field">
    /// The header to parse. <see cref="RTSPHeaderField.WwwAuthenticate"/> and
    /// <see cref="RTSPHeaderField.ProxyAuthenticate"/> are the challenges a
    /// server sends, <see cref="RTSPHeaderField.Authorization"/> the answer a
    /// client sends.
    /// </param>
    /// <returns>
    /// The credentials of that header, which the caller owns, or an empty
    /// array.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_message_parse_auth_credentials</c>. The C answers a
    /// null pointer rather than an empty array whenever the message carries no
    /// such header, or carries one that names no <c>Basic</c> and no
    /// <c>Digest</c> scheme; both of those read as the empty array here, so
    /// the answer is never <see langword="null"/> and never holds a
    /// <see langword="null"/> element.
    /// </para>
    /// <para>
    /// <see cref="RTSPHeaderField.Authorization"/> yields at most one
    /// credential — the C stops after the first — while every other field
    /// yields one per scheme the header lists. A <c>Basic</c> credential of an
    /// <c>Authorization</c> header carries its blob in
    /// <see cref="RTSPAuthCredential.Authorization"/> and no parameters at
    /// all; every other form carries its parameters and no authorization.
    /// </para>
    /// <para>
    /// What comes back are copies. Each credential is wrapped as a
    /// <c>transfer none</c> boxed value, which is a <c>g_boxed_copy</c>, and
    /// the copy of this type is deep: the scheme, every parameter and the
    /// authorization string are re-allocated. The block the C allocated is
    /// handed back to <c>gst_rtsp_auth_credentials_free</c> before this member
    /// returns, so the values outlive both it and this message, and the caller
    /// disposes them.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public unsafe RTSPAuthCredential[] ParseAuthCredentials(RTSPHeaderField field)
    {
        nint block = GstRtspMessageParseAuthCredentials(Handle, (int)field);
        if (block == 0)
        {
            System.GC.KeepAlive(this);
            return [];
        }

        List<RTSPAuthCredential> credentials = [];
        try
        {
            for (nint* element = (nint*)block; *element != 0; element++)
            {
                // A transfer of none is the g_boxed_copy that makes the
                // wrapper own a credential of its own, which is what lets the
                // block be freed below while the answer stays readable.
                credentials.Add(
                    RTSPAuthCredential.FromNative(*element, Gst.Interop.Transfer.None)
                    ?? throw new InvalidOperationException(
                        "gst_rtsp_message_parse_auth_credentials answered a block with a null credential in it."));
            }
        }
        finally
        {
            // The whole block is released by this one call, whether the walk
            // above finished or threw: it frees every credential it reaches
            // and then the block itself.
            GstRtspAuthCredentialsFree(block);
        }

        System.GC.KeepAlive(this);
        return [.. credentials];
    }

    /// <summary>The <c>gst_rtsp_message_parse_auth_credentials</c> entry point.</summary>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_message_parse_auth_credentials")]
    private static partial nint GstRtspMessageParseAuthCredentials(nint msg, int field);

    /// <summary>The <c>gst_rtsp_auth_credentials_free</c> entry point.</summary>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_auth_credentials_free")]
    private static partial void GstRtspAuthCredentialsFree(nint credentials);
}
