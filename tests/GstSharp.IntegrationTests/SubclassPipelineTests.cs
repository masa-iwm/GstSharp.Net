using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Managed elements doing the job they exist for: a source that produces
/// buffers, a filter that rewrites them and a sink that consumes them, driven
/// by a real pipeline on real streaming threads.
/// </summary>
/// <remarks>
/// Nothing here is version dependent: the class struct offsets the runtime
/// writes into have been frozen ABI since well before the 1.24 floor.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SubclassPipelineTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(10);

    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassPipelineTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A managed source feeds a managed sink: every buffer the
    /// <c>create</c> override produced reaches the <c>render</c> override, in
    /// order, and the <see cref="FlowReturn.Eos"/> that ends the production
    /// becomes an end of stream message on the bus.
    /// </summary>
    [Fact]
    public void AManagedSourceFeedsAManagedSinkUntilTheStreamEnds()
    {
        const int Buffers = 5;

        using Pipeline pipeline = Pipeline.New("managed-source-to-sink");
        ProbeSrc source = new() { BufferCount = Buffers };
        ProbeSink sink = new();

        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine(FormattableString.Invariant(
                $"bus: {message.Type}, produced={source.Produced}, rendered={sink.Rendered.Count}"));

            Assert.Equal(MessageType.Eos, message.Type);
            Assert.Equal(Buffers, source.Produced);
            Assert.Equal(Buffers, sink.Rendered.Count);

            // The bytes count up, which is what says that the buffers arrived
            // whole and in order rather than merely in the right number.
            Assert.Equal<byte>([0, 1, 2, 3, 4], sink.Rendered);

            // The first buffer prerolls before the pipeline plays, and is then
            // rendered as well, so it is seen twice by two different slots.
            Assert.Equal(1, sink.Prerolled);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The class initialiser really described the class: the metadata it set is
    /// what the element reports, and the pad templates it added are the ones
    /// the element's pads were created from.
    /// </summary>
    [Fact]
    public void TheClassInitialiserDescribedTheClass()
    {
        using ProbeSrc source = new();

        _output.WriteLine($"metadata: {source.GetMetadata("long-name")} / {source.GetMetadata("klass")}");

        Assert.Equal("GstSharp probe source", source.GetMetadata("long-name"));
        Assert.Equal("Source/Testing", source.GetMetadata("klass"));
        Assert.Equal("Produces a fixed number of counting buffers", source.GetMetadata("description"));
        Assert.Equal("GstSharp.Net integration tests", source.GetMetadata("author"));

        PadTemplate? template = source.GetPadTemplate("src");
        Assert.NotNull(template);
        Assert.Equal("src", template.GetName());

        // The pad the base class created from that template is what makes the
        // element linkable at all.
        Pad? pad = source.GetStaticPad("src");
        Assert.NotNull(pad);

        using ProbeTransform transform = new();
        Assert.NotNull(transform.GetPadTemplate("sink"));
        Assert.NotNull(transform.GetPadTemplate("src"));
        Assert.NotNull(transform.GetStaticPad("sink"));
        Assert.NotNull(transform.GetStaticPad("src"));
    }

    /// <summary>
    /// A managed in place filter rewrites the buffers that pass through it, and
    /// the sink downstream sees the rewritten bytes.
    /// </summary>
    /// <remarks>
    /// This is what the borrow of a mini object buys: the buffer the override
    /// is given is the one GStreamer made writable, so mapping it for writing
    /// works. A wrapper that took a reference of its own would make
    /// <c>gst_buffer_map</c> refuse.
    /// </remarks>
    [Fact]
    public void AManagedFilterRewritesTheBuffersInPlace()
    {
        const int Buffers = 4;

        using Pipeline pipeline = Pipeline.New("managed-filter");
        ProbeSrc source = new() { BufferCount = Buffers };
        ProbeTransform transform = new();
        ProbeSink sink = new();

        Assert.True(pipeline.AddMany(source, transform, sink));
        Assert.True(source.Link(transform, sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine(FormattableString.Invariant(
                $"bus: {message.Type}, transformed={transform.Transformed}, writable={transform.LastBufferWasWritable}"));
            _output.WriteLine($"rendered: [{string.Join(", ", sink.Rendered)}]");

            Assert.Equal(MessageType.Eos, message.Type);
            Assert.Equal(Buffers, transform.Transformed);

            // Every byte was incremented once, in the buffer the source
            // produced rather than in a copy of it.
            Assert.Equal<byte>([1, 2, 3, 4], sink.Rendered);
            Assert.True(transform.LastBufferWasWritable);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The caps that were negotiated reach the <c>set_caps</c> override of
    /// every managed element in the chain.
    /// </summary>
    [Fact]
    public void TheNegotiatedCapsReachEveryOverride()
    {
        using Pipeline pipeline = Pipeline.New("managed-negotiation");
        ProbeSrc source = new() { BufferCount = 1 };
        ProbeTransform transform = new();
        ProbeSink sink = new();

        Assert.True(pipeline.AddMany(source, transform, sink));
        Assert.True(source.Link(transform, sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));
            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);
            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);

            _output.WriteLine($"source caps: {source.NegotiatedCaps}");
            _output.WriteLine($"sink caps:   {sink.NegotiatedCaps}");

            Assert.Equal(ProbeSrc.MediaType, source.NegotiatedCaps);
            Assert.Equal(ProbeSrc.MediaType, sink.NegotiatedCaps);
            Assert.Contains("set-caps", transform.Lifecycle);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The lifecycle slots run in the order the base classes promise: started
    /// before any data flows, stopped after it stopped, and the caps in
    /// between.
    /// </summary>
    [Fact]
    public void TheLifecycleSlotsRunInOrder()
    {
        using Pipeline pipeline = Pipeline.New("managed-lifecycle");
        ProbeSrc source = new() { BufferCount = 2 };
        ProbeSink sink = new();

        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);
            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);

            // Nothing has been stopped yet: the pipeline is still playing.
            Assert.DoesNotContain("stop", source.Lifecycle);
            Assert.DoesNotContain("stop", sink.Lifecycle);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        _output.WriteLine($"source: {string.Join(" -> ", source.Lifecycle)}");
        _output.WriteLine($"sink:   {string.Join(" -> ", sink.Lifecycle)}");

        // Started once on the way up, stopped once on the way down, and the
        // caps arrived after the start of each element.
        Assert.Equal("start", source.Lifecycle[0]);
        Assert.Equal("stop", source.Lifecycle[^1]);
        Assert.Equal("start", sink.Lifecycle[0]);
        Assert.Equal("stop", sink.Lifecycle[^1]);

        Assert.True(
            source.Lifecycle.ToList().IndexOf("set-caps") > 0,
            "The source was told its caps before it was started.");
        Assert.True(
            sink.Lifecycle.ToList().IndexOf("set-caps") > 0,
            "The sink was told its caps before it was started.");
    }

    /// <summary>
    /// An exception in a <c>render</c> override becomes the documented error
    /// value of that slot: the flow stops, the pipeline posts an error, and the
    /// exception is reported through the trap rather than unwinding into the
    /// streaming thread of the source.
    /// </summary>
    [Fact]
    public void AThrowingRenderStopsTheStreamWithAnError()
    {
        using Pipeline pipeline = Pipeline.New("managed-throwing-sink");
        ProbeSrc source = new() { BufferCount = 5 };
        ProbeSink sink = new() { ThrowOnRender = true };

        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        Bus bus = pipeline.GetBus();

        List<Exception> reported = [];
        void Collect(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += Collect;

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine($"bus: {message.Type}");

            // GST_FLOW_ERROR from render is what basesink turns into an error
            // message and a stopped stream; the end of stream never arrives.
            Assert.Equal(MessageType.Error, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
            ExceptionTrap.UnhandledException -= Collect;
        }

        lock (reported)
        {
            _output.WriteLine($"trapped: {reported.Count} exception(s)");
            Assert.NotEmpty(reported);
            Assert.All(reported, exception => Assert.IsType<InvalidOperationException>(exception));
        }

        // Nothing was rendered, and the source stopped producing once the flow
        // was refused rather than running to its own end.
        Assert.Empty(sink.Rendered);
        Assert.True(source.Produced < 5, "The source kept producing after the sink refused a buffer.");
    }

    /// <summary>
    /// A managed bin sees the messages its children post, and passing them on
    /// is what puts them on the bus of the pipeline.
    /// </summary>
    [Fact]
    public void AManagedBinSeesTheMessagesOfItsChildren()
    {
        using Pipeline pipeline = Pipeline.New("managed-bin");
        ProbeBin bin = new();
        ProbeSrc source = new() { BufferCount = 2 };
        ProbeSink sink = new();

        Assert.True(bin.AddMany(source, sink));
        Assert.True(source.Link(sink));
        Assert.True(pipeline.Add(bin));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine($"bin handled: {string.Join(", ", bin.Handled)}");

            // The end of stream reached the pipeline because the override
            // chained up with it.
            Assert.Equal(MessageType.Eos, message.Type);
            Assert.Contains(MessageType.Eos, bin.Handled);
            Assert.Equal("GstSharp probe bin", bin.GetMetadata("long-name"));
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A managed bin that does not chain up drops the message: the override
    /// owns what it is given, so the end of stream never reaches the bus of the
    /// pipeline.
    /// </summary>
    [Fact]
    public void AManagedBinThatDoesNotChainUpDropsTheMessage()
    {
        using Pipeline pipeline = Pipeline.New("managed-bin-dropping");
        ProbeBin bin = new() { DropEos = true };
        ProbeSrc source = new() { BufferCount = 2 };
        ProbeSink sink = new();

        Assert.True(bin.AddMany(source, sink));
        Assert.True(source.Link(sink));
        Assert.True(pipeline.Add(bin));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(
                bus,
                MessageType.Eos | MessageType.Error,
                TimeSpan.FromSeconds(2));

            _output.WriteLine($"bin handled: {string.Join(", ", bin.Handled)}, bus: {message?.Type.ToString() ?? "nothing"}");

            // The children still ran to their end, and the bin still saw it.
            Assert.Contains(MessageType.Eos, bin.Handled);
            Assert.Equal(2, sink.Rendered.Count);

            // But nothing passed it on, so the pipeline never posted one.
            Assert.Null(message);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A managed source is a normal element: it can be built, driven through
    /// its states and torn down without a pipeline around it.
    /// </summary>
    [Fact]
    public void AManagedSourceCanBeDrivenOnItsOwn()
    {
        using ProbeSrc source = new() { BufferCount = 1 };

        Assert.Equal(ProbeSrc.RegisteredType, source.NativeType);
        Assert.NotEqual(StateChangeReturn.Failure, source.SetState(State.Ready));
        Assert.NotEqual(StateChangeReturn.Failure, source.SetState(State.Null));
        source.GetState(out State state, out _, StateTimeout);

        _output.WriteLine($"state: {state}, lifecycle: {string.Join(" -> ", source.Lifecycle)}");

        Assert.Equal(State.Null, state);
    }
}
