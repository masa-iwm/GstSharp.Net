using Gst;
using Gst.GLib;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The informational path of the bus. An info message carries a <c>GError</c>
/// exactly like an error message does, so reading one has to hand the error and
/// the debug string over without leaking either of them.
/// </summary>
/// <remarks>
/// See <see cref="MessageErrorTests"/>: both out parameters of
/// <c>gst_message_parse_info</c> are copies that the caller owns, and a missing
/// <c>g_free</c> or <c>g_error_free</c> shows up nowhere but in a repeated
/// parse of the same message.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MessageInfoTests
{
    private const int Iterations = 1000;

    private const int Code = 42;

    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MessageInfoTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// An element that reports something posts an info message, whose payload
    /// survives being read a thousand times.
    /// </summary>
    [Fact]
    public void InfoMessageCarriesTheReportAndTheDebugString()
    {
        Quark domain = Quark.FromString("gstsharp-test-info");

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("info"));
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        Bus bus = pipeline.GetBus();

        // Adding the element to the pipeline is what gives it the bus it posts
        // to; no state change is needed to post a message.
        Assert.True(pipeline.Add(element));

        element.MessageFull(
            MessageType.Info,
            domain,
            Code,
            "the report",
            "the debug text",
            "MessageInfoTests.cs",
            nameof(InfoMessageCarriesTheReportAndTheDebugString),
            1);

        using Message? message = BusPump.WaitFor(bus, MessageType.Info, MessageTimeout);

        Assert.NotNull(message);
        Assert.Equal(MessageType.Info, message.Type);
        Assert.Equal("sink", message.SourceName);

        (GException error, string? debug) = message.ParseInfo();

        _output.WriteLine(FormattableString.Invariant(
            $"domain={error.Domain} code={error.Code} message={error.Message}"));
        _output.WriteLine($"debug={debug}");

        Assert.Equal(domain, error.Domain);
        Assert.Equal(Code, error.Code);
        Assert.Equal("the report", error.Message);

        // The debug string of gst_element_message_full is the text of the
        // element wrapped in the call site it was posted from.
        Assert.NotNull(debug);
        Assert.Contains("the debug text", debug, StringComparison.Ordinal);

        // Reading the same message again returns another pair of copies.
        // Surviving a thousand of them is the canary for the two frees.
        for (int i = 0; i < Iterations; i++)
        {
            (GException repeated, string? repeatedDebug) = message.ParseInfo();
            Assert.Equal(error.Message, repeated.Message);
            Assert.Equal(error.Code, repeated.Code);
            Assert.Equal(debug, repeatedDebug);
        }

        _output.WriteLine(FormattableString.Invariant($"{Iterations} repeated parses of the same message."));

        // The message is an info message, so reading it as an error is a
        // mistake that has to be reported rather than answered with an empty
        // error.
        Assert.Throws<InvalidOperationException>(() => { _ = message.ParseError(); });
        Assert.Throws<InvalidOperationException>(() => { _ = message.ParseWarning(); });
    }

    /// <summary>
    /// The type check works in both directions: an error message is not an info
    /// message either.
    /// </summary>
    [Fact]
    public void ParseInfoRefusesAMessageOfAnotherType()
    {
        Quark domain = Quark.FromString("gstsharp-test-info");

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("info-wrong-type"));
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        Bus bus = pipeline.GetBus();

        Assert.True(pipeline.Add(element));

        element.MessageFull(
            MessageType.Error,
            domain,
            Code,
            "the failure",
            "the debug text",
            "MessageInfoTests.cs",
            nameof(ParseInfoRefusesAMessageOfAnotherType),
            1);

        using Message? message = BusPump.WaitFor(bus, MessageType.Error, MessageTimeout);

        Assert.NotNull(message);
        Assert.Equal(MessageType.Error, message.Type);

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => { _ = message.ParseInfo(); });
        _output.WriteLine(refused.Message);

        // And the message reads fine as what it is.
        (GException error, _) = message.ParseError();
        Assert.Equal("the failure", error.Message);
    }
}
