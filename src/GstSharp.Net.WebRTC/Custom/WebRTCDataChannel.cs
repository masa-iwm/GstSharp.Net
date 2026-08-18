using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.WebRTC;

/// <content>
/// The binary half of a data channel, which the generator skips because both
/// ends of it carry a <c>GBytes</c>.
/// </content>
/// <remarks>
/// A data channel speaks two kinds of message, a string one and a binary one,
/// and the generated surface covers only the string one:
/// <c>gst_webrtc_data_channel_send_data_full</c> takes a <c>GBytes</c> and
/// <c>on-message-data</c> delivers one, and the planner emits neither. An
/// application could open a channel, send text and receive text, and had no way
/// to move a byte. The two members here close that, over the
/// <see cref="Gst.GLib.Bytes"/> wrapper of the runtime.
/// </remarks>
public abstract unsafe partial class WebRTCDataChannel
{
    /// <summary>
    /// Gets what the channel is doing: connecting, open, closing or closed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>ready-state</c> property has no generated binding — the planner
    /// has no plan for a property whose type is an enumeration of another
    /// module, and every property of this class is skipped for that reason — so
    /// it is read here through
    /// <see cref="Gst.GObject.Object.GetProperty(string)"/>, which is the
    /// general route to any property of any object.
    /// </para>
    /// <para>
    /// A channel is connecting from the moment it is created until the peers
    /// have agreed on it, and only an open channel carries a message.
    /// <see cref="SendData"/> reads this before it sends.
    /// </para>
    /// <para>
    /// <b><see cref="Close"/> is where the last two states come from, and it
    /// is graceful.</b> The closing procedure of the specification does not
    /// throw away what is already queued: the channel moves to
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Closing"/>, the messages
    /// that were handed over before the call are still sent, and only then does
    /// the transport reset the stream and the channel reach
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Closed"/> and raise
    /// <c>on-close</c>. Nothing is accepted after the call, so a send that
    /// races it is refused rather than half delivered. Closing a channel that
    /// is already closing or closed does nothing, and closing one that never
    /// reached a peer — one <c>webrtcbin</c> never associated with an SCTP
    /// transport — leaves it in the state it was created in, because there is
    /// no transport to run the procedure on.
    /// </para>
    /// <para>
    /// <b>Compare against
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Open"/> rather than
    /// switching over the members.</b> The C enumeration starts at a zero that
    /// its introspection data does not describe — the state a channel is in
    /// before it starts connecting — so a channel that was just created reports
    /// a number this enumeration has no name for. Only the four named states
    /// are ones the library documents; the unnamed one means "not yet".
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.WebRTC.WebRTCDataChannelState ReadyState
    {
        get
        {
            using Gst.GObject.Value state = GetProperty("ready-state");
            return (Gst.WebRTC.WebRTCDataChannelState)state.GetEnum();
        }
    }

    /// <summary>
    /// Sends a binary message over the channel.
    /// </summary>
    /// <param name="data">
    /// The bytes to send. They are copied on the way out, so the caller keeps
    /// the span and may reuse the memory behind it as soon as the call returns.
    /// An empty span sends an empty message, which is what the protocol allows
    /// and what a null <c>GBytes</c> means in C.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the channel was open and the message was
    /// queued.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_webrtc_data_channel_send_data_full</c>, the form that
    /// reports a failure. Its predecessor <c>gst_webrtc_data_channel_send_data</c>
    /// is deprecated since 1.22 and says nothing at all when the channel is
    /// closed or the message is too large, which is why only the reporting form
    /// is bound: the plain one is also the <c>send-data</c> action signal, and
    /// that signal is deprecated in the same breath.
    /// </para>
    /// <para>
    /// <b>A channel that is not open is answered here rather than by the
    /// library.</b> <see cref="ReadyState"/> is read first, and anything but
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Open"/> is
    /// <see langword="false"/> without a call. That is what the C
    /// documentation promises the return value means, and it is not what the
    /// implementation of <c>webrtcbin</c> does: its binary send reaches for the
    /// SCTP transport that a channel which never opened has not got, and
    /// dereferences null. Its string send is guarded — it fails an assertion on
    /// <c>channel-&gt;opened</c> and returns — so the two halves of the same
    /// class behave differently, which is a bug of the library rather than a
    /// contract. Sending through the deprecated <c>send-data</c> action signal
    /// crashes in exactly the same way, so nothing about the shape of this call
    /// is what avoids it; the check is.
    /// </para>
    /// <para>
    /// The check is a read and not a lock. A channel that closes between it and
    /// the call is a window the library leaves open and this cannot close, so
    /// an application that sends from one thread while another one tears the
    /// connection down still has to keep the two apart. What the check does
    /// remove is the case that is not a race at all: sending before the peers
    /// have agreed on the channel, which is what an application does when it
    /// sends without waiting for the open notification.
    /// </para>
    /// <para>
    /// The <c>GBytes</c> the call needs is built here and released here.
    /// <c>g_bytes_new</c> copies the span, and the channel takes a reference of
    /// its own for as long as the message is queued, so nothing of the caller's
    /// is held after the call.
    /// </para>
    /// <para>
    /// An empty span is passed as the null <c>GBytes</c> that the parameter is
    /// annotated to accept, rather than as a block of length zero. The two are
    /// not the same to the library: the null case is the one it documents as an
    /// empty message and builds an empty buffer for, while a block of length
    /// zero goes down the branch that reads the bytes out of it, and
    /// <c>g_bytes_get_data</c> answers a null pointer for an empty block. The
    /// meaning an application asks for — send a message with no payload — is
    /// the first one.
    /// </para>
    /// <para>
    /// A message larger than the peer's maximum message size fails rather than
    /// being fragmented; that is the exception below, and its message is the
    /// one the library wrote.
    /// </para>
    /// </remarks>
    /// <exception cref="Gst.GLib.GException">The channel refused the message.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool SendData(ReadOnlySpan<byte> data)
    {
        // The handle is read before the block is built, so that a disposed
        // wrapper throws without allocating one that nothing would free.
        nint channel = Handle;

        // See the remarks: the library crashes rather than refusing here.
        if (ReadyState != Gst.WebRTC.WebRTCDataChannelState.Open)
        {
            return false;
        }

        // See the remarks: an empty message is the null GBytes and not a block
        // of length zero, so nothing is allocated for one.
        using Gst.GLib.Bytes? bytes = data.IsEmpty ? null : Gst.GLib.Bytes.New(data);

        nint errorNative = 0;
        int sent = GstWebrtcDataChannelSendDataFull(channel, bytes?.Handle ?? nint.Zero, &errorNative);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);

        Gst.GLib.GException.ThrowIfSet(ref errorNative);
        return sent != 0;
    }

    /// <summary>The arguments of the <c>on-message-data</c> signal of <c>GstWebRTCDataChannel</c>.</summary>
    public sealed class OnMessageDataSignalArgs : System.EventArgs
    {
        /// <summary>Initializes a new instance of the <see cref="OnMessageDataSignalArgs"/> class.</summary>
        /// <param name="data">The bytes that were received.</param>
        internal OnMessageDataSignalArgs(Gst.GLib.Bytes? data)
        {
            Data = data;
        }

        /// <summary>
        /// Gets the bytes that were received, or <see langword="null"/> for a
        /// message that carried none.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The block belongs to the binding and is released when the handler
        /// returns.</b> It is not the handler's to dispose and not the
        /// handler's to keep: reading it inside the handler is what it is for,
        /// and anything that outlives the handler has to be a copy, which
        /// <see cref="Gst.GLib.Bytes.ToArray"/> makes. Using it afterwards
        /// throws <see cref="ObjectDisposedException"/>, and the span of
        /// <see cref="Gst.GLib.Bytes.GetData"/> taken inside the handler points
        /// at memory that is gone by then.
        /// </para>
        /// <para>
        /// This is the rule <see cref="Gst.BusSyncHandler"/> follows for the
        /// message it is handed, and for the same reason: the signal borrows
        /// the block from the channel, so the wrapper around it can only borrow
        /// as well.
        /// </para>
        /// </remarks>
        public Gst.GLib.Bytes? Data { get; }
    }

    /// <summary>Raised for the <c>on-message-data</c> signal of <c>GstWebRTCDataChannel</c>.</summary>
    /// <remarks>
    /// <para>
    /// The handler runs on whichever thread the channel received the message
    /// on, which is a streaming thread of the pipeline and never the thread
    /// that added the handler. An exception that leaves it does not cross the
    /// native frame: it is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> and the emission continues.
    /// </para>
    /// <para>
    /// The handler is remembered on the wrapper it was added to and has to be
    /// removed from that same instance, exactly as for a generated event.
    /// Looking the object up again normally hands the same wrapper out, but one
    /// that was disposed in between is replaced by a new one, which knows
    /// nothing of the handler.
    /// </para>
    /// </remarks>
    public event System.EventHandler<Gst.WebRTC.WebRTCDataChannel.OnMessageDataSignalArgs> OnMessageData
    {
        add => Gst.WebRTC.SignalConnections.Add(
            this,
            "on-message-data",
            (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&OnMessageDataTrampoline,
            value);

        remove => Gst.WebRTC.SignalConnections.Remove(this, "on-message-data", value);
    }

    /// <summary>The native handler of the <c>on-message-data</c> signal of <c>GstWebRTCDataChannel</c>.</summary>
    /// <param name="instance">The channel that received the message.</param>
    /// <param name="data">The <c>GBytes</c> of the message, borrowed from the channel.</param>
    /// <param name="userData">The <c>GCHandle</c> of the managed handler.</param>
    /// <remarks>
    /// The trampoline is written by hand for one reason: the argument is a
    /// <c>GBytes</c>, which the marshalling planner has no plan for, so the
    /// signal emitter skips the signal. Everything else follows the shape of
    /// the generated ones — the state of the handler is read out of the
    /// <c>GCHandle</c>, the instance is wrapped borrowed, and an exception is
    /// trapped rather than thrown across the frame.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnMessageDataTrampoline(nint instance, nint data, nint userData)
    {
        try
        {
            if (Gst.Interop.CallbackHandle
                    .GetState<System.EventHandler<Gst.WebRTC.WebRTCDataChannel.OnMessageDataSignalArgs>>(userData)
                is not { } handler)
            {
                return;
            }

            // The signal borrows the block, so the wrapper takes a reference of
            // its own and gives it back when the handler returns. Nothing the
            // handler kept of it is valid afterwards, which is what the
            // documentation of the argument says.
            using Gst.GLib.Bytes? dataValue = Gst.GLib.Bytes.FromNative(data, Gst.Interop.Transfer.None);

            handler(
                Gst.GObject.Object.FromNative(instance, Gst.Interop.Transfer.None),
                new Gst.WebRTC.WebRTCDataChannel.OnMessageDataSignalArgs(dataValue));
        }
        catch (Exception exception)
        {
            Gst.Interop.ExceptionTrap.Report(exception);
        }
    }

    /// <summary>The <c>gst_webrtc_data_channel_send_data_full</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_data_channel_send_data_full")]
    private static partial int GstWebrtcDataChannelSendDataFull(nint channel, nint data, nint* error);
}
