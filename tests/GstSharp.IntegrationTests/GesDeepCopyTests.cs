using GES;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GESTimelineElementClass::deep_copy</c>: the copy the base class hands the
/// slot is a second instance of the very type the slot runs for, and it is
/// resolved the way the instance itself is — an interned or fabricated wrapper
/// that settles no reference, so the floating one the caller still means to
/// drop stays the caller's.
/// </summary>
/// <remarks>
/// <para>
/// Everything here runs on the thread of the test, which is what the editing
/// services assert for a timeline and its tracks.
/// </para>
/// <para>
/// <c>GESClip::_deep_copy</c> dereferences <c>self-&gt;priv-&gt;layer-&gt;timeline</c>
/// without checking it (<c>ges-clip.c:2474</c>), so every clip that is deep
/// copied here sits in a layer of a timeline first. A clip that does not would
/// take the test host down rather than fail a test.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class GesDeepCopyTests
{
    private static readonly ClockTime Length = ClockTime.FromSeconds(2);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GesDeepCopyTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The override runs with a copy of the managed type; the installed
    /// property was already carried over when it ran, and the state outside the
    /// property system is what the override itself copies. Inside the slot the
    /// copy is floating and carries two references — the caller's floating one
    /// and the one the wrapper took — and the wrapper is usable afterwards.
    /// </summary>
    [Fact]
    public void TheOverrideIsHandedACopyOfItsOwnManagedType()
    {
        GstGES.Initialize();
        DeepCopyProbeClip.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        DeepCopyProbeClip clip = DeepCopyProbeClip.New();

        using (clip)
        {
            Prepare(clip);
            clip.SetProperty("probe-tag", "carried");
            clip.Note = "outside the properties";
            Assert.True(layer.AddClip(clip));

            TimelineElement copied = clip.Copy(deep: true);
            DeepCopyProbeClip copy = Assert.IsType<DeepCopyProbeClip>(copied);

            DeepCopyObservation seen = Assert.Single(DeepCopyProbeClip.Observations);
            _output.WriteLine(FormattableString.Invariant($"observed: {seen}"));

            Assert.True(seen.CopyIsManaged);

            // ges_timeline_element_copy writes every readable and writable
            // property of the class onto the copy before the slot runs, the
            // installed ones included.
            Assert.Equal("carried", seen.Tag);

            // Nothing sank the reference the caller still holds: the copy was
            // floating, with the caller's reference and the wrapper's own.
            Assert.True(seen.IsFloating);
            Assert.Equal(2u, seen.RefCount);

            // The note is not a property, so only the override carries it.
            Assert.Equal("outside the properties", copy.Note);

            // The return of ges_timeline_element_copy settles the reference the
            // caller was holding, which leaves the wrapper's own one behind:
            // the copy is no longer floating and is still usable.
            Assert.Equal(0, GObjectNative.ObjectIsFloating(copy.Handle));
            Assert.Equal(1u, RefCountOf(copy.Handle));
            Assert.Equal("carried", copy.GetProperty<string>("probe-tag"));

            copy.Dispose();
        }
    }

    /// <summary>
    /// The hazard the slot was left out for: two managed clips inside a native
    /// group, the group deep copied and the copy released. The group stores the
    /// copy of each child by a plain assignment and unreferences it in its own
    /// free path, so a wrapper that had sunk the floating reference would lose
    /// the one it owns.
    /// </summary>
    /// <remarks>
    /// Two clips, because <c>ges_container_group</c> answers the one container
    /// it was given when the list holds a single one: a group is only built for
    /// two or more.
    /// </remarks>
    [Fact]
    public void AManagedClipInsideANativeGroupSurvivesTheGroupBeingCopied()
    {
        GstGES.Initialize();
        DeepCopyProbeClip.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        DeepCopyProbeClip first = DeepCopyProbeClip.New();
        DeepCopyProbeClip second = DeepCopyProbeClip.New();

        using (first)
        using (second)
        {
            Prepare(first);
            Prepare(second, ClockTime.FromSeconds(4));
            first.SetProperty("probe-tag", "first");
            second.SetProperty("probe-tag", "second");
            Assert.True(layer.AddClip(first));
            Assert.True(layer.AddClip(second));

            Container grouped = Container.Group([first, second])
                ?? throw new InvalidOperationException("The clips could not be grouped.");

            // A real GESGroup, not one of the two clips handed back.
            Group group = Assert.IsType<Group>(grouped);

            using (group)
            {
                TimelineElement copied = group.Copy(deep: true);

                // Both managed clips inside the group were deep copied, each
                // through its own override.
                Assert.Equal(2, DeepCopyProbeClip.Observations.Count);
                Assert.All(DeepCopyProbeClip.Observations, static seen =>
                {
                    Assert.True(seen.CopyIsManaged);
                    Assert.True(seen.IsFloating);
                });

                // Releasing the copy of the group releases its mapping, which
                // unreferences the copy of each clip by hand. That reference is
                // the copy's own floating one and nothing the wrapper owns.
                copied.Dispose();
            }

            _output.WriteLine(FormattableString.Invariant(
                $"observations: {DeepCopyProbeClip.Observations.Count}"));

            // The originals and their wrappers are untouched by all of it.
            Assert.Equal("first", first.GetProperty<string>("probe-tag"));
            Assert.Equal("second", second.GetProperty<string>("probe-tag"));
            Assert.Equal(Length, first.Duration);
        }
    }

    /// <summary>
    /// A type defined without a wrapper factory has no fabrication to offer, so
    /// the copy resolves to nothing that carries the override. The copy is still
    /// made — the trampoline hands the slot to the implementation below it —
    /// and nothing crashes.
    /// </summary>
    [Fact]
    public void AClipWithoutAWrapperFactoryIsCopiedByTheBaseClassAlone()
    {
        GstGES.Initialize();
        PlainDeepCopyClip.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        SourceClip clip = PlainDeepCopyClip.New();

        using (clip)
        {
            Prepare(clip);
            Assert.True(layer.AddClip(clip));

            // The instance is of the registered type, and its wrapper is the
            // closest registered ancestor rather than the managed class.
            Assert.IsNotType<PlainDeepCopyClip>(clip);

            uint layerReferences = RefCountOf(layer.Handle);

            TimelineElement copied = clip.Copy(deep: true);

            Assert.NotNull(copied);
            Assert.Equal(0, PlainDeepCopyClip.Calls);
            Assert.Equal(Length, copied.Duration);

            // What ran instead is the C implementation below the trampoline:
            // GESClip::_deep_copy references the layer of the original onto the
            // copy (ges-clip.c:2473) and ges_clip_dispose is what lets it go
            // (:2578-2585), so the layer carries one reference more for as long
            // as the copy lives. The property loop of the copy cannot account
            // for it: layer is a read only property.
            Assert.Equal(layerReferences + 1, RefCountOf(layer.Handle));

            copied.Dispose();

            Assert.Equal(layerReferences, RefCountOf(layer.Handle));
        }
    }

    /// <summary>
    /// A copy no property carried anything onto reaches the trampoline
    /// unresolved, and the trampoline is what builds its wrapper: the clip
    /// installs no property and overrides neither property accessor, so nothing
    /// managed sees the copy before the slot does. The wrapper the override is
    /// handed is the one the caller gets back.
    /// </summary>
    [Fact]
    public void ACopyNoPropertyResolvedIsFabricatedByTheTrampoline()
    {
        GstGES.Initialize();
        FabricateDeepCopyClip.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        FabricateDeepCopyClip clip = FabricateDeepCopyClip.New();

        using (clip)
        {
            Prepare(clip);
            Assert.True(layer.AddClip(clip));

            // Every wrapper of this type the process has built so far. The copy
            // does not exist yet, so none of them is its.
            int built = FabricateDeepCopyClip.WrappersBuilt;

            TimelineElement copied = clip.Copy(deep: true);
            FabricateDeepCopyClip copy = Assert.IsType<FabricateDeepCopyClip>(copied);

            FabricateObservation seen = Assert.Single(FabricateDeepCopyClip.Observations);
            _output.WriteLine(FormattableString.Invariant($"observed: {seen}"));

            // Exactly one wrapper was built between the two, and the slot is
            // the only managed code the copy reached: the trampoline fabricated
            // it out of the adopted constructor.
            Assert.Equal(built + 1, seen.WrappersBuilt);
            Assert.True(seen.CopyIsManaged);

            // Fabrication settles nothing either: the copy is still the
            // caller's floating one, with the reference the wrapper took.
            Assert.True(seen.IsFloating);
            Assert.Equal(2u, seen.RefCount);

            // And it is that wrapper the copy comes back as.
            Assert.Same(copy, FabricateDeepCopyClip.CopySeen);

            copy.Dispose();
        }
    }

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    private static void Prepare(Clip clip) => Prepare(clip, ClockTime.Zero);

    private static void Prepare(Clip clip, ClockTime start)
    {
        clip.SupportedFormats = TrackType.Video;
        Assert.True(clip.SetStart(start));
        Assert.True(clip.SetDuration(Length));
    }
}
