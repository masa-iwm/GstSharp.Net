using System.Runtime.InteropServices;

namespace Gst;

public unsafe partial class Pad
{
    /// <summary>
    /// The address of <c>gst_pad_event_default</c>, resolved once: it is the
    /// handler <c>gst_pad_init</c> installs, and the one
    /// <see cref="SetEventFullFunction"/> puts back when it is unset.
    /// </summary>
    private static readonly Lazy<nint> EventDefaultAddress = new(
        static () => Resolve("gst_pad_event_default"),
        isThreadSafe: true);

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

    /// <summary>Sets the given event handler for the pad.</summary>
    /// <param name="event">
    /// The handler to install, or <see langword="null"/> to take the current
    /// one off the pad and restore GStreamer's default event handler,
    /// <c>gst_pad_event_default</c>, which is the one a pad carries until
    /// something installs its own.
    /// </param>
    /// <remarks>
    /// <para>
    /// This writes the same storage as <see cref="SetEventFunction"/>
    /// (gstpad.c:1933-1937, :1979-1984): a pad carries one of the two handlers,
    /// not both, and the later call releases the state of the earlier one.
    /// GStreamer reads this function pointer without holding a lock
    /// (gstpad.c:4590-4594), so replacing or unsetting the handler while the
    /// pad is running races an invocation that is already under way: the
    /// handler being replaced may still be executing when this returns.
    /// </para>
    /// <para>
    /// Passing <see langword="null"/> restores <c>gst_pad_event_default</c>,
    /// the handler <c>gst_pad_init</c> puts on a fresh pad (gstpad.c:422). The
    /// C call alone would not: it clears the full handler and installs its own
    /// <c>event_wrap</c> wrapper as the plain event function unconditionally
    /// (gstpad.c:1981-1982), and that wrapper dereferences the pointer the same
    /// call has just cleared, so the next event on such a pad crashes. Leaving
    /// the plain function <c>NULL</c> instead is the state GStreamer treats as
    /// a bug: a pad without an event handler answers every event with
    /// <c>GST_FLOW_NOT_SUPPORTED</c> and a warning that asks for a bug report
    /// (gstpad.c:6267-6277). This member is written by hand for that reason,
    /// and takes the pad back to the default handler instead.
    /// </para>
    /// </remarks>
    public void SetEventFullFunction(Gst.PadEventFullFunction? @event)
    {
        nint instanceHandle = Handle;
        Gst.Interop.CallbackHandle @eventState =
            Gst.Interop.InstanceKeyedCallbacks.Install(instanceHandle, "event", @event);
        GstPadSetEventFullFunctionFull(
            instanceHandle,
            @event is null ? 0 : Gst.PadEventFullFunctionTrampoline.Pointer,
            @eventState.UserData,
            @event is null ? 0 : (nint)Gst.Interop.InstanceKeyedCallbacks.DestroyNotify);

        if (@event is null)
        {
            // The call above left event_wrap on the pad over the full function
            // pointer it has just cleared. The default handler is what a pad
            // that was never given one carries, so restoring it is what
            // unsetting means here.
            GstPadSetEventFunctionFull(instanceHandle, EventDefaultAddress.Value, 0, 0);
        }

        System.GC.KeepAlive(this);
    }

    /// <summary>The <c>gst_pad_send_event</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_send_event")]
    private static partial int GstPadSendEvent(nint pad, nint @event);

    /// <summary>The <c>gst_pad_set_event_full_function_full</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_set_event_full_function_full")]
    private static partial void GstPadSetEventFullFunctionFull(nint pad, nint @event, nint userData, nint notify);

    /// <summary>Resolves an entry point of the running GStreamer by name.</summary>
    /// <param name="symbol">The C name of the entry point.</param>
    /// <returns>The address of the entry point.</returns>
    /// <exception cref="InvalidOperationException">The library does not export it.</exception>
    private static nint Resolve(string symbol)
    {
        nint module = Gst.Interop.NativeLoader.Load("Gst");

        if (!NativeLibrary.TryGetExport(module, symbol, out nint address))
        {
            throw new InvalidOperationException(
                $"The running GStreamer does not export '{symbol}', so the event handler of a pad cannot be " +
                "taken back to its default.");
        }

        return address;
    }

    /// <summary>The <c>gst_pad_push_event</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_push_event")]
    private static partial int GstPadPushEvent(nint pad, nint @event);
}
