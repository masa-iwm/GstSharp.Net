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
        Gst.Audio.InstanceFieldRegistry.CreateEntries,
        Gst.Video.InstanceFieldRegistry.CreateEntries,
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
    /// None of the classes carries a C <c>long</c>, a bitfield or a union anywhere
    /// in its own fields or in its parent chain, so these numbers hold on every 64
    /// bit target; the
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
    [InlineData("GstAudioDecoder", "input_segment", 32)]
    [InlineData("GstAudioDecoder", "output_segment", 152)]
    [InlineData("GstAudioEncoder", "input_segment", 32)]
    [InlineData("GstAudioEncoder", "output_segment", 152)]
    [InlineData("GstVideoDecoder", "input_segment", 32)]
    [InlineData("GstVideoDecoder", "output_segment", 152)]
    [InlineData("GstVideoEncoder", "input_segment", 32)]
    [InlineData("GstVideoEncoder", "output_segment", 152)]
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
    /// accessors add, and <c>GstElement</c> is the parent of every one of them.
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
    /// <c>gst_audio_decoder_sink_eventfunc</c> copies the segment of a TIME
    /// segment event into the input field, at <c>gstaudiodecoder.c:2457</c>.
    /// </summary>
    /// <remarks>
    /// The four codec base classes are minted by the test rather than borrowed
    /// from the machine, so none of these witnesses needs a plugin beyond core,
    /// and each of them keeps the source pad unlinked: every writer below runs
    /// before the push that an unlinked pad refuses.
    /// </remarks>
    [InstanceFieldWitness("GstAudioDecoder.input_segment")]
    [Fact]
    public void TheAudioDecoderInputAccessorReadsWhatASegmentEventWrote()
    {
        using ProbeAudioDecoder decoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(decoder, "sink", written, decoder.GetInputSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// The audio decoder queues the segment event and forwards it when the
    /// stream ends: EOS drains through <c>send_pending_events</c>
    /// (<c>gstaudiodecoder.c:2528-2529</c>), which copies the segment into the
    /// output field at <c>:641</c> before it pushes the event.
    /// </summary>
    [InstanceFieldWitness("GstAudioDecoder.output_segment")]
    [Fact]
    public void TheAudioDecoderOutputAccessorReadsWhatTheFlushedSegmentEventWrote()
    {
        using ProbeAudioDecoder decoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(
            decoder,
            "sink",
            written,
            decoder.GetOutputSegment,
            after: [Event.NewEos()]);

        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_audio_encoder_sink_event_default</c> copies the segment of a TIME
    /// segment event into the input field, at <c>gstaudioencoder.c:1622</c>.
    /// </summary>
    [InstanceFieldWitness("GstAudioEncoder.input_segment")]
    [Fact]
    public void TheAudioEncoderInputAccessorReadsWhatASegmentEventWrote()
    {
        using ProbeAudioEncoder encoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(encoder, "sink", written, encoder.GetInputSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// The audio encoder queues the segment event rather than forwarding it
    /// (<c>gstaudioencoder.c:1624</c> appends it to the early pending events) and
    /// flushes the queue when the stream ends (<c>:1657</c>), which copies the
    /// segment into the output field at <c>:611</c> before the push.
    /// </summary>
    [InstanceFieldWitness("GstAudioEncoder.output_segment")]
    [Fact]
    public void TheAudioEncoderOutputAccessorReadsWhatTheFlushedSegmentEventWrote()
    {
        using ProbeAudioEncoder encoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(
            encoder,
            "sink",
            written,
            encoder.GetOutputSegment,
            after: [Event.NewEos()]);

        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_video_decoder_sink_event_default</c> copies the segment of a TIME
    /// segment event into the input field, at <c>gstvideodecoder.c:1598</c>.
    /// </summary>
    [InstanceFieldWitness("GstVideoDecoder.input_segment")]
    [Fact]
    public void TheVideoDecoderInputAccessorReadsWhatASegmentEventWrote()
    {
        using ProbeVideoDecoder decoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(decoder, "sink", written, decoder.GetInputSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// The video decoder does not flush its queued events at EOS - that path
    /// only drains (<c>gstvideodecoder.c:1492-1499</c>) - so the witness sends a
    /// gap: <c>gst_video_decoder_handle_gap</c> pushes the pending events
    /// (<c>:1403-1417</c>), which copies the segment into the output field at
    /// <c>:1107</c>.
    /// </summary>
    /// <remarks>
    /// The gap path runs only once an output state exists, which the caps event
    /// makes the probe set. Negotiating it on an unlinked source pad fails, and
    /// the library logs a warning for that rather than refusing the caps, so
    /// neither the caps nor the gap is asserted on.
    /// </remarks>
    [InstanceFieldWitness("GstVideoDecoder.output_segment")]
    [Fact]
    public void TheVideoDecoderOutputAccessorReadsWhatTheGapFlushedSegmentEventWrote()
    {
        using ProbeVideoDecoder decoder = new();
        using Caps caps = Assert.IsType<Caps>(
            Caps.FromString("video/x-raw,format=GRAY8,width=16,height=16,framerate=30/1"));

        using Segment written = Distinctive();
        using Segment read = Witness(
            decoder,
            "sink",
            written,
            decoder.GetOutputSegment,
            before: [Event.NewCaps(caps)],
            after: [Event.NewGap(ClockTime.FromNanoseconds(13_000_000_000), ClockTime.FromNanoseconds(1_000_000_000))]);

        AssertSame(written, read);
    }

    /// <summary>
    /// <c>gst_video_encoder_sink_event_default</c> copies the segment of a TIME
    /// segment event into the input field, at <c>gstvideoencoder.c:1186</c>.
    /// </summary>
    [InstanceFieldWitness("GstVideoEncoder.input_segment")]
    [Fact]
    public void TheVideoEncoderInputAccessorReadsWhatASegmentEventWrote()
    {
        using ProbeVideoEncoder encoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(encoder, "sink", written, encoder.GetInputSegment);

        AssertSame(written, read);
    }

    /// <summary>
    /// The video encoder queues the segment event and pushes it when the stream
    /// ends (<c>gstvideoencoder.c:1151-1157</c>), which copies it into the output
    /// field at <c>:1062</c>. No time adjustment is applied, because none was set
    /// (<c>:482</c>).
    /// </summary>
    [InstanceFieldWitness("GstVideoEncoder.output_segment")]
    [Fact]
    public void TheVideoEncoderOutputAccessorReadsWhatTheFlushedSegmentEventWrote()
    {
        using ProbeVideoEncoder encoder = new();

        using Segment written = Distinctive();
        using Segment read = Witness(
            encoder,
            "sink",
            written,
            encoder.GetOutputSegment,
            after: [Event.NewEos()]);

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
    /// <param name="before">Events to send between the stream start and the segment.</param>
    /// <param name="after">Events to send after the segment, to make a base class act on it.</param>
    /// <returns>What the accessor answered while the element was still in PAUSED.</returns>
    /// <remarks>
    /// <para>
    /// The read happens before the element goes back to NULL, because the
    /// downward state change resets the segment of a base class that keeps one:
    /// a parser initialises the field in PAUSED to READY, so a read afterwards
    /// answers a default whatever the layout is. The element is returned to NULL
    /// all the same before it is disposed, because a disposal from PAUSED logs a
    /// critical - which this suite does not make fatal - and leaks the pads with
    /// it.
    /// </para>
    /// <para>
    /// The result of an event of <paramref name="before"/> or
    /// <paramref name="after"/> is written down and not asserted. The source pad
    /// is left unlinked on purpose, which a sticky event does not mind but an EOS
    /// and a gap do: <c>gst_pad_push_event</c> answers FALSE for them on a pad
    /// nothing is linked to, long after the base class has copied the segment it
    /// was flushing. What the field holds is the assertion.
    /// </para>
    /// </remarks>
    private Segment Witness(
        Element element,
        string padName,
        Segment segment,
        Func<Segment> read,
        Event[]? before = null,
        Event[]? after = null)
    {
        StateChangeReturn changed = element.SetState(State.Paused);
        _output.WriteLine(FormattableString.Invariant($"{element.Name}: set_state(PAUSED) = {changed}"));
        Assert.NotEqual(StateChangeReturn.Failure, changed);

        try
        {
            using Pad pad = Assert.IsAssignableFrom<Pad>(element.GetStaticPad(padName));

            Assert.True(pad.SendEvent(Event.NewStreamStart("instance-field-witness")));
            Send(pad, before);
            Assert.True(pad.SendEvent(Event.NewSegment(segment)));
            Send(pad, after);

            return read();
        }
        finally
        {
            _ = element.SetState(State.Null);
            _ = element.GetState(out State _, out State _, StateTimeout);
        }
    }

    /// <summary>Sends events at a pad and writes down what each of them answered.</summary>
    /// <param name="pad">The pad the events are sent to.</param>
    /// <param name="events">The events, or <see langword="null"/> for none.</param>
    private void Send(Pad pad, Event[]? events)
    {
        foreach (Event sent in events ?? [])
        {
            EventType type = sent.Type;
            bool answered = pad.SendEvent(sent);
            _output.WriteLine(FormattableString.Invariant($"{pad.Name}: send_event({type}) = {answered}"));
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
