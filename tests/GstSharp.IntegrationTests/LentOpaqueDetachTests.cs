using Gst;
using Gst.Audio;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The wrapper of a lent opaque record is detached when the call that lent it
/// returns. The pointer it held is regularly an address on the stack of the
/// caller, so a wrapper an override filed away has to say that it means nothing
/// any more rather than read that address again.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class LentOpaqueDetachTests
{
    private static readonly TimeSpan BusTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public LentOpaqueDetachTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The frame a video filter is lent points at a <c>GstVideoFrame</c> the
    /// base class mapped on its own stack, and the address is gone the moment
    /// the transform returns.
    /// </summary>
    [Fact]
    public void TheFrameAVideoFilterIsLentIsDetachedWhenTheTransformReturns()
    {
        const int Frames = 3;

        using Pipeline pipeline = Pipeline.New("lent-video-frame");
        using StowingVideoFilter filter = new();
        Element source = ElementFactory.Make("videotestsrc", "video-source")
            ?? throw new InvalidOperationException("videotestsrc is part of the base plugins.");
        Element sink = ElementFactory.Make("fakesink", "video-sink")
            ?? throw new InvalidOperationException("fakesink is missing.");

        source.SetProperty("num-buffers", Frames);
        Assert.True(pipeline.AddMany(source, filter, sink));
        Assert.True(source.Link(filter));
        Assert.True(filter.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        Assert.Equal(Frames, filter.Transformed);

        VideoFrame kept = filter.Kept
            ?? throw new InvalidOperationException("The override stowed no frame.");

        _ = Assert.Throws<ObjectDisposedException>(() => kept.Flags);

        // What the override read while the call ran is what it was supposed to
        // read, so the detach is what took the wrapper away and not a wrapper
        // that never pointed anywhere.
        Assert.Equal(VideoFrameFlags.None, filter.FlagsWhileRunning);
    }

    /// <summary>
    /// The same for the ring buffer specification an audio sink is lent: it is a
    /// field of the ring buffer the sink is being prepared for, and the wrapper
    /// stops meaning anything when <c>prepare</c> returns.
    /// </summary>
    [Fact]
    public void TheSpecificationAnAudioSinkIsLentIsDetachedWhenPrepareReturns()
    {
        using Pipeline pipeline = Pipeline.New("lent-ring-buffer-spec");
        using StowingAudioSink sink = new();
        Element source = ElementFactory.Make("audiotestsrc", "audio-source")
            ?? throw new InvalidOperationException("audiotestsrc is part of the base plugins.");

        source.SetProperty("num-buffers", 10);
        Assert.True(pipeline.AddMany(source, sink));
        Assert.True(source.Link(sink));

        BusPump.RunToEos(pipeline, BusTimeout, _output);

        AudioRingBufferSpec kept = sink.Kept
            ?? throw new InvalidOperationException("The override stowed no specification.");

        _ = Assert.Throws<ObjectDisposedException>(() => kept.Segsize);
        _ = Assert.Throws<ObjectDisposedException>(() => kept.GetCaps());

        Assert.True(sink.SegsizeWhileRunning > 0);
    }
}
