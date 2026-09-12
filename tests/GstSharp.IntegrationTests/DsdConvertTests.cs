using System;
using Gst.Audio;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written <see cref="AudioGlobal.DsdConvert"/>: the conversions it
/// forwards and the extent rules that keep the library inside the two spans it
/// is handed.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class DsdConvertTests
{
    /// <summary>Eight bytes of two channel interleaved DSD.</summary>
    private static ReadOnlySpan<byte> Source =>
        [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];

    /// <summary>A conversion that changes nothing copies the bytes over.</summary>
    [Fact]
    public void AConversionBetweenTheSameFormatAndLayoutCopiesTheBytes()
    {
        byte[] output = new byte[8];

        AudioGlobal.DsdConvert(
            Source,
            output,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false);

        Assert.Equal(Source.ToArray(), output);
    }

    /// <summary>The bit reversal turns every byte around.</summary>
    [Fact]
    public void TheBitReversalTurnsEveryByteAround()
    {
        byte[] output = new byte[8];

        AudioGlobal.DsdConvert(
            Source,
            output,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: true);

        for (int i = 0; i < output.Length; i++)
        {
            Assert.Equal(Reverse(Source[i]), output[i]);
        }
    }

    /// <summary>
    /// A round trip through a non-interleaved layout of two planes answers the
    /// bytes it started from.
    /// </summary>
    [Fact]
    public void ARoundTripThroughTwoPlanesAnswersTheInput()
    {
        ReadOnlySpan<nuint> planes = [0, 4];

        byte[] planar = new byte[8];
        AudioGlobal.DsdConvert(
            Source,
            planar,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.NonInterleaved,
            ReadOnlySpan<nuint>.Empty,
            planes,
            8,
            2,
            reverseByteBits: false);

        byte[] interleaved = new byte[8];
        AudioGlobal.DsdConvert(
            planar,
            interleaved,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.NonInterleaved,
            AudioLayout.Interleaved,
            planes,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false);

        Assert.Equal(Source.ToArray(), interleaved);
    }

    /// <summary>A round trip through the wider word format answers the input.</summary>
    [Fact]
    public void ARoundTripThroughTheWiderWordFormatAnswersTheInput()
    {
        byte[] wide = new byte[8];
        AudioGlobal.DsdConvert(
            Source,
            wide,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU16le,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false);

        byte[] narrow = new byte[8];
        AudioGlobal.DsdConvert(
            wide,
            narrow,
            DsdFormat.DsdFormatU16le,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false);

        Assert.Equal(Source.ToArray(), narrow);
    }

    /// <summary>An output block shorter than the conversion reaches is refused.</summary>
    [Fact]
    public void AShortOutputBlockIsRefused()
    {
        byte[] output = new byte[7];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            Source,
            output,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false));
    }

    /// <summary>A plane offset that reaches past the output block is refused.</summary>
    [Fact]
    public void APlaneOffsetPastTheOutputBlockIsRefused()
    {
        byte[] output = new byte[8];
        nuint[] planes = [0, 8];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            Source,
            output,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.NonInterleaved,
            ReadOnlySpan<nuint>.Empty,
            planes,
            8,
            2,
            reverseByteBits: false));
    }

    /// <summary>Two blocks that share memory are refused.</summary>
    [Fact]
    public void TwoOverlappingBlocksAreRefused()
    {
        byte[] block = new byte[16];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            block.AsSpan(0, 8),
            block.AsSpan(4, 8),
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false));
    }

    /// <summary>A byte count that is not a whole number of words is refused.</summary>
    [Fact]
    public void AByteCountThatIsNotAWholeNumberOfWordsIsRefused()
    {
        byte[] output = new byte[8];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            Source[..7],
            output.AsSpan(0, 7),
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU16le,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            7,
            2,
            reverseByteBits: false));
    }

    /// <summary>
    /// A byte count that is a whole number of words of both formats but not a
    /// whole number of frames is refused, because the conversion reads past
    /// the input for the partial frame at the end.
    /// </summary>
    /// <remarks>
    /// Four bytes of three channels from <c>U8</c> to <c>U16LE</c>: the output
    /// byte at index two is taken from the input byte at index four, which is
    /// one past a four byte input. Every other check accepts these arguments —
    /// four divides by one and by two, three channels is positive, and both
    /// extents come to four.
    /// </remarks>
    [Fact]
    public void AByteCountThatIsNotAWholeNumberOfFramesIsRefused()
    {
        byte[] output = new byte[4];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            Source[..4],
            output,
            DsdFormat.DsdFormatU8,
            DsdFormat.DsdFormatU16le,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            4,
            3,
            reverseByteBits: false));
    }

    /// <summary>
    /// The same rule on the way to two planes: a partial frame reaches past
    /// the input there as well.
    /// </summary>
    /// <remarks>
    /// Four bytes of two channels from <c>U32LE</c> to <c>U8</c> with the
    /// planes at zero and two: the second plane reads the input byte at index
    /// six of a four byte input. The plane extents come to four on both sides,
    /// so nothing but the frame rule refuses it.
    /// </remarks>
    [Fact]
    public void AByteCountThatIsNotAWholeNumberOfFramesIsRefusedForPlanesToo()
    {
        byte[] output = new byte[4];
        nuint[] outputPlanes = [0, 2];

        _ = Assert.Throws<ArgumentException>(() => AudioGlobal.DsdConvert(
            Source[..4],
            output,
            DsdFormat.DsdFormatU32le,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.NonInterleaved,
            ReadOnlySpan<nuint>.Empty,
            outputPlanes,
            4,
            2,
            reverseByteBits: false));
    }

    /// <summary>An unknown format has no word width and is refused.</summary>
    [Fact]
    public void AnUnknownFormatIsRefused()
    {
        byte[] output = new byte[8];

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => AudioGlobal.DsdConvert(
            Source,
            output,
            DsdFormat.DsdFormatUnknown,
            DsdFormat.DsdFormatU8,
            AudioLayout.Interleaved,
            AudioLayout.Interleaved,
            ReadOnlySpan<nuint>.Empty,
            ReadOnlySpan<nuint>.Empty,
            8,
            2,
            reverseByteBits: false));
    }

    /// <summary>Reverses the bits of one byte.</summary>
    /// <param name="value">The byte to reverse.</param>
    /// <returns>The byte with its bits in the opposite order.</returns>
    private static byte Reverse(byte value)
    {
        int reversed = 0;

        for (int bit = 0; bit < 8; bit++)
        {
            reversed |= ((value >> bit) & 1) << (7 - bit);
        }

        return (byte)reversed;
    }
}
