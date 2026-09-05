using Gst;
using Gst.Base;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated subclassing surface of <c>GstAggregator</c>: the required
/// <c>aggregate</c> slot, and a managed aggregator that really mixes two live
/// sources inside a pipeline.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassAggregatorTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassAggregatorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A managed aggregator aggregates: two sources feed its requested sink
    /// pads, the <c>aggregate</c> override pushes what it pops, and the stream
    /// ends once both sources are done.
    /// </summary>
    [Fact]
    public void AManagedAggregatorForwardsWhatItsSourcesProduce()
    {
        const int Buffers = 3;

        using Pipeline pipeline = Pipeline.New("managed-aggregator");
        using ProbeAggregator aggregator = new();
        Element first = NewSource("agg-source-1", Buffers);
        Element second = NewSource("agg-source-2", Buffers);
        Element sink = ElementFactory.Make("fakesink", "agg-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        Assert.True(pipeline.AddMany(first, second, aggregator, sink));

        Link(first, aggregator.RequestSinkPad());
        Link(second, aggregator.RequestSinkPad());
        Assert.True(aggregator.Link(sink));

        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);
            _output.WriteLine(FormattableString.Invariant(
                $"bus: {message.Type}, aggregated={aggregator.Aggregated}, pushed={aggregator.Pushed}"));

            Assert.Equal(MessageType.Eos, message.Type);

            // Both sources send the same number of buffers, so the override
            // pops a pair per call and pushes one buffer per pair.
            Assert.Equal(Buffers, aggregator.Pushed);
            Assert.True(aggregator.Aggregated >= Buffers);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// <c>aggregate</c> is required: the base class calls the slot unguarded,
    /// so a descriptor that leaves it out is refused before anything is
    /// registered and the name stays free.
    /// </summary>
    [Fact]
    public void AnAggregatorWithoutTheAggregateSlotIsRefused()
    {
        const string TypeName = "GstSharpTestAggregatorWithoutAggregate";

        Assert.False(GType.FromName(TypeName).IsValid);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Aggregator.DefineSubclass(TypeName, _ => { }, Aggregator.StartOverride));

        _output.WriteLine(error.Message);
        Assert.Contains("AggregateOverride", error.Message, StringComparison.Ordinal);

        // Nothing was registered: the check runs before the type is defined,
        // so the name is not burnt on a class that cannot be instantiated.
        Assert.False(GType.FromName(TypeName).IsValid);
    }

    private static Element NewSource(string name, int buffers)
    {
        Element source = ElementFactory.Make("fakesrc", name)
            ?? throw new InvalidOperationException("fakesrc is missing.");

        source.SetProperty("num-buffers", buffers);
        source.SetProperty("sizetype", 2);
        source.SetProperty("filltype", 2);
        return source;
    }

    private static void Link(Element source, Pad sink)
    {
        using Pad? pad = source.GetStaticPad("src");

        Assert.NotNull(pad);
        Assert.Equal(PadLinkReturn.Ok, pad.Link(sink));
    }
}
