using System.Runtime.InteropServices;

namespace Gst.Audio;

/// <summary>
/// The read side of a ring buffer, which the generator does not emit:
/// <c>gst_audio_ring_buffer_read</c> counts its length in samples while the
/// array it fills is annotated in bytes, and no annotation states the unit of a
/// length. See <c>$comment-skip-sample-count</c> in
/// <c>girs/overlays/fixups.json</c>.
/// </summary>
public abstract unsafe partial class AudioRingBuffer
{
    /// <summary>
    /// The most channels an audio format carries, which is the size of the
    /// position array of a <c>GstAudioInfo</c>.
    /// </summary>
    private const int MaximumChannels = 64;

    /// <summary>
    /// Reads samples from the ring buffer into <paramref name="data"/>,
    /// starting at <paramref name="sample"/>.
    /// </summary>
    /// <param name="sample">
    /// The position in the ring buffer the first sample is read from.
    /// </param>
    /// <param name="data">
    /// Where the samples are written. As many whole samples as fit are read,
    /// which is its length divided by the bytes per frame of the format the
    /// ring buffer was acquired with.
    /// </param>
    /// <param name="timestamp">
    /// The timestamp the ring buffer recorded for the segment the samples came
    /// from, or <see cref="Gst.ClockTime.None"/> when it kept none and when
    /// nothing was read.
    /// </param>
    /// <returns>
    /// The number of samples that were read, or <see cref="uint.MaxValue"/> —
    /// the <c>-1</c> of the C function — on error.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_audio_ring_buffer_read</c>. Its <c>len</c> argument counts
    /// <em>samples</em> and the memory it fills holds
    /// <c>len × bytes-per-frame</c> bytes, so a span of bytes has to be measured
    /// in samples before it is handed over. Passing its byte length is what the
    /// generated call did, and for every format wider than one byte per frame —
    /// which is every format an application uses, stereo <c>S16LE</c> being four
    /// — the library wrote that many times past the end of the span.
    /// </para>
    /// <para>
    /// The measurement is the ring buffer's own: <see cref="Convert"/> from
    /// <see cref="Gst.Format.Bytes"/> to <see cref="Gst.Format.Default"/> is a
    /// division by the bytes per frame of the spec the ring buffer was acquired
    /// with, which is the one number managed code cannot read for itself. A
    /// trailing part of a frame is therefore left alone: a span of ten bytes at
    /// four bytes per frame is read as two samples and its last two bytes are
    /// not touched.
    /// </para>
    /// <para>
    /// The ring buffer has to have been acquired, because that is what parses
    /// the format out of the caps. One that has not been has no memory to read
    /// from either, and the call reports the same failure the C function
    /// reports for it.
    /// </para>
    /// <para>
    /// The memory belongs to the caller for the whole call and nothing is
    /// retained: the span is pinned for the duration of the read and the
    /// samples are copied into it.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public uint Read(ulong sample, System.Span<byte> data, out Gst.ClockTime timestamp)
    {
        // The handle is read before anything else, so that a disposed wrapper
        // throws before the ring buffer is asked to convert anything.
        nint buffer = Handle;

        if (!Convert(Gst.Format.Bytes, data.Length, Gst.Format.Default, out long samples))
        {
            // A ring buffer that cannot convert bytes to samples has no format,
            // which means it was never acquired and holds no memory. That is
            // the case the C function answers with -1.
            timestamp = Gst.ClockTime.None;
            return uint.MaxValue;
        }

        if (samples <= 0)
        {
            // Fewer bytes than one sample takes. Nothing is read, and the C
            // function is not handed the null pointer that an empty span pins
            // to, which it rejects with a GLib critical.
            timestamp = Gst.ClockTime.None;
            return 0;
        }

        // An unread timestamp reads as unset rather than as the epoch, which is
        // also what the C function writes for a ring buffer that keeps none.
        ulong timestampNative = Gst.ClockTime.NoneValue;

        fixed (byte* dataPointer = data)
        {
            uint read = GstAudioRingBufferRead(buffer, sample, dataPointer, (uint)samples, &timestampNative);

            // The handle was read before the call, so nothing keeps this
            // wrapper alive across it on its own.
            GC.KeepAlive(this);

            timestamp = new Gst.ClockTime(timestampNative);
            return read;
        }
    }

    /// <summary>
    /// Tells the ring buffer which channel each channel of the data that is
    /// written to it carries, so that it reorders them into the order the
    /// device expects.
    /// </summary>
    /// <param name="positions">
    /// One position per channel of the format the ring buffer was acquired
    /// with, in the order the written data carries them. It has to hold
    /// exactly as many entries as that format has channels.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_audio_ring_buffer_set_channel_positions</c>, which reads
    /// as many entries as the acquired format has channels out of a bare
    /// pointer it is never told the length of. That count is
    /// <c>spec.info.channels</c>, a public field of the ring buffer that
    /// carries no accessor, so the wrapper reads it out of the instance and
    /// measures <paramref name="positions"/> against it. The positions are
    /// then copied into a sixty four entry buffer padded with
    /// <see cref="Gst.Audio.AudioChannelPosition.Invalid"/>, which is the size
    /// of the position array of a <c>GstAudioInfo</c>: the checked count is
    /// what bounds the call, and the pad is what keeps a ring buffer that
    /// changed its format between the read and the call inside this memory.
    /// </para>
    /// <para>
    /// A format of more than sixty four channels carries no channel order at
    /// all — the library has no reorder map for one, and reads past its own
    /// sixty four entry position array on the way to saying so — so an
    /// acquired count above sixty four is refused rather than served.
    /// </para>
    /// <para>
    /// The two fields are read raw and without the object lock, which is what
    /// the C function does as well although the header marks them as held
    /// under it. That is deliberate: <see cref="Acquire"/> holds the object
    /// lock across the acquire path, and the acquire path — the <c>prepare</c>
    /// of a sink, say — is where this call belongs, because it is the one
    /// place the spec is stable. Taking the lock here would deadlock that
    /// caller. A caller anywhere else has to serialise the call against
    /// <see cref="Acquire"/> and <see cref="Release"/> itself.
    /// </para>
    /// <para>
    /// The positions have to be a permutation of the ones the acquired format
    /// carries. The library logs a critical and leaves the order unchanged for
    /// anything else, which is the one condition managed code cannot check for
    /// itself.
    /// </para>
    /// <para>
    /// Nothing is retained: the positions are read during the call and the
    /// reorder map the ring buffer derives from them is its own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="positions"/> is empty or holds more than sixty four
    /// entries, which no format has.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="positions"/> does not hold exactly one entry per
    /// channel of the acquired format.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The ring buffer was not acquired, or was acquired for a format of more
    /// than sixty four channels, which has no channel order.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetChannelPositions(System.ReadOnlySpan<Gst.Audio.AudioChannelPosition> positions)
    {
        // The handle is read before anything else, so that a disposed wrapper
        // throws before the arguments are looked at.
        nint buffer = Handle;

        if (positions.Length is 0 or > MaximumChannels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(positions),
                positions.Length,
                FormattableString.Invariant(
                    $"A channel order holds between 1 and {MaximumChannels} positions."));
        }

        // Both fields are read the way the C function reads them: straight out
        // of the instance and without the object lock. The lock is not an
        // option here — gst_audio_ring_buffer_acquire holds it across the
        // acquire vfunc, which is where this call belongs, so taking it (by
        // calling IsAcquired, say) would deadlock the one caller the function
        // is written for.
        AudioRingBufferHeadRaw* raw = (AudioRingBufferHeadRaw*)buffer;
        bool acquired = raw->Acquired != 0;
        int channels = raw->Spec.Info.Channels;
        GC.KeepAlive(this);

        if (!acquired)
        {
            // The C function answers this one with a critical, and the spec of
            // an unacquired ring buffer holds nothing to measure the positions
            // against.
            throw new InvalidOperationException(
                "The ring buffer was not acquired: the channel order belongs to the format it was "
                + "acquired with, and an unacquired ring buffer has none.");
        }

        if (channels == 0)
        {
            // Encoded caps that carry no channel count leave the field at
            // zero. The C reads no positions at all in that state; there is no
            // channel order to set.
            throw new InvalidOperationException(
                "The ring buffer was acquired for a format that states no channel count, which "
                + "therefore has no channel order.");
        }

        if (channels is < 0 or > MaximumChannels)
        {
            throw new InvalidOperationException(
                FormattableString.Invariant(
                    $"The ring buffer was acquired for {channels} channels, which carries no channel order: the library has one for at most {MaximumChannels}."));
        }

        if (positions.Length != channels)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"The ring buffer was acquired for {channels} channels, so the channel order has to hold {channels} positions rather than {positions.Length}."),
                nameof(positions));
        }

        System.Span<Gst.Audio.AudioChannelPosition> padded = stackalloc Gst.Audio.AudioChannelPosition[MaximumChannels];
        padded.Fill(Gst.Audio.AudioChannelPosition.Invalid);
        positions.CopyTo(padded);

        fixed (Gst.Audio.AudioChannelPosition* positionPointer = padded)
        {
            GstAudioRingBufferSetChannelPositions(buffer, positionPointer);
        }

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);
    }

    /// <summary>The <c>gst_audio_ring_buffer_read</c> entry point.</summary>
    /// <param name="buf">The ring buffer to read from.</param>
    /// <param name="sample">The position the first sample is read from.</param>
    /// <param name="data">The memory the samples are written to.</param>
    /// <param name="len">The number of samples to read, not the number of bytes.</param>
    /// <param name="timestamp">Where the timestamp of the segment is written.</param>
    /// <returns>The number of samples read, or <c>-1</c> on error.</returns>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_ring_buffer_read")]
    private static partial uint GstAudioRingBufferRead(
        nint buf,
        ulong sample,
        byte* data,
        uint len,
        ulong* timestamp);

    /// <summary>The <c>gst_audio_ring_buffer_set_channel_positions</c> entry point.</summary>
    /// <param name="buf">The acquired ring buffer whose channel order is set.</param>
    /// <param name="position">
    /// One position per channel of the acquired format, of which the function
    /// reads exactly as many as that format has channels.
    /// </param>
    [LibraryImport("GstAudio", EntryPoint = "gst_audio_ring_buffer_set_channel_positions")]
    private static partial void GstAudioRingBufferSetChannelPositions(
        nint buf,
        Gst.Audio.AudioChannelPosition* position);
}

/// <summary>
/// The head of a <c>GObject</c> instance, which is what every instance of a
/// <c>GstObject</c> derived type starts with.
/// </summary>
/// <remarks>
/// <c>GTypeInstance</c> is one pointer, the reference count is a
/// <c>guint</c>, and the alignment of the <c>GData</c> pointer behind it is
/// what the runtime pads to, exactly as the C compiler does.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GObjectInstanceRaw
{
    /// <summary>The <c>g_class</c> field of the <c>GTypeInstance</c>.</summary>
    internal nint TypeClass;

    /// <summary>The <c>ref_count</c> field.</summary>
    internal uint RefCount;

    /// <summary>The <c>qdata</c> field.</summary>
    internal nint QData;
}

/// <summary>
/// The head of a <c>GstObject</c> instance, up to the private tail nothing
/// here reads.
/// </summary>
/// <remarks>
/// A <c>GMutex</c> is a union of one pointer and two <c>guint</c>, so it is
/// one pointer wide on every platform; the <c>flags</c> field is a
/// <c>guint32</c> the pointer behind it is padded away from.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GstObjectInstanceRaw
{
    /// <summary>The <c>GObject</c> the instance starts with.</summary>
    internal GObjectInstanceRaw Object;

    /// <summary>The <c>lock</c> field, the object LOCK.</summary>
    internal nint Lock;

    /// <summary>The <c>name</c> field.</summary>
    internal nint Name;

    /// <summary>The <c>parent</c> field.</summary>
    internal nint Parent;

    /// <summary>The <c>flags</c> field.</summary>
    internal uint Flags;

    /// <summary>The <c>control_bindings</c> field.</summary>
    internal nint ControlBindings;

    /// <summary>The <c>control_rate</c> field.</summary>
    internal ulong ControlRate;

    /// <summary>The <c>last_sync</c> field.</summary>
    internal ulong LastSync;

    /// <summary>The <c>_gst_reserved</c> field.</summary>
    internal nint GstReserved;
}

/// <summary>A <c>GCond</c>, which is one pointer and two <c>guint</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GCondRaw
{
    /// <summary>The <c>p</c> field.</summary>
    internal nint Pointer;

    /// <summary>The first of the two <c>i</c> fields.</summary>
    internal uint First;

    /// <summary>The second of the two <c>i</c> fields.</summary>
    internal uint Second;
}

/// <summary>
/// The head of a <c>GstAudioRingBuffer</c> instance, up to and including the
/// <c>spec</c> field.
/// </summary>
/// <remarks>
/// <para>
/// <c>acquired</c> and <c>spec</c> are public fields of the ring buffer, and
/// <c>spec.info.channels</c> is the number of entries
/// <c>gst_audio_ring_buffer_set_channel_positions</c> reads out of the array
/// it is handed. Both are read through this mirror, unlocked, exactly as the
/// C function reads them: the accessor
/// <c>gst_audio_ring_buffer_is_acquired</c> takes the object lock, and the
/// acquire path this call belongs on already holds it, so the accessor cannot
/// be used. The mirror is only ever read through a pointer into memory that
/// GStreamer owns; it is never allocated, assigned or copied.
/// </para>
/// <para>
/// The layout is the one of the 1.28 headers, and the runtime lays this out
/// the way a C compiler lays out the same field list. Written as offsets, for
/// a 64 bit platform, where a pointer and a <c>gsize</c> are 8 bytes and an
/// <c>int</c>, a <c>guint</c>, a <c>gboolean</c> and an enumeration are 4:
/// </para>
/// <para>
/// <c>GObject</c>: <c>g_class</c> 0, <c>ref_count</c> 8, <c>qdata</c> 16, so
/// 24 bytes. <c>GstObject</c>: the <c>GObject</c> 0, <c>lock</c> 24,
/// <c>name</c> 32, <c>parent</c> 40, <c>flags</c> 48, <c>control_bindings</c>
/// 56, <c>control_rate</c> 64, <c>last_sync</c> 72, <c>_gst_reserved</c> 80,
/// so 88 bytes. <c>GstAudioRingBuffer</c>: the <c>GstObject</c> 0,
/// <c>cond</c> 88, <c>open</c> 104, <c>acquired</c> 108, <c>memory</c> 112,
/// <c>size</c> 120, <c>timestamps</c> 128, <c>spec</c> 136.
/// <c>GstAudioRingBufferSpec</c>: <c>caps</c> 0, <c>type</c> 8, <c>info</c>
/// 16. <c>GstAudioInfo</c>: <c>finfo</c> 0, <c>flags</c> 8, <c>layout</c> 12,
/// <c>rate</c> 16, <c>channels</c> 20. The two fields the wrapper reads are
/// therefore at 108 and at 172, which is what
/// <c>AudioRingBufferPositionsTests</c> asserts and what the library itself
/// has to agree with for a ring buffer to acquire at all.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct AudioRingBufferHeadRaw
{
    /// <summary>The <c>GstObject</c> the instance starts with.</summary>
    internal GstObjectInstanceRaw Object;

    /// <summary>The <c>cond</c> field.</summary>
    internal GCondRaw Cond;

    /// <summary>The <c>open</c> field.</summary>
    internal int Open;

    /// <summary>The <c>acquired</c> field.</summary>
    internal int Acquired;

    /// <summary>The <c>memory</c> field.</summary>
    internal nint Memory;

    /// <summary>The <c>size</c> field.</summary>
    internal nuint Size;

    /// <summary>The <c>timestamps</c> field.</summary>
    internal nint Timestamps;

    /// <summary>The <c>spec</c> field.</summary>
    internal Gst.Audio.AudioRingBufferSpecRaw Spec;
}
