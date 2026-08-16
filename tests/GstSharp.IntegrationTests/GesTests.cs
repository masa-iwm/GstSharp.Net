using GES;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GES</c> binding against the library that is installed: the second
/// initialisation gate, and a timeline built out of the synthetic sources the
/// editing services bring with them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here reads a media file. A test clip is a generated pattern and a
/// tone, so a timeline made of them is as real as one made of assets while
/// needing nothing on disk, and every assertion below is about state the
/// library keeps in memory rather than about a pipeline that runs.
/// </para>
/// <para>
/// The collection fixture has already called <c>GstSharp.Initialize</c> by the
/// time any of these run, which is exactly the ordering an application that
/// uses several modules produces. <see cref="GstGES.Initialize"/> has to be
/// callable after it and add the half that is missing, and the first test says
/// so.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesTests
{
    /// <summary>
    /// The gate: initialising after the fixture already initialised GstSharp
    /// works, is idempotent, and leaves the editing services registered.
    /// </summary>
    /// <remarks>
    /// <c>nlecomposition</c> is the element a track is built around, and it
    /// lives in the <c>nle</c> plugin that ships beside the GES library rather
    /// than inside it. Finding it in the registry is what says that
    /// <c>ges_init</c> ran and found its own plugin, which no assertion about
    /// managed state could show.
    /// </remarks>
    [Fact]
    public void TheEntryPointInitialisesTheEditingServicesAfterTheFixtureDid()
    {
        GstGES.Initialize();
        Assert.True(GstGES.IsInitialized);

        // The latch is the binding's; this is the library's own answer.
        Assert.True(GESGlobal.IsInitialized());

        // A second call runs ges_init no further and refuses nothing.
        GstGES.Initialize();
        Assert.True(GstGES.IsInitialized);

        using ElementFactory? nle = ElementFactory.Find("nlecomposition");
        Assert.NotNull(nle);
    }

    /// <summary>
    /// The library reports the version it was built as through four out
    /// parameters.
    /// </summary>
    [Fact]
    public void TheLibraryReportsItsVersion()
    {
        GstGES.Initialize();

        GESGlobal.Version(out uint major, out uint minor, out uint micro, out uint nano);

        // All four are written, and the editing services have been part of the
        // 1.x series for their whole existence.
        Assert.Equal(1u, major);
        Assert.InRange(minor, 10u, 99u);
        Assert.InRange(micro, 0u, 999u);
        Assert.InRange(nano, 0u, 1u);
    }

    /// <summary>
    /// A timeline built by <c>ges_timeline_new_audio_video</c> carries one
    /// audio and one video track.
    /// </summary>
    /// <remarks>
    /// The two tracks arrive as <see cref="GES.AudioTrack"/> and
    /// <see cref="GES.VideoTrack"/> rather than as the <see cref="GES.Track"/>
    /// the signature declares, which is the type registry of this module doing
    /// its work: the entries the module initialiser handed over are what maps
    /// the two <c>GType</c>s onto the wrappers.
    /// </remarks>
    [Fact]
    public void ANewAudioVideoTimelineCarriesOneTrackOfEach()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        IReadOnlyList<Track> tracks = timeline.GetTracks();

        try
        {
            Assert.Equal(2, tracks.Count);
            Assert.Single(tracks, static track => track is AudioTrack);
            Assert.Single(tracks, static track => track is VideoTrack);
        }
        finally
        {
            foreach (Track track in tracks)
            {
                track.Dispose();
            }
        }
    }

    /// <summary>
    /// Appending layers adds them to the timeline, in the order they were
    /// appended and with the priority that order gives them.
    /// </summary>
    [Fact]
    public void AnAppendedLayerBelongsToTheTimeline()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        Assert.Empty(timeline.GetLayers());

        using Layer first = timeline.AppendLayer();
        using Layer second = timeline.AppendLayer();

        IReadOnlyList<Layer> layers = timeline.GetLayers();

        try
        {
            Assert.Equal(2, layers.Count);

            // A GObject wrapper is interned, so the layers of the timeline are
            // the very instances that appending them handed out.
            Assert.Same(first, layers[0]);
            Assert.Same(second, layers[1]);

            Assert.Equal(0u, first.GetPriority());
            Assert.Equal(1u, second.GetPriority());
            Assert.Same(timeline, second.GetTimeline());
        }
        finally
        {
            foreach (Layer layer in layers)
            {
                layer.Dispose();
            }
        }
    }

    /// <summary>
    /// A test clip placed on a layer stretches the timeline to cover it.
    /// </summary>
    /// <remarks>
    /// The duration of a timeline is derived from the elements it contains, so
    /// this is the one assertion here that measures the editing services doing
    /// something rather than storing something: the clip is placed at one
    /// second with a length of two, and the timeline ends at three.
    /// </remarks>
    [Fact]
    public void TheDurationOfATimelineFollowsTheClipsOnItsLayers()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetStart(ClockTime.FromSeconds(1)));
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(2)));

            // Adding the clip is what builds the track elements behind it, one
            // per track of the timeline.
            Assert.True(layer.AddClip(clip));

            Assert.Equal(ClockTime.FromSeconds(3), timeline.GetDuration());
            Assert.Equal(ClockTime.FromSeconds(3), timeline.Duration);
        }
    }
}
