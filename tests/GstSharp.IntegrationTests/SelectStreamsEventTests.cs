using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The select-streams event: <c>gst_event_new_select_streams</c> and
/// <c>gst_event_parse_select_streams</c>.
/// </summary>
/// <remarks>
/// <para>
/// Both are written by hand. Their streams are a <c>GList</c> of stream-ids,
/// and a <c>GList</c> is bound in the return position only, so the generator
/// emits neither of them. That left the stream selection of <c>playbin3</c>
/// half bound: a <see cref="StreamCollection"/> could be read, every
/// <see cref="Gst.Stream"/> in it inspected, and none of them chosen.
/// </para>
/// <para>
/// What the tests pin is the ownership of that list, which is different on each
/// side. Going in it is <c>transfer-ownership="none"</c>: the binding builds the
/// list, the C call copies every id into the payload of the event, and the
/// binding frees the list and the strings again. Coming out it is
/// <c>transfer-ownership="full"</c>: the C call hands over the nodes and the
/// strings, and the binding releases both before it answers. Neither shape
/// leaves anything for the caller to free, and the loop at the end of this file
/// is what would fall over if either of them freed something twice.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SelectStreamsEventTests
{
    /// <summary>
    /// The whole point of the pair: the ids that went into the event come back
    /// out of it, as many, in order, and unchanged.
    /// </summary>
    [Fact]
    public void TheStreamIdsOfASelectStreamsEventSurviveTheRoundtrip()
    {
        string[] wanted = ["video-abc", "audio-def", "text-ghi"];

        using Event @event = Event.NewSelectStreams(wanted);

        Assert.Equal(EventType.SelectStreams, @event.Type);

        @event.ParseSelectStreams(out string[] parsed);

        Assert.Equal(wanted, parsed);
    }

    /// <summary>
    /// A selection of a single stream, which is what switching one track of a
    /// file that has one of each looks like.
    /// </summary>
    [Fact]
    public void ASelectionOfASingleStreamRoundtripsAsWell()
    {
        string[] wanted = ["audio-only"];

        using Event @event = Event.NewSelectStreams(wanted);

        Assert.Equal(EventType.SelectStreams, @event.Type);

        @event.ParseSelectStreams(out string[] parsed);

        Assert.Equal(wanted, parsed);
    }

    /// <summary>
    /// An empty selection is refused before it reaches the library, because the
    /// library refuses it too and says so by answering nothing.
    /// </summary>
    /// <remarks>
    /// <c>gst_event_new_select_streams</c> guards its parameter with
    /// <c>g_return_val_if_fail (streams != NULL, NULL)</c>, and an empty
    /// <c>GList</c> is exactly <c>NULL</c>. Passing the empty list on would
    /// print a critical and hand back no event at all, so the argument is
    /// rejected where the caller can see which argument it was.
    /// </remarks>
    [Fact]
    public void AnEmptySelectionIsRefused()
    {
        ArgumentException empty = Assert.Throws<ArgumentException>(
            () => Event.NewSelectStreams([]));

        Assert.Equal("streamIds", empty.ParamName);

        // Any empty sequence, not only an empty collection expression: the
        // sequence is materialized before it is counted.
        Assert.Throws<ArgumentException>(
            () => Event.NewSelectStreams(Enumerable.Empty<string>()));
    }

    /// <summary>
    /// Nothing about the selection may be null: neither the sequence nor any id
    /// in it, and no id may carry a null character either.
    /// </summary>
    [Fact]
    public void ASelectionThatIsNullOrContainsNullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => Event.NewSelectStreams(null!));

        ArgumentException nullElement = Assert.Throws<ArgumentException>(
            () => Event.NewSelectStreams(["video-abc", null!, "text-ghi"]));

        Assert.Equal("streamIds", nullElement.ParamName);

        // A null character would truncate the id in native code, which is the
        // one rule every string of this binding is held to.
        Assert.Throws<ArgumentException>(
            () => Event.NewSelectStreams(["video-abc", "audio\0def"]));
    }

    /// <summary>
    /// Parsing an event that is not a select-streams event throws rather than
    /// answering nothing.
    /// </summary>
    /// <remarks>
    /// <c>gst_event_parse_select_streams</c> guards with
    /// <c>g_return_if_fail (GST_EVENT_TYPE (event) == GST_EVENT_SELECT_STREAMS)</c>
    /// and leaves the out parameter untouched, so the C call is a silent no-op
    /// for every other type. The binding compares the type itself and names the
    /// one it found.
    /// </remarks>
    [Fact]
    public void ParsingAnEventOfAnotherTypeIsRefused()
    {
        using Event eos = Event.NewEos();

        InvalidOperationException fromEos = Assert.Throws<InvalidOperationException>(
            () => eos.ParseSelectStreams(out string[] _));

        Assert.Contains("Eos", fromEos.Message, StringComparison.Ordinal);

        using Structure payload = Structure.NewEmpty("GstSharpNotASelection");
        using Event custom = Event.NewCustom(EventType.CustomDownstream, payload);

        Assert.Throws<InvalidOperationException>(() => custom.ParseSelectStreams(out string[] _));
    }

    /// <summary>
    /// A disposed event is refused by the type check, which reads the handle
    /// before anything is parsed.
    /// </summary>
    [Fact]
    public void ParsingADisposedEventIsRefused()
    {
        Event spent = Event.NewSelectStreams(["video-abc"]);
        spent.Dispose();

        Assert.Throws<ObjectDisposedException>(() => spent.ParseSelectStreams(out string[] _));
    }

    /// <summary>
    /// A thousand roundtrips in a row, which is the cheap way to find out
    /// whether either side frees something it does not own.
    /// </summary>
    /// <remarks>
    /// This is not a leak detector — it is the shape a double free shows up in.
    /// Every iteration allocates a native list of three strings, hands it to the
    /// library, frees the list and the strings again, parses a second list back
    /// out and frees that one too, so a pointer that is released twice or read
    /// after it was released takes the process down long before the loop ends.
    /// </remarks>
    [Fact]
    public void AThousandRoundtripsLeaveNothingBehind()
    {
        string[] wanted = ["video-abc", "audio-def", "text-ghi"];

        for (int i = 0; i < 1000; i++)
        {
            using Event @event = Event.NewSelectStreams(wanted);

            @event.ParseSelectStreams(out string[] parsed);

            Assert.Equal(wanted, parsed);
        }
    }
}
