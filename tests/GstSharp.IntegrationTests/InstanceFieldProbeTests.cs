using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Names the instance field one witness proves the accessor of.
/// </summary>
/// <remarks>
/// It is what makes the meta test below registry driven: a field that joins the
/// <c>instanceFields</c> allowlist joins the registry with it, and the suite
/// fails until a test carries its key.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class InstanceFieldWitnessAttribute : Attribute
{
    /// <summary>Initialises the attribute.</summary>
    /// <param name="key">The field, as <c>GstBaseSink.segment</c>.</param>
    internal InstanceFieldWitnessAttribute(string key) => Key = key;

    /// <summary>Gets the field the witness proves.</summary>
    internal string Key { get; }
}

/// <summary>
/// Pins the generated instance field accessors to the library installed on this
/// machine, in three layers that do not rest on each other.
/// </summary>
/// <remarks>
/// <para>
/// An accessor reads a field at the instance size of the parent type plus the
/// offset the own fields mirror measured. Nothing in that arithmetic is checked
/// by the managed compiler, and a mirror with a wrong layout agrees with itself:
/// writing through it and reading back through it succeeds whatever bytes were
/// used. So each layer puts something other than the mirror on the far side of
/// the field.
/// </para>
/// <para>
/// Layer 1 is the total: the instance size the library registered for the parent
/// plus the size of the mirror has to equal the instance size it registered for
/// the class. It runs on every leg and covers every field of the mirror at once,
/// but it cannot see two members of the same width swapped.
/// </para>
/// <para>
/// Layer 2 is the per field offset against what a C compiler measured, which is
/// the only place a literal appears: the tests cite the C expression rather than
/// where it was run.
/// </para>
/// <para>
/// Layer 3 is behavioural: the library is made to write a distinctive segment
/// through a public path and the accessor reads it back, which is the only layer
/// that would catch two swapped members.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class InstanceFieldProbeTests
{
    private static readonly ClockTime StateTimeout = ClockTime.FromSeconds(5);

    /// <summary>
    /// The instance field registry of every module that emits one. A module that
    /// gains an exposed field is one entry here and nothing else.
    /// </summary>
    private static readonly Func<InstanceMirrorProbe[]>[] Registries =
    [
        Gst.Base.InstanceFieldRegistry.CreateEntries,
    ];

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public InstanceFieldProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Gets the C name of every class whose own fields are mirrored, which is
    /// what the size theory is parameterised by.
    /// </summary>
    public static TheoryData<string> MirroredClasses
    {
        get
        {
            TheoryData<string> data = [];
            foreach (InstanceMirrorProbe entry in Rows())
            {
                data.Add(entry.CName);
            }

            return data;
        }
    }

    /// <summary>
    /// The library is the ground truth for the total size of an instance:
    /// <c>g_type_query</c> reports what <c>g_type_create_instance</c> would
    /// allocate, and a class allocates its parent plus what it declares itself.
    /// </summary>
    /// <param name="cName">The C name of the class, as the registry rows name it.</param>
    /// <remarks>
    /// This is the one measurement that covers a whole mirror at once: a field it
    /// declares too narrow, one it leaves out and one it invents all move the
    /// total. <c>GstBaseSrc.typefind</c> stands behind
    /// <c>GST_REMOVE_DEPRECATED</c> in <c>gstbasesrc.h</c>, so a library built
    /// with that macro defined reports 8 bytes less for it; no CI leg uses such a
    /// build, and the field sits behind the exposed one, so the accessor would be
    /// right either way.
    /// </remarks>
    [Theory]
    [MemberData(nameof(MirroredClasses))]
    public void EveryMirrorIsTheDifferenceTheRunningLibraryReports(string cName)
    {
        InstanceMirrorProbe entry = Row(cName);

        GObjectNative.TypeQuery(entry.GetGType(), out GTypeQuery own);
        GObjectNative.TypeQuery(entry.ParentGetGType(), out GTypeQuery parent);

        _output.WriteLine(FormattableString.Invariant(
            $"{cName}: instance_size={own.InstanceSize} parent={parent.InstanceSize} mirror={entry.OwnSize}"));

        Assert.Equal((int)own.InstanceSize, (int)parent.InstanceSize + entry.OwnSize);
    }

    /// <summary>
    /// The own offset of every exposed field, against what a C compiler measured
    /// for it.
    /// </summary>
    /// <param name="cName">The C name of the class.</param>
    /// <param name="field">The gir name of the field.</param>
    /// <param name="offset">
    /// The offset a C compiler reported for
    /// <c>offsetof (&lt;class&gt;, &lt;field&gt;) - sizeof (GstElement)</c>.
    /// </param>
    /// <remarks>
    /// The four classes carry no C <c>long</c> anywhere in their own fields or in
    /// their parent chain, so these numbers hold on every 64 bit target; the
    /// absolute offsets do not, which is why the first term of the arithmetic is
    /// read from the library rather than written here. <c>sizeof (GstSegment)</c>
    /// is 120, which is what the 120 bytes between the offsets below and the
    /// tails of the mirrors are.
    /// </remarks>
    [Theory]
    [InlineData("GstBaseSink", "segment", 80)]
    [InlineData("GstBaseSrc", "segment", 64)]
    [InlineData("GstBaseTransform", "segment", 24)]
    [InlineData("GstBaseParse", "segment", 24)]
    public void EveryExposedFieldSitsWhereACompilerMeasuredIt(string cName, string field, int offset)
    {
        InstanceMirrorProbe entry = Row(cName);
        InstanceFieldProbe probe = Assert.Single(
            entry.Fields,
            candidate => string.Equals(candidate.Name, field, StringComparison.Ordinal));

        _output.WriteLine(FormattableString.Invariant(
            $"offsetof ({cName}, {field}) - sizeof (GstElement) = {probe.Offset}"));

        Assert.Equal(offset, probe.Offset);
    }

    /// <summary>
    /// The instance size the library reports for the parent is what the
    /// accessors add, and <c>GstElement</c> is the parent of all four.
    /// </summary>
    [Fact]
    public void TheParentTermComesFromTheLibrary()
    {
        GObjectNative.TypeQuery(Element.GetGType(), out GTypeQuery query);

        _output.WriteLine(FormattableString.Invariant($"sizeof (GstElement) = {query.InstanceSize}"));

        // Every own offset above is measured from this, so a library that
        // reported anything else would move all four accessors together and the
        // witnesses below are what would catch it.
        Assert.Equal(264u, query.InstanceSize);
        Assert.Equal(120, Unsafe.SizeOf<SegmentRaw>());
    }

    /// <summary>
    /// A row of the registry with no witness would leave a public accessor whose
    /// only proof is that the mirror agrees with itself.
    /// </summary>
    /// <remarks>
    /// It counts the attributes of the witnesses and not their executions, so
    /// none of them is gated on an element being installed: a witness that
    /// skipped would satisfy this test without proving anything. The three
    /// elements they use - <c>fakesrc</c>, <c>fakesink</c> and <c>identity</c> -
    /// are <c>coreelements</c>, which ships inside libgstreamer itself, and the
    /// fourth witness mints its parser in the test for the same reason.
    /// </remarks>
    [Fact]
    public void EveryExposedFieldHasABehaviouralWitness()
    {
        HashSet<string> witnessed = new(StringComparer.Ordinal);
        foreach (MethodInfo method in typeof(InstanceFieldProbeTests).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            foreach (InstanceFieldWitnessAttribute attribute in
                method.GetCustomAttributes<InstanceFieldWitnessAttribute>())
            {
                _ = witnessed.Add(attribute.Key);
            }
        }

        _output.WriteLine("witnessed: " + string.Join(", ", witnessed.Order(StringComparer.Ordinal)));

        foreach (InstanceMirrorProbe entry in Rows())
        {
            foreach (InstanceFieldProbe field in entry.Fields)
            {
                Assert.Contains(entry.CName + "." + field.Name, witnessed, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// <c>gst_base_src_new_segment</c> copies the segment it is handed into the
    /// field, so a public setter writes the very storage the accessor reads.
    /// </summary>
    /// <remarks>
    /// No pipeline is needed: the call is legal while the source is in NULL, and
    /// <c>gst_base_src_set_format</c> demands a state of READY or below.
    /// </remarks>
    [InstanceFieldWitness("GstBaseSrc.segment")]
    [Fact]
    public void TheSourceAccessorReadsWhatNewSegmentWrote()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesrc", "witness-src"));
        BaseSrc source = Assert.IsAssignableFrom<BaseSrc>(element);

        source.SetFormat(Format.Time);

        using Segment written = Distinctive();
        Assert.True(source.NewSegment(written));

        using Segment read = source.GetSegment();
        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_base_sink_event</c> assigns the segment of a segment event to the
    /// field, so the library writes it and the accessor reads it back.
    /// </summary>
    [InstanceFieldWitness("GstBaseSink.segment")]
    [Fact]
    public void TheSinkAccessorReadsWhatASegmentEventWrote()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "witness-sink"));
        BaseSink sink = Assert.IsAssignableFrom<BaseSink>(element);

        using Segment written = Distinctive();
        using Segment read = Witness(element, "sink", written, sink.GetSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_base_transform_sink_event</c> copies the segment out of the event
    /// before it forwards it, so an unlinked source pad does not matter.
    /// </summary>
    [InstanceFieldWitness("GstBaseTransform.segment")]
    [Fact]
    public void TheTransformAccessorReadsWhatASegmentEventWrote()
    {
        using Element element = Assert.IsAssignableFrom<Element>(
            ElementFactory.Make("identity", "witness-transform"));
        BaseTransform transform = Assert.IsAssignableFrom<BaseTransform>(element);

        using Segment written = Distinctive();
        using Segment read = Witness(element, "sink", written, transform.GetSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_base_parse_sink_event_default</c> copies a TIME segment into the
    /// field. A BYTES one is rewritten into a TIME segment of its own, which is
    /// why the witness sends TIME.
    /// </summary>
    /// <remarks>
    /// The parser is minted by the test rather than borrowed from the machine, so
    /// the witness needs no plugin beyond core.
    /// </remarks>
    [InstanceFieldWitness("GstBaseParse.segment")]
    [Fact]
    public void TheParserAccessorReadsWhatASegmentEventWrote()
    {
        using ProbeParse parse = new();

        using Segment written = Distinctive();
        using Segment read = Witness(parse, "sink", written, parse.GetSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// Takes one element to PAUSED, pushes a stream start and a segment at the
    /// named pad of it, and reads the field back.
    /// </summary>
    /// <param name="element">The element to drive.</param>
    /// <param name="padName">The pad the events are sent to.</param>
    /// <param name="segment">The segment the event carries.</param>
    /// <param name="read">The accessor under test.</param>
    /// <returns>What the accessor answered while the element was still in PAUSED.</returns>
    /// <remarks>
    /// The read happens before the element goes back to NULL, because the
    /// downward state change resets the segment of a base class that keeps one:
    /// a parser initialises the field in PAUSED to READY, so a read afterwards
    /// answers a default whatever the layout is. The element is returned to NULL
    /// all the same before it is disposed, because a disposal from PAUSED logs a
    /// critical - which this suite does not make fatal - and leaks the pads with
    /// it.
    /// </remarks>
    private Segment Witness(Element element, string padName, Segment segment, Func<Segment> read)
    {
        StateChangeReturn changed = element.SetState(State.Paused);
        _output.WriteLine(FormattableString.Invariant($"{element.Name}: set_state(PAUSED) = {changed}"));
        Assert.NotEqual(StateChangeReturn.Failure, changed);

        try
        {
            using Pad pad = Assert.IsAssignableFrom<Pad>(element.GetStaticPad(padName));

            Assert.True(pad.SendEvent(Event.NewStreamStart("instance-field-witness")));
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));

            return read();
        }
        finally
        {
            _ = element.SetState(State.Null);
            _ = element.GetState(out State _, out State _, StateTimeout);
        }
    }

    /// <summary>
    /// Builds a segment no default and no other test could produce, so that a
    /// read of the wrong storage cannot pass by coincidence.
    /// </summary>
    /// <returns>The segment.</returns>
    /// <remarks>
    /// Every member carries a value of its own, and none of them is the zero or
    /// the <c>-1</c> that <c>gst_segment_init</c> leaves, except the rate: the
    /// sink applies an instant rate multiplier to the rate it stores, and the
    /// multiplier of a segment event without an instant rate change is 1.0.
    /// </remarks>
    private static Segment Distinctive()
    {
        Segment segment = Segment.New();
        segment.Init(Format.Time);
        segment.SetRate(1.0);
        segment.SetAppliedRate(2.0);
        segment.SetBase(11_000_000_000);
        segment.SetOffset(12_000_000_000);
        segment.SetStart(13_000_000_000);
        segment.SetStop(14_000_000_000);
        segment.SetTime(15_000_000_000);
        segment.SetPosition(13_500_000_000);
        segment.SetDuration(17_000_000_000);
        return segment;
    }

    /// <summary>Compares every member of two segments.</summary>
    /// <param name="expected">What was written.</param>
    /// <param name="actual">What the accessor read.</param>
    /// <remarks>
    /// Every member is compared rather than one: the layer above measures the
    /// offset of the structure, and what is left for a witness to catch is two
    /// members of the same width laid out the wrong way round.
    /// </remarks>
    private void AssertSame(Segment expected, Segment actual)
    {
        _output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"read: flags={actual.Flags} rate={actual.Rate} applied={actual.AppliedRate} format={actual.Format} "
            + $"base={actual.Base} offset={actual.Offset} start={actual.Start} stop={actual.Stop} "
            + $"time={actual.Time} position={actual.Position} duration={actual.Duration}"));

        Assert.Equal(expected.Flags, actual.Flags);
        Assert.Equal(expected.Rate, actual.Rate);
        Assert.Equal(expected.AppliedRate, actual.AppliedRate);
        Assert.Equal(expected.Format, actual.Format);
        Assert.Equal(expected.Base, actual.Base);
        Assert.Equal(expected.Offset, actual.Offset);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.Stop, actual.Stop);
        Assert.Equal(expected.Time, actual.Time);
        Assert.Equal(expected.Position, actual.Position);
        Assert.Equal(expected.Duration, actual.Duration);
    }

    /// <summary>Returns every row of every registry.</summary>
    /// <returns>The rows.</returns>
    private static IEnumerable<InstanceMirrorProbe> Rows()
    {
        foreach (Func<InstanceMirrorProbe[]> registry in Registries)
        {
            foreach (InstanceMirrorProbe entry in registry())
            {
                yield return entry;
            }
        }
    }

    /// <summary>Returns the row of one class.</summary>
    /// <param name="cName">The C name of the class.</param>
    /// <returns>The row.</returns>
    private static InstanceMirrorProbe Row(string cName)
    {
        foreach (InstanceMirrorProbe entry in Rows())
        {
            if (string.Equals(entry.CName, cName, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        throw new InvalidOperationException($"No module registry carries a row for '{cName}'.");
    }
}
