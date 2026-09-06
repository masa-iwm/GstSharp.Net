using System;

namespace Gst.Audio;

public static unsafe partial class AudioGlobal
{
    /// <summary>
    /// The largest number of destination channels a downmix matrix may name.
    /// </summary>
    /// <remarks>
    /// The row table of the matrix is built on the stack, so the number of rows
    /// is bounded. The bound is well past what a channel layout can hold:
    /// <see cref="Gst.Audio.AudioGlobal.AudioChannelPositionsToMask"/> and the
    /// mask it builds are 64 positions wide.
    /// </remarks>
    private const int MaxDownmixChannels = 64;

    /// <summary>
    /// Attaches a downmix matrix to a buffer, which must be writable.
    /// </summary>
    /// <param name="buffer">The buffer to attach the metadata to.</param>
    /// <param name="fromPosition">The channel positions of the source.</param>
    /// <param name="toPosition">The channel positions of the destination.</param>
    /// <param name="matrix">
    /// The coefficients, row major: one row per destination channel, each row
    /// as wide as <paramref name="fromPosition"/>, so exactly
    /// <c>fromPosition.Length * toPosition.Length</c> of them. The i-th output
    /// channel is the sum of the input channels multiplied by the coefficients
    /// of row i.
    /// </param>
    /// <returns>The metadata item, which the buffer owns.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_add_audio_downmix_meta</c>. The library deep
    /// copies the positions and the coefficients into storage the metadata item
    /// owns (<c>gstaudiometa.c:159-172</c>), so the spans need not outlive the
    /// call. The transform the library registers for the item re-attaches it
    /// through this same call, so <see cref="Gst.Buffer.Copy"/> gives the copy
    /// a second deep copy of the positions and the matrix rather than a shared
    /// one.
    /// </para>
    /// <para>
    /// The C function takes the matrix as a <c>const gfloat**</c>, a table of
    /// <c>to_channels</c> row pointers, which the gir describes as an array of
    /// <c>gpointer</c> with no length at all; the row table is built here from
    /// the one flat span, which is why this member is written by hand rather
    /// than generated.
    /// </para>
    /// <para>
    /// The writability of the buffer is checked before anything is called. The
    /// C body does not check what <c>gst_buffer_add_meta</c> answered
    /// (<c>gstaudiometa.c:151-156</c>), so a shared buffer would be a NULL
    /// dereference inside the library rather than a NULL return; the pre-check
    /// is what keeps the call off a process crash, exactly as on
    /// <see cref="BufferAddAudioMeta(Gst.Buffer, Gst.Audio.AudioInfo, nuint, System.ReadOnlySpan{nuint})"/>.
    /// The return is therefore not nullable: every other way the library
    /// answers nothing is a guard the arguments below exclude. The sibling
    /// attach <c>Gst.Video.VideoGlobal.BufferAddVideoGLTextureUploadMeta</c>
    /// answers <see langword="null"/> for a shared buffer instead, because its
    /// C body does check what <c>gst_buffer_add_meta</c> answered
    /// (<c>gstvideometa.c:1259-1260</c>); the difference in the two C bodies is
    /// the whole difference between the two signatures.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="buffer"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fromPosition"/> or <paramref name="toPosition"/> is
    /// empty, or <paramref name="matrix"/> does not hold one coefficient per
    /// source channel per destination channel.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="toPosition"/> names more than 64 channels.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The buffer wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The buffer is not writable, or the library answered nothing even though
    /// every guard of it was excluded above.
    /// </exception>
    public static Gst.Audio.AudioDownmixMeta BufferAddAudioDownmixMeta(
        Gst.Buffer buffer,
        ReadOnlySpan<Gst.Audio.AudioChannelPosition> fromPosition,
        ReadOnlySpan<Gst.Audio.AudioChannelPosition> toPosition,
        ReadOnlySpan<float> matrix)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        nint bufferHandle = buffer.Handle;

        if (fromPosition.IsEmpty)
        {
            throw new ArgumentException(
                "A downmix matrix needs at least one source channel.",
                nameof(fromPosition));
        }

        if (toPosition.IsEmpty)
        {
            throw new ArgumentException(
                "A downmix matrix needs at least one destination channel.",
                nameof(toPosition));
        }

        if (toPosition.Length > MaxDownmixChannels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toPosition),
                toPosition.Length,
                FormattableString.Invariant(
                    $"A downmix matrix is built with at most {MaxDownmixChannels} destination channels."));
        }

        int from = fromPosition.Length;
        int to = toPosition.Length;
        if (matrix.Length != checked(from * to))
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"matrix must hold {to} rows of {from} coefficients, {from * to} in all; {matrix.Length} were given."),
                nameof(matrix));
        }

        if (!buffer.IsWritable)
        {
            throw new InvalidOperationException(
                "The buffer is not writable: somebody else holds a reference to it, and writing a field " +
                "would change what they see. Make it writable first.");
        }

        fixed (Gst.Audio.AudioChannelPosition* fromPointer = fromPosition)
        fixed (Gst.Audio.AudioChannelPosition* toPointer = toPosition)
        fixed (float* matrixPointer = matrix)
        {
            // The library reads matrix[i] for i below to_channels and then
            // from_channels floats through each of those pointers, so what it
            // is handed is a table of rows into the one flat block above.
            float** rows = stackalloc float*[to];
            for (int row = 0; row < to; row++)
            {
                rows[row] = matrixPointer + ((nint)row * from);
            }

            nint nativeResult = AudioGlobalNative.BufferAddAudioDownmixMeta(
                bufferHandle,
                fromPointer,
                from,
                toPointer,
                to,
                rows);
            GC.KeepAlive(buffer);
            return Gst.Audio.AudioDownmixMeta.FromNative(nativeResult)
                ?? throw new InvalidOperationException(
                    "gst_buffer_add_audio_downmix_meta returned no value.");
        }
    }
}
