using System.Runtime.InteropServices;

namespace Gst.WebRTC;

/// <content>
/// The send half of the binary side of a data channel - the two members that
/// take a span and a block - which the generator skips because the C refuses
/// the block an empty message would be built from.
/// </content>
/// <remarks>
/// <para>
/// The receiving half is generated: <c>on-message-data</c> carries a
/// <c>GBytes</c>, which the planner marshals like every other wrapper of the
/// runtime, so the event and its arguments come out of
/// <c>Generated/WebRTCDataChannel.cs</c>. The send half stays here because of
/// what <c>webrtcbin</c> does with an empty block rather than because of the
/// marshalling: it reads the data pointer out of the block and refuses a null
/// one with a critical and a <see langword="false"/> that carries no error,
/// and every empty block <c>g_bytes_new</c> builds — which is every block this
/// binding builds — has a null data pointer. The method below answers an empty
/// message the way the library documents it — with no block at all — and it
/// answers a channel that is not open, which the C dereferences null for.
/// </para>
/// <para>
/// <b>Receiving is the generated <see cref="OnMessageData"/> event, and a
/// handler of it does not run where a send does.</b> <c>webrtcbin</c>
/// does not emit the signal where it read the message: it wraps the
/// received bytes and queues the emission on the main context of the
/// thread it starts for the peer connection, and the handler is called
/// from there. That is the thread the state changes and the promise
/// replies of the same connection are delivered on, so a handler that
/// blocks holds all of them up, and it is never the thread that added the
/// handler. That is the contract of <c>webrtcbin</c> rather than of this
/// class: a channel some other element implements emits wherever its
/// implementation calls <c>gst_webrtc_data_channel_on_message_data</c>. An
/// exception that leaves the handler does not cross the native frame
/// either — it is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and the emission continues.
/// </para>
/// <para>
/// The block such a handler is handed is borrowed for the duration of the
/// call and released when the handler returns, which is the rule the
/// message of a <see cref="Gst.BusSyncHandler"/> follows for the same
/// reason: the signal borrows the block from the channel, so the wrapper
/// around it can only borrow as well. It is not the handler's to dispose
/// and not the handler's to keep. Reading it inside the handler is what it
/// is for, and anything that outlives the handler has to be a copy, which
/// <see cref="Gst.GLib.Bytes.ToArray"/> makes; using the wrapper
/// afterwards throws <see cref="ObjectDisposedException"/>, and a span
/// <see cref="Gst.GLib.Bytes.GetData"/> handed out inside the handler
/// points at memory that is gone by then.
/// </para>
/// </remarks>
public abstract unsafe partial class WebRTCDataChannel
{
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
    /// is what avoids it; the check is. It is a comparison against
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Open"/> rather than a
    /// switch over the members, because the members are not all the states
    /// there are: the C enumeration has a zero its introspection data does not
    /// describe — the state a channel is in before it starts connecting — so
    /// the generated enumeration begins at
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Connecting"/> and a channel
    /// that was just created reports a number it has no name for.
    /// </para>
    /// <para>
    /// The check is a read and not a lock. A channel that closes between it and
    /// the call is a window the library leaves open and this cannot close, so
    /// an application that sends from one thread while another one tears the
    /// connection down still has to keep the two apart. What the check does
    /// remove is the case that is not a race at all: sending before the peers
    /// have agreed on the channel, which is what an application does when it
    /// sends without waiting for the open notification. The window is narrow
    /// on the closing side, because <see cref="Close"/> is graceful: the
    /// channel moves to
    /// <see cref="Gst.WebRTC.WebRTCDataChannelState.Closing"/>, the messages
    /// handed over before the call are still sent, and only then does the
    /// transport reset the stream. A channel that never reached a peer is not
    /// closed by it at all — there is no transport to run the procedure on, so
    /// it stays in the state it was created in.
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

    /// <summary>
    /// Sends a binary message over the channel, from a block the caller
    /// already holds.
    /// </summary>
    /// <param name="data">
    /// The block to send, or <see langword="null"/> for an empty message. The
    /// block is referenced rather than copied, and the caller may dispose its
    /// wrapper as soon as the call returns.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the channel was open and the message was
    /// queued.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <see cref="SendData"/> for a caller that already has a
    /// <see cref="Gst.GLib.Bytes"/> - the block an
    /// <see cref="OnMessageData"/> handler copied out, for one - and it is a
    /// member of its own rather than an overload, so that a call with an empty
    /// collection expression keeps meaning the span.
    /// </para>
    /// <para>
    /// The channel takes a reference of its own and holds it until the SCTP
    /// path has consumed the message, so what the caller owns is its wrapper
    /// and nothing else: disposing it after the call releases the caller's
    /// reference and not the message.
    /// </para>
    /// <para>
    /// <see langword="null"/> and an empty block are the same message here. An
    /// empty <c>GBytes</c> has a null data pointer, which the C refuses with a
    /// critical and a <see langword="false"/> that carries no error, so both
    /// are passed as the null block the library documents as the empty
    /// message - exactly as <see cref="SendData"/> passes an empty span.
    /// </para>
    /// <para>
    /// A channel that is not open is answered here rather than by the library,
    /// for the reason <see cref="SendData"/> states: the binary send of
    /// <c>webrtcbin</c> reads the size of the message through a transport a
    /// channel that never opened has not got.
    /// </para>
    /// </remarks>
    /// <exception cref="Gst.GLib.GException">The channel refused the message.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or the wrapper of <paramref name="data"/> was disposed.
    /// </exception>
    public bool SendBytes(Gst.GLib.Bytes? data)
    {
        // The handle is read first, so that a disposed wrapper throws before
        // anything else happens.
        nint channel = Handle;

        // See the remarks of SendData: the library crashes rather than
        // refusing here.
        if (ReadyState != Gst.WebRTC.WebRTCDataChannelState.Open)
        {
            return false;
        }

        // An empty block carries the null data pointer the C refuses, so it
        // travels as the null block that means the empty message.
        nint block = data is null || data.Size == 0 ? nint.Zero : data.Handle;

        nint errorNative = 0;
        int sent = GstWebrtcDataChannelSendDataFull(channel, block, &errorNative);

        // Both handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);
        GC.KeepAlive(data);

        Gst.GLib.GException.ThrowIfSet(ref errorNative);
        return sent != 0;
    }

    /// <summary>The <c>gst_webrtc_data_channel_send_data_full</c> entry point.</summary>
    [LibraryImport("GstWebRTC", EntryPoint = "gst_webrtc_data_channel_send_data_full")]
    private static partial int GstWebrtcDataChannelSendDataFull(nint channel, nint data, nint* error);
}
