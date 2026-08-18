using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The two corners of the event surface the generator cannot emit: the factory
/// of a custom event, whose payload the call takes over, and the select-streams
/// event, whose streams are a <c>GList</c>.
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
/// <para>
/// The select-streams pair is a gap of the other kind. Its streams are a
/// <c>GList</c> of stream-ids, and a <c>GList</c> is bound in the return
/// position only, so the generator emits neither
/// <c>gst_event_new_select_streams</c> nor
/// <c>gst_event_parse_select_streams</c>. Without them the read side of
/// <c>playbin3</c> is complete and useless: a
/// <see cref="StreamCollection"/> can be inspected and nothing can be chosen
/// out of it. The list lives for the length of one call in either direction,
/// which is what keeps it out of the generator and inside this file.
/// </para>
/// </remarks>
public sealed unsafe partial class Event
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

    /// <summary>
    /// Creates a select-streams event, which asks the pipeline to activate
    /// exactly the streams whose ids are named.
    /// </summary>
    /// <param name="streamIds">
    /// The stream-id of every stream that is to play, and of no other. The call
    /// borrows them: the strings are copied into the event, and the caller keeps
    /// whatever it passed. The selection may not be empty.
    /// </param>
    /// <returns>The event, which the caller owns and has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_event_new_select_streams</c>, the write side of the stream
    /// selection of <c>playbin3</c> and of <c>decodebin3</c>. The ids are the
    /// ones the collection announced:
    /// <see cref="StreamCollection.GetStream(uint)"/> hands out a
    /// <see cref="Stream"/> and <see cref="Stream.GetStreamId()"/> its id. The
    /// event is then sent to the pipeline with
    /// <see cref="Element.SendEvent(Event)"/>, which consumes it, so the event
    /// is constructed, sent and forgotten rather than disposed.
    /// </para>
    /// <code>
    /// using StreamCollection collection = ...;
    /// List&lt;string&gt; wanted = [];
    ///
    /// for (uint i = 0; i &lt; collection.GetSize(); i++)
    /// {
    ///     using Stream? stream = collection.GetStream(i);
    ///     if (stream?.GetStreamId() is { } id &amp;&amp; (stream.GetStreamType() &amp; StreamType.Audio) != 0)
    ///     {
    ///         wanted.Add(id);
    ///     }
    /// }
    ///
    /// pipeline.SendEvent(Event.NewSelectStreams(wanted));   // consumes the event
    /// </code>
    /// <para>
    /// The <c>streams</c> parameter is a <c>GList</c> of strings, which the
    /// generator does not emit in a parameter position, so the list is built
    /// here for the length of the call and released afterwards. It is
    /// <c>transfer-ownership="none"</c> and the C call means it: every id is
    /// copied into a value of the payload structure while the call runs, so
    /// nothing that is allocated here outlives it.
    /// </para>
    /// <para>
    /// An empty selection is refused rather than passed on. The C call answers
    /// <c>NULL</c> for an empty list — <c>NULL</c> is how C spells one — and a
    /// call that quietly does nothing is not a shape this binding hands out. A
    /// pipeline that is to play nothing is asked for that by its state, not by
    /// an event that selects no stream.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="streamIds"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="streamIds"/> is empty, or one of its elements is
    /// <see langword="null"/> or contains a null character.
    /// </exception>
    public static Gst.Event NewSelectStreams(IEnumerable<string> streamIds)
    {
        ArgumentNullException.ThrowIfNull(streamIds);

        // The sequence is walked exactly once, before anything native is
        // allocated, so that an argument that throws or that answers differently
        // on a second pass cannot do so with a list already half built.
        string[] ids = [.. streamIds];

        if (ids.Length == 0)
        {
            throw new ArgumentException(
                "A select-streams event has to name at least one stream: " +
                "gst_event_new_select_streams refuses an empty list.",
                nameof(streamIds));
        }

        for (int i = 0; i < ids.Length; i++)
        {
            if (ids[i] is null)
            {
                throw new ArgumentException(
                    $"The stream id at index {i} is null. Every element of a " +
                    "select-streams event is the id of a stream to activate.",
                    nameof(streamIds));
            }
        }

        nint streams = GListMarshal.BuildStringList(ids);

        try
        {
            nint @event = GstEventNewSelectStreams(streams);

            return Gst.Event.FromNative(@event, Transfer.Full)
                ?? throw new InvalidOperationException("gst_event_new_select_streams returned no event.");
        }
        finally
        {
            // The call copied what it keeps, so the list and every string in it
            // are ours to release, whether the call answered or threw.
            GListMarshal.FreeStringList(streams);
        }
    }

    /// <summary>
    /// Reads the stream-ids out of a select-streams event.
    /// </summary>
    /// <param name="streamIds">
    /// The id of every stream the event asks for, in the order the event names
    /// them. These are managed strings: the native list and the native strings
    /// are released here, and there is nothing left for the caller to free.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_event_parse_select_streams</c>, the read side of what
    /// <see cref="NewSelectStreams(IEnumerable{string})"/> builds. An element
    /// that handles stream selection itself reads the event this way; an
    /// application mostly sees it in a pad probe or in a
    /// <see cref="Pad"/> event handler, on its way upstream to
    /// <c>decodebin3</c>.
    /// </para>
    /// <para>
    /// The <c>streams</c> parameter is <c>transfer-ownership="full"</c> over a
    /// <c>GList</c> of strings, so the caller of the C function owns the nodes
    /// and the strings both. Both are released before this returns, which is why
    /// the parameter is an array of managed strings and not a wrapper of
    /// anything.
    /// </para>
    /// <para>
    /// The type is checked here because the C call does not. It guards with
    /// <c>g_return_if_fail</c> on the event type and leaves the out parameter
    /// untouched, so parsing the wrong event would answer whatever the caller
    /// happened to have. That is the silent no-op this binding turns into an
    /// exception.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This event is not a select-streams event.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// This wrapper was disposed.
    /// </exception>
    public void ParseSelectStreams(out string[] streamIds)
    {
        // Reading the type also reads the handle, so a disposed wrapper throws
        // before anything is parsed.
        Gst.EventType type = Type;

        if (type != Gst.EventType.SelectStreams)
        {
            throw new InvalidOperationException(
                $"This is a {type} event, and only a {Gst.EventType.SelectStreams} event carries " +
                "stream ids. gst_event_parse_select_streams answers nothing at all for any other type.");
        }

        nint streams = default;
        GstEventParseSelectStreams(Handle, &streams);
        System.GC.KeepAlive(this);

        // Ours: the spine goes first, then every string, and what is left is
        // managed.
        nint[] items = GListMarshal.CollectAndFreeSpine(streams);
        string[] ids = new string[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            ids[i] = GMarshal.PtrToStringUtf8AndFree(items[i]) ?? string.Empty;
        }

        streamIds = ids;
    }

    /// <summary>The <c>gst_event_new_custom</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_event_new_custom")]
    private static partial nint GstEventNewCustom(int type, nint structure);

    /// <summary>The <c>gst_event_new_select_streams</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_event_new_select_streams")]
    private static partial nint GstEventNewSelectStreams(nint streams);

    /// <summary>The <c>gst_event_parse_select_streams</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_event_parse_select_streams")]
    private static partial void GstEventParseSelectStreams(nint @event, nint* streams);
}
