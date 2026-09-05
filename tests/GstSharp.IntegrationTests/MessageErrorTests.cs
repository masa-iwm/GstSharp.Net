using Gst;
using Gst.GLib;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers the error path of the bus: a source that cannot open its resource
/// posts an error message, and reading that message must hand the
/// <c>GError</c> and the debug string over without leaking either of them.
/// </summary>
/// <remarks>
/// Both out parameters of <c>gst_message_parse_error</c> are copies that the
/// caller owns. The same message is parsed repeatedly here, because a missing
/// <c>g_free</c> or <c>g_error_free</c> shows up nowhere else.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MessageErrorTests
{
    private const int Iterations = 1000;

    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MessageErrorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A file source that points at nothing reports a resource error, which
    /// arrives as an error message whose source is the element that failed and
    /// whose payload survives being read a thousand times.
    /// </summary>
    [Fact]
    public void ErrorMessageCarriesTheErrorAndTheDebugString()
    {
        string missing = Path.Combine(
            Path.GetTempPath(),
            FormattableString.Invariant($"gstsharp-missing-{Guid.NewGuid():N}.bin"));

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("errors"));
        using Element source = Assert.IsAssignableFrom<Element>(ElementFactory.Make("filesrc", "source"));
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        Bus bus = pipeline.GetBus();

        // A property set by name with a string value, which is how an
        // application configures an element it parsed.
        Global.UtilSetObjectArg(source, "location", missing);

        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        try
        {
            // The state change fails, but the reason only arrives on the bus.
            pipeline.SetState(State.Playing);

            using Message? message = BusPump.WaitFor(bus, MessageType.Error | MessageType.Eos, MessageTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Error, message.Type);

            // The type of the source goes through the type registry. GstFileSrc
            // is a plugin type with no binding of its own, so the closest
            // registered base type is what the wrapper is built from.
            Gst.Object? src = message.Src;
            Assert.NotNull(src);
            Assert.True(src is Element, $"The source of the message is a {src.GetType().Name}.");
            Assert.Equal("source", message.SourceName);

            (GException error, string? debug) = message.ParseError();

            _output.WriteLine(FormattableString.Invariant(
                $"domain={error.Domain} code={error.Code} message={error.Message}"));
            _output.WriteLine($"debug={debug}");

            Assert.NotEqual(Quark.Zero, error.Domain);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.False(string.IsNullOrWhiteSpace(debug));

            // Reading the same message again returns another pair of copies.
            // Surviving a thousand of them is the canary for the two frees;
            // building a thousand pipelines would measure something else.
            for (int i = 0; i < Iterations; i++)
            {
                (GException repeated, string? repeatedDebug) = message.ParseError();
                Assert.Equal(error.Message, repeated.Message);
                Assert.Equal(error.Code, repeated.Code);
                Assert.Equal(debug, repeatedDebug);
            }

            _output.WriteLine(FormattableString.Invariant($"{Iterations} repeated parses of the same message."));

            // The message is an error, so reading it as a warning is a mistake
            // that has to be reported rather than answered with an empty error.
            Assert.Throws<InvalidOperationException>(() => { _ = message.ParseWarning(); });
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
