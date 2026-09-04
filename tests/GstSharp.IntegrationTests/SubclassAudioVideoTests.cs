using Gst;
using Gst.Audio;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated subclassing surface of the audio and video base classes: an
/// audio sink that counts what the ring buffer writes, an audio source that
/// fills it with silence, a video sink that counts frames, and the two filters
/// whose own slot is not bindable and which override their base transform
/// instead.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class SubclassAudioVideoTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SubclassAudioVideoTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A managed audio sink really is the device end of a pipeline: the ring
    /// buffer opens it, prepares it with the negotiated format, writes every
    /// sample of a bounded test source through the span, and takes it down
    /// again on the way back to NULL.
    /// </summary>
    [Fact]
    public void AManagedAudioSinkWritesWhatTheRingBufferHandsIt()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-sink");
        using ProbeAudioSink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "audio-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 20);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        RunToEos(pipeline);

        _output.WriteLine(FormattableString.Invariant(
            $"managed audio sink: written={sink.Written}, lifecycle={string.Join(", ", sink.Lifecycle)}"));

        Assert.True(sink.Written > 0);

        // The device is opened once on the way out of NULL and prepared once
        // the caps are known; both run before a single byte is written.
        Assert.Contains("open", sink.Lifecycle);
        Assert.Contains(sink.Lifecycle, call => call.StartsWith("prepare", StringComparison.Ordinal));
    }

    /// <summary>
    /// A managed audio source fills the ring buffer through its span: the
    /// bytes it wrote reach the sink of the pipeline as buffers.
    /// </summary>
    [Fact]
    public void AManagedAudioSourceFillsTheRingBufferThroughItsSpan()
    {
        using Pipeline pipeline = Pipeline.New("managed-audio-source");
        using ProbeAudioSrc source = new();
        Element sink = ElementFactory.Make("fakesink", "audio-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        source.SetProperty("num-buffers", 20);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        RunToEos(pipeline);

        _output.WriteLine(FormattableString.Invariant(
            $"managed audio source: read={source.Read}, opened={source.Opened}, segsize={source.Segsize}"));

        Assert.Equal(1, source.Opened);
        Assert.True(source.Segsize > 0);
        Assert.True(source.Read > 0);
    }

    /// <summary>
    /// A managed video sink is shown every frame a bounded test source
    /// produces.
    /// </summary>
    [Fact]
    public void AManagedVideoSinkIsShownEveryFrame()
    {
        const int Frames = 5;

        using Pipeline pipeline = Pipeline.New("managed-video-sink");
        using ProbeVideoSink sink = new();
        Element source = ElementFactory.Make("videotestsrc", "video-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", Frames);
        sink.SetProperty("sync", false);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        RunToEos(pipeline);

        _output.WriteLine(FormattableString.Invariant(
            $"managed video sink: shown={sink.Shown}, bytes={sink.Bytes}"));

        // The preroll frame is shown through the same slot, so the sink sees
        // one more frame than the source produced.
        Assert.Equal(Frames + 1, sink.Shown);
        Assert.True(sink.Bytes > 0);
    }

    /// <summary>
    /// A managed video filter sees every frame in place, with the video base
    /// class mapping and unmapping it around the call.
    /// </summary>
    [Fact]
    public void AManagedVideoFilterSeesEveryFrameInPlace()
    {
        const int Frames = 5;

        using Pipeline pipeline = Pipeline.New("managed-video-filter");
        using ProbeVideoFilter filter = new();
        Element source = ElementFactory.Make("videotestsrc", "video-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");
        Element sink = ElementFactory.Make("fakesink", "video-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, filter, sink));
        Assert.True(source.Link(filter));
        Assert.True(filter.Link(sink));

        RunToEos(pipeline);

        _output.WriteLine(FormattableString.Invariant(
            $"managed video filter: transformed={filter.Transformed}, flags={filter.FrameFlags}"));

        Assert.Equal(Frames, filter.Transformed);

        // The wrapper really pointed at the frame the base class mapped: a
        // mapped frame carries no flag of its own, which is not the value the
        // probe starts out with.
        Assert.Equal(Gst.Video.VideoFrameFlags.None, filter.FrameFlags);
    }

    /// <summary>
    /// A managed audio filter sees every buffer in place through the transform
    /// of its base class, which is the only slot of an audio filter that is
    /// bindable.
    /// </summary>
    [Fact]
    public void AManagedAudioFilterSeesEveryBufferInPlace()
    {
        const int Buffers = 10;

        using Pipeline pipeline = Pipeline.New("managed-audio-filter");
        using ProbeAudioFilter filter = new();
        Element source = ElementFactory.Make("audiotestsrc", "audio-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");
        Element sink = ElementFactory.Make("fakesink", "audio-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        source.SetProperty("num-buffers", Buffers);
        Assert.True(pipeline.AddMany(source, filter, sink));
        Assert.True(source.Link(filter));
        Assert.True(filter.Link(sink));

        RunToEos(pipeline);

        _output.WriteLine(FormattableString.Invariant(
            $"managed audio filter: transformed={filter.Transformed}, bytes={filter.Bytes}"));

        Assert.Equal(Buffers, filter.Transformed);
        Assert.True(filter.Bytes > 0);
    }

    /// <summary>
    /// <c>write</c> is required: the thread of the ring buffer answers a NULL
    /// slot by stopping before it starts, so a descriptor that leaves it out
    /// is refused before the type name is taken.
    /// </summary>
    [Fact]
    public void AnAudioSinkWithoutTheWriteSlotIsRefused()
    {
        const string TypeName = "GstSharpTestAudioSinkWithoutWrite";

        Assert.False(GType.FromName(TypeName).IsValid);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => AudioSink.DefineSubclass(
                TypeName,
                _ => { },
                AudioSink.OpenOverride,
                AudioSink.PrepareOverride));

        _output.WriteLine(error.Message);
        Assert.Contains("WriteOverride", error.Message, StringComparison.Ordinal);
        Assert.False(GType.FromName(TypeName).IsValid);
    }

    /// <summary>
    /// The same holds for the ring buffer of an audio base sink: without one
    /// the element cannot leave the NULL state, so the registration says so
    /// instead of letting the state change fail later.
    /// </summary>
    [Fact]
    public void AnAudioBaseSinkWithoutTheRingBufferSlotIsRefused()
    {
        const string TypeName = "GstSharpTestAudioBaseSinkWithoutRingBuffer";

        Assert.False(GType.FromName(TypeName).IsValid);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => AudioBaseSink.DefineSubclass(TypeName, _ => { }, AudioBaseSink.PayloadOverride));

        _output.WriteLine(error.Message);
        Assert.Contains("CreateRingbufferOverride", error.Message, StringComparison.Ordinal);
        Assert.False(GType.FromName(TypeName).IsValid);
    }

    private void RunToEos(Pipeline pipeline)
    {
        Bus bus = pipeline.GetBus();

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, BusTimeout);

            Assert.NotNull(message);

            if (message.Type == MessageType.Error)
            {
                (Gst.GLib.GException error, string? debug) = message.ParseError();

                _output.WriteLine(FormattableString.Invariant($"bus error: {error.Message} ({debug})"));
            }

            _output.WriteLine(FormattableString.Invariant($"bus: {message.Type}"));
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
