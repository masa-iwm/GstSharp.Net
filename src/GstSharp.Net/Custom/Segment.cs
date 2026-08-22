namespace Gst;

public sealed unsafe partial class Segment
{
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
    /// generated field properties of the class because those are get only; a
    /// later generator change may add setters next to them, and these methods
    /// stay valid when it does.
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
    /// <para>
    /// <b>How to read the values back.</b> What these methods write and the
    /// generated field properties return is what <c>GstSegment</c> holds, and
    /// the gir says little about it, so the rules the C documentation states
    /// are collected here once rather than repeated on each member.
    /// </para>
    /// <para>
    /// <b>The unit of every position is whatever <see cref="Format"/> says</b>:
    /// <see cref="Base"/>, <see cref="Offset"/>, <see cref="Start"/>,
    /// <see cref="Stop"/>, <see cref="Time"/>, <see cref="Position"/> and
    /// <see cref="Duration"/> are nanoseconds in <see cref="Gst.Format.Time"/>,
    /// bytes in <see cref="Gst.Format.Bytes"/>, and so on. The rate is a factor
    /// and has no unit.
    /// </para>
    /// <para>
    /// <see cref="ulong.MaxValue"/> is the <c>-1</c> that C writes into an
    /// unsigned field to mean "no value", and it is what
    /// <see cref="Init(Gst.Format)"/> leaves in <see cref="Stop"/> and
    /// <see cref="Duration"/>: a segment with no end, of a stream of unknown
    /// length.
    /// </para>
    /// <para>
    /// <see cref="Flags"/> is not something an application usually sets. It is
    /// what
    /// <see cref="DoSeek(double, Gst.Format, Gst.SeekFlags, Gst.SeekType, ulong, Gst.SeekType, ulong, out bool)"/>
    /// derives from the <see cref="Gst.SeekFlags"/> of the seek, so a segment
    /// that came out of a seek reports the trick mode the seek asked for.
    /// </para>
    /// <para>
    /// <see cref="Rate"/> is what sinks synchronise on and is never <c>0.0</c>;
    /// <see cref="AppliedRate"/> is what an upstream element has already done to
    /// the data. The speed the data is consumed at is the product of the two,
    /// and the direction of playback is the sign of <see cref="Rate"/>.
    /// </para>
    /// <para>
    /// <see cref="Base"/> is the running time that had already elapsed when
    /// playback reached <see cref="Start"/> — <see cref="Stop"/> rather than
    /// <see cref="Start"/> when the rate is negative, because that is the end
    /// playback enters the segment from.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetFlags(Gst.SegmentFlags flags)
    {
        ((SegmentRaw*)Handle)->Flags = flags;
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
        ((SegmentRaw*)Handle)->Format = format;
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
