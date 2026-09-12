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

    /// <summary>
    /// Converts DSD data from one format and layout into another.
    /// </summary>
    /// <param name="inputData">The data to convert.</param>
    /// <param name="outputData">
    /// Where the converted data is written. It may not overlap
    /// <paramref name="inputData"/>: the conversion is not defined in place.
    /// </param>
    /// <param name="inputFormat">The format <paramref name="inputData"/> carries.</param>
    /// <param name="outputFormat">The format to write.</param>
    /// <param name="inputLayout">The layout <paramref name="inputData"/> carries.</param>
    /// <param name="outputLayout">The layout to write.</param>
    /// <param name="inputPlaneOffsets">
    /// Where each plane of <paramref name="inputData"/> starts, in bytes, with
    /// one entry per channel. It is ignored, and may be empty, for an
    /// interleaved input.
    /// </param>
    /// <param name="outputPlaneOffsets">
    /// Where each plane of <paramref name="outputData"/> starts, in bytes,
    /// with one entry per channel. It is ignored, and may be empty, for an
    /// interleaved output.
    /// </param>
    /// <param name="numDsdBytes">
    /// How many bytes of DSD data are converted, counted over all channels. It
    /// has to be a whole number of <em>frames</em> of both formats — one word
    /// per channel, so the channel count times the wider of the two words —
    /// which is also a whole number of words and of planes.
    /// </param>
    /// <param name="numChannels">How many channels the data carries.</param>
    /// <param name="reverseByteBits">
    /// Whether the bits of every byte are reversed on the way, which is what
    /// turns the bit order of one DSD source into the other.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_dsd_convert</c>. Neither block carries a length: the C
    /// function is handed two bare pointers and measures the interleaved side
    /// by <paramref name="numDsdBytes"/> and a non-interleaved side by the
    /// largest plane offset plus <paramref name="numDsdBytes"/> divided by
    /// <paramref name="numChannels"/>. Both extents are computed and checked
    /// against the spans here, because every one of the preconditions the C
    /// function states it only answers with a critical.
    /// </para>
    /// <para>
    /// Those two extents are the whole of the input only when
    /// <paramref name="numDsdBytes"/> is a whole number of frames of both
    /// formats: the index the conversion computes for a byte of the last,
    /// partial frame reaches past the block it belongs to. That is a
    /// precondition the C function neither states nor checks, so it is checked
    /// here.
    /// </para>
    /// <para>
    /// Nothing is retained: both blocks are pinned for the length of the call
    /// and the conversion writes only into <paramref name="outputData"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="numDsdBytes"/> or <paramref name="numChannels"/> is
    /// zero or negative, a format is not one of the DSD formats, a layout is
    /// not one of the two layouts, or a plane offset is so large that the
    /// extent it implies does not fit a length.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A span is shorter than the extent the arguments imply, the two data
    /// spans overlap, a plane offset span does not hold one entry per channel,
    /// or <paramref name="numDsdBytes"/> is not a whole number of words, of
    /// frames or of planes.
    /// </exception>
    public static void DsdConvert(
        System.ReadOnlySpan<byte> inputData,
        System.Span<byte> outputData,
        Gst.Audio.DsdFormat inputFormat,
        Gst.Audio.DsdFormat outputFormat,
        Gst.Audio.AudioLayout inputLayout,
        Gst.Audio.AudioLayout outputLayout,
        System.ReadOnlySpan<nuint> inputPlaneOffsets,
        System.ReadOnlySpan<nuint> outputPlaneOffsets,
        nuint numDsdBytes,
        int numChannels,
        bool reverseByteBits)
    {
        if (numDsdBytes == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numDsdBytes),
                numDsdBytes,
                "A conversion of no bytes at all is refused by the library with a critical.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numChannels);

        uint inputWidth = WordWidth(inputFormat, nameof(inputFormat));
        uint outputWidth = WordWidth(outputFormat, nameof(outputFormat));

        CheckLayout(inputLayout, nameof(inputLayout));
        CheckLayout(outputLayout, nameof(outputLayout));

        ulong bytes = numDsdBytes;

        CheckWholeWords(bytes, inputWidth, nameof(inputFormat));
        CheckWholeWords(bytes, outputWidth, nameof(outputFormat));
        CheckWholeFrames(bytes, inputWidth, outputWidth, numChannels);

        ulong inputExtent = RequiredExtent(
            inputLayout, inputPlaneOffsets, bytes, numChannels, nameof(inputPlaneOffsets));
        ulong outputExtent = RequiredExtent(
            outputLayout, outputPlaneOffsets, bytes, numChannels, nameof(outputPlaneOffsets));

        CheckLength(inputExtent, inputData.Length, nameof(inputData));
        CheckLength(outputExtent, outputData.Length, nameof(outputData));

        if (inputData.Overlaps(outputData))
        {
            // The same format path copies with memcpy and the general one
            // reads and writes across word boundaries, so neither is defined
            // for two blocks that share memory.
            throw new ArgumentException(
                "The input and the output of a DSD conversion may not overlap.",
                nameof(outputData));
        }

        // An interleaved side is handed no offsets at all, which is the NULL
        // the C function documents for it.
        System.ReadOnlySpan<nuint> inputOffsets =
            inputLayout == Gst.Audio.AudioLayout.NonInterleaved ? inputPlaneOffsets : default;
        System.ReadOnlySpan<nuint> outputOffsets =
            outputLayout == Gst.Audio.AudioLayout.NonInterleaved ? outputPlaneOffsets : default;

        fixed (byte* inputPointer = inputData)
        fixed (byte* outputPointer = outputData)
        fixed (nuint* inputOffsetPointer = inputOffsets)
        fixed (nuint* outputOffsetPointer = outputOffsets)
        {
            AudioGlobalNative.DsdConvert(
                inputPointer,
                outputPointer,
                (int)inputFormat,
                (int)outputFormat,
                (int)inputLayout,
                (int)outputLayout,
                inputOffsetPointer,
                outputOffsetPointer,
                numDsdBytes,
                numChannels,
                reverseByteBits ? 1 : 0);
        }
    }

    /// <summary>Measures one word of a DSD format, in bytes.</summary>
    /// <param name="format">The format to measure.</param>
    /// <param name="parameterName">The parameter the format was passed as.</param>
    /// <returns>The width of one word of it.</returns>
    private static uint WordWidth(Gst.Audio.DsdFormat format, string parameterName)
    {
        uint width = Gst.Audio.DsdFormatExtensions.GetWidth(format);

        return width == 0
            ? throw new ArgumentOutOfRangeException(
                parameterName,
                format,
                "The unknown format and anything outside the DSD formats have no word width.")
            : width;
    }

    /// <summary>Refuses a layout that is neither of the two the library knows.</summary>
    /// <param name="layout">The layout to check.</param>
    /// <param name="parameterName">The parameter it was passed as.</param>
    private static void CheckLayout(Gst.Audio.AudioLayout layout, string parameterName)
    {
        if (layout is not (Gst.Audio.AudioLayout.Interleaved or Gst.Audio.AudioLayout.NonInterleaved))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                layout,
                "A DSD conversion reads a layout as interleaved or as non-interleaved and nothing else.");
        }
    }

    /// <summary>Refuses a byte count that is not a whole number of words.</summary>
    /// <param name="bytes">The byte count.</param>
    /// <param name="width">The width of one word.</param>
    /// <param name="formatName">The format parameter the width came from.</param>
    private static void CheckWholeWords(ulong bytes, uint width, string formatName)
    {
        if (bytes % width != 0)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"One word of the format passed as {formatName} is {width} bytes long, which {bytes} is not a whole number of."),
                "numDsdBytes");
        }
    }

    /// <summary>
    /// Refuses a byte count that is not a whole number of frames of both
    /// formats, which is the condition under which the index the C computes
    /// stays inside the block it was given.
    /// </summary>
    /// <param name="bytes">How many bytes are converted over all channels.</param>
    /// <param name="inputWidth">The width of one word of the input format.</param>
    /// <param name="outputWidth">The width of one word of the output format.</param>
    /// <param name="numChannels">How many channels the data carries.</param>
    /// <remarks>
    /// <para>
    /// Every one of the four conversion paths walks the output byte by byte
    /// and computes the input byte it comes from out of the word widths and
    /// the channel count, with no bound on the result. That index is a
    /// permutation of the bytes of the block only when the block holds a whole
    /// number of frames of both formats: with a partial frame at the end the
    /// last few indices reach past it. Four bytes of three channels converted
    /// from <c>U8</c> to <c>U16LE</c>, for instance, reads the fifth byte of a
    /// four byte input.
    /// </para>
    /// <para>
    /// A frame is one word per channel, so the condition is that
    /// <paramref name="bytes"/> divides by the channel count times the least
    /// common multiple of the two widths; the widths are 1, 2 and 4, so the
    /// larger of the two is that multiple. The C checks neither this nor the
    /// divisibility by the channel count, and the same-format paths, which
    /// copy rather than permute, would survive a partial frame; the check is
    /// applied to all of them so that the rule the documentation states is one
    /// rule.
    /// </para>
    /// </remarks>
    private static void CheckWholeFrames(ulong bytes, uint inputWidth, uint outputWidth, int numChannels)
    {
        ulong frame = (ulong)numChannels * Math.Max(inputWidth, outputWidth);

        if (bytes % frame != 0)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"One frame of the two formats is {frame} bytes over {numChannels} channels, which {bytes} is not a whole number of: the conversion would read past the input."),
                "numDsdBytes");
        }
    }

    /// <summary>
    /// Measures how many bytes of one side of a conversion the library reaches.
    /// </summary>
    /// <param name="layout">The layout of that side.</param>
    /// <param name="planeOffsets">Where its planes start, for a non-interleaved side.</param>
    /// <param name="bytes">How many bytes are converted over all channels.</param>
    /// <param name="numChannels">How many channels the data carries.</param>
    /// <param name="offsetsName">The parameter the offsets were passed as.</param>
    /// <returns>The number of bytes the block has to hold.</returns>
    private static ulong RequiredExtent(
        Gst.Audio.AudioLayout layout,
        System.ReadOnlySpan<nuint> planeOffsets,
        ulong bytes,
        int numChannels,
        string offsetsName)
    {
        if (layout == Gst.Audio.AudioLayout.Interleaved)
        {
            // An interleaved block holds every channel of every word in a row.
            return bytes;
        }

        if (planeOffsets.Length != numChannels)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"A non-interleaved side needs one plane offset per channel, which is {numChannels}, and {planeOffsets.Length} were given."),
                offsetsName);
        }

        if (bytes % (ulong)numChannels != 0)
        {
            // The C divides without checking and converts a truncated plane.
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"A non-interleaved side divides the {bytes} bytes over {numChannels} channels, which does not come out even."),
                "numDsdBytes");
        }

        ulong perPlane = bytes / (ulong)numChannels;
        ulong furthest = 0;

        foreach (nuint offset in planeOffsets)
        {
            furthest = System.Math.Max(furthest, (ulong)offset);
        }

        if (furthest > ulong.MaxValue - perPlane)
        {
            throw new ArgumentOutOfRangeException(
                offsetsName,
                furthest,
                "The plane offset is so large that the block it implies cannot be measured.");
        }

        return furthest + perPlane;
    }

    /// <summary>Refuses a span shorter than the extent the arguments imply.</summary>
    /// <param name="required">The number of bytes the library reaches.</param>
    /// <param name="length">The length of the span that was given.</param>
    /// <param name="parameterName">The parameter it was passed as.</param>
    private static void CheckLength(ulong required, int length, string parameterName)
    {
        if (required > (ulong)length)
        {
            throw new ArgumentException(
                FormattableString.Invariant(
                    $"The conversion reaches {required} bytes of this block, and the span holds {length}."),
                parameterName);
        }
    }
}
