using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

public sealed unsafe partial class Segment
{
    /// <summary>
    /// Gets what the segment asks of the pipeline: whether it resets the
    /// running time, whether it ends with a segment-done message instead of an
    /// end of stream, and which trick mode it plays in.
    /// </summary>
    /// <remarks>
    /// <see cref="DoSeek(double, Gst.Format, Gst.SeekFlags, Gst.SeekType, ulong, Gst.SeekType, ulong, out bool)"/>
    /// derives these from the <see cref="Gst.SeekFlags"/> of the seek, so a
    /// segment that came out of a seek reports the trick mode that seek asked
    /// for.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.SegmentFlags Flags
    {
        get
        {
            int value = ((SegmentRaw*)Handle)->Flags;
            GC.KeepAlive(this);
            return (Gst.SegmentFlags)value;
        }
    }

    /// <summary>
    /// Gets the playback rate of the segment: <c>1.0</c> for normal speed,
    /// greater for fast forward, and negative for reverse playback.
    /// </summary>
    /// <remarks>
    /// This is the rate the sinks synchronise on, so it is what a trick play
    /// surface reads to show the current speed. The effective speed of the
    /// stream is this times <see cref="AppliedRate"/>. It is never
    /// <c>0.0</c>; see <see cref="SetRate"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public double Rate
    {
        get
        {
            double value = ((SegmentRaw*)Handle)->Rate;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the rate that has already been applied to the data, by a server
    /// that sent it faster than real time or by an element that resampled it.
    /// </summary>
    /// <remarks>
    /// The effective speed of the stream is <see cref="Rate"/> times this. It
    /// is what the stream time conversions scale by, which is why an element
    /// that changes the speed of the data itself has to report it here.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public double AppliedRate
    {
        get
        {
            double value = ((SegmentRaw*)Handle)->AppliedRate;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the unit every other value of the segment is counted in.
    /// </summary>
    /// <remarks>
    /// <see cref="Init(Gst.Format)"/> sets this, and every generated call that
    /// takes a format refuses to run unless the format it is given is this one.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Format Format
    {
        get
        {
            int value = ((SegmentRaw*)Handle)->Format;
            GC.KeepAlive(this);
            return (Gst.Format)value;
        }
    }

    /// <summary>
    /// Gets the running time the segment starts at, that is the time already
    /// elapsed on the pipeline clock when playback reaches
    /// <see cref="Start"/> (<see cref="Stop"/> when <see cref="Rate"/> is
    /// negative).
    /// </summary>
    /// <remarks>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Base
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Base;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets how much of the segment had already been played when a seek that
    /// did not move the start position was applied.
    /// </summary>
    /// <remarks>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Offset
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Offset;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the timestamp of the first buffer inside the segment — the last
    /// one when <see cref="Rate"/> is negative. Data in front of it is clipped
    /// away.
    /// </summary>
    /// <remarks>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Start
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Start;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the timestamp of the last buffer inside the segment — the first
    /// one when <see cref="Rate"/> is negative — or
    /// <see cref="ulong.MaxValue"/> when the segment has no end. Data behind
    /// it is clipped away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ulong.MaxValue"/> is the <c>-1</c> that C writes into an
    /// unsigned field, which is what <see cref="Init(Gst.Format)"/> leaves
    /// here and what a segment that plays to the end of the stream keeps.
    /// </para>
    /// <para>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Stop
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Stop;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the stream time of <see cref="Start"/> — of <see cref="Stop"/>
    /// when <see cref="Rate"/> is negative — that is where the segment sits
    /// in the media as a whole.
    /// </summary>
    /// <remarks>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Time
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Time;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets how far playback has come inside the segment. Sources, demuxers
    /// and parsers move it forward as they push data.
    /// </summary>
    /// <remarks>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Position
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Position;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the length of the whole stream, or <see cref="ulong.MaxValue"/>
    /// when nothing has worked it out yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ulong.MaxValue"/> is the <c>-1</c> that C writes into an
    /// unsigned field, which is what <see cref="Init(Gst.Format)"/> leaves
    /// here. A seek with <see cref="Gst.SeekType.End"/> is measured against
    /// this, so an element that knows the duration — a demuxer, typically —
    /// is expected to fill it in.
    /// </para>
    /// <para>
    /// <b>The unit is whatever <see cref="Format"/> says</b>: nanoseconds in
    /// <see cref="Gst.Format.Time"/>, bytes in <see cref="Gst.Format.Bytes"/>,
    /// and so on for the other formats.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public ulong Duration
    {
        get
        {
            ulong value = ((SegmentRaw*)Handle)->Duration;
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Sets what the segment asks of the pipeline.
    /// </summary>
    /// <param name="flags">The flags to configure.</param>
    /// <remarks>
    /// <para>
    /// The eleven fields of <c>GstSegment</c> are written directly, which is
    /// what C does — the structure is public and callers assign
    /// <c>segment-&gt;flags</c> — and it is how a segment is configured before
    /// <see cref="DoSeek(double, Gst.Format, Gst.SeekFlags, Gst.SeekType, ulong, Gst.SeekType, ulong, out bool)"/>
    /// or read back after it. They are methods rather than setters on the
    /// properties above because the properties are get only today; a later
    /// generator change may add setters next to them, and these methods stay
    /// valid when it does.
    /// </para>
    /// <para>
    /// There is no writability rule to observe here, unlike on a buffer. A
    /// <see cref="Gst.GObject.Boxed"/> wrapper always owns a value nobody else
    /// holds: <see cref="Gst.Interop.Transfer.None"/> makes it adopt a
    /// <c>g_boxed_copy</c> rather than the value itself, so even the segment
    /// that comes out of <see cref="Gst.Event.ParseSegment(out Gst.Segment)"/>
    /// or <see cref="Gst.Sample.GetSegment"/> is the caller's own. Writing one
    /// changes nothing that anybody else can see, and equally nothing is
    /// written back into the event or the sample it came from — hand a
    /// configured segment on through a call such as
    /// <see cref="Gst.Event.NewSegment(Gst.Segment)"/> for that.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetFlags(Gst.SegmentFlags flags)
    {
        ((SegmentRaw*)Handle)->Flags = (int)flags;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the playback rate of the segment.
    /// </summary>
    /// <param name="rate">
    /// The rate: <c>1.0</c> for normal speed, greater for fast forward, and
    /// negative for reverse playback. It may not be <c>0.0</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// A rate of <c>0.0</c> is refused because it is meaningless and because C
    /// refuses it too: <c>gst_segment_do_seek</c> opens with
    /// <c>g_return_val_if_fail (rate != 0.0, FALSE)</c>, and the documentation
    /// of the field says the rate should never be <c>0.0</c>. It has no
    /// playback direction, and the running time conversions divide by its
    /// absolute value.
    /// </para>
    /// <para>See <see cref="SetFlags"/> for why these are methods.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="rate"/> is <c>0.0</c>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetRate(double rate)
    {
        if (rate == 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                rate,
                "A segment rate of 0.0 has no playback direction and every running time derived from it " +
                "would divide by zero, which is why gst_segment_do_seek refuses it as well.");
        }

        ((SegmentRaw*)Handle)->Rate = rate;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the rate that has already been applied to the data.
    /// </summary>
    /// <param name="appliedRate">
    /// The applied rate. An element that resamples the data itself reports what
    /// it applied here; an element that leaves the data alone leaves it at
    /// <c>1.0</c>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="SetRate"/> this is not guarded, because C guards
    /// nothing here either — no <c>g_return_val_if_fail</c> in
    /// <c>gstsegment.c</c> mentions the applied rate. A value of <c>0.0</c> is
    /// nonetheless as meaningless as a rate of <c>0.0</c>:
    /// <c>gst_segment_position_from_stream_time_full</c> divides by its
    /// absolute value.
    /// </para>
    /// <para>See <see cref="SetFlags"/> for why these are methods.</para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetAppliedRate(double appliedRate)
    {
        ((SegmentRaw*)Handle)->AppliedRate = appliedRate;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the unit every other value of the segment is counted in.
    /// </summary>
    /// <param name="format">The format to configure.</param>
    /// <remarks>
    /// <para>
    /// <see cref="Init(Gst.Format)"/> is the usual way to set this, because it
    /// sets the format and resets every other field to a value that agrees with
    /// it. Setting the format alone leaves the values that were counted in the
    /// old unit in place, so this is for the element that knows they still mean
    /// what they say.
    /// </para>
    /// <para>See <see cref="SetFlags"/> for why these are methods.</para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetFormat(Gst.Format format)
    {
        ((SegmentRaw*)Handle)->Format = (int)format;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the running time the segment starts at.
    /// </summary>
    /// <param name="base">
    /// The base. <b>The unit is whatever <see cref="Format"/> says</b>:
    /// nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetBase(ulong @base)
    {
        ((SegmentRaw*)Handle)->Base = @base;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets how much of the segment had already been played when a seek that
    /// did not move the start position was applied.
    /// </summary>
    /// <param name="offset">
    /// The offset. <b>The unit is whatever <see cref="Format"/> says</b>:
    /// nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetOffset(ulong offset)
    {
        ((SegmentRaw*)Handle)->Offset = offset;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the timestamp of the first buffer inside the segment.
    /// </summary>
    /// <param name="start">
    /// The start. <b>The unit is whatever <see cref="Format"/> says</b>:
    /// nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetStart(ulong start)
    {
        ((SegmentRaw*)Handle)->Start = start;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the timestamp of the last buffer inside the segment.
    /// </summary>
    /// <param name="stop">
    /// The stop, or <see cref="ulong.MaxValue"/> — the <c>-1</c> of C — when
    /// the segment has no end. <b>The unit is whatever <see cref="Format"/>
    /// says</b>: nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetStop(ulong stop)
    {
        ((SegmentRaw*)Handle)->Stop = stop;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the stream time of the start of the segment.
    /// </summary>
    /// <param name="time">
    /// The stream time. <b>The unit is whatever <see cref="Format"/> says</b>:
    /// nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetTime(ulong time)
    {
        ((SegmentRaw*)Handle)->Time = time;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets how far playback has come inside the segment.
    /// </summary>
    /// <param name="position">
    /// The position. <b>The unit is whatever <see cref="Format"/> says</b>:
    /// nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetPosition(ulong position)
    {
        ((SegmentRaw*)Handle)->Position = position;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the length of the whole stream.
    /// </summary>
    /// <param name="duration">
    /// The duration, or <see cref="ulong.MaxValue"/> — the <c>-1</c> of C —
    /// when it is not known. <b>The unit is whatever <see cref="Format"/>
    /// says</b>: nanoseconds in <see cref="Gst.Format.Time"/>, bytes in
    /// <see cref="Gst.Format.Bytes"/>, and so on.
    /// </param>
    /// <remarks>See <see cref="SetFlags"/> for why these are methods.</remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetDuration(ulong duration)
    {
        ((SegmentRaw*)Handle)->Duration = duration;
        GC.KeepAlive(this);
    }
}

/// <summary>The native layout of <c>GstSegment</c>.</summary>
/// <remarks>
/// <para>
/// The mirror is only ever read through a pointer into memory that GStreamer
/// owns; it is never allocated, assigned or copied.
/// </para>
/// <para>
/// It is written by hand because <c>GstSegment</c> is a boxed type, and the
/// record emitter builds no mirror for one — the wrapper is a pointer holder
/// and the generator has no reason to project the fields. Every field is public
/// API in C and none of them has an accessor function, so reading them is what
/// a binding has to do. The two enumerations sit in front of an eight byte
/// value each and therefore carry four bytes of padding behind them, which
/// sequential layout reproduces on its own: <c>flags</c> at 0, <c>rate</c> at
/// 8, <c>applied_rate</c> at 16, <c>format</c> at 24, then <c>base</c>,
/// <c>offset</c>, <c>start</c>, <c>stop</c>, <c>time</c>, <c>position</c> and
/// <c>duration</c> from 32 to 80, and the reserved tail at 88, for 120 bytes.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SegmentRaw
{
    /// <summary>The <c>flags</c> field, a <c>GstSegmentFlags</c>.</summary>
    internal int Flags;

    /// <summary>The <c>rate</c> field.</summary>
    internal double Rate;

    /// <summary>The <c>applied_rate</c> field.</summary>
    internal double AppliedRate;

    /// <summary>The <c>format</c> field, a <c>GstFormat</c>.</summary>
    internal int Format;

    /// <summary>The <c>base</c> field.</summary>
    internal ulong Base;

    /// <summary>The <c>offset</c> field.</summary>
    internal ulong Offset;

    /// <summary>The <c>start</c> field.</summary>
    internal ulong Start;

    /// <summary>The <c>stop</c> field.</summary>
    internal ulong Stop;

    /// <summary>The <c>time</c> field.</summary>
    internal ulong Time;

    /// <summary>The <c>position</c> field.</summary>
    internal ulong Position;

    /// <summary>The <c>duration</c> field.</summary>
    internal ulong Duration;

    /// <summary>The private <c>_gst_reserved</c> field.</summary>
    private GstReservedArray _gstReserved;

    /// <summary>Inline storage of the 4 elements of the <c>_gst_reserved</c> field of <c>GstSegment</c>.</summary>
    [InlineArray(4)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}
