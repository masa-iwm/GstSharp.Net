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
}
