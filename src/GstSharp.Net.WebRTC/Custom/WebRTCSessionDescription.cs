using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.WebRTC;

public sealed unsafe partial class WebRTCSessionDescription
{
    /// <summary>
    /// Creates a session description of the given kind around a session
    /// description message, taking the message over.
    /// </summary>
    /// <param name="type">
    /// What the description is: an offer, an answer, or one of the provisional
    /// forms of the two.
    /// </param>
    /// <param name="sdp">
    /// The message the description carries. The call consumes it:
    /// <paramref name="sdp"/> is disposed when this method returns, and using it
    /// afterwards throws <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>The description, which owns the message from here on.</returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_webrtc_session_description_new</c>,
    /// whose <c>sdp</c> parameter is <c>transfer-ownership="full"</c>: the C
    /// function stores the pointer it is handed, and freeing the description
    /// frees the message with it. The generator does not emit calls that take a
    /// wrapper over, because handing the only value of a wrapper to the library
    /// would let both of them release it, so this method is written by hand
    /// along the lines of <c>Gst.App.AppSrc.PushBuffer</c>: it hands the call a
    /// value of its own and then disposes the wrapper, which leaves the caller
    /// with exactly what the C call leaves it with.
    /// </para>
    /// <para>
    /// What "a value of its own" means differs from the mini object case. A
    /// boxed value has no reference count to raise, so the copy that
    /// <c>g_boxed_copy</c> makes is what a reference is there: the description
    /// owns a copy of the message, the wrapper is disposed and its own message
    /// is freed with it. Nothing is shared afterwards, which is the one
    /// observable difference to C — a C caller that kept the pointer would see
    /// the description's message, and a caller here cannot, because the wrapper
    /// it handed over is gone.
    /// </para>
    /// <para>
    /// The consuming shape is the one the C API has and the one applications
    /// expect, and it keeps the ownership rule of the binding intact: after this
    /// call the wrapper owns nothing, which is precisely what its disposed state
    /// means. <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the message stays correct.
    /// </para>
    /// <para>
    /// The name is the name of the C function:
    /// <c>gst_webrtc_session_description_new</c> becomes <c>New</c>, which is
    /// what the generator would have called it and which nothing on the
    /// generated class competes for.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sdp"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="sdp"/> was disposed.</exception>
    public static WebRTCSessionDescription New(Gst.WebRTC.WebRTCSDPType type, Gst.Sdp.SDPMessage sdp)
    {
        ArgumentNullException.ThrowIfNull(sdp);

        // The handle is read before anything is copied, so that a disposed
        // wrapper throws before a copy exists that nobody would free.
        nint owned = sdp.Handle;
        nuint boxedType = sdp.BoxedType.Value;

        // The value the call consumes.
        nint copy = GObjectNative.BoxedCopy(boxedType, owned);

        nint description = GstWebrtcSessionDescriptionNew((int)type, copy);

        // And the message of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. The dispose is
        // also what keeps the wrapper alive across the call above: it is the
        // last use of it, and the handle was read before the call.
        sdp.Dispose();

        return FromNative(description, Transfer.Full)
            ?? throw new InvalidOperationException("gst_webrtc_session_description_new returned no value.");
    }

    /// <summary>
    /// Gets the session description message this description carries, as a
    /// message of the caller's own.
    /// </summary>
    /// <returns>
    /// A copy of the message. The caller owns it and disposes it; the message of
    /// the description is left alone.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The <c>sdp</c> field has no accessor function in C, so it is read
    /// through the generated mirror of the structure, and what the field holds
    /// is a pointer the description owns. A <see cref="Gst.GObject.Boxed"/> wrapper
    /// cannot borrow: it frees what it holds when it is disposed, and its
    /// <see cref="Gst.Interop.Transfer.None"/> constructor therefore adopts a
    /// copy rather than the value itself. That is what this method hands out,
    /// the same way every generated binding of a boxed value that the library
    /// hands out borrowed does.
    /// </para>
    /// <para>
    /// A copy is also the only shape that is safe to hand a caller here. The
    /// message belongs to the description and is freed with it, so a wrapper
    /// that pointed at it would outlive its value the moment the description is
    /// disposed. This is a method rather than a property because it produces a
    /// new wrapper on every call, which is a thing to dispose rather than a
    /// thing to read.
    /// </para>
    /// <para>
    /// <see cref="Type"/> is in the same position and has the easier answer: it
    /// has no accessor function in C either and is read straight out of the
    /// mirror, but it is a value rather than a pointer, so it is a generated
    /// property and nothing is owned. That the mirror agrees with the library on
    /// where both fields sit is what the round trip of the integration tests
    /// establishes — a description built by <see cref="New"/> and read back
    /// through <see cref="Type"/> and this method.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The description carries no message. The library never builds one that
    /// way — <c>gst_webrtc_session_description_copy</c> would not survive it
    /// either — so this is a description that came from somewhere else.
    /// </exception>
    public Gst.Sdp.SDPMessage GetSdp()
    {
        nint sdp = ((WebRTCSessionDescriptionRaw*)Handle)->Sdp;

        // The copy is taken while the description is still known to be alive,
        // so the keep alive covers the read of the field and the copy both.
        Gst.Sdp.SDPMessage? message = Gst.Sdp.SDPMessage.FromNative(sdp, Transfer.None);
        GC.KeepAlive(this);

        return message
            ?? throw new InvalidOperationException("The session description carries no SDP message.");
    }

    /// <summary>The <c>gst_webrtc_session_description_new</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_session_description_new")]
    private static partial nint GstWebrtcSessionDescriptionNew(int type, nint sdp);
}
