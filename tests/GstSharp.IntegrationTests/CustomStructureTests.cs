using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The four hand written calls that take a <see cref="Structure"/> over:
/// <see cref="Event.NewCustom"/>, <see cref="Message.NewCustom"/>,
/// <see cref="Query.NewCustom"/> and <see cref="Promise.Reply"/>.
/// </summary>
/// <remarks>
/// <para>
/// All four are <c>transfer-ownership="full"</c> in their structure parameter,
/// which is a shape the generator refuses, so each of them is written by hand
/// the way <c>Message.NewApplication</c> is: the call is handed a copy of the
/// structure and the wrapper is disposed afterwards. Two claims follow, and
/// both are made here for every one of them — the payload arrives intact on the
/// other side, and the wrapper the caller passed owns nothing once the call
/// returns.
/// </para>
/// <para>
/// The payload round trip is what a missing <c>g_boxed_copy</c> would break,
/// and it would break it as a double free rather than as a leak: the wrapper
/// and the object the payload went to would both release the one structure.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class CustomStructureTests
{
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public CustomStructureTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A custom event is built with the type it was asked for and hands the
    /// payload back.
    /// </summary>
    [Fact]
    public void ACustomEventCarriesItsTypeAndItsPayload()
    {
        using Event @event = Event.NewCustom(EventType.CustomDownstream, Payload("GstSharpMark", "left"));

        Assert.Equal(EventType.CustomDownstream, @event.Type);

        using Structure? structure = @event.GetStructure();

        Assert.NotNull(structure);
        Assert.True(structure.HasName("GstSharpMark"));
        Assert.Equal("left", structure.GetString("where"));

        _output.WriteLine(structure.ToString());
    }

    /// <summary>
    /// The payload went to the event, so the wrapper the caller passed is spent
    /// when the call returns.
    /// </summary>
    [Fact]
    public void ACustomEventConsumesItsPayload()
    {
        Structure structure = Structure.NewEmpty("GstSharpMark");
        using Event @event = Event.NewCustom(EventType.CustomDownstream, structure);

        Assert.True(structure.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => structure.HasName("GstSharpMark"));

        // Disposing twice does nothing, so a `using` around a payload that ends
        // up being handed over stays correct.
        structure.Dispose();
    }

    /// <summary>
    /// <c>gst_event_new_custom</c> annotates its structure non-nullable, which
    /// is the one of the four that requires a payload, and a payload that was
    /// already spent is refused before anything is copied.
    /// </summary>
    [Fact]
    public void ACustomEventRefusesAPayloadItCannotTake()
    {
        Assert.Throws<ArgumentNullException>(
            () => Event.NewCustom(EventType.CustomDownstream, null!));

        Structure spent = Structure.NewEmpty("GstSharpMark");
        spent.Dispose();

        Assert.Throws<ObjectDisposedException>(
            () => Event.NewCustom(EventType.CustomDownstream, spent));
    }

    /// <summary>
    /// The event is an event GStreamer accepts, and not merely a wrapper around
    /// a pointer: stored on the sink pad of a running pipeline as a sticky
    /// event, it is read back with its payload.
    /// </summary>
    /// <remarks>
    /// A pad only stores sticky events while it is active, so the pipeline has
    /// to be playing for this to say anything. <c>gst_pad_store_sticky_event</c>
    /// is <c>transfer-ownership="none"</c> and borrows the event, which is the
    /// contrast worth seeing next to the constructor above: the event is
    /// disposed by this test, the payload was not.
    /// </remarks>
    [Fact]
    public void ACustomStickyEventSurvivesARoundTripThroughAPad()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("fakesrc ! fakesink name=sink sync=false"));
        using Element? sink = pipeline.GetByName("sink");

        Assert.NotNull(sink);

        using Pad? pad = sink.GetStaticPad("sink");

        Assert.NotNull(pad);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out State _, StateTimeout));
            Assert.Equal(State.Playing, state);

            using (Event sticky = Event.NewCustom(
                EventType.CustomDownstreamSticky,
                Payload("GstSharpSticky", "here")))
            {
                Assert.Equal(FlowReturn.Ok, pad.StoreStickyEvent(sticky));
            }

            using Event? stored = pad.GetStickyEvent(EventType.CustomDownstreamSticky, 0);

            Assert.NotNull(stored);

            using Structure? structure = stored.GetStructure();

            Assert.NotNull(structure);
            Assert.True(structure.HasName("GstSharpSticky"));
            Assert.Equal("here", structure.GetString("where"));
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A custom message carries the type, the source and the payload it was
    /// built with, and spends the payload wrapper.
    /// </summary>
    [Fact]
    public void ACustomMessageCarriesItsTypeSourceAndPayload()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("custom-message"));

        Structure payload = Payload("GstSharpElementMessage", "sink");
        using Message message = Message.NewCustom(MessageType.Element, pipeline, payload);

        Assert.True(payload.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => payload.HasName("GstSharpElementMessage"));

        Assert.Equal(MessageType.Element, message.Type);
        Assert.Equal("custom-message", message.SourceName);
        Assert.Equal(pipeline.Handle, Assert.IsAssignableFrom<Gst.Object>(message.Src).Handle);

        using Structure? structure = message.GetStructure();

        Assert.NotNull(structure);
        Assert.True(structure.HasName("GstSharpElementMessage"));
        Assert.Equal("sink", structure.GetString("where"));
    }

    /// <summary>
    /// Both handles of a custom message are nullable in the gir, and a message
    /// with neither a source nor a payload is a message all the same.
    /// </summary>
    [Fact]
    public void ACustomMessageWithoutASourceOrAPayloadIsAllowed()
    {
        using Message message = Message.NewCustom(MessageType.Element, null, null);

        Assert.Equal(MessageType.Element, message.Type);
        Assert.Null(message.Src);
        Assert.Null(message.SourceName);
        Assert.Null(message.GetStructure());
    }

    /// <summary>
    /// A custom message goes through the bus like any other, which is what the
    /// type it was built with is for.
    /// </summary>
    [Fact]
    public void ACustomMessageReachesTheBus()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("custom-message-bus"));
        using Bus bus = pipeline.GetBus();

        using (Message message = Message.NewCustom(
            MessageType.Element,
            pipeline,
            Payload("GstSharpElementMessage", "bus")))
        {
            Assert.True(pipeline.PostMessage(message));
        }

        using Message? posted = BusPump.WaitFor(bus, MessageType.Element, TimeSpan.FromSeconds(15));

        Assert.NotNull(posted);

        using Structure? structure = posted.GetStructure();

        Assert.Equal("bus", structure?.GetString("where"));
    }

    /// <summary>
    /// A custom query carries the type and the payload it was built with, and
    /// spends the payload wrapper.
    /// </summary>
    [Fact]
    public void ACustomQueryCarriesItsTypeAndItsPayload()
    {
        Structure payload = Payload("GstSharpAsk", "position");
        using Query query = Query.NewCustom(QueryType.Custom, payload);

        Assert.True(payload.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => payload.HasName("GstSharpAsk"));

        Assert.Equal(QueryType.Custom, query.Type);

        using Structure? structure = query.GetStructure();

        Assert.NotNull(structure);
        Assert.True(structure.HasName("GstSharpAsk"));
        Assert.Equal("position", structure.GetString("where"));
    }

    /// <summary>
    /// The payload of a custom query is nullable in the gir, and a query
    /// without one carries no structure.
    /// </summary>
    [Fact]
    public void ACustomQueryWithoutAPayloadIsAllowed()
    {
        using Query query = Query.NewCustom(QueryType.Custom, null);

        Assert.Equal(QueryType.Custom, query.Type);
        Assert.Null(query.GetStructure());
    }

    /// <summary>
    /// A promise that is answered moves out of
    /// <see cref="PromiseResult.Pending"/> and hands the reply back, and the
    /// reply wrapper is spent by the call.
    /// </summary>
    [Fact]
    public void APromiseIsAnsweredWithTheReplyItIsGiven()
    {
        using Promise promise = Promise.New();

        Structure reply = Payload("GstSharpAnswer", "done");
        promise.Reply(reply);

        Assert.True(reply.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => reply.HasName("GstSharpAnswer"));

        Assert.Equal(PromiseResult.Replied, promise.Wait());

        using Structure? answer = promise.GetReply();

        Assert.NotNull(answer);
        Assert.True(answer.HasName("GstSharpAnswer"));
        Assert.Equal("done", answer.GetString("where"));
    }

    /// <summary>
    /// The reply is nullable in the gir: a promise answered with nothing is
    /// still answered, and there is nothing to consume.
    /// </summary>
    [Fact]
    public void APromiseCanBeAnsweredWithNoReply()
    {
        using Promise promise = Promise.New();

        promise.Reply(null);

        Assert.Equal(PromiseResult.Replied, promise.Wait());
        Assert.Null(promise.GetReply());
    }

    /// <summary>
    /// Builds a named structure with one string field in it.
    /// </summary>
    /// <param name="name">The name the receiver dispatches on.</param>
    /// <param name="where">The value of the <c>where</c> field.</param>
    /// <returns>The structure, which the caller hands over.</returns>
    private static Structure Payload(string name, string @where)
    {
        Structure structure = Structure.NewEmpty(name);

        using (Value value = Value.New(GType.String))
        {
            value.SetString(@where);
            structure.SetValue("where", value);
        }

        return structure;
    }
}
