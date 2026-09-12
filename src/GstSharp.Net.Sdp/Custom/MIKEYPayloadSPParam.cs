namespace Gst.Sdp;

/// <content>
/// The value of a security policy parameter, which the generated half carries
/// as the raw pointer the C structure holds.
/// </content>
/// <remarks>
/// <c>GstMIKEYPayloadSPParam</c> is <c>{ guint8 type; guint8 len; guint8 *val; }</c>
/// (gstmikey.h:366-370): the bytes live in a block of their own that the
/// payload owns, so the structure the generator emits from the gir carries the
/// address and no projection of it. The span below is that projection, read
/// from the payload's memory at the moment it is asked for, the way the string
/// fields of a copied out row are.
/// </remarks>
public partial struct MIKEYPayloadSPParam
{
    /// <summary>The bytes of the parameter.</summary>
    /// <value>
    /// The <see cref="Len"/> bytes at <see cref="ValPtr"/>, or an empty span
    /// when the parameter carries none.
    /// </value>
    /// <remarks>
    /// The bytes are borrowed from the payload this parameter was read out of:
    /// <c>gst_mikey_payload_sp_add_param</c> duplicates them into a block the
    /// payload owns (gstmikey.c:539-557) and removing the parameter, or
    /// releasing the payload, frees that block (gstmikey.c:508-524, :431).
    /// The span is read at access time and points into that memory, so it is
    /// valid for exactly as long as the parameter is - copy whatever has to
    /// outlive it. A parameter whose length is zero has no block at all:
    /// <c>g_memdup2</c> answers <c>NULL</c> for it, which reads here as the
    /// empty span.
    /// </remarks>
    public unsafe ReadOnlySpan<byte> Val => ValPtr == 0 ? default : new((void*)ValPtr, Len);
}
