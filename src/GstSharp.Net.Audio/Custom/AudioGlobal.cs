using System;

namespace Gst.Audio;

public static unsafe partial class AudioGlobal
{
    /// <summary>
    /// Attaches audio metadata to a buffer, which must be writable, and has the
    /// plane offsets computed.
    /// </summary>
    /// <param name="buffer">The buffer to attach the metadata to.</param>
    /// <param name="info">The format of the audio the buffer holds.</param>
    /// <param name="samples">How many samples per channel the buffer holds.</param>
    /// <returns>
    /// The metadata item, which the buffer owns, or <see langword="null"/> when
    /// the buffer refused it.
    /// </returns>
    /// <remarks>
    /// This is the overload for an interleaved layout and for a non-interleaved
    /// one whose planes follow each other without a gap; the other overload
    /// takes the offsets.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="buffer"/> or <paramref name="info"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public static Gst.Audio.AudioMeta? BufferAddAudioMeta(
        Gst.Buffer buffer,
        Gst.Audio.AudioInfo info,
        nuint samples) =>
        BufferAddAudioMeta(buffer, info, samples, ReadOnlySpan<nuint>.Empty);

    /// <summary>
    /// Attaches audio metadata to a buffer, which must be writable.
    /// </summary>
    /// <param name="buffer">The buffer to attach the metadata to.</param>
    /// <param name="info">The format of the audio the buffer holds.</param>
    /// <param name="samples">How many samples per channel the buffer holds.</param>
    /// <param name="offsets">
    /// Where each channel plane starts, in bytes; exactly
    /// <see cref="Gst.Audio.AudioInfo.Channels"/> entries. An empty span means
    /// compute them, which is what the shorter overload does.
    /// </param>
    /// <returns>
    /// The metadata item, which the buffer owns, or <see langword="null"/> when
    /// the buffer refused it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The offsets belong to a non-interleaved layout: the C function requires
    /// them to be absent when <see cref="Gst.Audio.AudioInfo.Layout"/> is
    /// interleaved, and asserts when two channel ranges overlap or when the
    /// buffer is too small to hold what they describe.
    /// </para>
    /// <para>
    /// The gir gives <c>offsets</c> no length — the length is
    /// <c>info-&gt;channels</c>, a field of another argument — which is why this
    /// member is written by hand rather than generated.
    /// </para>
    /// <para>
    /// The writability of the buffer is checked before anything is called. The
    /// sibling <c>gst_buffer_add_video_region_of_interest_meta_id</c>
    /// dereferences the NULL that <c>gst_buffer_add_meta</c> answers on a shared
    /// buffer, so a pre-check is what keeps this family off a process crash.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="buffer"/> or <paramref name="info"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="offsets"/> is neither empty nor
    /// <see cref="Gst.Audio.AudioInfo.Channels"/> long.
    /// </exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    /// <exception cref="InvalidOperationException">The buffer is not writable.</exception>
    public static Gst.Audio.AudioMeta? BufferAddAudioMeta(
        Gst.Buffer buffer,
        Gst.Audio.AudioInfo info,
        nuint samples,
        ReadOnlySpan<nuint> offsets)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(info);

        nint bufferHandle = buffer.Handle;
        nint infoHandle = info.Handle;

        if (!buffer.IsWritable)
        {
            throw new InvalidOperationException(
                "The buffer is not writable: somebody else holds a reference to it, and writing a field " +
                "would change what they see. Make it writable first.");
        }

        int channels = info.Channels;
        if (offsets.Length != 0 && offsets.Length != channels)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"offsets must be empty or have exactly {channels} entries, one per channel."),
                nameof(offsets));
        }

        fixed (nuint* offsetsPointer = offsets)
        {
            nint nativeResult = AudioGlobalNative.BufferAddAudioMeta(
                bufferHandle,
                infoHandle,
                samples,
                offsetsPointer);
            GC.KeepAlive(buffer);
            GC.KeepAlive(info);
            return Gst.Audio.AudioMeta.FromNative(nativeResult);
        }
    }
}
