using GES;
using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed effect that makes itself a time effect in its
/// <c>create_element</c> override (<see cref="ProbeTimeEffect"/>), and what a
/// clip does with it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs on the thread of the test, because the editing
/// services assert the thread a timeline was created on.
/// </para>
/// <para>
/// The functions are not called on a schedule the test controls: adding the
/// effect to a clip and setting its time property both recompute the duration
/// limit of the clip, which translates times on its own
/// (<c>ges-clip.c:478</c>). No test counts translation calls exactly.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesManagedTimeEffectTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(1);

    private static readonly ClockTime Half = ClockTime.FromMilliseconds(500);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GesManagedTimeEffectTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The override registers the time property and sets the functions while
    /// the effect is extracted, with no parent and no internal source, and the
    /// effect is a time effect from then on.
    /// </summary>
    [RequiresElementFact("videorate", "capsfilter", "videotestsrc", "audiotestsrc")]
    public void AManagedTimeEffectRegistersItselfDuringExtraction()
    {
        GstGES.Initialize();
        ProbeTimeEffect.Reset();

        using ProbeTimeEffect effect = ProbeTimeEffect.New();

        Assert.Equal(1, ProbeTimeEffect.CreateElementCalls);
        Assert.True(ProbeTimeEffect.SawNoParent);
        Assert.True(ProbeTimeEffect.SawNoInternalSource);
        Assert.True(ProbeTimeEffect.RegisteredTimeProperty);
        Assert.True(ProbeTimeEffect.SetTranslationFuncs);
        Assert.True(effect.IsTimeEffect());
        Assert.Equal(TrackType.Video, effect.TrackType);

        Assert.True(effect.LookupChild(
            ProbeTimeEffect.ChildPropertyName,
            out Gst.GObject.Object? child,
            out ParamSpec? pspec));
        child?.Dispose();
        pspec?.Dispose();

        // A time effect forbids the flag (ges-track-element.c:934-937).
        Assert.False(effect.SetHasInternalSource(true));
        Assert.False(effect.HasInternalSource());
    }

    /// <summary>
    /// The clip converts times in both directions through the functions the
    /// override set, handing them the value of the registered property.
    /// </summary>
    [RequiresElementFact("videorate", "capsfilter", "videotestsrc", "audiotestsrc")]
    public void TheClipConvertsTimesThroughAManagedTimeEffect()
    {
        GstGES.Initialize();
        ProbeTimeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using ProbeTimeEffect effect = ProbeTimeEffect.New();
        Assert.True(clip.AddTopEffect(effect, -1));
        SetRate(effect, 2.0);

        using TrackElement source = CoreVideoSource(timeline, clip);

        // Sink to source (ges-clip.c:4127-4134).
        ClockTime timelineTime = clip.GetTimelineTimeFromInternalTime(source, ClockTime.FromMilliseconds(200));
        _output.WriteLine($"timeline from internal: {timelineTime.Nanoseconds}");
        Assert.Equal(ClockTime.FromMilliseconds(100), timelineTime);

        // Source to sink (ges-clip.c:4271-4278).
        ClockTime internalTime = clip.GetInternalTimeFromTimelineTime(source, ClockTime.FromMilliseconds(100));
        _output.WriteLine($"internal from timeline: {internalTime.Nanoseconds}");
        Assert.Equal(ClockTime.FromMilliseconds(200), internalTime);

        Assert.Equal(2.0, ProbeTimeEffect.LastSeenRate);
        Assert.True(ProbeTimeEffect.TranslationCalls > 0, "No translation function ran.");
    }

    /// <summary>
    /// The duration limit of the clip follows the registered property, and the
    /// clip trims itself to a shorter limit.
    /// </summary>
    /// <remarks>
    /// The clip goes into the layer before its maximum duration is set, because
    /// the maximum duration reaches only the children the clip already has
    /// (<c>ges-clip.c:1412-1450</c>), and the maximum duration is set before the
    /// effect is added.
    /// </remarks>
    [RequiresElementFact("videorate", "capsfilter", "videotestsrc", "audiotestsrc")]
    public void TheDurationLimitOfTheClipFollowsTheRegisteredProperty()
    {
        GstGES.Initialize();
        ProbeTimeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        // The test sources have an internal source (ges-source.c:332), so the
        // maximum duration of the clip reaches them.
        Assert.True(clip.SetMaxDuration(Length));
        Assert.Equal(Length, clip.DurationLimit);
        Assert.Equal(Length, clip.Duration);

        using ProbeTimeEffect effect = ProbeTimeEffect.New();
        Assert.True(clip.AddTopEffect(effect, -1));
        Assert.Equal(Length, clip.DurationLimit);

        // The clip listens to the property because the effect was a time
        // effect when it was added (ges-clip.c:2041-2043), and trims itself to
        // the shorter limit (ges-clip.c:561-576).
        SetRate(effect, 2.0);
        Assert.Equal(Half, clip.DurationLimit);
        Assert.Equal(Half, clip.Duration);

        // The trim is one way.
        SetRate(effect, 1.0);
        Assert.Equal(Length, clip.DurationLimit);
        Assert.Equal(Half, clip.Duration);

        // None from sink to source removes the limit of the video track only
        // (ges-clip.c:478); the audio source still limits the clip
        // (ges-clip.c:299-301, :518).
        SetRate(effect, 0.0);
        Assert.Equal(0.0, ProbeTimeEffect.LastSeenRate);
        Assert.Equal(Length, clip.DurationLimit);
        Assert.Equal(Half, clip.Duration);
    }

    /// <summary>
    /// Splitting a clip extracts a copy of the effect, which runs the override
    /// again and is a time effect with the same rate.
    /// </summary>
    [RequiresElementFact("videorate", "capsfilter", "videotestsrc", "audiotestsrc")]
    public void SplittingAClipCopiesAManagedTimeEffectAsATimeEffect()
    {
        GstGES.Initialize();
        ProbeTimeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using ProbeTimeEffect effect = ProbeTimeEffect.New();
        Assert.True(clip.AddTopEffect(effect, -1));
        SetRate(effect, 2.0);

        int calls = ProbeTimeEffect.CreateElementCalls;

        Clip? split = clip.Split(Half.Nanoseconds);
        TestClip copy = Assert.IsType<TestClip>(split);

        using (copy)
        {
            TrackElement only = Assert.Single(copy.GetTopEffects());
            ProbeTimeEffect copied = Assert.IsType<ProbeTimeEffect>(only);

            Assert.True(copied.IsTimeEffect());

            // One copy per top effect (ges-clip.c:2385-2407), each a fresh
            // extraction that runs the override once.
            Assert.Equal(calls + 1, ProbeTimeEffect.CreateElementCalls);

            // The copy carries the child properties over
            // (ges-track-element.c:1635-1645).
            using (Value rate = copied.GetChildProperty(ProbeTimeEffect.ChildPropertyName))
            {
                Assert.Equal(2.0, rate.GetDouble());
            }

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            copied.Dispose();
        }
    }

    /// <summary>Writes the rate of the effect through its child property.</summary>
    private static void SetRate(BaseEffect effect, double rate)
    {
        using Value value = Value.New(GType.Double);
        value.SetDouble(rate);
        effect.SetChildProperty(ProbeTimeEffect.ChildPropertyName, value);
    }

    /// <summary>Builds a test clip of a known length at the start of the timeline.</summary>
    private static TestClip NewTestClip()
    {
        TestClip clip = TestClip.New()
            ?? throw new InvalidOperationException("The test clip could not be created.");

        Assert.True(clip.SetStart(ClockTime.Zero));
        Assert.True(clip.SetDuration(Length));

        return clip;
    }

    /// <summary>Finds the core child of a clip in the video track of a timeline.</summary>
    private static TrackElement CoreVideoSource(Timeline timeline, TestClip clip)
    {
        Track video = Assert.Single(timeline.GetTracks(), track => track.TrackType == TrackType.Video);
        return clip.FindTrackElement(video, GType.FromName("GESVideoSource"))
            ?? throw new InvalidOperationException("The clip has no video source.");
    }
}
