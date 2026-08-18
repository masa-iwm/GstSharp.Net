using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The factory of a custom event, the one <c>gst_event_new_*</c> whose payload
/// the call takes over.
/// </content>
/// <remarks>
/// <para>
/// Every other event constructor borrows what it is given and the generator
/// emits it without help. <c>gst_event_new_custom</c> is different: its
/// <c>structure</c> parameter is <c>transfer-ownership="full"</c>, and the
/// generator emits no call that takes a wrapper over, because handing the only
/// value of a wrapper to the library would let both of them release it. See
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#calls-that-consume-their-argument">Calls that consume their argument</see>.
/// </para>
/// <para>
/// Without it the custom event types — <see cref="EventType.CustomUpstream"/>
/// and its five neighbours — have no constructor at all, which is the half of
/// the event surface that an application defines for itself.
/// </para>
/// </remarks>
public sealed partial class Event
{
    /// <summary>
    /// Creates an event of one of the custom types, carrying a payload of the
    /// application's own and taking that payload over.
    /// </summary>
    /// <param name="type">
    /// What the event is. This is normally one of
    /// <see cref="EventType.CustomUpstream"/>,
    /// <see cref="EventType.CustomDownstream"/>,
    /// <see cref="EventType.CustomDownstreamOob"/>,
    /// <see cref="EventType.CustomDownstreamSticky"/>,
    /// <see cref="EventType.CustomBoth"/> or
    /// <see cref="EventType.CustomBothOob"/>, whose direction and
    /// serialisation the name states; a type built with the
    /// <c>GST_EVENT_MAKE_TYPE</c> macro of C works here as well.
    /// </param>
    /// <param name="structure">
    /// The payload, whose name is what the receiver dispatches on. The call
    /// consumes it: <paramref name="structure"/> is disposed when this method
    /// returns, and using it afterwards throws
    /// <see cref="ObjectDisposedException"/>.
    /// </param>
    /// <returns>The event, which the caller owns and has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_event_new_custom</c>. It is what an application or a
    /// plugin uses to say something the event types of GStreamer do not cover,
    /// and the event travels the pipeline in the direction its type names.
    /// <see cref="Element.SendEvent"/>, <see cref="Pad.SendEvent"/> and
    /// <see cref="Pad.PushEvent"/> are the calls that send it, and they consume
    /// the event the same way this one consumes the payload.
    /// </para>
    /// <code>
    /// using Structure structure = Structure.NewEmpty("GstSharpMark");
    /// Event mark = Event.NewCustom(EventType.CustomDownstream, structure);
    ///
    /// pipeline.SendEvent(mark);   // consumes the event
    /// </code>
    /// <para>
    /// The <c>structure</c> parameter is <c>transfer-ownership="full"</c>, so
    /// this is written by hand along the lines of
    /// <see cref="Message.NewApplication"/>: the call is handed a value of its
    /// own and the wrapper is disposed afterwards, which leaves the caller with
    /// exactly what the C call leaves it with. A
    /// <see cref="Gst.GObject.Boxed"/> value has no reference count to raise, so
    /// the copy that <c>g_boxed_copy</c> makes is what a reference is there.
    /// <see cref="Gst.GObject.Boxed.Dispose()"/> is idempotent, so a
    /// <c>using</c> declaration around the payload stays correct.
    /// </para>
    /// <para>
    /// The payload is required. The gir annotates it non-nullable, and an event
    /// of a custom type with nothing in it says nothing;
    /// <see cref="Structure.NewEmpty(string)"/> is the payload that carries a
    /// name and no fields.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="structure"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// <paramref name="structure"/> was disposed.
    /// </exception>
    public static Gst.Event NewCustom(Gst.EventType type, Gst.Structure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        // The handle is read before anything is copied, so that a disposed
        // wrapper throws before a copy exists that nobody would free.
        nint owned = structure.Handle;
        nuint boxedType = structure.BoxedType.Value;

        // The value the call consumes.
        nint copy = GObjectNative.BoxedCopy(boxedType, owned);

        nint @event = GstEventNewCustom((int)type, copy);

        // And the structure of the wrapper goes away with the wrapper, which is
        // what makes this call consuming rather than borrowing. The dispose is
        // also what keeps the wrapper alive across the call above: it is the
        // last use of it, and the handle was read before the call.
        structure.Dispose();

        return Gst.Event.FromNative(@event, Transfer.Full)
            ?? throw new InvalidOperationException("gst_event_new_custom returned no event.");
    }

    /// <summary>The <c>gst_event_new_custom</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_event_new_custom")]
    private static partial nint GstEventNewCustom(int type, nint structure);
}
