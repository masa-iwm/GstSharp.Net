using System.Diagnostics;
using Gst;
using Gst.GLib;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Runs real pipelines and drives them from a polled bus: no main loop, no
/// signal handler, every wait bounded.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class PipelineBusTests
{
    private static readonly TimeSpan MessageTimeout = TimeSpan.FromSeconds(15);
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public PipelineBusTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A parsed pipeline reaches PLAYING, posts its own state transitions and
    /// ends with an end of stream message once the source runs out of buffers.
    /// </summary>
    [Fact]
    public void ParsedPipelineReachesPlayingAndPostsEos()
    {
        // gst_parse_launch returns a GstPipeline; the cast is the type registry
        // at work, and a null here is the silent failure a missing registry
        // entry produces.
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("fakesrc num-buffers=5 ! fakesink"));
        Bus bus = pipeline.GetBus();

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

        StateChangeReturn reached = pipeline.GetState(out State state, out State pending, StateTimeout);
        Assert.Equal(StateChangeReturn.Success, reached);
        Assert.Equal(State.Playing, state);
        Assert.Equal(State.VoidPending, pending);

        List<(State Old, State New)> transitions = [];
        bool eos = false;
        Stopwatch elapsed = Stopwatch.StartNew();

        while (!eos && elapsed.Elapsed < MessageTimeout)
        {
            using Message? message = bus.TimedPopFiltered(
                ClockTime.FromMilliseconds(100),
                MessageType.Eos | MessageType.Error | MessageType.StateChanged);

            if (message is null)
            {
                continue;
            }

            switch (message.Type)
            {
                case MessageType.Eos:
                    eos = true;
                    break;

                case MessageType.Error:
                    Assert.Fail(Describe(message));
                    break;

                case MessageType.StateChanged:
                    // Src is not disposed here: it is the very wrapper this
                    // test holds, and every lookup of an object hands the same
                    // instance out.
                    if (message.Src is Gst.Object source && source.Handle == pipeline.Handle)
                    {
                        message.ParseStateChanged(out State old, out State current, out State _);
                        transitions.Add((old, current));
                    }

                    break;

                default:
                    break;
            }
        }

        _output.WriteLine(string.Join(", ", transitions.Select(t => $"{t.Old}->{t.New}")));

        Assert.True(eos, FormattableString.Invariant($"No end of stream within {MessageTimeout.TotalSeconds} s."));
        Assert.Equal([(State.Null, State.Ready), (State.Ready, State.Paused), (State.Paused, State.Playing)], transitions);

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Null));
        Assert.Equal(StateChangeReturn.Success, pipeline.GetState(out State stopped, out State _, StateTimeout));
        Assert.Equal(State.Null, stopped);
    }

    /// <summary>
    /// A pipeline that is built by hand runs as well: the elements are added
    /// with <see cref="Bin.AddMany"/> and linked into a chain with
    /// <see cref="Element.Link(Gst.Element[])"/>.
    /// </summary>
    [Fact]
    public void ManuallyBuiltPipelineLinksAndReachesEos()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Pipeline.New("manual"));
        using Element source = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "source"));
        using Element middle = Assert.IsAssignableFrom<Element>(ElementFactory.Make("identity", "middle"));
        using Element sink = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "sink"));
        Bus bus = pipeline.GetBus();

        Global.UtilSetObjectArg(source, "num-buffers", "5");

        Assert.True(pipeline.AddMany(source, middle, sink));

        // Three elements, so this is the chaining overload rather than the
        // generated Link(Element).
        Assert.True(source.Link(middle, sink));

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

        using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, MessageTimeout);

        Assert.NotNull(message);
        Assert.Equal(MessageType.Eos, message.Type);
        Assert.Equal("manual", message.SourceName);

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Null));

        // The elements survive the pipeline they were added to, because the
        // wrappers hold references of their own.
        source.Unlink(middle, sink);
        Assert.True(pipeline.RemoveMany(source, middle, sink));
        Assert.Equal("source", source.Name);
    }

    /// <summary>
    /// Formats an error message for a failed assertion.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The text of the failure.</returns>
    private static string Describe(Message message)
    {
        (GException error, string? debug) = message.ParseError();
        return $"{message.SourceName ?? "?"}: {error.Message} ({debug ?? "no debug string"})";
    }
}
