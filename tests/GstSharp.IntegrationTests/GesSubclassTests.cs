using GES;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Managed subclasses of the editing services: a <c>GESSourceClip</c> that
/// builds its own track element and a <c>GESVideoSource</c> that answers the
/// element behind it.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs on the thread of the test and nothing here is
/// asynchronous. The editing services assert the thread a timeline and its
/// tracks were created on, so a timeline built on the test thread may only be
/// changed there — a <c>Task.Run</c> around any of this would abort the process
/// rather than fail a test.
/// </para>
/// <para>
/// The clips of these tests are extracted from an asset for their own type,
/// which is the same contract their children follow. It is what a split needs:
/// copying a timeline element asserts that the element has an asset.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesSubclassTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(2);

    /// <summary>
    /// Adding a managed clip to a layer runs its <c>create_track_element</c>
    /// override, and the child the clip is given is the very wrapper the
    /// override answered.
    /// </summary>
    [Fact]
    public void AddingAManagedClipGivesItTheChildItsOverrideExtracted()
    {
        GstGES.Initialize();
        ProbeVideoSource.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeSourceClip clip = ProbeSourceClip.New();

        using (clip)
        {
            PrepareForVideo(clip);
            Assert.True(layer.AddClip(clip));

            TimelineElement only = Assert.Single(clip.GetChildren(false));
            ProbeVideoSource child = Assert.IsType<ProbeVideoSource>(only);

            // The interning is the point: the child the library handed back is
            // the instance the override built, not a second wrapper for it.
            Assert.Same(clip.AnsweredChild, child);
            Assert.Equal(1, ProbeVideoSource.WrappersBuilt);
            Assert.Equal(TrackType.Video, child.TrackType);

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            child.Dispose();
        }
    }

    /// <summary>
    /// Splitting a managed clip builds a second clip and a second child of the
    /// managed types, natively, and carries a managed property over to the copy.
    /// </summary>
    [Fact]
    public void SplittingAManagedClipCopiesTheTypesAndTheManagedProperty()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeSourceClip clip = ProbeSourceClip.New();

        using (clip)
        {
            PrepareForVideo(clip);
            clip.SetProperty("probe-tag", "carried");
            Assert.True(layer.AddClip(clip));

            Clip? split = clip.Split(ClockTime.FromSeconds(1).Nanoseconds);
            ProbeSourceClip copy = Assert.IsType<ProbeSourceClip>(split);

            // ges_timeline_element_copy writes every readable and writable
            // property of the class onto the copy, the managed ones included,
            // once the copy has been extracted and so has a wrapper.
            Assert.Equal("carried", copy.Tag);
            Assert.Equal("carried", copy.GetProperty<string>("probe-tag"));

            TimelineElement only = Assert.Single(copy.GetChildren(false));
            ProbeVideoSource copiedChild = Assert.IsType<ProbeVideoSource>(only);

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            copiedChild.Dispose();
            copy.Dispose();
            clip.AnsweredChild?.Dispose();
        }
    }

    /// <summary>
    /// A child the override built itself has no asset, so no track takes it:
    /// the add fails, the child is removed again and the clip leaves the layer.
    /// </summary>
    [Fact]
    public void AChildNoAssetBuiltCostsTheClipItsAdd()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeNewChildSourceClip clip = ProbeNewChildSourceClip.New();

        using (clip)
        {
            PrepareForVideo(clip);

            // ges_layer_add_clip_full removes every child that was created for
            // the failed add and then removes the clip from the layer.
            Assert.False(layer.AddClip(clip));

            Assert.NotNull(clip.AnsweredChild);
            Assert.Empty(clip.GetChildren(false));
            Assert.Empty(layer.GetClips());
            Assert.Null(clip.Layer);

            // This one the test built, and no container kept it.
            clip.AnsweredChild.Dispose();
        }
    }

    /// <summary>
    /// Removing the child from the clip tells the child, through its
    /// <c>set_parent</c> override, that it has no parent any more.
    /// </summary>
    [Fact]
    public void RemovingTheChildTellsItThatItHasNoParent()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeSourceClip clip = ProbeSourceClip.New();

        using (clip)
        {
            PrepareForVideo(clip);
            Assert.True(layer.AddClip(clip));

            ProbeVideoSource child =
                Assert.IsType<ProbeVideoSource>(Assert.Single(clip.GetChildren(false)));
            int adopted = child.SetParentCalls;
            Assert.True(adopted >= 1);
            Assert.False(child.LastParentWasNull);

            Assert.True(clip.Remove(child));

            Assert.Empty(clip.GetChildren(false));
            Assert.True(child.SetParentCalls > adopted);
            Assert.True(child.LastParentWasNull);
            Assert.Null(child.Parent);

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            child.Dispose();
        }
    }

    /// <summary>
    /// An asset for the <c>GType</c> of a managed type extracts an instance of
    /// that type, says so through its extractable type, and refuses to be read
    /// as anything else.
    /// </summary>
    [Fact]
    public void AnAssetForAManagedTypeExtractsThatTypeAndNoOther()
    {
        GstGES.Initialize();

        Asset asset = Assert.IsAssignableFrom<Asset>(
            Asset.Request(ProbeVideoSource.Registration.GType, null));

        using (asset)
        {
            Assert.Equal(ProbeVideoSource.Registration.GType, asset.ExtractableType);

            ProbeVideoSource extracted = asset.Extract<ProbeVideoSource>();

            using (extracted)
            {
                Assert.Same(asset, extracted.GetAsset());
            }

            // The extraction succeeds and the cast is what fails, so the
            // instance that was built is released before this is thrown.
            _ = Assert.Throws<InvalidCastException>(() => asset.Extract<Clip>());
        }
    }

    /// <summary>
    /// The one slot that runs inside <c>g_object_new</c> reaches the managed
    /// override, and reaches it while the instance is still half built.
    /// </summary>
    /// <remarks>
    /// <c>max-duration</c> is a <c>CONSTRUCT</c> property and is written before
    /// <c>name</c> is (<c>ges-timeline-element.c:543-547</c>), so an override of
    /// <c>set_max_duration</c> — and only that one — sees an instance that has
    /// no name yet. The wrapper is fabricated by the dispatch itself, which is
    /// what makes the call observable at all.
    /// </remarks>
    [Fact]
    public void TheConstructionTimeSlotIsNotDispatchedToTheWrapper()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        ProbeSourceClip clip = ProbeSourceClip.New();

        using (clip)
        {
            PrepareForVideo(clip);
            Assert.True(layer.AddClip(clip));

            ProbeVideoSource child =
                Assert.IsType<ProbeVideoSource>(Assert.Single(clip.GetChildren(false)));

            // The construction of the child ran the override once, and the
            // instance had no name at that point.
            Assert.True(child.MaxDurationCalls >= 1);
            Assert.True(child.SawUnnamedMaxDuration);
            Assert.Contains(null, child.MaxDurationNames);

            // Once the construction is over the instance is named, and the
            // same override keeps working on it.
            Assert.False(string.IsNullOrEmpty(child.Name));

            int before = child.MaxDurationCalls;
            Assert.True(child.SetMaxDuration(ClockTime.FromSeconds(5)));
            Assert.True(child.MaxDurationCalls > before);
            Assert.Contains(child.Name, child.MaxDurationNames);

            // The library built this wrapper; it holds the toggle reference
            // until it is disposed, so it goes before the timeline does.
            child.Dispose();
        }
    }

    /// <summary>
    /// A <c>create_source</c> override that answers no element does not take
    /// the process with it: the trampoline reports the null answer and hands
    /// the library an <c>identity</c> in its place.
    /// </summary>
    [Fact]
    public void ASourceThatAnswersNothingIsGivenAnIdentity() =>
        AssertARefusedSourceIsGuarded(throws: false);

    /// <summary>
    /// An override that throws is the same answer one level down — the trap
    /// turns it into a null one — and is guarded the same way.
    /// </summary>
    [Fact]
    public void ASourceThatThrowsIsGivenAnIdentity() =>
        AssertARefusedSourceIsGuarded(throws: true);

    /// <summary>
    /// Adds a clip whose source refuses to build an element and asserts that
    /// the refusal was reported, substituted for and survived.
    /// </summary>
    /// <param name="throws">
    /// Whether the override throws rather than answering nothing.
    /// </param>
    /// <remarks>
    /// Without the substitute the track element is left with an nleobject the
    /// composition frees under it (<c>ges-track-element.c:1022</c>,
    /// <c>1066-1070</c>, <c>269-271</c>), and the release of the wrapper — here,
    /// or under whichever later drain reaches it — reads freed memory.
    /// </remarks>
    private static void AssertARefusedSourceIsGuarded(bool throws)
    {
        GstGES.Initialize();
        ProbeNullVideoSource.Throws = throws;

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
            using Timeline timeline = Timeline.NewAudioVideo();
            using Layer layer = timeline.AppendLayer();

            ProbeNullSourceClip clip = ProbeNullSourceClip.New();

            using (clip)
            {
                PrepareForVideo(clip);

                // The add succeeds because the slot answered an element after
                // all: the top bin is built and the nlesource is configured the
                // way it is for a source that answered one itself.
                Assert.True(layer.AddClip(clip));

                ProbeNullVideoSource child =
                    Assert.IsType<ProbeNullVideoSource>(Assert.Single(clip.GetChildren(false)));

                Gst.Bin topBin = Assert.IsAssignableFrom<Gst.Bin>(child.GetElement());

                using (topBin)
                {
                    using Iterator identities = topBin.IterateAllByElementFactoryName("identity");
                    Assert.NotEmpty(identities.Items<Gst.Element>());
                }

                // The library built this wrapper; it holds the toggle reference
                // until it is disposed, and the object has to die with the
                // timeline rather than under a later drain.
                child.Dispose();
            }
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
            ProbeNullVideoSource.Throws = false;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        Gst.GObject.Object.DrainPendingReleases();

        Exception only = Assert.Single(reported);
        InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(only);

        if (throws)
        {
            Assert.Equal(ProbeNullVideoSource.RefusalMessage, refusal.Message);
        }
        else
        {
            Assert.Contains("answered null", refusal.Message, StringComparison.Ordinal);
        }
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
