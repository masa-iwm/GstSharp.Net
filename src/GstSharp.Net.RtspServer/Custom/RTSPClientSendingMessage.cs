using System.Runtime.InteropServices;

namespace Gst.RtspServer;

/// <content>
/// The <c>send-message</c> signal of <c>GstRTSPClient</c>, whose introspection
/// data names an argument type the C never registered.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_rtsp_client_class_init</c> registers the signal with
/// <c>(GST_TYPE_RTSP_CONTEXT, G_TYPE_POINTER)</c> (1.28.6
/// <c>rtsp-client.c:531-535</c>) and <c>send_message</c> emits
/// <c>(ctx, message)</c> (<c>rtsp-client.c:935</c>). The doc comment above the
/// registration calls the first argument
/// <c>@session: (type GstRtspServer.RTSPSession)</c>, which is what the gir
/// carries; the introspection data has said so since 2014, apparently copied
/// from the <c>gst_rtsp_client_send_message (client, session, message)</c>
/// method beside it, while the signal has emitted a context since the day it
/// was added. The 1.24 floor registers and documents it the same way.
/// </para>
/// <para>
/// A generated event follows the gir, so it wrapped a
/// <c>GstRTSPContext</c> - a <c>G_DEFINE_POINTER_TYPE</c> structure on the
/// stack of the caller - as a <c>GObject</c>, which raised on every emission
/// before the handler was reached. The event is therefore kept off the
/// generated surface by <c>girs/overlays/fixups.json</c> and written here
/// instead, under the names the generated member carried, so that the
/// assembly stays compatible with the shape that shipped.
/// </para>
/// </remarks>
public unsafe partial class RTSPClient
{
    /// <summary>The arguments of the <c>send-message</c> signal of <c>GstRTSPClient</c>.</summary>
    public sealed class SendingMessageSignalArgs : System.EventArgs
    {
        /// <summary>Initializes a new instance of the <see cref="SendingMessageSignalArgs"/> class.</summary>
        /// <param name="ctx">a #GstRTSPContext</param>
        /// <param name="message">The message that is about to be sent</param>
        internal SendingMessageSignalArgs(Gst.RtspServer.RTSPContext ctx, Gst.Rtsp.RTSPMessage message)
        {
            Ctx = ctx;
            Message = message;
        }

        /// <summary>a #GstRTSPContext</summary>
        /// <remarks>
        /// A read only snapshot: the structure is copied out of the storage the
        /// emitter holds, so writing to it changes nothing the emission reads
        /// back. Every pointer inside it is borrowed for the length of the
        /// handler and must not be kept past it.
        /// </remarks>
        public Gst.RtspServer.RTSPContext Ctx { get; }

        /// <summary>The session of the context, where the request has one.</summary>
        /// <remarks>
        /// <para>
        /// This reads <see cref="Gst.RtspServer.RTSPContext.Session"/> of
        /// <see cref="Ctx"/>. It is <see langword="null"/> whenever the request
        /// being answered carries no session, which is every <c>OPTIONS</c> and
        /// every <c>DESCRIBE</c>.
        /// </para>
        /// </remarks>
        [Obsolete(
            "The send-message signal never carried a session: the C registers and emits a GstRTSPContext "
            + "as its first argument, and only the introspection data called it a session. Read Ctx, and "
            + "Ctx.Session where the session itself is wanted.")]
        public Gst.RtspServer.RTSPSession? Session => Ctx.Session;

        /// <summary>The message that is about to be sent</summary>
        /// <remarks>
        /// The emission lends this value for the length of the handler: the
        /// wrapper borrows it, holds no reference and no copy of its own, and is
        /// disposed once the handler returns, so it must not be stored. It is
        /// the very message the client writes to the connection next - the C
        /// emits the signal, then sends this pointer and unsets it
        /// (<c>rtsp-client.c:935-945</c>) - so a header a handler adds to it is
        /// a header the peer receives. Copy it where something has to outlive
        /// the handler.
        /// </remarks>
        public Gst.Rtsp.RTSPMessage Message { get; }
    }

    /// <summary>Raised for the <c>send-message</c> signal of <c>GstRTSPClient</c>.</summary>
    /// <remarks>
    /// The handler is remembered on the wrapper it was added to and has to be
    /// removed from that same instance. Looking the object up again normally
    /// hands the same wrapper out, but one that was disposed in between is
    /// replaced by a new one, which knows nothing of the handler.
    /// </remarks>
    public event System.EventHandler<Gst.RtspServer.RTSPClient.SendingMessageSignalArgs> SendingMessage
    {
        add => Gst.RtspServer.SignalConnections.Add(this, "send-message", (nint)(delegate* unmanaged[Cdecl]<nint, Gst.RtspServer.RTSPContext*, nint, nint, void>)&SendingMessageTrampoline, value);
        remove => Gst.RtspServer.SignalConnections.Remove(this, "send-message", value);
    }

    /// <summary>The native handler of the <c>send-message</c> signal of <c>GstRTSPClient</c>.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void SendingMessageTrampoline(nint instance, Gst.RtspServer.RTSPContext* ctx, nint message, nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle.GetState<System.EventHandler<Gst.RtspServer.RTSPClient.SendingMessageSignalArgs>>(userData) is not { } handler)
            {
                return;
            }

            Gst.RtspServer.RTSPContext ctxValue = *ctx;
            using Gst.Rtsp.RTSPMessage messageValue = (message == nint.Zero ? null : Gst.Rtsp.RTSPMessage.Borrow(message))
                ?? throw new InvalidOperationException("The send-message signal of GstRTSPClient passed no message.");
            handler(
                Gst.GObject.Object.FromNative(instance, Gst.Interop.Transfer.None),
                new Gst.RtspServer.RTSPClient.SendingMessageSignalArgs(ctxValue, messageValue));
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
    }
}
