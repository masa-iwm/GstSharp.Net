using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers the round trip an application makes through its own bus:
/// <see cref="Message.NewApplication"/> builds the message,
/// <see cref="Element.PostMessage"/> puts it on the bus, and the loop that
/// polls the bus reads it back with its payload intact.
/// </summary>
/// <remarks>
/// Both calls consume their argument, so the test also checks what that means:
/// the wrappers are disposed when the calls return, and using them afterwards
/// throws rather than releasing something the library now owns.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MessageApplicationTests
{
    private const int Iterations = 500;

    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MessageApplicationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A posted application message arrives on the bus with the name and the
    /// field it was built with.
    /// </summary>
    [Fact]
    public void APostedApplicationMessageArrivesWithItsPayload()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("application"));
        Bus bus = pipeline.GetBus();

        Assert.True(Post(pipeline, "Pipeline interrupted"));

        using Message? message = BusPump.WaitFor(bus, MessageType.Application, MessageTimeout);

        Assert.NotNull(message);
        Assert.Equal(MessageType.Application, message.Type);
        Assert.Equal("application", message.SourceName);

        using Structure? structure = message.GetStructure();

        Assert.NotNull(structure);
        Assert.True(structure.HasName("GstLaunchInterrupt"));
        Assert.Equal("Pipeline interrupted", structure.GetString("message"));

        _output.WriteLine(structure.ToString());
    }

    /// <summary>
    /// Both calls take their argument over, so both wrappers are disposed when
    /// they return.
    /// </summary>
    [Fact]
    public void BothCallsConsumeTheirArgument()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("application-consumes"));
        Bus bus = pipeline.GetBus();

        Structure structure = Structure.NewEmpty("GstLaunchInterrupt");
        Message message = Message.NewApplication(pipeline, structure);

        // The payload went to the message, and the wrapper owns nothing now.
        Assert.True(structure.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => structure.HasName("GstLaunchInterrupt"));

        Assert.True(pipeline.PostMessage(message));

        // And the message went to the bus.
        Assert.True(message.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => { _ = message.Type; });

        using Message? posted = BusPump.WaitFor(bus, MessageType.Application, MessageTimeout);
        Assert.NotNull(posted);
    }

    /// <summary>
    /// A message with no source is a message all the same, which is what a
    /// <see langword="null"/> source produces.
    /// </summary>
    [Fact]
    public void AMessageWithoutASourceIsAllowed()
    {
        using Structure structure = Structure.NewEmpty("GstLaunchInterrupt");
        using Message message = Message.NewApplication(null, structure);

        Assert.Equal(MessageType.Application, message.Type);
        Assert.Null(message.Src);
        Assert.Null(message.SourceName);
    }

    /// <summary>
    /// Neither call leaks: five hundred round trips through the bus, each one
    /// building a payload, a message, posting it and reading it back.
    /// </summary>
    /// <remarks>
    /// A missing <c>g_boxed_copy</c> or a missing <c>gst_mini_object_ref</c>
    /// would be a double free rather than a leak, and both show up here and
    /// nowhere else.
    /// </remarks>
    [Fact]
    public void TheRoundTripSurvivesRepetition()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("application-repeated"));
        Bus bus = pipeline.GetBus();

        for (int i = 0; i < Iterations; i++)
        {
            Assert.True(Post(pipeline, "Pipeline interrupted"));

            using Message? message = BusPump.WaitFor(bus, MessageType.Application, MessageTimeout);

            Assert.NotNull(message);

            using Structure? structure = message.GetStructure();
            Assert.Equal("Pipeline interrupted", structure?.GetString("message"));
        }

        _output.WriteLine(FormattableString.Invariant($"{Iterations} round trips through the bus."));
    }

    /// <summary>
    /// Builds the message <c>gst-launch-1.0</c> posts for an interrupt and puts
    /// it on the bus of the pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to post on.</param>
    /// <param name="text">The text to carry.</param>
    /// <returns><see langword="true"/> when the bus took the message.</returns>
    private static bool Post(Pipeline pipeline, string text)
    {
        using Structure structure = Structure.NewEmpty("GstLaunchInterrupt");

        using (Value value = Value.New(GType.String))
        {
            value.SetString(text);
            structure.SetValue("message", value);
        }

        // Both calls consume their argument; the using declarations stay
        // correct because disposal is idempotent.
        using Message message = Message.NewApplication(pipeline, structure);

        return pipeline.PostMessage(message);
    }
}
