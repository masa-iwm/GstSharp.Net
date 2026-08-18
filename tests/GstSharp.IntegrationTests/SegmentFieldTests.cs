using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The eleven fields of a <c>GstSegment</c> can be read and written, and they
/// sit where the raw mirror says they do.
/// </summary>
/// <remarks>
/// <para>
/// A mirror with wrong offsets agrees with itself: writing a field through it
/// and reading the same field back succeeds no matter which bytes were used.
/// So none of these tests is a round trip. Every one of them puts a native
/// function on the other side of the field — <c>gst_segment_init</c>,
/// <c>gst_segment_do_seek</c>, <c>gst_segment_to_running_time</c>,
/// <c>gst_segment_to_stream_time</c>, <c>gst_segment_clip</c> and
/// <c>gst_segment_is_equal</c> — so that the library either wrote the bytes the
/// accessors read or read the bytes the setters wrote, and the answer is an
/// arithmetic one that only comes out right for one layout.
/// </para>
/// <para>
/// The values are nanoseconds because the segments are in
/// <see cref="Gst.Format.Time"/>. The fields carry no unit of their own; see
/// <see cref="Gst.Segment.Format"/>.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SegmentFieldTests
{
    private const ulong HalfSecond = 500_000_000;

    private const ulong OneSecond = 1_000_000_000;

    private const ulong TwoSeconds = 2 * OneSecond;

    private const ulong ThreeSeconds = 3 * OneSecond;

    private const ulong FourSeconds = 4 * OneSecond;

    private const ulong FiveSeconds = 5 * OneSecond;

    private const ulong NineSeconds = 9 * OneSecond;

    private const ulong TenSeconds = 10 * OneSecond;

    private const ulong ThirtySeconds = 30 * OneSecond;

    /// <summary>The value C writes as <c>-1</c> into an unsigned field.</summary>
    private const ulong None = ulong.MaxValue;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SegmentFieldTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <c>gst_segment_init</c> writes all eleven fields, and the accessors read
    /// back exactly what its documentation promises: no flags, a rate and an
    /// applied rate of <c>1.0</c>, the format it was given, a start and a
    /// position of <c>0</c>, and a stop and a duration of <c>-1</c>.
    /// </summary>
    [Fact]
    public void InitLeavesTheDefaultsTheLibraryDocuments()
    {
        using Segment segment = Segment.New();

        // gst_segment_new initialises to GST_FORMAT_UNDEFINED, so the format
        // field changes under a call that touches nothing else in managed code.
        Assert.Equal(Gst.Format.Undefined, segment.Format);

        segment.Init(Gst.Format.Time);

        _output.WriteLine(Describe(segment));

        Assert.Equal(SegmentFlags.None, segment.Flags);
        Assert.Equal(1.0, segment.Rate);
        Assert.Equal(1.0, segment.AppliedRate);
        Assert.Equal(Gst.Format.Time, segment.Format);
        Assert.Equal(0UL, segment.Base);
        Assert.Equal(0UL, segment.Offset);
        Assert.Equal(0UL, segment.Start);
        Assert.Equal(None, segment.Stop);
        Assert.Equal(0UL, segment.Time);
        Assert.Equal(0UL, segment.Position);
        Assert.Equal(None, segment.Duration);
    }

    /// <summary>
    /// A flushing seek is the library writing ten of the eleven fields at once,
    /// each to a different value, and the accessors have to read every one of
    /// them back. This is the read side of the mirror: the bytes were written
    /// by <c>gst_segment_do_seek</c>.
    /// </summary>
    [Fact]
    public void AFlushingSeekWritesTheFieldsTheAccessorsRead()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        bool seeked = segment.DoSeek(
            rate: 2.0,
            Gst.Format.Time,
            SeekFlags.Flush | SeekFlags.Trickmode,
            SeekType.Set,
            OneSecond,
            SeekType.Set,
            FiveSeconds,
            out bool update);

        _output.WriteLine(Describe(segment));

        Assert.True(seeked);
        Assert.True(update);

        // do_seek translates the seek flags into segment flags one by one:
        // FLUSH becomes RESET and TRICKMODE stays TRICKMODE.
        Assert.Equal(SegmentFlags.Reset | SegmentFlags.Trickmode, segment.Flags);

        Assert.Equal(2.0, segment.Rate);
        Assert.Equal(1.0, segment.AppliedRate);
        Assert.Equal(Gst.Format.Time, segment.Format);

        // A flushing seek resets the running time, and the seek moved the start
        // itself, so nothing has elapsed inside the new segment.
        Assert.Equal(0UL, segment.Base);
        Assert.Equal(0UL, segment.Offset);

        Assert.Equal(OneSecond, segment.Start);
        Assert.Equal(FiveSeconds, segment.Stop);
        Assert.Equal(OneSecond, segment.Time);
        Assert.Equal(OneSecond, segment.Position);

        // The duration is the one field a seek never writes.
        Assert.Equal(None, segment.Duration);
    }

    /// <summary>
    /// A seek at a negative rate plays the segment backwards, so playback
    /// starts at the stop end: the library leaves the segment time at the start
    /// and the position at the stop. It is the one seek in which those two
    /// fields hold different values, which is what tells them apart.
    /// </summary>
    [Fact]
    public void AReverseSeekLeavesThePositionAtTheStopEnd()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        bool seeked = segment.DoSeek(
            rate: -2.0,
            Gst.Format.Time,
            SeekFlags.Flush,
            SeekType.Set,
            OneSecond,
            SeekType.Set,
            FiveSeconds,
            out bool update);

        _output.WriteLine(Describe(segment));

        Assert.True(seeked);
        Assert.True(update);

        Assert.Equal(-2.0, segment.Rate);
        Assert.Equal(OneSecond, segment.Start);
        Assert.Equal(FiveSeconds, segment.Stop);
        Assert.Equal(OneSecond, segment.Time);
        Assert.Equal(FiveSeconds, segment.Position);
    }

    /// <summary>
    /// A non flushing seek reads the position that was written by hand and
    /// turns it into a base and an offset the accessors then read. It is the
    /// only shape in which those two fields come out non zero, which is what
    /// pins them: a base of two seconds is the running time of a position of
    /// three seconds in a segment that starts at one.
    /// </summary>
    [Fact]
    public void ANonFlushingSeekCarriesThePositionWrittenByHand()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        segment.SetStart(OneSecond);
        segment.SetStop(NineSeconds);
        segment.SetPosition(ThreeSeconds);

        bool seeked = segment.DoSeek(
            rate: 1.0,
            Gst.Format.Time,
            SeekFlags.None,
            SeekType.None,
            0,
            SeekType.None,
            0,
            out bool update);

        _output.WriteLine(Describe(segment));

        Assert.True(seeked);

        // Neither end moved, so the position did not either.
        Assert.False(update);

        // Without a flush the elapsed time is remembered: the base becomes the
        // running time of the old position, and the offset becomes how far into
        // the segment that position sat.
        Assert.Equal(TwoSeconds, segment.Base);
        Assert.Equal(TwoSeconds, segment.Offset);

        Assert.Equal(OneSecond, segment.Start);
        Assert.Equal(NineSeconds, segment.Stop);
        Assert.Equal(OneSecond, segment.Time);
        Assert.Equal(ThreeSeconds, segment.Position);
        Assert.Equal(SegmentFlags.None, segment.Flags);
        Assert.Equal(1.0, segment.Rate);
    }

    /// <summary>
    /// A seek relative to the end reads the duration that was written by hand:
    /// <c>gst_segment_do_seek</c> refuses to move the start at all when the
    /// duration is unknown, so a start of thirty seconds can only come from the
    /// duration field being where the mirror says it is.
    /// </summary>
    [Fact]
    public void AnEndRelativeSeekReadsTheDurationWrittenByHand()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        segment.SetDuration(ThirtySeconds);

        // Everything else is still 0 or -1, so a duration read out of any other
        // slot would be one of those instead.
        Assert.Equal(ThirtySeconds, segment.Duration);

        bool seeked = segment.DoSeek(
            rate: 1.0,
            Gst.Format.Time,
            SeekFlags.Flush,
            SeekType.End,
            0,
            SeekType.None,
            0,
            out bool update);

        _output.WriteLine(Describe(segment));

        Assert.True(seeked);
        Assert.True(update);

        Assert.Equal(ThirtySeconds, segment.Start);
        Assert.Equal(ThirtySeconds, segment.Time);
        Assert.Equal(ThirtySeconds, segment.Position);
        Assert.Equal(None, segment.Stop);
        Assert.Equal(0UL, segment.Base);
        Assert.Equal(SegmentFlags.Reset, segment.Flags);

        // The seek left the duration alone.
        Assert.Equal(ThirtySeconds, segment.Duration);
    }

    /// <summary>
    /// The running time is computed by the library out of the rate, the base,
    /// the offset and the start that were written by hand, and it is an
    /// arithmetic answer rather than a copy: three seconds sits two seconds
    /// into a segment that starts at one, which at double speed is one second
    /// of running time, plus half a second of base.
    /// </summary>
    [Fact]
    public void RunningTimeIsComputedFromTheFieldsWrittenByHand()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        segment.SetRate(2.0);
        segment.SetStart(OneSecond);
        segment.SetStop(NineSeconds);
        segment.SetBase(HalfSecond);

        ulong runningTime = segment.ToRunningTime(Gst.Format.Time, ThreeSeconds);

        _output.WriteLine(FormattableString.Invariant(
            $"to_running_time(3s) = {runningTime} ns"));

        Assert.Equal(OneSecond + HalfSecond, runningTime);

        // And the inverse, which reads the same four fields the other way
        // round, lands back on the position it started from.
        Assert.Equal(ThreeSeconds, segment.PositionFromRunningTime(Gst.Format.Time, runningTime));
    }

    /// <summary>
    /// The stream time is computed out of the segment time and the applied
    /// rate: three seconds is two seconds into a segment that starts at one,
    /// the applied rate stretches that to four, and the segment sits at ten
    /// seconds in the stream.
    /// </summary>
    [Fact]
    public void StreamTimeIsComputedFromTheTimeAndAppliedRateWrittenByHand()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        segment.SetStart(OneSecond);
        segment.SetStop(NineSeconds);
        segment.SetTime(TenSeconds);
        segment.SetAppliedRate(2.0);

        ulong streamTime = segment.ToStreamTime(Gst.Format.Time, ThreeSeconds);

        _output.WriteLine(FormattableString.Invariant(
            $"to_stream_time(3s) = {streamTime} ns"));

        Assert.Equal(TenSeconds + FourSeconds, streamTime);
    }

    /// <summary>
    /// Clipping is the library reading the start and the stop that were written
    /// by hand and answering with the boundaries themselves, which is a value
    /// that comes straight out of the two fields.
    /// </summary>
    [Fact]
    public void ClipUsesTheStartAndStopWrittenByHand()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        segment.SetStart(TwoSeconds);
        segment.SetStop(FourSeconds);

        Assert.True(segment.Clip(Gst.Format.Time, OneSecond, FiveSeconds, out ulong clipStart, out ulong clipStop));

        _output.WriteLine(FormattableString.Invariant(
            $"clip(1s, 5s) = [{clipStart}, {clipStop}] ns"));

        Assert.Equal(TwoSeconds, clipStart);
        Assert.Equal(FourSeconds, clipStop);

        // And a region behind the stop falls outside the segment entirely.
        Assert.False(segment.Clip(Gst.Format.Time, FiveSeconds, NineSeconds, out _, out _));
    }

    /// <summary>
    /// <c>gst_segment_is_equal</c> compares all eleven fields, so it is the
    /// library reading the flags that were written by hand: two segments that
    /// only differ in their flags are unequal, and they are equal again once
    /// the other one carries the same flags.
    /// </summary>
    [Fact]
    public void TheFlagsWrittenByHandAreTheOnesTheLibraryCompares()
    {
        using Segment left = Segment.New();
        using Segment right = Segment.New();

        left.Init(Gst.Format.Time);
        right.Init(Gst.Format.Time);
        Assert.True(left.IsEqual(right));

        left.SetFlags(SegmentFlags.Trickmode | SegmentFlags.Segment);
        Assert.Equal(SegmentFlags.Trickmode | SegmentFlags.Segment, left.Flags);
        Assert.False(left.IsEqual(right));

        right.SetFlags(SegmentFlags.Trickmode | SegmentFlags.Segment);
        Assert.True(left.IsEqual(right));
    }

    /// <summary>
    /// Every setter writes a slot of its own. The eleven values are pairwise
    /// different, so an accessor that read the wrong field would come back with
    /// one of the other ten.
    /// </summary>
    [Fact]
    public void EveryFieldKeepsItsOwnSlot()
    {
        using Segment segment = Segment.New();

        // Not the format the segment ends up in, so that SetFormat has
        // something to change.
        segment.Init(Gst.Format.Bytes);

        segment.SetFlags(SegmentFlags.TrickmodeNoAudio);
        segment.SetRate(2.5);
        segment.SetAppliedRate(0.5);
        segment.SetFormat(Gst.Format.Time);
        segment.SetBase(11);
        segment.SetOffset(22);
        segment.SetStart(33);
        segment.SetStop(44);
        segment.SetTime(55);
        segment.SetPosition(66);
        segment.SetDuration(77);

        Assert.Equal(SegmentFlags.TrickmodeNoAudio, segment.Flags);
        Assert.Equal(2.5, segment.Rate);
        Assert.Equal(0.5, segment.AppliedRate);
        Assert.Equal(Gst.Format.Time, segment.Format);
        Assert.Equal(11UL, segment.Base);
        Assert.Equal(22UL, segment.Offset);
        Assert.Equal(33UL, segment.Start);
        Assert.Equal(44UL, segment.Stop);
        Assert.Equal(55UL, segment.Time);
        Assert.Equal(66UL, segment.Position);
        Assert.Equal(77UL, segment.Duration);

        // A copy of a segment is equal to it, field by field, which is the
        // library reading all eleven of them back.
        using Segment copy = segment.Copy();
        Assert.True(segment.IsEqual(copy));
    }

    /// <summary>
    /// A rate of <c>0.0</c> is refused, because it has no playback direction
    /// and <c>gst_segment_do_seek</c> refuses it as well. A negative rate is
    /// not: that is reverse playback.
    /// </summary>
    [Fact]
    public void ARateOfZeroIsRefused()
    {
        using Segment segment = Segment.New();
        segment.Init(Gst.Format.Time);

        Assert.Throws<ArgumentOutOfRangeException>(() => segment.SetRate(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => segment.SetRate(-0.0));

        // The refusal is complete: nothing was written.
        Assert.Equal(1.0, segment.Rate);

        segment.SetRate(-1.0);
        Assert.Equal(-1.0, segment.Rate);
    }

    /// <summary>
    /// A disposed wrapper owns no segment, so it neither reads nor writes one.
    /// </summary>
    [Fact]
    public void TheFieldsAreRefusedOnADisposedWrapper()
    {
        Segment segment = Segment.New();
        segment.Dispose();

        Assert.Throws<ObjectDisposedException>(() => segment.SetStart(OneSecond));
        Assert.Throws<ObjectDisposedException>(() => _ = segment.Start);
    }

    /// <summary>
    /// Formats every field of a segment for the test output.
    /// </summary>
    /// <param name="segment">The segment to describe.</param>
    /// <returns>One line naming each field this suite reads.</returns>
    /// <remarks>
    /// <c>gst_debug_print_segment</c> would answer this, but it arrived in
    /// GStreamer 1.26 and the Linux leg of CI runs the 1.24 floor, where the
    /// call is an <see cref="EntryPointNotFoundException"/>. The line is built
    /// out of the accessors under test instead, which needs nothing beyond
    /// 1.24 and reads every field once more while it is at it.
    /// </remarks>
    private static string Describe(Segment segment) =>
        FormattableString.Invariant(
            $"flags={segment.Flags} rate={segment.Rate} applied_rate={segment.AppliedRate} format={segment.Format}") +
        FormattableString.Invariant(
            $" base={segment.Base} offset={segment.Offset} start={segment.Start} stop={segment.Stop}") +
        FormattableString.Invariant(
            $" time={segment.Time} position={segment.Position} duration={segment.Duration}");
}
