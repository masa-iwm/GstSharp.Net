using System.Runtime.InteropServices;
using GES;
using Gst;
using Gst.GObject;
using Xunit;
using Value = Gst.GObject.Value;

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
public sealed partial class GesSubclassTests
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
    /// A managed clip that declares <c>set_parent</c> and chains up is adopted by
    /// a group and let go of again, and nothing is reported: the slot is empty
    /// below every clip, and the chain-up answers what the library answers for an
    /// empty one (<c>ges-timeline-element.c:995-1000</c>).
    /// </summary>
    /// <remarks>
    /// <c>ges_container_add</c> is the caller of the slot — adding a clip to a
    /// layer is not — so a group is what asks a clip for its parent. A refusal
    /// there would not even roll the removal back
    /// (<c>ges-container.c:127-130</c>): the child would keep a parent pointer
    /// with no parent behind it.
    /// </remarks>
    [Fact]
    public void AManagedClipThatChainsUpSetParentIsAdoptedByAGroup()
    {
        GstGES.Initialize();
        ProbeVideoSource.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        List<Exception> reported = [];

        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        ProbeSourceClip first = ProbeSourceClip.New();
        ProbeSourceClip second = ProbeSourceClip.New();

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            using (first)
            using (second)
            {
                PrepareForVideo(first);
                PrepareForVideo(second);
                Assert.True(second.SetStart(ClockTime.FromSeconds(4)));

                Assert.True(layer.AddClip(first));
                Assert.True(layer.AddClip(second));

                Container grouped = Container.Group([first, second])
                    ?? throw new InvalidOperationException("The clips could not be grouped.");

                Group group = Assert.IsType<Group>(grouped);

                using (group)
                {
                    Assert.True(first.SetParentCalls >= 1);
                    Assert.Same(group, first.Parent);

                    Assert.Equal(2, group.Ungroup(recursive: false).Count);
                    Assert.Null(first.Parent);
                }

                // The overrides built these wrappers and the clips took a
                // reference of their own, so they go before the timeline does.
                first.AnsweredChild?.Dispose();
                second.AnsweredChild?.Dispose();
            }
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
    /// The three child property slots of a managed track element: the block it answers
    /// carries what the override added to what the registry holds, the lookup
    /// answers a child and a specification of the override's own, and the
    /// setter is reached through the public call that writes one.
    /// </summary>
    [Fact]
    public void TheChildPropertySlotsOfAManagedTrackElementAreReached()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();

        ParamSpec[] properties = source.ListChildrenProperties();

        try
        {
            // The registered property and the one the override adds, in the
            // order GES sorts them into (ges-timeline-element.c:2260).
            Assert.Contains(properties, property => property.Name == ProbeVideoSource.TagName);
            Assert.Contains(properties, property => property.Name == ProbeVideoSource.AliasName);
            Assert.Equal(
                properties.Select(static property => property.Name).Order(StringComparer.Ordinal),
                properties.Select(static property => property.Name));

            // Every element is a wrapper of the caller's, so the derived class
            // that matches G_PARAM_SPEC_TYPE is what came back.
            ParamSpec alias = Assert.Single(
                properties,
                property => property.Name == ProbeVideoSource.AliasName);
            Assert.IsType<ParamSpecInt>(alias);
        }
        finally
        {
            foreach (ParamSpec property in properties)
            {
                property.Dispose();
            }
        }

        // The lookup answers the element itself as the child, which the
        // interning makes the very wrapper the test holds, and a specification
        // the caller owns.
        Assert.True(
            source.LookupChild(ProbeVideoSource.AliasName, out Gst.GObject.Object? child, out ParamSpec? pspec));
        Assert.Same(source, child);
        Assert.NotNull(pspec);
        Assert.Equal(ProbeVideoSource.AliasName, pspec.Name);
        pspec.Dispose();

        // The wrapper the override handed out is consumed: the trampoline took
        // its reference and disposed it, so the one the override still holds
        // answers nothing. The specification itself is alive - the class holds
        // AliasSpec - and the reference the caller was handed is the one
        // disposed above.
        ParamSpec handedOut = Assert.IsAssignableFrom<ParamSpec>(source.HandedOutSpec);
        Assert.Throws<ObjectDisposedException>(() => handedOut.Handle);

        // The false path writes nothing. Measuring that through the forward
        // binding proves nothing - it pre-nulls its own storage whatever the
        // slot did - so the raw call is what the sentinels go through: the
        // caller's two pointers still read 1 after the slot answered FALSE
        // (ges-timeline-element.c:257-289).
        nint missing = 1;
        nint none = 1;
        Assert.Equal(
            0,
            GesTimelineElementLookupChild(source.Handle, "no-such-child-property", ref missing, ref none));
        Assert.Equal((nint)1, missing);
        Assert.Equal((nint)1, none);
        GC.KeepAlive(source);

        int lookups = source.LookupCalls;
        Assert.True(lookups >= 2, $"the lookup slot ran {lookups} times");

        using (Value written = Value.New(GType.String))
        {
            written.SetString("through the slot");
            source.SetChildProperty(ProbeVideoSource.TagName, written);
        }

        // The public setter went through the slot, and chaining up wrote the
        // value onto the child the registry names - the clip itself.
        Assert.Contains(ProbeVideoSource.TagName, source.ChildPropertyWrites);
        Assert.Equal("through the slot", source.Tag);
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
    /// A <c>create_element</c> override that answers no element is guarded the
    /// same way one slot up: the trampoline reports the null answer and hands
    /// the library an <c>identity</c> in its place.
    /// </summary>
    [Fact]
    public void AnElementThatAnswersNothingIsGivenAnIdentity() =>
        AssertARefusedElementIsGuarded(throws: false);

    /// <summary>
    /// An override that throws is the same answer one level down — the trap
    /// turns it into a null one — and is guarded the same way.
    /// </summary>
    [Fact]
    public void AnElementThatThrowsIsGivenAnIdentity() =>
        AssertARefusedElementIsGuarded(throws: true);

    /// <summary>
    /// Adds a clip whose source refuses to build the element behind it and
    /// asserts that the refusal was reported, substituted for and survived.
    /// </summary>
    /// <param name="throws">
    /// Whether the override throws rather than answering nothing.
    /// </param>
    /// <remarks>
    /// The twin of <see cref="AssertARefusedSourceIsGuarded"/> one slot down.
    /// <c>GESSource</c> leaves <c>create_element</c> NULL;
    /// <c>GESVideoSource</c> implements it and calls <c>create_source</c> from
    /// there (<c>GESAudioSource</c> is its twin), so an override of the lower slot
    /// replaces the whole of that: the element the track element is given is
    /// the substitute itself rather than a top bin built around one.
    /// </remarks>
    private static void AssertARefusedElementIsGuarded(bool throws)
    {
        GstGES.Initialize();
        ProbeNullElementVideoSource.Throws = throws;

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

            ProbeNullElementSourceClip clip = ProbeNullElementSourceClip.New();

            using (clip)
            {
                PrepareForVideo(clip);

                // The add succeeds because the slot answered an element after
                // all: the nlesource is configured the way it is for a source
                // that answered one itself.
                Assert.True(layer.AddClip(clip));

                ProbeNullElementVideoSource child =
                    Assert.IsType<ProbeNullElementVideoSource>(Assert.Single(clip.GetChildren(false)));

                using Gst.Element element = child.GetElement()
                    ?? throw new InvalidOperationException("The track element was given no element.");

                // The factory is not disposed with it: an element factory is
                // one of the objects the registry interns, and user code never
                // releases those.
                Gst.ElementFactory factory = element.GetFactory()
                    ?? throw new InvalidOperationException("The substitute was built by no factory.");

                Assert.Equal("identity", factory.GetName());

                // The library built this wrapper; it holds the toggle reference
                // until it is disposed, and the object has to die with the
                // timeline rather than under a later drain.
                child.Dispose();
            }
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
            ProbeNullElementVideoSource.Throws = false;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        Gst.GObject.Object.DrainPendingReleases();

        Exception only = Assert.Single(reported);
        InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(only);

        if (throws)
        {
            Assert.Equal(ProbeNullElementVideoSource.RefusalMessage, refusal.Message);
        }
        else
        {
            Assert.Contains("answered null", refusal.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The refusal of the <c>set_child_property_full</c> override reaches the
    /// caller as the error it was built with, domain, code and message.
    /// </summary>
    [Fact]
    public void ARefusedChildPropertyWriteCarriesTheReasonOfTheOverride()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();

        Gst.GLib.GException refusal = new(
            CoreErrorExtensions.Quark(),
            (int)CoreError.Failed,
            "the probe has nothing to write the tag onto");
        source.RefuseWith = refusal;

        using Value written = Value.New(GType.String);
        written.SetString("refused");

        Gst.GLib.GException thrown = Assert.Throws<Gst.GLib.GException>(
            () => source.SetChildPropertyFull(ProbeVideoSource.TagName, written));

        Assert.Equal(refusal.Domain, thrown.Domain);
        Assert.Equal(refusal.Code, thrown.Code);
        Assert.Equal(refusal.Message, thrown.Message);

        // The refusal never reached the property, so neither the slot below nor
        // the value behind it moved.
        Assert.Equal(1, source.ChildPropertyFullCalls);
        Assert.Empty(source.ChildPropertyWrites);
        Assert.Null(source.Tag);
    }

    /// <summary>
    /// A refusal that says nothing is the answer GES itself gives on several of
    /// its own paths: the call answers false and throws nothing.
    /// </summary>
    [Fact]
    public void AChildPropertyWriteRefusedWithoutAReasonAnswersFalse()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();
        source.RefuseWithoutReason = true;

        using Value written = Value.New(GType.String);
        written.SetString("refused");

        Assert.False(source.SetChildPropertyFull(ProbeVideoSource.TagName, written));
        Assert.Equal(1, source.ChildPropertyFullCalls);
        Assert.Null(source.Tag);
    }

    /// <summary>
    /// Chaining up out of the <c>set_child_property_full</c> override reaches
    /// the <c>set_child_property</c> slot below it and writes the value.
    /// </summary>
    [Fact]
    public void ChainingUpOutOfTheFullSlotStillReachesTheChildPropertySlot()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();

        using Value written = Value.New(GType.String);
        written.SetString("through the full slot");

        Assert.True(source.SetChildPropertyFull(ProbeVideoSource.TagName, written));

        Assert.Equal(1, source.ChildPropertyFullCalls);
        Assert.Contains(ProbeVideoSource.TagName, source.ChildPropertyWrites);
        Assert.Equal("through the full slot", source.Tag);

        using Value read = source.GetChildProperty(ProbeVideoSource.TagName);
        Assert.Equal("through the full slot", read.GetString());
    }

    /// <summary>
    /// An exception out of the override is caught on the boundary: the write
    /// fails, the process lives, and the trap saw what happened.
    /// </summary>
    [Fact]
    public void AnExceptionOutOfTheFullSlotIsTrappedAndTheWriteFails()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();
        source.ThrowOnChildPropertyFull = true;

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            using Value written = Value.New(GType.String);
            written.SetString("never written");

            // Nothing is written through the error either: the trampoline
            // leaves the pointer of the caller alone, which the slot documents
            // as a legal refusal, so the call answers false rather than throwing.
            Assert.False(source.SetChildPropertyFull(ProbeVideoSource.TagName, written));
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Null(source.Tag);

        lock (failures)
        {
            Exception trapped = Assert.Single(failures);
            InvalidOperationException refusal = Assert.IsType<InvalidOperationException>(trapped);
            Assert.Contains(ProbeVideoSource.TagName, refusal.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An override that throws its refusal instead of answering with it is
    /// still a bug the trap sees, but the reason it carries reaches the caller
    /// all the same.
    /// </summary>
    [Fact]
    public void AThrownRefusalIsBothReportedAndForwarded()
    {
        GstGES.Initialize();

        using ProbeVideoSource source = ProbeVideoSource.New();

        Gst.GLib.GException refusal = new(
            CoreErrorExtensions.Quark(),
            (int)CoreError.Failed,
            "the probe throws the reason instead of answering it");
        source.ThrowRefusalWith = refusal;

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        Gst.GLib.GException thrown;
        try
        {
            using Value written = Value.New(GType.String);
            written.SetString("never written");

            thrown = Assert.Throws<Gst.GLib.GException>(
                () => source.SetChildPropertyFull(ProbeVideoSource.TagName, written));
        }
        finally
        {
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
        }

        // The forward binding rebuilds the error the trampoline wrote, so what
        // arrives is a copy of the reason and not the instance that was thrown.
        Assert.Equal(refusal.Domain, thrown.Domain);
        Assert.Equal(refusal.Code, thrown.Code);
        Assert.Equal(refusal.Message, thrown.Message);

        Assert.Null(source.Tag);
        Assert.Empty(source.ChildPropertyWrites);

        lock (failures)
        {
            Exception trapped = Assert.Single(failures);
            Gst.GLib.GException reported = Assert.IsType<Gst.GLib.GException>(trapped);
            Assert.Equal(refusal.Domain, reported.Domain);
            Assert.Equal(refusal.Code, reported.Code);
        }
    }

    /// <summary>
    /// A subclass that takes <c>set_child_property</c> over and leaves the
    /// slot above it alone is reached through the implementation the editing
    /// services install on every class.
    /// </summary>
    [Fact]
    public void ThePlainChildPropertySlotIsReachedThroughTheDefaultFullSlot()
    {
        GstGES.Initialize();

        using PlainChildPropertyVideoSource source = PlainChildPropertyVideoSource.New();

        using Value written = Value.New(GType.String);
        written.SetString("through the default");

        Assert.True(source.SetChildPropertyFull(PlainChildPropertyVideoSource.TagName, written));

        Assert.Contains(PlainChildPropertyVideoSource.TagName, source.ChildPropertyWrites);
        Assert.Equal("through the default", source.Tag);
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

    [LibraryImport(
        "GES",
        EntryPoint = "ges_timeline_element_lookup_child",
        StringMarshalling = StringMarshalling.Utf8)]
    private static partial int GesTimelineElementLookupChild(
        nint element,
        string propName,
        ref nint child,
        ref nint pspec);
}
