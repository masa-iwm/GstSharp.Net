using System.Collections.Generic;

namespace Gst.Rtsp;

/// <content>
/// The parameters of a credential, which the C keeps in a NULL terminated
/// block behind the <c>params</c> field.
/// </content>
/// <remarks>
/// The generated accessors of this record read their field through
/// <c>RTSPAuthCredentialRaw</c>, the mirror of
/// <c>struct _GstRTSPAuthCredential</c>, and the member below reads the third
/// field the same way. It carries no accessor of its own because the field is
/// a pointer to a block whose end is a null element: handing that address out
/// would be a value nothing managed could walk, so the field is filed under
/// the hand bound entries of <c>girs/overlays/fixups.json</c> and answered
/// here instead.
/// </remarks>
public sealed partial class RTSPAuthCredential
{
    /// <summary>
    /// Reads the parameters of this credential.
    /// </summary>
    /// <returns>
    /// The parameters, which the caller owns, or an empty array when the
    /// credential carries none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>params</c> field of <c>GstRTSPAuthCredential</c>, which
    /// the C leaves null for a credential with no parameter at all — a
    /// <c>Basic</c> credential of an <c>Authorization</c> header is the usual
    /// one — and otherwise points at a block whose last element is null.
    /// </para>
    /// <para>
    /// It is a method rather than a property because every call allocates: the
    /// parameters are wrapped as <c>transfer none</c> boxed values, which is a
    /// <c>g_boxed_copy</c>, and that copy re-allocates the name and the value
    /// of every parameter. What comes back therefore outlives this credential
    /// and is the caller's to dispose, which is not what a property beside
    /// <see cref="Scheme"/> and <see cref="Authorization"/> would suggest.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public unsafe RTSPAuthParam[] GetParams()
    {
        nint block = ((RTSPAuthCredentialRaw*)Handle)->Params;
        if (block == 0)
        {
            System.GC.KeepAlive(this);
            return [];
        }

        List<RTSPAuthParam> parameters = [];
        for (nint* element = (nint*)block; *element != 0; element++)
        {
            parameters.Add(
                RTSPAuthParam.FromNative(*element, Gst.Interop.Transfer.None)
                ?? throw new InvalidOperationException(
                    "A credential carries a parameter block with a null parameter in it."));
        }

        System.GC.KeepAlive(this);
        return [.. parameters];
    }
}
