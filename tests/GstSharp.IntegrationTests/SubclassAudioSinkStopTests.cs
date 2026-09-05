using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GstAudioSinkClass.stop</c>, which the binding emits as
/// <c>OnStopDevice</c>: where it sits in the teardown of an audio sink, and
/// that declaring it replaces the fallback to <c>reset</c> the C code takes
/// for a sink that leaves the slot NULL.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassAudioSinkStopTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassAudioSinkStopTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The slot runs once, after the last sample was written and before the
    /// ring buffer is released and the device closed, and it runs instead of
    /// <c>reset</c> rather than beside it: the ring buffer only falls back to
    /// <c>reset</c> when the stop slot is NULL, and this sink declares it.
    /// </summary>
    [Fact]
    public void TheStopSlotRunsOnceBetweenTheLastWriteAndTheUnprepare()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-sink-stop");
        using StopDeviceAudioSink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "audio-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 20);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        Bus bus = pipeline.GetBus();
        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

        using (Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout))
        {
            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }

        // The ring buffer is still started here: the teardown has not begun,
        // so neither the stop slot nor the fallback has run.
        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Paused));
        int resetsBeforeTeardown = sink.CountOf("reset");
        Assert.Equal(0, sink.CountOf("stop"));

        // PAUSED to READY is where gst_audio_ring_buffer_release stops the
        // ring buffer and then releases it.
        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

        IReadOnlyList<string> calls = sink.Calls;
        _output.WriteLine("calls: " + string.Join(", ", calls));

        Assert.Equal(1, sink.CountOf("stop"));

        // The fallback the C code takes for a NULL stop slot is a call to
        // reset; declaring the slot replaces it, so nothing new reached
        // OnReset across the transition that ran the stop.
        Assert.Equal(resetsBeforeTeardown, sink.CountOf("reset"));

        int stop = calls.ToList().IndexOf("stop");
        int lastWrite = calls.ToList().LastIndexOf("write");
        int unprepare = calls.ToList().IndexOf("unprepare");

        Assert.True(lastWrite >= 0, "the sink was handed no sample at all");
        Assert.True(lastWrite < stop, "the stop slot ran before the last write");
        Assert.True(unprepare > stop, "the ring buffer was released before the stop slot ran");

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Null));

        int close = sink.Calls.ToList().IndexOf("close");
        Assert.True(close > stop, "the device was closed before the stop slot ran");
    }

    /// <summary>
    /// A device that never opened has no ring buffer that ever started, and the
    /// stop slot is only reached when the ring buffer leaves the started state:
    /// the whole teardown after a failed open is skipped.
    /// </summary>
    [Fact]
    public void TheStopSlotDoesNotRunWhenTheDeviceNeverOpened()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-sink-open-fails");
        using StopDeviceAudioSink sink = new() { OpenSucceeds = false };
        Element source = ElementFactory.Make("audiotestsrc", "audio-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 20);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        // The sink refuses NULL to READY, so the pipeline never plays.
        Assert.Equal(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

        Assert.Equal(StateChangeReturn.Success, pipeline.SetState(State.Null));

        IReadOnlyList<string> calls = sink.Calls;
        _output.WriteLine("calls: " + string.Join(", ", calls));

        Assert.Contains("open", calls);
        Assert.Equal(0, sink.CountOf("stop"));
        Assert.Equal(0, sink.CountOf("unprepare"));
        Assert.Equal(0, sink.CountOf("close"));
    }
}
