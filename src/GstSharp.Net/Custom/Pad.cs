using System.Runtime.InteropServices;

namespace Gst;

public unsafe partial class Pad
{
    /// <summary>
    /// Sends an event into this pad, taking the event over.
    /// </summary>
    /// <param name="event">
    /// The event to send. The call consumes it: <paramref name="event"/> is
    /// disposed when this method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the event was handled, which for a pad that
    /// refuses the event or has no peer to pass it on to is
    /// <see langword="false"/>. The event is consumed in either case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_pad_send_event</c>, whose
    /// <c>event</c> parameter is <c>transfer-ownership="full"</c>. The
    /// generator does not emit calls that take a wrapper over, because handing
    /// the only reference of a wrapper to the library would let both of them
    /// release it, so this method is written by hand: it hands the call a
    /// reference of its own and then disposes the wrapper, which leaves the
    /// native reference count exactly where the C call leaves it.
    /// </para>
    /// <para>
    /// The consuming shape is the one the C API has and the one applications
    /// expect, and it keeps the ownership rule of the binding intact: after
    /// this call the wrapper owns nothing, which is precisely what its disposed
    /// state means. <see cref="Gst.MiniObject.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the event stays correct.
    /// </para>
    /// <para>
    /// This call sends the event <em>into</em> the pad, which is the direction
    /// an application wants: the pad handles it and the element behind it acts
    /// on it. <see cref="PushEvent"/> is the other direction, out of the pad
    /// and into its peer, and it is what an element uses on its own pads. Which
    /// of the two fits follows from the event: a sink pad takes downstream
    /// events, so an end of stream or a segment is sent to one of those, and a
    /// source pad takes upstream events, so a seek or a reconfigure is sent to
    /// one of those. An event that travels the wrong way is refused rather than
    /// delivered.
    /// </para>
    /// <para>
    /// An event that has to be serialized with the data flow, an end of stream
    /// among them, is delivered under the stream lock of the pad, so the call
    /// blocks until the buffer that is being processed is through. An
    /// out-of-band event such as a flush is not serialized and overtakes the
    /// data that is queued.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="event"/> was disposed.
    /// </exception>
    public bool SendEvent(Gst.Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint pad = Handle;
        nint owned = @event.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        GstNative.MiniObjectRef(owned);

        int result = GstPadSendEvent(pad, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing.
        @event.Dispose();

        return result != 0;
    }

    /// <summary>
    /// Sends an event out of this pad and into its peer, taking the event over.
    /// </summary>
    /// <param name="event">
    /// The event to push. The call consumes it: <paramref name="event"/> is
    /// disposed when this method returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the event was handled. A pad with no peer,
    /// or a peer that refuses the event, answers <see langword="false"/>. The
    /// event is consumed in either case.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the managed face of <c>gst_pad_push_event</c>, whose
    /// <c>event</c> parameter is <c>transfer-ownership="full"</c>, and it is
    /// written by hand for the reason <see cref="SendEvent"/> is: it hands the
    /// call a reference of its own and then disposes the wrapper, which leaves
    /// the native reference count exactly where the C call leaves it. Every
    /// remark of that method about the consumed wrapper applies here unchanged.
    /// </para>
    /// <para>
    /// Pushing is what an element does with its own pads, which is why it is
    /// the rarer of the two from an application: it takes the event out of this
    /// pad and hands it to the pad it is linked to, so it needs a pad the
    /// application owns or one it may act for. Sending, the other direction, is
    /// the call that drives an element from the outside.
    /// </para>
    /// <para>
    /// A sticky event that is pushed downstream is remembered on the pad and is
    /// replayed to every peer that is linked to it later, which is how caps and
    /// segments survive a relink.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="event"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper or <paramref name="event"/> was disposed.
    /// </exception>
    public bool PushEvent(Gst.Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        // Both handles are read before anything is referenced, so that a
        // disposed wrapper throws without leaking the reference of the other.
        nint pad = Handle;
        nint owned = @event.Handle;

        // The reference the call consumes. Without it the wrapper and the
        // library would both own the one reference the wrapper holds.
        GstNative.MiniObjectRef(owned);

        int result = GstPadPushEvent(pad, owned);

        // The handles were read before the call, so nothing keeps either
        // wrapper alive across it on its own.
        GC.KeepAlive(this);

        // And the reference of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing.
        @event.Dispose();

        return result != 0;
    }

    /// <summary>The <c>gst_pad_send_event</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_send_event")]
    private static partial int GstPadSendEvent(nint pad, nint @event);

    /// <summary>The <c>gst_pad_push_event</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_push_event")]
    private static partial int GstPadPushEvent(nint pad, nint @event);
}
