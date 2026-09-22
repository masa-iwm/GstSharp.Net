using GES;
using Gst;
using Gst.Audio;
using Gst.GObject;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The lifecycle slots the base classes leave empty, chained up directly
/// rather than through a stream: each answers what the library reads an empty
/// slot as, and none of them throws.
/// </summary>
/// <remarks>
/// A chain-up reads the slot of the parent class, which is the base class
/// itself for every probe here, so no pipeline and no declared override is
/// needed to reach one. The slots a stream does reach are witnessed a second
/// time by the pipeline tests of the same probes.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SubclassLifecycleDefaultTests
{
    private const string AudioCaps =
        "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, rate=(int)44100, channels=(int)2";

    private const string VideoCaps =
        "video/x-raw, format=(string)I420, width=(int)320, height=(int)240, framerate=(fraction)30/1";

    /// <summary>
    /// The three lifecycle slots of a managed parser answer true rather than
    /// throwing.
    /// </summary>
    [Fact]
    public void TheLifecycleSlotsOfAParserAnswerTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeParse parser = new();
        using Caps caps = ParseCaps(AudioCaps);

        Assert.True(parser.ChainUpStartForTest());
        Assert.True(parser.ChainUpSetSinkCapsForTest(caps));
        Assert.True(parser.ChainUpStopForTest());
    });

    /// <summary>
    /// The lifecycle slots of a managed audio decoder answer true, including
    /// the <c>set_format</c> its encoder counterpart refuses to chain up.
    /// </summary>
    [Fact]
    public void TheLifecycleSlotsOfAnAudioDecoderAnswerTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeAudioDecoder decoder = new();
        using Caps caps = ParseCaps(AudioCaps);

        Assert.True(decoder.ChainUpOpenForTest());
        Assert.True(decoder.ChainUpCloseForTest());
        Assert.True(decoder.ChainUpStartForTest());
        Assert.True(decoder.ChainUpStopForTest());
        Assert.True(decoder.ChainUpSetFormatForTest(caps));
    });

    /// <summary>
    /// The four lifecycle slots of a managed audio encoder answer true;
    /// <c>set_format</c> is not among them, because the C refuses an empty one.
    /// </summary>
    [Fact]
    public void TheLifecycleSlotsOfAnAudioEncoderAnswerTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeAudioEncoder encoder = new();

        Assert.True(encoder.ChainUpOpenForTest());
        Assert.True(encoder.ChainUpCloseForTest());
        Assert.True(encoder.ChainUpStartForTest());
        Assert.True(encoder.ChainUpStopForTest());
    });

    /// <summary>The <c>setup</c> of a managed audio filter answers true.</summary>
    [Fact]
    public void TheSetupOfAnAudioFilterAnswersTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeAudioFilter filter = new();
        using Caps caps = ParseCaps(AudioCaps);
        using AudioInfo info = AudioInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("The audio info could not be read out of the caps.");

        Assert.True(filter.ChainUpSetupForTest(info));
    });

    /// <summary>
    /// The lifecycle slots of a managed video decoder answer true, and the two
    /// flow returning ones answer Ok.
    /// </summary>
    [Fact]
    public void TheLifecycleSlotsOfAVideoDecoderAnswerTheirDefault() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeVideoDecoder decoder = new();

        Assert.True(decoder.ChainUpOpenForTest());
        Assert.True(decoder.ChainUpCloseForTest());
        Assert.True(decoder.ChainUpStartForTest());
        Assert.True(decoder.ChainUpStopForTest());

        // flush and the deprecated reset are called bare by the base class and
        // what they answer is discarded, so true means "nothing below refused".
        Assert.True(decoder.ChainUpFlushForTest());
        Assert.True(decoder.ChainUpResetForTest(true));

        Assert.Equal(FlowReturn.Ok, decoder.ChainUpFinishForTest());
        Assert.Equal(FlowReturn.Ok, decoder.ChainUpDrainForTest());
    });

    /// <summary>The same for a managed video encoder, which has no drain.</summary>
    [Fact]
    public void TheLifecycleSlotsOfAVideoEncoderAnswerTheirDefault() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeVideoEncoder encoder = new();

        Assert.True(encoder.ChainUpOpenForTest());
        Assert.True(encoder.ChainUpCloseForTest());
        Assert.True(encoder.ChainUpStartForTest());
        Assert.True(encoder.ChainUpStopForTest());
        Assert.True(encoder.ChainUpFlushForTest());
        Assert.True(encoder.ChainUpResetForTest(true));
        Assert.Equal(FlowReturn.Ok, encoder.ChainUpFinishForTest());
    });

    /// <summary>The <c>set_info</c> of a managed video filter answers true.</summary>
    [Fact]
    public void TheSetInfoOfAVideoFilterAnswersTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeVideoFilter filter = new();
        using Caps caps = ParseCaps(VideoCaps);
        using VideoInfo inInfo = VideoInfoOf(caps);
        using VideoInfo outInfo = VideoInfoOf(caps);

        Assert.True(filter.ChainUpSetInfoForTest(caps, inInfo, caps, outInfo));
    });

    /// <summary>The <c>set_info</c> of a managed video sink answers true.</summary>
    [Fact]
    public void TheSetInfoOfAVideoSinkAnswersTrue() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeVideoSink sink = new();
        using Caps caps = ParseCaps(VideoCaps);
        using VideoInfo info = VideoInfoOf(caps);

        Assert.True(sink.ChainUpSetInfoForTest(caps, info));
    });

    /// <summary>
    /// The <c>filter_meta</c> of a managed transform answers false, which is
    /// the base class removing the metadata from the allocation query.
    /// </summary>
    [Fact]
    public void TheFilterMetaOfATransformAnswersFalse() => TrapWatch.NothingIsReported(() =>
    {
        using ProbeTransform transform = new();
        using Caps caps = ParseCaps(VideoCaps);
        using Query query = Query.NewAllocation(caps, false);

        // The C allows NULL parameters here; the generated one is not
        // nullable, so an empty structure stands for none.
        using Structure @params = Structure.NewEmpty("x");

        Assert.False(transform.ChainUpFilterMetaForTest(
            query,
            VideoGlobal.VideoMetaApiGetType(),
            @params));
    });

    /// <summary>
    /// The <c>select_pad</c> of a managed editing services source answers true,
    /// which is the caller accepting the pad.
    /// </summary>
    [Fact]
    public void TheSelectPadOfAManagedSourceAnswersTrue() => TrapWatch.NothingIsReported(() =>
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();
        using Pad pad = Pad.New("probe-src", PadDirection.Src)
            ?? throw new InvalidOperationException("The pad could not be created.");

        Assert.True(source.ChainUpSelectPadForTest(pad));
    });

    private static Caps ParseCaps(string description) =>
        Caps.FromString(description)
        ?? throw new InvalidOperationException($"The caps {description} could not be parsed.");

    private static VideoInfo VideoInfoOf(Caps caps) =>
        VideoInfo.NewFromCaps(caps)
        ?? throw new InvalidOperationException("The video info could not be read out of the caps.");
}
