using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The one corner of the caps surface the generator does not emit:
/// <c>gst_caps_fixate</c>, the one conversion of its family that does not
/// always consume what it is given. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-the-instance-they-are-called-on">Calls that consume the instance they are called on</see>.
/// </content>
public sealed partial class Caps
{
    /// <summary>
    /// Fixates the caps: keeps their first structure, replaces every range and
    /// list in it by one value, and answers the result.
    /// </summary>
    /// <returns>
    /// The fixated caps, which the caller owns and disposes. They are writable.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_caps_fixate</c>. It is hand written because it is the one
    /// member of its family that does not consume the reference it is given on
    /// every path: <c>gstcaps.c</c> guards it with
    /// <c>g_return_val_if_fail (!CAPS_IS_ANY (caps), NULL)</c>, so ANY caps
    /// answer <c>NULL</c> without the reference ever reaching the conversion.
    /// The generated shape mints a reference for the call and adopts the
    /// answer, which on that path would leak the mint. The check happens here
    /// instead, before anything is minted; every other path is what the
    /// generator emits for <see cref="Truncate"/> and its relatives.
    /// </para>
    /// <para>
    /// This wrapper is left alone: the call is handed a reference minted for
    /// it, so the caps this wrapper stands for keep the reference they own and
    /// both wrappers are disposed by whoever holds them.
    /// </para>
    /// <para>
    /// The returned wrapper may refer to the same native caps as this one when
    /// the call did not need to change them; it is then shared and not
    /// writable.
    /// </para>
    /// <para>
    /// Empty caps are fixated: they answer empty caps, which carry no structure
    /// at all. Only ANY caps are refused, and
    /// <see cref="Caps.IsAny"/> is the test to make first.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The caps are ANY, which the C function refuses.
    /// </exception>
    public Gst.Caps Fixate()
    {
        if (IsAny())
        {
            throw new InvalidOperationException(
                "ANY caps cannot be fixated: gst_caps_fixate refuses them and answers nothing, without " +
                "consuming anything. Test Caps.IsAny() first and fixate a concrete set of caps instead.");
        }

        nint instanceHandle = Handle;

        // The call takes a reference over, so it is handed one of its own and
        // this wrapper keeps the one it holds.
        nint instanceOwned = GstNative.MiniObjectRef(instanceHandle);
        nint nativeResult = GstCapsFixate(instanceOwned);

        // Reading Handle is the last use of this wrapper before the call, so
        // without this the collector may finalize it while the call runs.
        GC.KeepAlive(this);
        return Gst.Caps.FromNative(nativeResult, Transfer.Full)
            ?? throw new InvalidOperationException("gst_caps_fixate returned no value.");
    }

    /// <summary>The <c>gst_caps_fixate</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_caps_fixate")]
    private static partial nint GstCapsFixate(nint caps);
}
