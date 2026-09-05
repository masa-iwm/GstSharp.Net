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
            _ = Assert.IsType<ProbeVideoSource>(only);
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
