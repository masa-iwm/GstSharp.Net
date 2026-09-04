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
    [InlineData("Gst", 3, 17)]
    [InlineData("GstBase", 5, 80)]
    [InlineData("GstApp", 0, 0)]
    [InlineData("GstAudio", 0, 0)]
    [InlineData("GstVideo", 0, 0)]
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
    [InlineData("GES", 0, 0)]
    public void TheSubclassingCensusIsStable(string module, int classStructs, int vfuncs)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(classStructs, census.EmittedCount(module, "class struct"));
        Assert.Equal(vfuncs, census.EmittedCount(module, "vfunc"));
    }

    /// <summary>
    /// The run as a whole: eight mirrors and ninety-seven slots, the numbers
    /// the release notes and <c>docs/subclassing.md</c> quote.
    /// </summary>
    [Fact]
    public void TheRunEmitsEightMirrorsAndNinetySevenSlots()
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(8, census.EmittedCount("Gst", "class struct") + census.EmittedCount("GstBase", "class struct"));
        Assert.Equal(97, census.EmittedCount("Gst", "vfunc") + census.EmittedCount("GstBase", "vfunc"));
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
        const string Boxed =
            "boxed parameter lent by pointer; Boxed has no borrow mode, and a copy would hide "
            + "writes the caller reads back";

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

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["GstBase.Aggregator::create_new_pad"] =
                    "Pad subclassing is Stage 3: needs a Pad ClassConfig and construct properties",
                ["GstBase.BaseSrc::do_seek"] = Boxed,
                ["GstBase.BaseSrc::prepare_seek_segment"] = Boxed,
                ["GstBase.BaseTransform::filter_meta"] = Boxed,

                // The planner refusing a shape, not an overlay entry: the meta
                // is an opaque record whose wrapper has no transfer taking
                // constructor.
                ["GstBase.BaseTransform::transform_meta"] = "UnsupportedSignature",
            },
            census.SkippedVirtuals("GstBase"));

        Assert.Equal(12, census.SkippedVirtualCount());
    }
}
