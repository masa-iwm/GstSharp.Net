using GES;
using Gst;
using Gst.Audio;
using Gst.Interop;
using Gst.Pbutils;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;
using Uri = Gst.Uri;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members that are given a <c>GList</c>, against the library that is
/// installed: the borrowed shape builds a list the callee reads and the binding
/// releases, and the consumed shape hands over a list and one reference per
/// element that the callee keeps.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point here is at or below the 1.24 floor of the CI matrix, so
/// none of them is gated on <c>NativeAvailability</c>. What is gated is the two
/// encoders, because an element is not a symbol: <c>theoraenc</c> and
/// <c>vorbisenc</c> are gst-plugins-base elements and exist on every leg that
/// installs the plugins, and the Linux leg names both in
/// <c>GSTSHARP_REQUIRED_ELEMENTS</c> so that they cannot degrade into a
/// permanent skip everywhere.
/// </para>
/// <para>
/// The half of <see cref="GListScope"/> that needs a real GLib is here too, for
/// the same reason: <c>GstSharp.Core.Tests</c> runs on a machine without
/// GStreamer, and a spine that is really built cannot be faked. The round trips
/// below walk one: <see cref="Uri.GetPathSegments"/> reads back exactly what
/// <see cref="Uri.SetPathSegments"/> was given, in order.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ListArgumentTests
{
    /// <summary>
    /// <c>GST_ELEMENT_FACTORY_TYPE_ANY</c>, which the gir carries as a constant
    /// the binding does not emit (<c>Gst-1.0.gir</c>,
    /// <c>ELEMENT_FACTORY_TYPE_ANY</c>).
    /// </summary>
    private const ulong FactoryTypeAny = 562949953421311UL;

    /// <summary>
    /// <c>gst_element_factory_list_filter</c> is the borrowed shape over the
    /// result of a list return: the input is walked and copied into a temporary
    /// spine, and everything the filter answers was in it.
    /// </summary>
    [Fact]
    public void AFilteredFactoryListIsASubsetOfTheInput()
    {
        IReadOnlyList<ElementFactory> all = ElementFactory.ListGetElements(FactoryTypeAny, Rank.None);
        Assert.NotEmpty(all);

        using Caps any = Caps.NewAny();
        IReadOnlyList<ElementFactory> sinks = ElementFactory.ListFilter(all, any, PadDirection.Sink, subsetonly: false);

        try
        {
            Assert.NotEmpty(sinks);

            HashSet<string> names = [.. all.Select(static factory => factory.Name ?? string.Empty)];
            foreach (ElementFactory factory in sinks)
            {
                Assert.Contains(factory.Name ?? string.Empty, names);
            }
        }
        finally
        {
            foreach (ElementFactory factory in new HashSet<ElementFactory>([.. all, .. sinks]))
            {
                factory.Dispose();
            }
        }
    }

    /// <summary>
    /// A null sequence and an empty one are the same value, and the C function
    /// answers the empty result to both. The gir carries no <c>nullable</c> on
    /// the parameter; the overlay does, off the loop in
    /// <c>gstelementfactory.c</c>.
    /// </summary>
    [Fact]
    public void ANullAndAnEmptyFactoryListBothFilterToNothing()
    {
        using Caps any = Caps.NewAny();

        Assert.Empty(ElementFactory.ListFilter(null, any, PadDirection.Sink, subsetonly: false));
        Assert.Empty(ElementFactory.ListFilter([], any, PadDirection.Sink, subsetonly: false));
    }

    /// <summary>
    /// <c>gst_object_check_uniqueness</c> walks the list and compares names, so
    /// it says whether the binding built the spine in the first place.
    /// </summary>
    [Fact]
    public void CheckUniquenessSeesTheNamesOfTheListItIsGiven()
    {
        using Element first = ElementFactory.Make("fakesrc", "list-argument-source")
            ?? throw new InvalidOperationException("fakesrc is a core element and has to exist.");
        using Element second = ElementFactory.Make("fakesink", "list-argument-sink")
            ?? throw new InvalidOperationException("fakesink is a core element and has to exist.");

        Gst.Object[] objects = [first, second];

        Assert.False(Gst.Object.CheckUniqueness(objects, "list-argument-source"));
        Assert.False(Gst.Object.CheckUniqueness(objects, "list-argument-sink"));
        Assert.True(Gst.Object.CheckUniqueness(objects, "list-argument-absent"));

        // An empty list has no name in it, and a null one is the same list.
        Assert.True(Gst.Object.CheckUniqueness([], "list-argument-source"));
        Assert.True(Gst.Object.CheckUniqueness(null, "list-argument-source"));
    }

    /// <summary>
    /// <c>gst_plugin_feature_list_copy</c> is the borrowed shape whose answer is
    /// a transferred list, so it round trips a spine through the binding in both
    /// directions at once.
    /// </summary>
    [Fact]
    public void APluginFeatureListRoundTripsThroughListCopy()
    {
        IReadOnlyList<PluginFeature> features = Registry.Get().GetFeatureListByPlugin("coreelements");
        Assert.NotEmpty(features);

        IReadOnlyList<PluginFeature> copy = PluginFeature.ListCopy(features);

        try
        {
            Assert.Equal(
                features.Select(static feature => feature.Name).ToArray(),
                copy.Select(static feature => feature.Name).ToArray());
        }
        finally
        {
            foreach (PluginFeature feature in new HashSet<PluginFeature>([.. features, .. copy]))
            {
                feature.Dispose();
            }
        }

        Assert.Empty(PluginFeature.ListCopy(null));
        Assert.Empty(PluginFeature.ListCopy([]));
    }

    /// <summary>
    /// <c>gst_plugin_feature_list_debug</c> answers nothing and is a no-op for a
    /// null list twice over, its body being compiled out where debugging is
    /// disabled. What it proves is that a void member of this shape returns at
    /// all.
    /// </summary>
    [Fact]
    public void ListDebugAcceptsANullAndAnEmptyList()
    {
        PluginFeature.ListDebug(null);
        PluginFeature.ListDebug([]);
    }

    /// <summary>
    /// The consumed shape, round tripped: the segments the call was handed come
    /// back out of the URI in the order they were given.
    /// </summary>
    [Fact]
    public void ThePathSegmentsOfAUriRoundTrip()
    {
        using Uri uri = Uri.New("file", null, "localhost", 0, "/", null, null);
        Assert.True(uri.IsWritable());

        Assert.True(uri.SetPathSegments(["", "one", "two", "three"]));
        Assert.Equal(["", "one", "two", "three"], uri.GetPathSegments());

        // NULL is how the C function is told to clear the path, and an empty
        // sequence is the very same value.
        Assert.True(uri.SetPathSegments(null));
        Assert.Empty(uri.GetPathSegments());
    }

    /// <summary>
    /// The documented leak: <c>gst_uri_set_path_segments</c> takes the list over
    /// before it tests whether the URI is writable, so a call on a read only URI
    /// answers false and keeps everything it was handed. The binding follows C
    /// rather than second guessing it, which is what the remarks of the member
    /// say.
    /// </summary>
    [Fact]
    public void SettingPathSegmentsOnANonWritableUriAnswersFalse()
    {
        using Uri uri = Uri.New("file", null, "localhost", 0, "/start", null, null);
        Assert.True(uri.IsWritable());

        // A GstUri is a mini object behind a boxed registration, so a second
        // wrapper of the same instance is a second reference and the instance
        // stops being writable. That is the only way to reach the branch from
        // managed code, and the branch is the point of the test.
        using Uri alias = Uri.FromNative(uri.Handle, Transfer.None)
            ?? throw new InvalidOperationException("A live URI always wraps.");

        Assert.False(uri.IsWritable());

        // The list is consumed all the same: C takes ownership before it
        // checks, so this call answers false and leaks what it was handed. The
        // leak is upstream's and is documented on the member; the test spends
        // one node and one string on proving the answer.
        Assert.False(uri.SetPathSegments(["never", "stored"]));
        Assert.Equal(["start"], uri.GetPathSegments());
    }

    /// <summary>
    /// The two 1.24 members that order the query string read the list the same
    /// way, and an empty sequence asks for the unordered form.
    /// </summary>
    [Fact]
    public void TheOrderedQueryStringFollowsTheKeysItIsGiven()
    {
        using Uri uri = Uri.New("http", null, "example.test", 0, "/", "b=2&a=1", null);

        Assert.Equal("b=2&a=1", uri.GetQueryStringOrdered(["b", "a"]));
        Assert.Equal("a=1&b=2", uri.GetQueryStringOrdered(["a", "b"]));
        Assert.Equal("a=1", uri.GetQueryStringOrdered(["a"]));

        Assert.Contains("a=1", uri.ToStringWithKeys(["a", "b"]), StringComparison.Ordinal);
        Assert.Equal(uri.ToString(), uri.ToStringWithKeys([]));
        Assert.Equal(uri.ToString(), uri.ToStringWithKeys(null));
    }

    /// <summary>
    /// <c>gst_encoding_target_new</c> copies the profiles it is given, one
    /// reference each, and a null list is a target with no profiles. The gir
    /// spells the parameter non-nullable and the overlay corrects it off the
    /// <c>while (profiles)</c> loop of <c>encoding-target.c</c>.
    /// </summary>
    [Fact]
    public void ANewEncodingTargetCarriesTheProfilesItWasGiven()
    {
        using Caps format = Caps.FromString("audio/x-vorbis")
            ?? throw new InvalidOperationException("audio/x-vorbis is a caps string GStreamer always parses.");
        using EncodingAudioProfile profile = EncodingAudioProfile.New(format, null, null, 0);

        using EncodingTarget? target = EncodingTarget.New(
            "list-argument-target",
            "device",
            "A target built by the test suite",
            [profile]);

        Assert.NotNull(target);

        IReadOnlyList<EncodingProfile> profiles = target.GetProfiles();

        try
        {
            Assert.Single(profiles);
            Assert.Equal(profile.GetName(), profiles[0].GetName());
        }
        finally
        {
            foreach (EncodingProfile held in profiles)
            {
                held.Dispose();
            }
        }

        using EncodingTarget? empty = EncodingTarget.New(
            "list-argument-empty",
            "device",
            "A target with no profiles",
            null);

        Assert.NotNull(empty);
        Assert.Empty(empty.GetProfiles());
    }

    /// <summary>
    /// The mini object half of the consumed shape. The encoder takes one
    /// reference per buffer, so a buffer that was writable before the call is
    /// not writable after it, and the caller's wrapper stays usable; clearing
    /// the headers with <see langword="null"/> gives the writability back.
    /// </summary>
    [RequiresElementFact("theoraenc")]
    public void AVideoEncoderTakesAReferenceOfEveryHeaderBuffer()
    {
        using Element element = ElementFactory.Make("theoraenc", null)
            ?? throw new InvalidOperationException("The fact gates on theoraenc being present.");

        VideoEncoder encoder = Assert.IsAssignableFrom<VideoEncoder>(element);

        using Buffer header = Buffer.NewAllocate(null, 16, null)
            ?? throw new InvalidOperationException("The default allocator always answers sixteen bytes.");
        Assert.True(header.IsWritable);

        encoder.SetHeaders([header]);

        // The encoder holds a reference of its own now, which is what the
        // consumed shape mints, and the wrapper is still the caller's.
        Assert.False(header.IsWritable);
        Assert.Equal(16, (int)header.GetSize());

        encoder.SetHeaders(null);
        Assert.True(header.IsWritable);
    }

    /// <summary>The audio sibling of the same call.</summary>
    [RequiresElementFact("vorbisenc")]
    public void AnAudioEncoderTakesAReferenceOfEveryHeaderBuffer()
    {
        using Element element = ElementFactory.Make("vorbisenc", null)
            ?? throw new InvalidOperationException("The fact gates on vorbisenc being present.");

        AudioEncoder encoder = Assert.IsAssignableFrom<AudioEncoder>(element);

        using Buffer header = Buffer.NewAllocate(null, 16, null)
            ?? throw new InvalidOperationException("The default allocator always answers sixteen bytes.");
        Assert.True(header.IsWritable);

        encoder.SetHeaders([header]);
        Assert.False(header.IsWritable);

        encoder.SetHeaders(null);
        Assert.True(header.IsWritable);
    }

    /// <summary>
    /// <c>ges_container_group</c> of two clips that cannot be merged answers a
    /// group, which is the call the feature exists for.
    /// </summary>
    [Fact]
    public void GroupingTwoClipsAnswersAGroup()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        using TestClip first = NewClip(layer, seconds: 0);
        using TestClip second = NewClip(layer, seconds: 4);

        using Container? grouped = Container.Group([first, second]);

        Assert.NotNull(grouped);
        Group group = Assert.IsType<Group>(grouped);

        IReadOnlyList<TimelineElement> children = group.GetChildren(recursive: false);

        try
        {
            Assert.Equal(2, children.Count);
        }
        finally
        {
            foreach (TimelineElement child in children)
            {
                child.Dispose();
            }
        }
    }

    /// <summary>
    /// The interning quirk the remarks of the member state: a list of one is
    /// answered with that element itself rather than with a new group, and
    /// because a GObject wrapper is interned that is the very instance the
    /// caller passed.
    /// </summary>
    [Fact]
    public void GroupingOneContainerAnswersTheSameWrapper()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip only = NewClip(layer, seconds: 0);

        Container? grouped = Container.Group([only]);

        Assert.Same(only, grouped);
    }

    /// <summary>
    /// A null or empty list answers a new, empty group rather than nothing.
    /// </summary>
    [Fact]
    public void GroupingNothingAnswersAnEmptyGroup()
    {
        GstGES.Initialize();

        using Container? empty = Container.Group(null);

        Assert.NotNull(empty);
        Assert.IsType<Group>(empty);
        Assert.Empty(empty.GetChildren(recursive: false));
    }

    /// <summary>
    /// The three <c>edit</c> members take a list GStreamer ignores, so the whole
    /// of what a test can say about them is that <see langword="null"/> is
    /// accepted and the edit happens. Both overloads are exercised: an integer
    /// literal binds the deprecated one of the container and a
    /// <see langword="long"/> reaches the one of the timeline element.
    /// </summary>
    [Fact]
    public void EditingWithNullLayersMovesTheClip()
    {
        GstGES.Initialize();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();
        using TestClip clip = NewClip(layer, seconds: 0);

        Assert.True(clip.Edit(null, 0L, EditMode.EditNormal, Edge.EdgeNone, ClockTime.FromSeconds(2).Nanoseconds));
        Assert.Equal(ClockTime.FromSeconds(2), clip.GetStart());

        // ges_container_edit is deprecated since 1.18 and is deliberately under
        // test: the member the binding emits for it is the one being exercised,
        // so the deprecation warning is suppressed here and nowhere else.
#pragma warning disable CS0618 // ges_container_edit, deprecated in 1.18 in favour of ges_timeline_element_edit
        Assert.True(clip.Edit(null, 0, EditMode.EditNormal, Edge.EdgeNone, ClockTime.FromSeconds(5).Nanoseconds));
#pragma warning restore CS0618
        Assert.Equal(ClockTime.FromSeconds(5), clip.GetStart());
    }

    /// <summary>
    /// The singly linked spine, which no bound member of the sixteen modules
    /// asks for: <c>g_slist_prepend</c> and <c>g_slist_free</c> are reachable
    /// through the <c>singly</c> flag alone, and the generator fixture only
    /// pins the literal that sets it. This is the one place the two entry
    /// points are actually called, in both directions.
    /// </summary>
    /// <remarks>
    /// A <c>GSList</c> node is <c>{ gpointer data; GSList *next; }</c>, so
    /// <see cref="GListMarshal.Collect"/> walks it exactly as it walks a
    /// <c>GList</c>: the two fields it reads sit at the same offsets. Releasing
    /// it does not follow that rule — a two pointer node handed to
    /// <c>g_list_free</c> is freed against the wrong slice size — which is why
    /// the consumed half below releases the spine through
    /// <see cref="GListMarshal.FreeSpine"/> with the same flag it was built
    /// with.
    /// </remarks>
    [Fact]
    public void ASinglyLinkedSpineIsBuiltAndReleasedInOrder()
    {
        string[] values = ["alpha", "beta", "gamma"];

        using (GListScope scope = GMarshal.AllocList(values, singly: true))
        {
            Assert.NotEqual(nint.Zero, scope.Head);
            Assert.Equal(values, Decode(GListMarshal.Collect(scope.Head)));
        }

        // The consumed half: nothing releases the spine for the caller, so the
        // test is the callee and releases it the way the callee would.
        nint head = GMarshal.ConsumeList(values, singly: true);
        Assert.NotEqual(nint.Zero, head);

        nint[] items = GListMarshal.Collect(head);

        try
        {
            Assert.Equal(values, Decode(items));
        }
        finally
        {
            foreach (nint item in items)
            {
                GMarshal.Free(item);
            }

            GListMarshal.FreeSpine(head, singly: true);
        }
    }

    /// <summary>
    /// A scope may be disposed twice, and the second call frees nothing a
    /// second time. Proving it needs a spine that really was allocated, which
    /// is why it lives here rather than beside the null and empty cases.
    /// </summary>
    [Fact]
    public void DisposingAScopeTwiceFreesNothingTwice()
    {
        GListScope scope = GMarshal.AllocList(["one", "two"], singly: false);
        Assert.NotEqual(nint.Zero, scope.Head);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(nint.Zero, scope.Head);
    }

    /// <summary>Reads a list of UTF-8 pointers back into strings.</summary>
    /// <param name="items">The pointers, in list order.</param>
    /// <returns>The decoded strings.</returns>
    private static string[] Decode(nint[] items) =>
        [.. items.Select(static item => GMarshal.PtrToStringUtf8(item) ?? string.Empty)];

    /// <summary>Places a one second test clip on a layer.</summary>
    /// <param name="layer">The layer to add it to.</param>
    /// <param name="seconds">Where the clip starts.</param>
    /// <returns>The clip.</returns>
    private static TestClip NewClip(Layer layer, int seconds)
    {
        TestClip clip = TestClip.New()
            ?? throw new InvalidOperationException("The editing services always provide a test clip.");

        Assert.True(clip.SetStart(ClockTime.FromSeconds((ulong)seconds)));
        Assert.True(clip.SetDuration(ClockTime.FromSeconds(1)));
        Assert.True(layer.AddClip(clip));

        return clip;
    }
}
