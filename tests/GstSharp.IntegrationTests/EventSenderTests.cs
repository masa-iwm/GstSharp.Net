using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The calls that send an event: <c>gst_element_send_event</c>,
/// <c>gst_pad_send_event</c> and <c>gst_pad_push_event</c>.
/// </summary>
/// <remarks>
/// <para>
/// The binding can build every event there is and, until these three were
/// written by hand, could send none of them, because all three are
/// <c>transfer-ownership="full"</c> in their event parameter and the generator
/// emits no call that takes a wrapper over. That gap had one consequence worth
/// a test of its own: a capture pipeline could not be ended on purpose. Only an
/// end of stream that travels the whole graph makes a muxer write its trailer,
/// and tearing the pipeline down instead leaves a file that no player will
/// open.
/// </para>
/// <para>
/// Every pipeline here has a source that never runs out, so the end of stream
/// these tests wait for can only be the one that was injected.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class EventSenderTests
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public EventSenderTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The scenario the whole surface exists for: an end of stream that is sent
    /// to a running pipeline reaches the sink and is posted on the bus.
    /// </summary>
    /// <remarks>
    /// A bin forwards a downstream event to its source elements, so the event
    /// lands on the <c>videotestsrc</c>, which stops producing and pushes the
    /// end of stream down the graph. Nothing else in this pipeline can ever
    /// produce that message: <c>videotestsrc</c> is unbounded and would run
    /// until the process ends.
    /// </remarks>
    [Fact]
    public void AnEndOfStreamSentToAPipelineArrivesOnTheBus()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("videotestsrc ! fakesink sync=false"));
        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out State _, StateTimeout));
            Assert.Equal(State.Playing, state);

            // Nothing has ended anything yet, and nothing will on its own.
            Assert.Null(BusPump.WaitFor(bus, MessageType.Eos, TimeSpan.FromMilliseconds(200)));

            Event stop = Event.NewEos();
            bool handled = pipeline.SendEvent(stop);
            _output.WriteLine($"gst_element_send_event answered {handled}");

            // The source took the event, which is what a bin forwards a
            // downstream event to.
            Assert.True(handled);

            // And the wrapper is spent, whatever the answer was.
            Assert.True(stop.IsDisposed);

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, MessageTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The same end of stream sent into the sink pad of the sink rather than to
    /// the pipeline: <c>gst_pad_send_event</c> delivers it to the pad it is
    /// called on, so only the element behind that pad sees it.
    /// </summary>
    /// <remarks>
    /// This is the direction an application wants from a pad. The source keeps
    /// producing while the event is delivered, so the call waits for the stream
    /// lock of the pad, which is what serialising an end of stream with the
    /// data flow means.
    /// </remarks>
    [Fact]
    public void AnEndOfStreamSentIntoASinkPadEndsTheSink()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("videotestsrc ! fakesink name=sink sync=false"));
        Bus bus = pipeline.GetBus();
        using Element? sink = pipeline.GetByName("sink");

        Assert.NotNull(sink);
        using Pad? pad = sink.GetStaticPad("sink");
        Assert.NotNull(pad);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out State _, StateTimeout));
            Assert.Equal(State.Playing, state);

            Event stop = Event.NewEos();
            bool handled = pad.SendEvent(stop);
            _output.WriteLine($"gst_pad_send_event answered {handled}");

            Assert.True(handled);
            Assert.True(stop.IsDisposed);

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, MessageTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);

            // A bin collects the end of stream of every sink below it and
            // posts one of its own once they have all reported, so the message
            // that reaches the application comes from the pipeline. Src is not
            // disposed here: it is the very wrapper this test holds.
            Assert.Equal(pipeline.Handle, Assert.IsAssignableFrom<Gst.Object>(message.Src).Handle);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// <c>gst_pad_push_event</c> is the other direction: the event leaves the
    /// pad it is called on and goes to the pad that is linked to it. Pushed out
    /// of the source pad of the source, it arrives at the same sink.
    /// </summary>
    [Fact]
    public void AnEndOfStreamPushedOutOfASourcePadReachesThePeer()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("videotestsrc name=source ! fakesink name=sink sync=false"));
        Bus bus = pipeline.GetBus();
        using Element? source = pipeline.GetByName("source");

        Assert.NotNull(source);
        using Pad? pad = source.GetStaticPad("src");
        Assert.NotNull(pad);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State state, out State _, StateTimeout));
            Assert.Equal(State.Playing, state);

            Event stop = Event.NewEos();
            bool handled = pad.PushEvent(stop);
            _output.WriteLine($"gst_pad_push_event answered {handled}");

            Assert.True(handled);
            Assert.True(stop.IsDisposed);

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, MessageTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);

            // A bin collects the end of stream of every sink below it and
            // posts one of its own once they have all reported, so the message
            // that reaches the application comes from the pipeline. Src is not
            // disposed here: it is the very wrapper this test holds.
            Assert.Equal(pipeline.Handle, Assert.IsAssignableFrom<Gst.Object>(message.Src).Handle);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// All three calls are <c>transfer-ownership="full"</c>: the event belongs
    /// to the library afterwards and the wrapper of the caller is left owning
    /// nothing, whatever the call answered.
    /// </summary>
    /// <remarks>
    /// This is the shape <c>AppSrcTransferTests</c> pins for
    /// <c>gst_app_src_push_buffer</c>, with one difference. The reference count
    /// of the event cannot be read afterwards: nothing else holds these events,
    /// so the release the call performs is the last one and the memory is gone.
    /// What is left to observe is the wrapper, which is exactly what the caller
    /// has to know about.
    /// </remarks>
    [Fact]
    public void EverySenderConsumesTheEventItIsGiven()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "consuming"));
        using Pad? pad = element.GetStaticPad("sink");

        Assert.NotNull(pad);

        Event sentToElement = Event.NewEos();
        element.SendEvent(sentToElement);

        // What the caller had is gone: the call consumed the reference of the
        // wrapper, so the wrapper is disposed and its handle is unreachable.
        // This is the whole pattern - construct, send, forget; never dispose an
        // event that was sent, and never touch it again.
        Assert.True(sentToElement.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => sentToElement.Handle);

        // Disposing twice does nothing, so a `using` around an event that ends
        // up being sent stays correct.
        sentToElement.Dispose();

        Event sentToPad = Event.NewEos();
        pad.SendEvent(sentToPad);

        Assert.True(sentToPad.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => sentToPad.Handle);

        // Pushing leaves the pad for its peer, so the event has to travel the
        // way the pad points: out of a sink pad is upstream, and an end of
        // stream pushed there would be refused for its direction rather than
        // for the state of the pad.
        Event pushedFromPad = Event.NewReconfigure();
        pad.PushEvent(pushedFromPad);

        Assert.True(pushedFromPad.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => pushedFromPad.Handle);
    }

    /// <summary>
    /// A sender rejects a null event and leaves a disposed one to the wrapper
    /// that owns it, which is what the guards of the hand written calls are
    /// for.
    /// </summary>
    [Fact]
    public void ASenderRefusesWhatItCannotSend()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "guarded"));

        Assert.Throws<ArgumentNullException>(() => element.SendEvent(null!));

        Event spent = Event.NewEos();
        spent.Dispose();

        Assert.Throws<ObjectDisposedException>(() => element.SendEvent(spent));
    }
}
