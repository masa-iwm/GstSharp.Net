using System.Diagnostics;
using Gst;
using Gst.App;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The polymorphic half of the consumer contract: the two calls that hand out
/// whatever the sink has queued, an event as readily as a sample.
/// </summary>
/// <remarks>
/// A sample tells an application what the data is, and nothing else. The
/// events that came with it — where the stream starts, which caps it carries,
/// which segment it runs in — are discarded by the sample-only pull, so an
/// application that has to see them pulls objects instead. What these tests
/// hold the binding to is that the concrete type of the answer is the type the
/// library meant, that the two families share one queue, and that the end of
/// the stream is still a null answer and an <c>is-eos</c> of its own.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSinkPullObjectTests
{
    /// <summary>The longest a pipeline of a few frames may take.</summary>
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The most objects a pipeline of a few frames may hand out.</summary>
    private const int PullLimit = 64;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSinkPullObjectTests(ITestOutputHelper output)
    {
        _output = output;

        // Naming AppSink in a cast does not run the module initialiser of
        // GstSharp.Net.App, and until it has run the type registry has no entry
        // for GstAppSink; the pull of an object needs the registry for the
        // answer as well. See Gst.App.GstApp.
        GstApp.Initialize();
    }

    /// <summary>
    /// The whole stream through one call: the serialized events that open it,
    /// then a sample per buffer, then the null that says it is over.
    /// </summary>
    [Fact]
    public void PullObjectHandsOutEventsThenSamplesThenNullAtEos()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=2 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            List<EventType> leadingEvents = [];
            int samples = 0;
            int pulls = 0;
            bool ended = false;

            while (pulls++ < PullLimit)
            {
                using MiniObject? pulled = sink.PullObject();

                if (pulled is null)
                {
                    ended = true;
                    break;
                }

                switch (pulled)
                {
                    case Sample sample:
                        samples++;
                        using (Buffer? buffer = sample.GetBuffer())
                        {
                            Assert.NotNull(buffer);
                        }

                        break;

                    case Event @event:
                        if (samples == 0)
                        {
                            leadingEvents.Add(@event.Type);
                        }

                        break;

                    default:
                        Assert.Fail($"the sink handed out a {pulled.GetType().Name}.");
                        break;
                }
            }

            _output.WriteLine(
                $"pulled {pulls} object(s): {samples} sample(s) after " +
                $"{string.Join(", ", leadingEvents)}");

            Assert.True(ended, $"the sink was still handing out objects after {PullLimit} pulls.");

            // Which serialized events a stream carries is up to the elements
            // and the version, so the assertion is on the order of the three
            // every stream has, not on the list being exactly those three.
            int streamStart = leadingEvents.IndexOf(EventType.StreamStart);
            int caps = leadingEvents.IndexOf(EventType.Caps);
            int segment = leadingEvents.IndexOf(EventType.Segment);

            Assert.True(streamStart >= 0, "no stream-start event was handed out.");
            Assert.True(caps > streamStart, "the caps event did not follow the stream-start event.");
            Assert.True(segment > caps, "the segment event did not follow the caps event.");

            Assert.Equal(2, samples);
            Assert.True(sink.IsEos());
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The non blocking contract of the object pull: a sink that has nothing to
    /// give returns null when the timeout is over, and not later.
    /// </summary>
    [Fact]
    public void TryPullObjectGivesUpWhenTheTimeoutIsOver()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "idle"));
        AppSink sink = Assert.IsType<AppSink>(element);

        Stopwatch elapsed = Stopwatch.StartNew();
        MiniObject? pulled = sink.TryPullObject(ClockTime.FromMilliseconds(100));
        TimeSpan waited = elapsed.Elapsed;

        _output.WriteLine($"try_pull_object on a sink that was never started returned after {waited.TotalMilliseconds:F0} ms");

        Assert.Null(pulled);
        Assert.True(waited < TimeSpan.FromSeconds(5), $"try_pull_object blocked for {waited}.");
    }

    /// <summary>
    /// The other end of the same contract: once the stream is over, the pull
    /// loop is handed null for good, which is how it learns that it is done.
    /// </summary>
    [Fact]
    public void TryPullObjectReturnsNullAfterTheEndOfTheStream()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=3 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            int samples = 0;
            int events = 0;
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < RunTimeout)
            {
                using MiniObject? pulled = sink.TryPullObject(ClockTime.FromMilliseconds(100));

                if (pulled is null)
                {
                    if (sink.IsEos())
                    {
                        break;
                    }

                    continue;
                }

                if (pulled is Sample)
                {
                    samples++;
                }
                else
                {
                    events++;
                }
            }

            _output.WriteLine(
                $"pulled {samples} sample(s) and {events} event(s) in {elapsed.Elapsed.TotalMilliseconds:F0} ms");

            Assert.Equal(3, samples);
            Assert.True(events > 0, "the stream carried no serialized event at all.");
            Assert.True(sink.IsEos());

            // A sink at the end of the stream keeps saying no, and says it
            // immediately.
            Stopwatch again = Stopwatch.StartNew();
            Assert.Null(sink.TryPullObject(ClockTime.FromMilliseconds(100)));
            Assert.True(again.Elapsed < TimeSpan.FromSeconds(5), $"try_pull_object blocked for {again.Elapsed}.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// One queue, two ways of reading it: a sample pull discards the events it
    /// walks over, so what an object pull after it is handed is a sample too.
    /// </summary>
    [Fact]
    public void PullObjectAndPullSampleShareTheQueue()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=2 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Sample? first = sink.PullSample();
            Assert.NotNull(first);

            using MiniObject? second = sink.PullObject();
            Assert.NotNull(second);
            Assert.IsType<Sample>(second);

            using Buffer? buffer = ((Sample)second).GetBuffer();
            Assert.NotNull(buffer);

            _output.WriteLine("the object pull after a sample pull was handed a sample, not an event");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
