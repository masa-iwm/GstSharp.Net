using System.Runtime.InteropServices;
using GES;
using Gst;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The four GES signals whose signature needed a container or a lent
/// <c>GValue</c>: <c>GESLayer::active-changed</c>,
/// <c>GESTimeline::group-removed</c>,
/// <c>GESTimeline::select-tracks-for-object</c> and
/// <c>GESMetaContainer::notify-meta</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two of them carry a <c>GPtrArray</c> the emission owns and frees when it
/// ends — <c>group-removed</c> empties its array immediately after the emit —
/// so what is measured here is that the array the handler is given survives the
/// emission. One of them answers with a <c>GPtrArray</c> the timeline takes
/// over, so what is measured there is where the element lands and that the
/// timeline is still usable afterwards, which a reference released twice would
/// have made impossible.
/// </para>
/// <para>
/// Two triggers have no binding of their own and are called through an import
/// of this file. <c>ges_container_ungroup</c> is annotated as taking over the
/// container it is given, which keeps it off the generated surface, but the
/// library releases nothing of the caller's (the annotation describes the
/// timeline dropping its own reference once the group is empty), so the handle
/// is passed as it is and no reference is minted for the call;
/// <c>ges_meta_container_set_meta</c> takes the <c>GValue</c> the binding does
/// not project on a method, and passing it none is the only way to make
/// <c>notify-meta</c> carry no value.
/// </para>
/// <para>
/// Every member called here is GStreamer 1.18 or older, so the file needs no
/// availability gate. Each test builds a timeline of its own and connects no
/// <c>select-element-track</c> handler: from 1.28 on the timeline emits
/// <c>select-tracks-for-object</c> only when no handler of that other signal is
/// connected.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class GesPtrArraySignalTests
{
    /// <summary>
    /// The layer hands the handler the tracks it was deactivated for, and the
    /// array is the handler's own.
    /// </summary>
    [Fact]
    public void ActiveChangedCarriesTheTracksItChanged()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        Track[]? seen = null;
        int calls = 0;
        void OnActiveChanged(object? sender, Layer.ActiveChangedSignalArgs args)
        {
            calls++;
            seen = args.Tracks;
        }

        IReadOnlyList<Track> tracks = timeline.GetTracks();

        try
        {
            layer.ActiveChanged += OnActiveChanged;

            try
            {
                Assert.True(layer.SetActiveForTracks(active: false, [tracks[0]]));
            }
            finally
            {
                layer.ActiveChanged -= OnActiveChanged;
            }

            Assert.Equal(1, calls);
            Assert.NotNull(seen);

            // The library frees its own array when the emission ends; this one
            // is still readable, and it holds the track that was asked for.
            Assert.Single(seen);
            Assert.Same(tracks[0], seen[0]);
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
    /// The timeline hands the handler the children the group had, and the array
    /// is still readable after the emission — which is the whole point of the
    /// eager copy, because GES empties its own array as soon as the emit
    /// returns.
    /// </summary>
    [Fact]
    public void GroupRemovedCarriesTheFormerChildren()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip first = AddTestClip(layer, seconds: 0);
        using TestClip second = AddTestClip(layer, seconds: 2);

        Container? grouped = Container.Group([first, second]);
        Assert.NotNull(grouped);

        Container[]? seen = null;
        Group? removed = null;
        void OnGroupRemoved(object? sender, Timeline.GroupRemovedSignalArgs args)
        {
            removed = args.Group;
            seen = args.Children;
        }

        using (grouped)
        {
            timeline.GroupRemoved += OnGroupRemoved;

            try
            {
                // The gir annotates the container as transfer full, but the call
                // unrefs nothing: what the annotation describes is the timeline
                // dropping the reference it took when the group was added, once
                // the group has lost its last child. The wrapper's toggle
                // reference keeps the group alive through the emission, so the
                // handle is passed as it is.
                nint list = GesContainerUngroup(grouped.Handle, recursive: 0);
                FreeOwnedList(list);
            }
            finally
            {
                timeline.GroupRemoved -= OnGroupRemoved;
            }
        }

        Assert.NotNull(removed);
        Assert.NotNull(seen);

        // GES frees its array with g_ptr_array_free (TRUE) the moment the emit
        // returns, so a retained one would be empty here.
        Assert.Equal(2, seen.Length);
        Assert.Contains(seen, child => ReferenceEquals(child, first));
        Assert.Contains(seen, child => ReferenceEquals(child, second));
    }

    /// <summary>
    /// The one track a handler answers with is the one track the element lands
    /// in, and the timeline is usable afterwards.
    /// </summary>
    [Fact]
    public void TheTracksAHandlerReturnsTakeTheElement()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.New();
        using AudioTrack audio = AudioTrack.New();
        Assert.True(timeline.AddTrack(audio));

        int calls = 0;
        Timeline.SelectTracksForObjectHandler handler = (sender, args) =>
        {
            calls++;
            return [audio];
        };

        timeline.SelectTracksForObject += handler;

        try
        {
            using Layer layer = timeline.AppendLayer();
            using TestClip clip = AddTestClip(layer, seconds: 0, TrackType.Audio);

            Assert.Equal(1, calls);
            AssertHoldsOneElement(audio);
        }
        finally
        {
            timeline.SelectTracksForObject -= handler;
        }

        // The reference the trampoline minted was consumed by the array the
        // timeline took over, and nothing was released twice.
        Assert.Same(timeline, audio.GetTimeline());
    }

    /// <summary>A handler that answers nothing puts the element in no track.</summary>
    [Fact]
    public void ANullAnswerPutsTheElementInNoTrack()
    {
        AssertAnswerSelectsNothing(static (sender, args) => null);
    }

    /// <summary>An empty array answers no tracks, exactly as a null does.</summary>
    [Fact]
    public void AnEmptyAnswerPutsTheElementInNoTrack()
    {
        AssertAnswerSelectsNothing(static (sender, args) => []);
    }

    /// <summary>
    /// A handler that throws leaves the element in no track, and the trap sees
    /// what it threw.
    /// </summary>
    [Fact]
    public void AThrowingHandlerIsReportedAndSelectsNothing()
    {
        List<Exception> caught = [];
        void OnUnhandled(Exception exception)
        {
            lock (caught)
            {
                caught.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnUnhandled;

        try
        {
            AssertAnswerSelectsNothing(static (sender, args)
                => throw new InvalidTimeZoneException("the handler refused"));
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnUnhandled;
        }

        lock (caught)
        {
            Assert.Contains(caught, exception => exception is InvalidTimeZoneException);
        }
    }

    /// <summary>
    /// Setting a meta hands the handler a readable view of the value, and the
    /// view is refused once the emission has ended.
    /// </summary>
    [Fact]
    public void NotifyMetaCarriesTheValueThatWasSet()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.New();

        MetaContainerExtensions.NotifyMetaSignalArgs? held = null;
        string? key = null;
        string? read = null;
        void OnNotifyMeta(object? sender, MetaContainerExtensions.NotifyMetaSignalArgs args)
        {
            held = args;
            key = args.Key;
            read = args.HasValue ? args.Value.GetString() : null;
        }

        timeline.AddNotifyMetaHandler(OnNotifyMeta);

        try
        {
            Assert.True(timeline.SetString("author", "someone"));
        }
        finally
        {
            timeline.RemoveNotifyMetaHandler(OnNotifyMeta);
        }

        Assert.Equal("author", key);
        Assert.Equal("someone", read);
        Assert.NotNull(held);

        // What the emission carried is still stated after it ended; only the
        // reading of the storage the emitter held is closed off.
        Assert.True(held.HasValue);
        Assert.Throws<InvalidOperationException>(() => _ = held.Value.Type);
    }

    /// <summary>
    /// Removing a meta emits with no value at all, which the arguments state
    /// rather than hand out.
    /// </summary>
    [Fact]
    public void NotifyMetaCarriesNoValueOnRemoval()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.New();
        Assert.True(timeline.SetString("author", "someone"));

        int calls = 0;
        bool? had = null;
        bool refused = false;
        void OnNotifyMeta(object? sender, MetaContainerExtensions.NotifyMetaSignalArgs args)
        {
            calls++;
            had = args.HasValue;
            try
            {
                _ = args.Value.Type;
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }
        }

        timeline.AddNotifyMetaHandler(OnNotifyMeta);

        try
        {
            // ges_meta_container_set_meta with no value is the one thing that
            // removes a meta, and it is not bound: the binding projects no
            // nullable GValue onto a method parameter.
            Assert.Equal(1, GesMetaContainerSetMeta(timeline.Handle, "author", 0));
        }
        finally
        {
            timeline.RemoveNotifyMetaHandler(OnNotifyMeta);
        }

        Assert.Equal(1, calls);
        Assert.False(had);
        Assert.True(refused);
        Assert.Null(timeline.GetString("author"));
    }

    private static void AssertAnswerSelectsNothing(Timeline.SelectTracksForObjectHandler answer)
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.New();
        using AudioTrack audio = AudioTrack.New();
        Assert.True(timeline.AddTrack(audio));

        // The count is what keeps an empty track from being read as a pass when
        // the signal was never emitted at all.
        int calls = 0;
        Timeline.SelectTracksForObjectHandler handler = (sender, args) =>
        {
            calls++;
            return answer(sender, args);
        };

        timeline.SelectTracksForObject += handler;

        try
        {
            using Layer layer = timeline.AppendLayer();
            using TestClip clip = AddTestClip(layer, seconds: 0, TrackType.Audio);

            Assert.Equal(1, calls);

            IReadOnlyList<TrackElement> elements = audio.GetElements();

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
        finally
        {
            timeline.SelectTracksForObject -= handler;
        }
    }

    private static TestClip AddTestClip(Layer layer, int seconds, TrackType? formats = null)
    {
        TestClip? clip = TestClip.New();
        Assert.NotNull(clip);

        if (formats is { } supported)
        {
            clip.SetSupportedFormats(supported);
        }

        Assert.True(clip.SetStart(ClockTime.FromSeconds((ulong)seconds)));
        Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
        Assert.True(layer.AddClip(clip));
        return clip;
    }

    private static void AssertHoldsOneElement(Track track)
    {
        IReadOnlyList<TrackElement> elements = track.GetElements();

        try
        {
            Assert.Single(elements);
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
    /// Releases the list <c>ges_container_ungroup</c> answers with, which
    /// carries a reference per element and a list the caller frees.
    /// </summary>
    /// <param name="head">The head of the list.</param>
    private static void FreeOwnedList(nint head)
    {
        for (nint node = head; node != 0; node = ((GListNode*)node)->Next)
        {
            nint data = ((GListNode*)node)->Data;
            if (data != 0)
            {
                GObjectNative.ObjectUnref(data);
            }
        }

        GListFree(head);
    }

    /// <summary>The two fields of <c>GList</c> this file walks.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct GListNode
    {
        internal nint Data;
        internal nint Next;
    }

    [LibraryImport("GES", EntryPoint = "ges_container_ungroup")]
    private static partial nint GesContainerUngroup(nint container, int recursive);

    [LibraryImport("GES", EntryPoint = "ges_meta_container_set_meta", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GesMetaContainerSetMeta(nint container, string metaItem, nint value);

    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void GListFree(nint list);
}
