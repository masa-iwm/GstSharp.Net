using GES;
using Gst;
using Gst.GLib;
using Gst.Pbutils;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three signals whose handler transfers a GObject out:
/// <c>GESTimeline::select-element-track</c>,
/// <c>GESDiscovererManager::load-serialized-info</c> and
/// <c>GstDiscoverer::load-serialized-info</c>.
/// </summary>
/// <remarks>
/// <para>
/// The trampoline mints a reference for the value the handler returns, because
/// the generic marshal hands it to <c>g_value_take_object</c>. What that has to
/// be measured against is the emitter: the timeline puts the track it was given
/// into a <c>GPtrArray</c> whose free function is <c>gst_object_unref</c>, and
/// the discoverer keeps the info it was given as the result of the discovery.
/// Both release exactly the reference the trampoline minted, so the wrapper the
/// handler returned stays the handler's own and is usable afterwards.
/// </para>
/// <para>
/// The discoverer is the one signal of the three that carries an accumulator,
/// which reads the handler return and stores it in the return accumulator with
/// a reference of its own. That is why it has a test here rather than being
/// covered by the argument that the mechanism is shared: an unbalanced
/// accumulator would show up as an info that cannot be read afterwards or as a
/// second release at the end of the test.
/// </para>
/// <para>
/// Every member called here is GStreamer 1.24 or older — the two
/// <c>load-serialized-info</c> signals arrived in 1.24 and
/// <c>select-element-track</c> in 1.18 — so the file runs on the 1.24 floor of
/// the Linux leg without an availability gate. One test still reads
/// <see cref="NativeAvailability.Has128"/>, not because a member is missing
/// there but because what the timeline does with a null answer changed in
/// 1.28; see its own remarks.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GesSignalReturnTests
{
    /// <summary>
    /// The track a handler returns is the track the element lands in, and the
    /// timeline stays usable afterwards: the minted reference was consumed by
    /// the array the timeline built, and nothing was released twice.
    /// </summary>
    [Fact]
    public void TheTrackAHandlerReturnsTakesTheElement()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        IReadOnlyList<Track> tracks = timeline.GetTracks();

        try
        {
            Track video = tracks.First(static track => track.TrackType == TrackType.Video);
            Track audio = tracks.First(static track => track.TrackType == TrackType.Audio);

            int calls = 0;
            Timeline.SelectElementTrackHandler handler = (sender, args) =>
            {
                calls++;
                return args.TrackElement.GetTrackType() == TrackType.Video ? video : audio;
            };

            timeline.SelectElementTrack += handler;

            try
            {
                using Layer layer = timeline.AppendLayer();
                AddOneSecondTestClip(layer);

                // A test clip carries one core element per track type.
                Assert.Equal(2, calls);

                AssertHoldsOneElementOfType(video, TrackType.Video);
                AssertHoldsOneElementOfType(audio, TrackType.Audio);
            }
            finally
            {
                timeline.SelectElementTrack -= handler;
            }

            // Both tracks are still alive and still answer, which a second
            // release of the reference the trampoline minted would have made
            // impossible.
            Assert.Same(timeline, video.GetTimeline());
            Assert.Same(timeline, audio.GetTimeline());
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
    /// A handler that answers nothing hands the decision on, and what the
    /// emission then does with it is the version's own business.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the null path of the trampoline has to guarantee is that it crosses
    /// as the null pointer and that the timeline stays usable. Both are
    /// asserted on every version; the track content is not the same on all of
    /// them, so it is read against the installed one.
    /// </para>
    /// <para>
    /// From 1.28 on, <c>_get_selected_tracks</c> reads the fallback off
    /// <c>g_signal_has_handler_pending</c> (ges-timeline.c:1493-1498): the
    /// older <c>select-tracks-for-object</c> signal is only emitted when
    /// nothing is connected to this one, so <see langword="null"/> here means
    /// "no track" and the element is discarded. Before that the <c>else</c> was
    /// unconditional (1.24.13 ges-timeline.c:1492-1496), so the fallback fired
    /// anyway and its default class handler put each core element into the
    /// track matching its type — the same placement a timeline with no handler
    /// at all makes.
    /// </para>
    /// <para>
    /// The boundary is exact: the gate is upstream commit d3d8989798 ("ges:
    /// timeline: Respect SELECT_ELEMENT_TRACK signal discard decision", October
    /// 2025), and the first tags that contain it are 1.27.50 and 1.28.0. No
    /// 1.24.x or 1.26.x release carries it, which is why <c>Has128</c> is the
    /// right question to ask here.
    /// </para>
    /// </remarks>
    [Fact]
    public void AHandlerThatAnswersNothingCrossesAsTheNullPointer()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        IReadOnlyList<Track> tracks = timeline.GetTracks();

        try
        {
            int calls = 0;
            Timeline.SelectElementTrackHandler handler = (sender, args) =>
            {
                calls++;
                return null;
            };

            timeline.SelectElementTrack += handler;

            try
            {
                using Layer layer = timeline.AppendLayer();
                AddOneSecondTestClip(layer);

                // The emission reaches the handler on every version: the
                // fallback is a second signal, not a second handler of this
                // one.
                Assert.Equal(2, calls);

                foreach (Track track in tracks)
                {
                    if (NativeAvailability.Has128)
                    {
                        AssertHoldsNoElement(track);
                    }
                    else
                    {
                        AssertHoldsOneElementOfType(track, track.TrackType);
                    }
                }
            }
            finally
            {
                timeline.SelectElementTrack -= handler;
            }

            Assert.Same(timeline, tracks[0].GetTimeline());
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
    /// The discoverer answers the info its handler returned, and the wrapper
    /// the handler returned is still the caller's afterwards.
    /// </summary>
    /// <remarks>
    /// This is the empirical check on the accumulator of the signal. It reads
    /// the handler return with <c>g_value_get_object</c> and stores it in the
    /// return accumulator with <c>g_value_set_object</c>, which takes a
    /// reference of its own while the reference the marshal took is released
    /// when the handler return is unset: exactly one owned reference reaches
    /// the emitter, which is what a transfer-full handler return provides. The
    /// discovery hands that reference back out, and because a GObject wrapper
    /// is interned the caller gets the very instance the handler returned, with
    /// the extra reference already dropped.
    /// </remarks>
    [Fact]
    public void TheDiscovererAnswersTheInfoItsHandlerReturned()
    {
        const string uri = "file:///gstsharp/never-read.mkv";

        using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(5));
        using DiscovererInfo answer = NewEmptyDiscovererInfo();

        int calls = 0;
        string? seen = null;
        Discoverer.LoadSerializedInfoHandler handler = (sender, args) =>
        {
            calls++;
            seen = args.Uri;
            return answer;
        };

        discoverer.LoadSerializedInfo += handler;

        try
        {
            // The signal is emitted before anything is set up, and answering it
            // short circuits the discovery: no pipeline is built and no media
            // is read, which is what makes this test independent of any file.
            DiscovererInfo result = discoverer.DiscoverUri(uri);

            Assert.Equal(1, calls);
            Assert.Equal(uri, seen);

            // The interned wrapper of the object the handler returned.
            Assert.Same(answer, result);
            Assert.Equal(uri, result.GetUri());
        }
        finally
        {
            discoverer.LoadSerializedInfo -= handler;
        }

        // Still usable after the emission, and the using of the declaration
        // releases it exactly once.
        Assert.False(answer.IsDisposed);
        Assert.Equal(DiscovererResult.Ok, answer.GetResult());
    }

    /// <summary>
    /// A handler that answers nothing lets the discovery run as it would
    /// without one.
    /// </summary>
    /// <remarks>
    /// The URI names no file, so what the discovery ends in — a failed info or
    /// a <see cref="GException"/> — depends on the installation rather than on
    /// this item. What is asserted is the part that does not: the handler was
    /// called, its null answer crossed as the null pointer, and the emission
    /// went on to the default handler of the class.
    /// </remarks>
    [Fact]
    public void TheDiscovererFallsThroughWhenItsHandlerAnswersNothing()
    {
        using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(5));

        int calls = 0;
        Discoverer.LoadSerializedInfoHandler handler = (sender, args) =>
        {
            calls++;
            return null;
        };

        discoverer.LoadSerializedInfo += handler;

        try
        {
            try
            {
                using DiscovererInfo info = discoverer.DiscoverUri("file:///gstsharp/does-not-exist.mkv");
                Assert.NotEqual(DiscovererResult.Ok, info.GetResult());
            }
            catch (GException)
            {
                // The discovery reported the missing file as a GError, which is
                // the other legitimate outcome.
            }

            Assert.Equal(1, calls);
        }
        finally
        {
            discoverer.LoadSerializedInfo -= handler;
        }
    }

    /// <summary>
    /// The editing services carry the same signal on their discoverer manager,
    /// with the same handler shape.
    /// </summary>
    /// <remarks>
    /// The manager is a process wide singleton, so nothing here starts a
    /// discovery on it: what is exercised is that a handle returning trampoline
    /// can be subscribed and unsubscribed on a second module, which is the part
    /// of the emission that is this item's.
    /// </remarks>
    [Fact]
    public void TheDiscovererManagerCarriesTheSameSignal()
    {
        GstGES.Initialize();

        using DiscovererManager manager = DiscovererManager.GetDefault();

        int calls = 0;
        DiscovererManager.LoadSerializedInfoHandler handler = (sender, args) =>
        {
            calls++;
            return null;
        };

        manager.LoadSerializedInfo += handler;
        manager.LoadSerializedInfo -= handler;

        Assert.Equal(0, calls);
    }

    private static void AddOneSecondTestClip(Layer layer)
    {
        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        using (clip)
        {
            Assert.True(clip.SetStart(ClockTime.FromSeconds(0)));
            Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
            Assert.True(layer.AddClip(clip));
        }
    }

    private static void AssertHoldsOneElementOfType(Track track, TrackType type)
    {
        IReadOnlyList<TrackElement> elements = track.GetElements();

        try
        {
            Assert.Single(elements);
            Assert.Equal(type, elements[0].GetTrackType());
        }
        finally
        {
            foreach (TrackElement element in elements)
            {
                element.Dispose();
            }
        }
    }

    private static void AssertHoldsNoElement(Track track)
    {
        IReadOnlyList<TrackElement> elements = track.GetElements();

        try
        {
            Assert.Empty(elements);
        }
        finally
        {
            foreach (TrackElement element in elements)
            {
                element.Dispose();
            }
        }
    }

    /// <summary>
    /// Builds the empty <c>GstDiscovererInfo</c> a handler answers with. The
    /// library has no public constructor for one, so this is the
    /// <c>g_object_new</c> the library itself uses.
    /// </summary>
    private static DiscovererInfo NewEmptyDiscovererInfo()
    {
        nint handle = TestNatives.ObjectNewWithProperties(
            TestNatives.DiscovererInfoGetType(), 0, 0, 0);
        Assert.NotEqual(0, handle);

        DiscovererInfo? info = Gst.GObject.Object.FromNative<DiscovererInfo>(
            handle, Gst.Interop.Transfer.Full);
        Assert.NotNull(info);
        return info;
    }
}
