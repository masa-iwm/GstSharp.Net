using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The frozen size of the subclassing surface: how many class struct mirrors
/// the run emits, how many overridable slots they carry, and which slots the
/// ledger says carry no <c>OnX</c> member.
/// </summary>
/// <remarks>
/// The two categories are counted the same way the classes, records and methods
/// of <see cref="ClassEmitterTests"/> are, so a class joining or leaving the
/// <c>subclassable</c> allowlist, and a slot the planner starts or stops
/// refusing, both move a number here rather than passing unnoticed.
/// </remarks>
public sealed class SubclassCensusTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    /// <summary>
    /// Every module of the run, so that a mirror appearing in a module that
    /// has none today fails as loudly as a count that moves.
    /// </summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="classStructs">The mirrored class structs.</param>
    /// <param name="vfuncs">The slots those mirrors give an <c>OnX</c> member.</param>
    [Theory]
    [InlineData("Gst", 4, 19)]
    [InlineData("GstBase", 7, 99)]
    [InlineData("GstApp", 0, 0)]
    [InlineData("GstAudio", 7, 56)]
    [InlineData("GstVideo", 4, 45)]
    [InlineData("GstPbutils", 0, 0)]
    [InlineData("GstSdp", 0, 0)]
    [InlineData("GstWebRTC", 0, 0)]
    [InlineData("GstNet", 0, 0)]
    [InlineData("GstRtsp", 0, 0)]
    [InlineData("GstRtp", 0, 0)]
    [InlineData("GstRtspServer", 0, 0)]
    [InlineData("GstAllocators", 0, 0)]
    [InlineData("GstTag", 0, 0)]
    [InlineData("GstTranscoder", 0, 0)]
    [InlineData("GstPlay", 0, 0)]
    [InlineData("GES", 8, 22)]
    public void TheSubclassingCensusIsStable(string module, int classStructs, int vfuncs)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(classStructs, census.EmittedCount(module, "class struct"));
        Assert.Equal(vfuncs, census.EmittedCount(module, "vfunc"));
    }

    /// <summary>
    /// The run as a whole: thirty mirrors and two hundred and forty one slots,
    /// the numbers the release notes and <c>docs/subclassing.md</c> quote.
    /// </summary>
    [Fact]
    public void TheRunEmitsThirtyMirrorsAndTwoHundredAndFortyOneSlots()
    {
        EmissionCensus census = Generated.Census;
        int mirrors = 0;
        int slots = 0;
        foreach (string module in new[] { "Gst", "GstBase", "GstAudio", "GstVideo", "GES" })
        {
            mirrors += census.EmittedCount(module, "class struct");
            slots += census.EmittedCount(module, "vfunc");
        }

        Assert.Equal(30, mirrors);
        Assert.Equal(241, slots);
    }

    /// <summary>
    /// The other half of the measurement: the slots a mirror lays out and the
    /// managed surface leaves alone, with the statement that says why. Freezing
    /// the reasons and not only the count is what keeps an overlay entry from
    /// silently changing what a slot is missing for.
    /// </summary>
    [Fact]
    public void TheVirtualLedgerListsTheExpectedSlots()
    {
        EmissionCensus census = Generated.Census;

        const string ClassClosure =
            "signal class closure: read by g_signal at emission time, never called through the class "
            + "pointer by the base class; managed code subscribes to the signal instead";
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Gst.Bin::deep_element_added"] = ClassClosure,
                ["Gst.Bin::deep_element_removed"] = ClassClosure,
                ["Gst.Bin::element_added"] = ClassClosure,
                ["Gst.Bin::element_removed"] = ClassClosure,
                ["Gst.Element::no_more_pads"] = ClassClosure,
                ["Gst.Element::pad_added"] = ClassClosure,
                ["Gst.Element::pad_removed"] = ClassClosure,
            },
            census.SkippedVirtuals("Gst"));

        Assert.Empty(census.SkippedVirtuals("GstBase"));

        Assert.Empty(census.SkippedVirtuals("GstAudio"));

        Assert.Empty(census.SkippedVirtuals("GstVideo"));

        const string GListReturn =
            "the slot answers a GList whose container the caller takes, which the reverse planner has "
            + "no bucket for; the C default implementation wraps create_track_element "
            + "(ges-clip.c:2796-2808), so a managed clip that implements create_track_element already "
            + "behaves the way this slot would make it behave";
        const string FloatingCopy =
            "the copy the slot is handed arrives floating, and FromNative(copy, None) settles that "
            + "reference into the wrapper, while GESContainer::_deep_copy (ges-container.c:332) adopts "
            + "the very same reference by a plain store and unrefs it in _free_mapping: a managed clip "
            + "deep copied inside a native group would lose the reference its wrapper owns. Temporary, "
            + "until a floating vfunc argument can be borrowed without settling it; state that is worth "
            + "copying belongs in an installed property, which ges_timeline_element_copy copies by itself";
        const string Opaque = "OpaqueSlot";
        const string Unsupported = "UnsupportedSignature";
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GES.AudioSource::create_source"] = Opaque,
                ["GES.Clip::create_track_elements"] = GListReturn,
                ["GES.Container::group"] = Opaque,
                ["GES.TimelineElement::deep_copy"] = FloatingCopy,
                ["GES.TimelineElement::list_children_properties"] = Unsupported,
                ["GES.TimelineElement::lookup_child"] = Unsupported,
                ["GES.TimelineElement::set_child_property"] = Unsupported,
                ["GES.TimelineElement::set_child_property_full"] = Unsupported,
                ["GES.TrackElement::list_children_properties"] = Unsupported,
                ["GES.TrackElement::lookup_child"] = Unsupported,
                ["GES.VideoSource::create_filters"] = Opaque,
                ["GES.VideoSource::create_source"] = Opaque,
                ["GES.VideoSource::get_natural_size"] = Opaque,
                ["GES.VideoSource::needs_converters"] = Opaque,
            },
            census.SkippedVirtuals("GES"));

        Assert.Equal(21, census.SkippedVirtualCount());
    }
}
