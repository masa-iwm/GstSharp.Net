using System.Diagnostics;
using GES;
using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Managed subclasses of the effect classes of the editing services: a
/// <c>GESEffect</c> that keeps the inherited element, one that builds its own, a
/// direct <c>GESBaseEffect</c>, and the two clips that carry them.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs on the thread of the test and nothing here is
/// asynchronous, for the reason <see cref="GesSubclassTests"/> gives: the editing
/// services assert the thread a timeline was created on.
/// </para>
/// <para>
/// Three shapes would end the process rather than fail, if they reached the
/// library: an asset requested for a <c>GESEffect</c> or <c>GESEffectClip</c>
/// type with a <see langword="null"/> id (<c>ges-effect-asset.c:390-391</c>,
/// <c>ges-asset.c:752-766</c>), and an effect built with <c>new</c> - so
/// without an asset - added to a clip as a top effect
/// (<c>ges-clip.c:1786-1790</c>). The binding refuses all three before the
/// call, which is what the refusal tests below exercise: they are the one
/// place those shapes are spelled out, and the process surviving them is half
/// of what each asserts. See <c>docs/subclassing.md</c> §11.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesEffectSubclassTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(1);

    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(20);

    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

    /// <summary>
    /// An asset for the <c>GType</c> of a managed effect extracts an instance of
    /// that type, and its id is the bin description the instance reports.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert")]
    public void AnAssetForAManagedEffectExtractsThatType()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        Asset asset = Assert.IsAssignableFrom<Asset>(
            Asset.Request(ProbeEffect.Registration.GType, "video videobalance"));

        Assert.Equal(ProbeEffect.Registration.GType, asset.ExtractableType);

        ProbeEffect effect = asset.Extract<ProbeEffect>();

        using (effect)
        {
            Assert.Equal("videobalance", effect.BinDescription);
            Assert.Equal(TrackType.Video, effect.TrackType);
            Assert.Same(asset, effect.GetAsset());
        }

        // The asset is keyed on the requested type as well as on the id, so the
        // native class answers an asset of its own for the same description.
        GType nativeEffect = GType.FromName("GESEffect");
        Asset native = Assert.IsAssignableFrom<Asset>(Asset.Request(nativeEffect, "video videobalance"));

        Assert.NotSame(asset, native);
        Assert.Equal(nativeEffect, native.ExtractableType);
    }

    /// <summary>
    /// A managed effect joins a native clip as a top effect, and the clip hands
    /// the very wrapper back; removing it again leaves the wrapper usable.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AManagedEffectJoinsANativeClipAsATopEffect()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using ProbeEffect effect = ProbeEffect.NewFromDescription("video videobalance");

        Assert.True(clip.AddTopEffect(effect, -1));

        TrackElement only = Assert.Single(clip.GetTopEffects());
        Assert.Same(effect, only);
        Assert.False(effect.IsCore());
        Assert.NotNull(effect.GetNleobject());
        Assert.Same(clip, effect.Parent);
        Assert.True(effect.SetParentCalls >= 1);
        Assert.False(effect.LastParentWasNull);

        Assert.True(clip.RemoveTopEffect(effect));

        // The wrapper holds a reference of its own - Extract sank the floating
        // instance into it - so losing the clip costs it nothing.
        Assert.Null(effect.Parent);
        Assert.True(effect.LastParentWasNull);
        Assert.Equal("videobalance", effect.BinDescription);
    }

    /// <summary>
    /// Disposing the wrapper of an effect the clip holds leaves the effect in the
    /// clip, dispatched through the static chain-up alone — and removing it again
    /// still works, with nothing reported and no parent left behind.
    /// </summary>
    /// <remarks>
    /// This is the ownership rule after <c>AddTopEffect</c>: the clip took a
    /// reference of its own (<c>ges-container.c:733</c>), so the effect outlives
    /// the wrapper, and no second wrapper is fabricated for a disposed one. The
    /// removal calls <c>set_parent</c> with no parent, which the chain-up answers
    /// for; a refusal there is ignored by the container
    /// (<c>ges-container.c:127-130</c>) and would leave a stale parent pointer.
    /// </remarks>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void ADisposedEffectWrapperLeavesTheEffectRemovable()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        ProbeEffect effect = ProbeEffect.NewFromDescription("video videobalance");
        Assert.True(clip.AddTopEffect(effect, -1));
        Assert.True(effect.SetParentCalls >= 1);

        List<Exception> reported = [];

        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            effect.Dispose();

            TrackElement single = Assert.Single(clip.GetTopEffects());

            // A disposed wrapper is not fabricated again: what the clip hands
            // back is a plain wrapper over an instance of the managed GType.
            Assert.IsNotType<ProbeEffect>(single);

            using BaseEffect only = Assert.IsAssignableFrom<BaseEffect>(single);
            Assert.Equal(ProbeEffect.Registration.GType, only.NativeType);

            Assert.True(clip.RemoveTopEffect(only));
            Assert.Null(only.Parent);
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (reported)
        {
            Assert.Empty(reported);
        }
    }

    /// <summary>
    /// A child property of the element a managed effect inherited is written and
    /// read through the effect itself, which is the native
    /// <c>set_child_property_full</c> the managed type inherits.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AChildPropertyOfAManagedEffectReachesTheElement()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using ProbeEffect effect = ProbeEffect.NewFromDescription("video videobalance");
        Assert.True(clip.AddTopEffect(effect, -1));

        using (Value written = Value.New(GType.Double))
        {
            written.SetDouble(1.25);

            // The inherited create_element registered the properties of the bin
            // it built as child properties (ges-effect.c:338).
            Assert.True(effect.SetChildPropertyFull("saturation", written));
        }

        using Value read = effect.GetChildProperty("saturation");

        Assert.Equal(GType.Double, read.Type);
        Assert.Equal(1.25, read.GetDouble());
    }

    /// <summary>
    /// The <c>create_element</c> override of a managed effect runs while the
    /// instance is being extracted, on the thread that asked for it.
    /// </summary>
    [RequiresElementFact("identity")]
    public void TheElementOverrideOfAManagedEffectRunsDuringExtraction()
    {
        GstGES.Initialize();
        ProbeElementEffect.Reset();

        using ProbeElementEffect effect = ProbeElementEffect.NewFromDescription("video identity");

        Assert.Equal(1, ProbeElementEffect.CreateElementCalls);
        Assert.Equal(Environment.CurrentManagedThreadId, ProbeElementEffect.ManagedThreadId);
        Assert.True(ProbeElementEffect.SawNoParent);

        // The dispatch found the wrapper the extraction is building, not a
        // second one for the same instance.
        Assert.Same(effect, ProbeElementEffect.Caller);
    }

    /// <summary>
    /// An override that throws is trapped and substituted for: the extraction
    /// answers an instance all the same, with an <c>identity</c> behind it.
    /// </summary>
    /// <remarks>
    /// The shape of <c>AnElementThatThrowsIsGivenAnIdentity</c> in
    /// <see cref="GesSubclassTests"/>, one class family over.
    /// </remarks>
    [RequiresElementFact("identity")]
    public void AManagedEffectThatThrowsIsGivenAnIdentity()
    {
        GstGES.Initialize();
        ProbeElementEffect.Reset();
        ProbeElementEffect.Throws = true;

        List<Exception> reported = [];

        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            using ProbeElementEffect effect = ProbeElementEffect.NewFromDescription("video identity");

            Assert.Equal(1, ProbeElementEffect.CreateElementCalls);

            using Element element = effect.GetElement()
                ?? throw new InvalidOperationException("The track element was given no element.");

            ElementFactory factory = element.GetFactory()
                ?? throw new InvalidOperationException("The substitute was built by no factory.");

            Assert.Equal("identity", factory.GetName());
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
            ProbeElementEffect.Throws = false;
        }

        lock (reported)
        {
            Exception only = Assert.Single(reported);
            InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(only);
            Assert.Equal(ProbeElementEffect.RefusalMessage, refusal.Message);
        }
    }

    /// <summary>
    /// Splitting a clip copies its top effects natively, so the managed effect is
    /// copied too and the copy is a second wrapper of the managed type.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void SplittingAClipCopiesItsManagedEffect()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using ProbeEffect effect = ProbeEffect.NewFromDescription("video videobalance");
        Assert.True(clip.AddTopEffect(effect, -1));

        int before = ProbeEffect.WrappersBuilt;

        Clip? split = clip.Split(ClockTime.FromMilliseconds(500).Nanoseconds);
        TestClip copy = Assert.IsType<TestClip>(split);

        using (copy)
        {
            TrackElement only = Assert.Single(copy.GetTopEffects());
            ProbeEffect copied = Assert.IsType<ProbeEffect>(only);

            // The library extracted the copy itself (ges-clip.c:2385-2407), so
            // its wrapper was fabricated rather than asked for.
            Assert.NotSame(effect, copied);
            Assert.True(ProbeEffect.WrappersBuilt > before);

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            copied.Dispose();
        }
    }

    /// <summary>
    /// A direct <c>GESBaseEffect</c> subclass has no description to take a track
    /// type from, so it takes it from its asset, and is a usable top effect.
    /// </summary>
    [RequiresElementFact("identity", "videotestsrc", "audiotestsrc")]
    public void ADirectBaseEffectSubclassTakesItsTrackTypeFromTheAsset()
    {
        GstGES.Initialize();
        ProbeBaseEffect.Reset();

        // A null id is legal here: this class parses nothing.
        Asset asset = Assert.IsAssignableFrom<Asset>(
            Asset.Request(ProbeBaseEffect.Registration.GType, null));

        TrackElementAsset trackAsset = Assert.IsAssignableFrom<TrackElementAsset>(asset);

        // set_asset copies the track type of the asset into every instance whose
        // own is still unknown (ges-track-element.c:286-290).
        trackAsset.SetTrackType(TrackType.Video);

        using ProbeBaseEffect effect = trackAsset.Extract<ProbeBaseEffect>();

        Assert.Equal(TrackType.Video, effect.TrackType);
        Assert.True(ProbeBaseEffect.CreateElementCalls >= 1);

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));
        Assert.True(clip.AddTopEffect(effect, -1));

        Assert.Same(effect, Assert.Single(clip.GetTopEffects()));
    }

    /// <summary>
    /// A managed effect clip is given the child its <c>create_track_element</c>
    /// override extracted, and that child is a core child of the clip.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert")]
    public void AManagedEffectClipGetsTheCoreChildItsOverrideExtracted()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeEffectClip clip = ProbeEffectClip.New();

        using (clip)
        {
            PrepareForVideo(clip);
            Assert.True(layer.AddClip(clip));

            TimelineElement only = Assert.Single(clip.GetChildren(false));
            ProbeEffect child = Assert.IsType<ProbeEffect>(only);

            Assert.Same(clip.AnsweredChild, child);

            // The clip stamped the child with its own asset, which is what makes
            // it a core child rather than a top effect
            // (ges-clip.c:2785-2789, :1697-1700).
            Assert.True(child.IsCore());

            // The override built this wrapper and the clip took a reference of
            // its own, so it goes before the timeline does.
            child.Dispose();
        }
    }

    /// <summary>
    /// A managed <c>GESEffectClip</c> subtype that declares nothing keeps the
    /// native child the inherited slot builds, and an empty id is a clip with no
    /// description and no child at all.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert")]
    public void AManagedEffectClipSubtypeKeepsTheNativeChild()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeNativeEffectClip clip = ProbeNativeEffectClip.New("video videobalance");

        using (clip)
        {
            Assert.Equal("videobalance", clip.VideoBinDescription);

            PrepareForVideo(clip);
            Assert.True(layer.AddClip(clip));

            TimelineElement only = Assert.Single(clip.GetChildren(false));

            // The inherited slot extracts a GESEffect and nothing derived from
            // it: the description is what the clip passes on, not its own type.
            Assert.Equal(GType.FromName("GESEffect"), only.NativeType);

            only.Dispose();
        }

        using Layer empty = timeline.AppendLayer();

        ProbeNativeEffectClip silent = ProbeNativeEffectClip.New(string.Empty);

        using (silent)
        {
            Assert.Null(silent.VideoBinDescription);
            Assert.Null(silent.AudioBinDescription);

            PrepareForVideo(silent);
            Assert.True(empty.AddClip(silent));
            Assert.Empty(silent.GetChildren(false));
        }
    }

    /// <summary>
    /// An id that carries both halves sets both descriptions — as long as no space
    /// follows the bars, which is what the grammar of such an id turns on.
    /// </summary>
    /// <remarks>
    /// The half after the bars is split on its first space
    /// (<c>ges-effect-asset.c:390-397</c>), so a space there leaves an empty first
    /// token, the half has no type, the whole of it is parsed as a bin instead and
    /// it is dropped with nothing but a log line. The library's own writers emit no
    /// space either (<c>ges-effect-clip.c:122-124</c>).
    /// </remarks>
    [RequiresElementFact("videobalance", "audioconvert")]
    public void AnEffectClipIdWithBothHalvesSetsBothDescriptions()
    {
        GstGES.Initialize();

        ProbeNativeEffectClip clip = ProbeNativeEffectClip.New("audio audioconvert ||video videobalance");

        using (clip)
        {
            // The space before the bars belongs to the half it ends, which is why
            // the audio description carries a trailing one.
            Assert.Equal("audioconvert ", clip.AudioBinDescription);
            Assert.Equal("videobalance", clip.VideoBinDescription);
        }
    }

    /// <summary>
    /// An id whose description names nothing installed is refused rather than
    /// answered, and the refusal costs the process nothing.
    /// </summary>
    [Fact]
    public void AnIdThatDoesNotParseIsRefusedWithoutACrash()
    {
        GstGES.Initialize();

        // check_id instantiates the description on every request
        // (ges-effect-asset.c:405, ges-asset.c:1263), so the refusal arrives
        // here rather than when an element is finally built.
        Asset? asset;

        try
        {
            asset = Asset.Request(ProbeEffect.Registration.GType, "video nosuchelement_row45");
        }
        catch (Gst.GLib.GException)
        {
            return;
        }

        Assert.Null(asset);
    }

    /// <summary>
    /// A timeline whose clip carries a managed effect that keeps the inherited
    /// element renders to the end of stream.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc", "fakesink")]
    public void ATimelineWithAManagedEffectRendersToTheEnd()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        AssertATimelineRendersWith(static () => ProbeEffect.NewFromDescription("video videobalance"));
    }

    /// <summary>
    /// The same with an effect whose <c>create_element</c> override replaces the
    /// description with an <c>identity</c>.
    /// </summary>
    [RequiresElementFact("identity", "videoconvert", "videotestsrc", "audiotestsrc", "fakesink")]
    public void ATimelineWithAManagedElementEffectRendersToTheEnd()
    {
        GstGES.Initialize();
        ProbeElementEffect.Reset();

        AssertATimelineRendersWith(static () => ProbeElementEffect.NewFromDescription("video identity"));

        // Without this the test would also pass with the override slot absent,
        // where the inherited create_element parses the description instead.
        Assert.True(ProbeElementEffect.CreateElementCalls >= 1);
    }

    /// <summary>
    /// The same with a direct <c>GESBaseEffect</c> subclass, whose track type
    /// comes from its asset.
    /// </summary>
    [RequiresElementFact("identity", "videotestsrc", "audiotestsrc", "fakesink")]
    public void ATimelineWithADirectBaseEffectRendersToTheEnd()
    {
        GstGES.Initialize();
        ProbeBaseEffect.Reset();

        AssertATimelineRendersWith(static () =>
        {
            Asset asset = GES.Asset.Request(ProbeBaseEffect.Registration.GType, null)
                ?? throw new InvalidOperationException("The effect asset could not be requested.");

            TrackElementAsset trackAsset = (TrackElementAsset)asset;
            trackAsset.SetTrackType(TrackType.Video);

            return trackAsset.Extract<ProbeBaseEffect>();
        });

        Assert.True(ProbeBaseEffect.CreateElementCalls >= 1);
    }

    /// <summary>
    /// Plays a one-second timeline whose only clip carries the effect the factory
    /// answers, and asserts that it reaches the end of stream.
    /// </summary>
    /// <param name="newEffect">Builds the effect to add as a top effect.</param>
    /// <remarks>
    /// The run loop of <c>samples/GesCustomSource</c>: both previews go to a
    /// <c>fakesink</c>, so the run is headless, and the pipeline is back in the
    /// NULL state before anything is released — a pipeline that is still playing
    /// when its last reference goes away leaves its streaming threads running.
    /// </remarks>
    private static void AssertATimelineRendersWith(Func<BaseEffect> newEffect)
    {
        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip clip = NewTestClip();
        Assert.True(layer.AddClip(clip));

        using BaseEffect effect = newEffect();
        Assert.True(clip.AddTopEffect(effect, -1));

        using GES.Pipeline pipeline = GES.Pipeline.New();

        try
        {
            Assert.True(pipeline.SetTimeline(timeline));

            // A preview sink can only be chosen while the pipeline is in NULL,
            // and each preview needs a sink of its own.
            using Element videoSink = ElementFactory.Make("fakesink", null)
                ?? throw new InvalidOperationException("fakesink is not installed.");
            using Element audioSink = ElementFactory.Make("fakesink", null)
                ?? throw new InvalidOperationException("fakesink is not installed.");

            videoSink.SetProperty("sync", false);
            audioSink.SetProperty("sync", false);
            pipeline.PreviewSetVideoSink(videoSink);
            pipeline.PreviewSetAudioSink(audioSink);

            // The bus wrapper is interned, shared with every other lookup of the
            // same bus, so it is not disposed here.
            Bus bus = pipeline.GetBus();

            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < RenderTimeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    PollInterval,
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    continue;
                }

                Assert.Equal(MessageType.Eos, message.Type);
                return;
            }

            Assert.Fail($"No end of stream within {RenderTimeout.TotalSeconds:F0} s.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// A null id is refused for a GESEffect type, the managed one and the
    /// native one alike, before it reaches the library that dereferences it.
    /// </summary>
    [Fact]
    public void ANullIdIsRefusedForAnEffectType()
    {
        GstGES.Initialize();

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => Asset.Request(ProbeEffect.Registration.GType, null));

        Assert.Equal("id", refused.ParamName);
        Assert.Contains(ProbeEffect.GTypeName, refused.Message, StringComparison.Ordinal);

        // check_id belongs to the native class, so the native type is no more
        // exempt than a subtype of it (ges-effect.c:138-144).
        ArgumentException native = Assert.Throws<ArgumentException>(
            () => Asset.Request(GType.FromName("GESEffect"), null));

        Assert.Equal("id", native.ParamName);
        Assert.Contains("GESEffect", native.Message, StringComparison.Ordinal);

        // The process is still here, which is the other half of the assertion:
        // a type that parses nothing still takes the null id.
        Assert.NotNull(Asset.Request(ProbeBaseEffect.Registration.GType, null));
    }

    /// <summary>
    /// A null id is refused for a GESEffectClip type as well, where the library
    /// would hash the null key of the wrong-id asset instead.
    /// </summary>
    [Fact]
    public void ANullIdIsRefusedForAnEffectClipType()
    {
        GstGES.Initialize();

        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => Asset.Request(ProbeNativeEffectClip.Registration.GType, null));

        Assert.Equal("id", refused.ParamName);
        Assert.Contains(ProbeNativeEffectClip.GTypeName, refused.Message, StringComparison.Ordinal);

        ArgumentException native = Assert.Throws<ArgumentException>(
            () => Asset.Request(GType.FromName("GESEffectClip"), null));

        Assert.Equal("id", native.ParamName);
        Assert.Contains("GESEffectClip", native.Message, StringComparison.Ordinal);

        // The emptiest id such a type takes is the empty string, and the
        // process is there to take it.
        using Asset empty = Assert.IsAssignableFrom<Asset>(
            Asset.Request(ProbeNativeEffectClip.Registration.GType, string.Empty));

        Assert.Equal(string.Empty, empty.Id);
    }

    /// <summary>
    /// The reload request runs the same check as the request and is refused the
    /// same way.
    /// </summary>
    [Fact]
    public void ANullIdIsRefusedByTheReloadRequest()
    {
        GstGES.Initialize();

        // ges-asset.c:1533 is the same _check_and_update_parameters as the
        // request makes at :1263.
        Assert.Equal(
            "id",
            Assert.Throws<ArgumentException>(
                () => Asset.NeedsReload(ProbeEffect.Registration.GType, null)).ParamName);

        Assert.Equal(
            "id",
            Assert.Throws<ArgumentException>(
                () => Asset.NeedsReload(ProbeNativeEffectClip.Registration.GType, null)).ParamName);

        // A type that parses nothing is left alone by the same check. That the
        // library still answers a null id for such a type is asserted through
        // the request itself, in ANullIdIsRefusedForAnEffectType, rather than
        // here, where a reload would mark whatever the cache holds by now.
    }

    /// <summary>
    /// Both asynchronous request overloads refuse the null id where the caller
    /// stands, rather than handing back a task that fails later.
    /// </summary>
    [Fact]
    public void ANullIdIsRefusedByTheAsynchronousRequest()
    {
        GstGES.Initialize();

        Assert.Equal(
            "id",
            Assert.Throws<ArgumentException>(
                () => { _ = Asset.RequestAsync(ProbeEffect.Registration.GType, null); }).ParamName);

        using Gst.Gio.Cancellable cancellable = Gst.Gio.Cancellable.New();

        Assert.Equal(
            "id",
            Assert.Throws<ArgumentException>(
                () =>
                {
                    _ = Asset.RequestAsync(
                        ProbeNativeEffectClip.Registration.GType,
                        null,
                        cancellable);
                }).ParamName);

        // Nothing was started, so there is no task to await and no callback
        // left on the dispatcher.
        Assert.False(cancellable.IsCancelled);
    }

    /// <summary>
    /// An effect that was built with <c>new</c>, and so has no asset, is
    /// refused by <c>AddTopEffect</c> instead of taking the process down.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AnEffectWithNoAssetIsRefusedByAddTopEffect()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using TestClip clip = NewTestClip();
        using ProbeEffect assetless = ProbeEffect.NewWithoutAsset();

        Assert.Null(assetless.GetAsset());

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => clip.AddTopEffect(assetless, -1));

        Assert.Contains("GES.Asset.Request", refused.Message, StringComparison.Ordinal);
        Assert.Empty(clip.GetTopEffects());

        // The clip is unharmed and still takes an effect that has an asset,
        // which is the proof that nothing wider was refused.
        using ProbeEffect fromAsset = ProbeEffect.NewFromDescription("video videobalance");

        Assert.True(clip.AddTopEffect(fromAsset, -1));
        Assert.True(clip.RemoveTopEffect(fromAsset));
    }

    /// <summary>
    /// The same refusal answers <c>Container.Add</c>, which is the other way to
    /// reach the branch that reads the missing asset.
    /// </summary>
    [RequiresElementFact("videobalance", "videoconvert", "videotestsrc", "audiotestsrc")]
    public void AnEffectWithNoAssetIsRefusedByContainerAdd()
    {
        GstGES.Initialize();
        ProbeEffect.Reset();

        using TestClip clip = NewTestClip();
        using ProbeEffect assetless = ProbeEffect.NewWithoutAsset();

        InvalidOperationException refused =
            Assert.Throws<InvalidOperationException>(() => clip.Add(assetless));

        Assert.Contains("GES.Asset.Request", refused.Message, StringComparison.Ordinal);
        Assert.Empty(clip.GetChildren(false));

        // A container that is not a clip reads no asset at all, so a group
        // still takes whatever it is given - here, the clip itself.
        using Group group = Assert.IsAssignableFrom<Group>(Group.New());

        Assert.True(group.Add(clip));
        Assert.True(group.Remove(clip));
    }

    /// <summary>Builds a one-second audio and video test clip.</summary>
    /// <returns>The clip, which has an asset.</returns>
    private static TestClip NewTestClip()
    {
        TestClip clip = TestClip.New()
            ?? throw new InvalidOperationException("The test clip could not be created.");

        Assert.True(clip.SetStart(ClockTime.Zero));
        Assert.True(clip.SetDuration(Length));

        return clip;
    }

    /// <summary>
    /// Makes a clip a video-only clip of a known length, so that
    /// <c>create_track_element</c> is asked for the video track alone.
    /// </summary>
    /// <param name="clip">The clip to prepare.</param>
    private static void PrepareForVideo(Clip clip)
    {
        clip.SupportedFormats = TrackType.Video;
        Assert.True(clip.SetStart(ClockTime.Zero));
        Assert.True(clip.SetDuration(Length));
    }
}
