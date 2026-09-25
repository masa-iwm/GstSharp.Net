using System.Runtime.CompilerServices;
using GES;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="BaseEffect.SetTimeTranslationFuncs"/>: the functions that make a
/// native effect a time effect, run by the clip that converts times through it.
/// </summary>
/// <remarks>
/// <para>
/// The functions are not called on a schedule the test controls: adding the
/// effect to a clip and setting one of its child properties both run the
/// computation of the duration limit of the clip, which translates times on
/// its own (<c>ges-clip.c:478</c>). The recording functions therefore keep what
/// the last call saw, and no test counts calls exactly.
/// </para>
/// <para>
/// A function is lent the values it is handed for the length of the call, so
/// what a test asserts on is read out inside the call into plain fields.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesBaseEffectTimeTranslationTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(1);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GesBaseEffectTimeTranslationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// The functions are set, the clip converts times through them in both
    /// directions, and each call is handed the effect and the values of the
    /// registered time property.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void TheClipConvertsTimesThroughTheFunctions()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using Effect effect = NewEffect();

        // The spelling the lookup of a child property accepts is the one the
        // registration takes, so a failure here names the cause.
        Assert.True(effect.LookupChild("brightness", out Gst.GObject.Object? child, out ParamSpec? pspec));
        child?.Dispose();
        pspec?.Dispose();
        Assert.True(effect.RegisterTimeProperty("brightness"));

        Recorder sinkToSource = new();
        Recorder sourceToSink = new();
        Assert.True(effect.SetTimeTranslationFuncs(
            Recording(sourceToSink, time => time / 2),
            Recording(sinkToSource, time => time * 2)));
        Assert.True(effect.IsTimeEffect());

        Assert.True(clip.AddTopEffect(effect, -1));

        using (Value brightness = Value.New(GType.Double))
        {
            brightness.SetDouble(0.25);
            effect.SetChildProperty("brightness", brightness);
        }

        using TrackElement source = CoreVideoSource(timeline, clip);
        ClockTime internalTime = ClockTime.FromSeconds(0.2);

        ClockTime timelineTime = clip.GetTimelineTimeFromInternalTime(source, internalTime);
        _output.WriteLine($"timeline from internal: {timelineTime.Nanoseconds}");
        Assert.Equal(internalTime.Nanoseconds * 2, timelineTime.Nanoseconds);
        sinkToSource.AssertSaw(effect, 0.25);

        ClockTime back = clip.GetInternalTimeFromTimelineTime(source, ClockTime.FromSeconds(0.4));
        _output.WriteLine($"internal from timeline: {back.Nanoseconds}");
        Assert.Equal(ClockTime.FromSeconds(0.2).Nanoseconds, back.Nanoseconds);
        sourceToSink.AssertSaw(effect, 0.25);
    }

    /// <summary>
    /// <see cref="ClockTime.None"/> from a function is the answer of the clip:
    /// the time cannot be converted.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void NoneFromAFunctionMeansTheTimeCannotBeConverted()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using Effect effect = NewEffect();
        Assert.True(effect.SetTimeTranslationFuncs((e, time, values) => ClockTime.None, null));
        Assert.True(clip.AddTopEffect(effect, -1));

        using TrackElement source = CoreVideoSource(timeline, clip);
        Assert.True(clip.GetInternalTimeFromTimelineTime(source, ClockTime.FromSeconds(0.2)).IsNone);

        // The other direction has no function and stays the identity.
        Assert.Equal(
            ClockTime.FromSeconds(0.2),
            clip.GetTimelineTimeFromInternalTime(source, ClockTime.FromSeconds(0.2)));
    }

    /// <summary>
    /// Setting new functions releases the ones set before, inside the call.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void NewFunctionsReleaseTheOldOnes()
    {
        GstGES.Initialize();

        using Effect effect = NewEffect();
        WeakReference first = Install(effect);

        Assert.True(effect.SetTimeTranslationFuncs((e, time, values) => time, null));

        Collect();
        Assert.False(first.IsAlive, "The functions set first outlived their replacement.");
    }

    /// <summary>
    /// An effect that is already in a clip refuses the functions, and nothing
    /// of them is kept.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AnEffectInAClipRefusesTheFunctions()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using Effect effect = NewEffect();
        Assert.True(clip.AddTopEffect(effect, -1));
        Assert.NotNull(effect.Parent);

        WeakReference refused = TryInstallQuietly(effect, out bool accepted);
        Assert.False(accepted);

        Collect();
        Assert.False(refused.IsAlive, "The refused functions were kept.");

        // The parent that was looked at is the caller's own wrapper, and it is
        // still usable afterwards.
        Assert.Same(clip, effect.Parent);
        Assert.Single(clip.GetTopEffects());
    }

    /// <summary>
    /// An effect whose <c>has-internal-source</c> is set refuses the functions,
    /// when the effect accepts that flag at all.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AnEffectWithAnInternalSourceRefusesTheFunctions()
    {
        GstGES.Initialize();

        using Effect effect = NewEffect();
        bool flagged = effect.SetHasInternalSource(true);
        _output.WriteLine($"has-internal-source accepted: {flagged}");
        if (!flagged)
        {
            // An effect may forbid the flag, and then there is nothing to test.
            return;
        }

        WeakReference refused = TryInstallQuietly(effect, out bool accepted);
        Assert.False(accepted);

        Collect();
        Assert.False(refused.IsAlive, "The refused functions were kept.");
    }

    /// <summary>
    /// Two <see langword="null"/> functions are accepted and clear the
    /// translation; the effect is a time effect again once a time property is
    /// registered.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void TwoNullFunctionsClearTheTranslation()
    {
        GstGES.Initialize();

        using Effect effect = NewEffect();
        WeakReference first = Install(effect);
        Assert.True(effect.IsTimeEffect());

        Assert.True(effect.SetTimeTranslationFuncs(null, null));
        Assert.False(effect.IsTimeEffect());

        Collect();
        Assert.False(first.IsAlive, "The cleared functions were kept.");

        Assert.True(effect.RegisterTimeProperty("brightness"));
        Assert.True(effect.IsTimeEffect());
    }

    /// <summary>
    /// An exception thrown by a function is reported, and the time is answered
    /// unchanged.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AnExceptionOfAFunctionIsReportedAndAnsweredWithTheIdentity()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        InvalidOperationException thrown = new("The time translation threw.");
        int reports = 0;

        void OnFailure(Exception exception)
        {
            // The event is process wide, so only the instance this test threw
            // says anything about this test.
            if (ReferenceEquals(exception, thrown))
            {
                Interlocked.Increment(ref reports);
            }
        }

        using Effect effect = NewEffect();
        ClockTime converted;

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            Assert.True(effect.SetTimeTranslationFuncs(null, (e, time, values) => throw thrown));
            Assert.True(clip.AddTopEffect(effect, -1));

            using TrackElement source = CoreVideoSource(timeline, clip);
            converted = clip.GetTimelineTimeFromInternalTime(source, ClockTime.FromSeconds(0.2));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.True(reports >= 1, $"The trap saw {reports} reports.");
        Assert.Equal(ClockTime.FromSeconds(0.2), converted);
    }

    /// <summary>
    /// Disposing an effect that was never added to a clip releases its
    /// functions.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void DisposingTheEffectReleasesTheFunctions()
    {
        GstGES.Initialize();

        Effect effect = NewEffect();
        WeakReference installed = Install(effect);
        effect.Dispose();

        Collect();
        Assert.False(installed.IsAlive, "The functions outlived the effect.");
    }

    /// <summary>Builds the video balance effect every test runs on.</summary>
    private static Effect NewEffect() =>
        Effect.New("videobalance") ?? throw new InvalidOperationException("The effect could not be created.");

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

    /// <summary>
    /// Sets functions whose only state is an object nothing else references,
    /// in a frame of its own so that no local of the test keeps it alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Install(BaseEffect effect)
    {
        WeakReference probe = TryInstall(effect, out bool accepted);
        Assert.True(accepted);
        return probe;
    }

    /// <summary>
    /// Tries to set functions whose only state is an object nothing else
    /// references, in a frame of its own.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TryInstall(BaseEffect effect, out bool accepted)
    {
        object marker = new();
        accepted = effect.SetTimeTranslationFuncs(
            (e, time, values) => marker.GetHashCode() >= 0 ? time : time,
            (e, time, values) => marker.GetHashCode() >= 0 ? time : time);
        return new WeakReference(marker);
    }

    /// <summary>
    /// Tries to set functions the effect is expected to refuse, and checks
    /// that the refusal came from the binding rather than from the C.
    /// </summary>
    /// <remarks>
    /// The C refuses the same effects with a critical
    /// (<c>ges-base-effect.c:309-311</c>) and returns before it stores the
    /// functions, so only the absence of that critical tells the pre-check of
    /// <see cref="BaseEffect.SetTimeTranslationFuncs"/> apart from the C. The
    /// net behind the pre-check, which frees the functions when the C refuses
    /// after all, is not reached by any test: the pre-check refuses first.
    /// </remarks>
    private WeakReference TryInstallQuietly(BaseEffect effect, out bool accepted)
    {
        WeakReference? refused = null;
        bool answer = true;
        IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(
            () => refused = TryInstall(effect, out answer));
        accepted = answer;

        _output.WriteLine($"log probe installed: {InitializeLogProbe.IsInstalled}, logged: {logged.Count}");
        if (InitializeLogProbe.IsInstalled)
        {
            Assert.DoesNotContain(
                logged,
                message => message.Contains("CRITICAL", StringComparison.Ordinal));
        }

        return refused!;
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Builds a function that records what it saw and computes its answer.</summary>
    private static BaseEffectTimeTranslationFunc Recording(Recorder recorder, Func<ulong, ulong> compute) =>
        (effect, time, values) =>
        {
            recorder.Calls++;
            recorder.EffectHandle = effect.Handle;
            recorder.Keys = string.Join(",", values.Keys);
            if (values.TryGetValue("brightness", out Value value))
            {
                recorder.ValueType = value.Type;
                recorder.Brightness = value.GetDouble();
            }

            return new ClockTime(compute(time.Nanoseconds));
        };

    /// <summary>What the last call of a recording function saw.</summary>
    private sealed class Recorder
    {
        public int Calls { get; set; }

        public nint EffectHandle { get; set; }

        public string? Keys { get; set; }

        public GType ValueType { get; set; }

        public double Brightness { get; set; }

        public void AssertSaw(BaseEffect effect, double brightness)
        {
            Assert.True(Calls > 0, "The function was never called.");
            Assert.Equal(effect.Handle, EffectHandle);
            Assert.Equal("brightness", Keys);
            Assert.Equal(GType.Double, ValueType);
            Assert.Equal(brightness, Brightness);
        }
    }
}
