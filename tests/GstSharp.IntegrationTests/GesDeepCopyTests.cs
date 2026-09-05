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
    /// The hazard the slot was left out for: a managed clip inside a native
    /// group, the group deep copied and the copy released. The group stores the
    /// copy of the clip by a plain assignment and unreferences it in its own
    /// free path, so a wrapper that had sunk the floating reference would lose
    /// the one it owns.
    /// </summary>
    [Fact]
    public void AManagedClipInsideANativeGroupSurvivesTheGroupBeingCopied()
    {
        GstGES.Initialize();
        DeepCopyProbeClip.Reset();

        using Timeline timeline = Timeline.NewAudioVideo();
        using Layer layer = timeline.AppendLayer();

        DeepCopyProbeClip clip = DeepCopyProbeClip.New();

        using (clip)
        {
            Prepare(clip);
            clip.SetProperty("probe-tag", "grouped");
            Assert.True(layer.AddClip(clip));

            Container grouped = Container.Group([clip])
                ?? throw new InvalidOperationException("The clip could not be grouped.");

            using (grouped)
            {
                TimelineElement copied = grouped.Copy(deep: true);

                // The managed clip inside the group was deep copied too.
                DeepCopyObservation seen = Assert.Single(DeepCopyProbeClip.Observations);
                Assert.True(seen.CopyIsManaged);
                Assert.True(seen.IsFloating);

                // Releasing the copy of the group releases its mapping, which
                // unreferences the copy of the clip by hand.
                copied.Dispose();

                // The original clip and its wrapper are untouched by all of
                // it: the unreference the mapping made was of the copy's own
                // floating reference and of nothing the wrapper owns.
                Assert.Equal("grouped", clip.GetProperty<string>("probe-tag"));
                Assert.Equal(Length, clip.Duration);
            }
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

            TimelineElement copied = clip.Copy(deep: true);

            Assert.NotNull(copied);
            Assert.Equal(0, PlainDeepCopyClip.Calls);
            Assert.Equal(Length, copied.Duration);

            copied.Dispose();
        }
    }

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    private static void Prepare(Clip clip)
    {
        clip.SupportedFormats = TrackType.Video;
        Assert.True(clip.SetStart(ClockTime.Zero));
        Assert.True(clip.SetDuration(Length));
    }
}
