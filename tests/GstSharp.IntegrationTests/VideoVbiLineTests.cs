using System;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written line halves of the VBI pair:
/// <see cref="VideoVBIEncoder.WriteLine"/> and
/// <see cref="VideoVBIParser.AddLine"/>, whose length rule is the stride of the
/// geometry the two objects were created with.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class VideoVbiLineTests
{
    /// <summary>The width of an HD line, which both halves are exercised at.</summary>
    private const uint Width = 1920;

    /// <summary>The stride of one <c>UYVY</c> line of that width, two bytes per pixel.</summary>
    private const int UyvyStride = (int)Width * 2;

    /// <summary>
    /// The stride of one <c>v210</c> line of that width, which is 128 bytes
    /// per 48 pixels: 1920 pixels are 40 such groups, so 5120 bytes.
    /// </summary>
    private const int V210Stride = (((int)Width + 47) / 48) * 128;

    /// <summary>The Data Identifier of the packet the round trip carries.</summary>
    private const byte DID = 0x41;

    /// <summary>The Secondary Data Identifier of that packet.</summary>
    private const byte SDID = 0x05;

    /// <summary>
    /// A packet written into a line is read back out of it by a parser of the
    /// same geometry.
    /// </summary>
    [Fact]
    public void APacketWrittenIntoALineIsParsedBackOutOfIt()
    {
        byte[] payload = [0x10, 0x20, 0x30, 0x40];

        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");

        Assert.True(encoder.AddAncillary(false, DID, SDID, payload));

        byte[] line = new byte[UyvyStride];
        encoder.WriteLine(line);

        using VideoVBIParser parser = VideoVBIParser.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");

        parser.AddLine(line);

        Assert.Equal(VideoVBIParserResult.Ok, parser.GetAncillary(out VideoAncillary ancillary));
        Assert.Equal(DID, ancillary.DID);
        Assert.Equal(SDID, ancillary.SDIDBlockNumber);
        Assert.Equal(payload.Length, ancillary.DataCount);

        for (int i = 0; i < payload.Length; i++)
        {
            Assert.Equal(payload[i], ancillary.Data[i]);
        }

        // The line carried exactly the one packet.
        Assert.Equal(VideoVBIParserResult.Done, parser.GetAncillary(out _));
    }

    /// <summary>
    /// The same round trip in <c>v210</c>, whose stride is not two bytes per
    /// pixel but 128 bytes per 48 pixels.
    /// </summary>
    /// <remarks>
    /// It is the other format the two halves support, and the one whose line
    /// size does not follow from the width by a multiplication, so it is what
    /// pins the measurement to <c>GstVideoInfo</c> rather than to an
    /// arithmetic of the wrapper's own: 1920 pixels of <c>v210</c> are 5120
    /// bytes.
    /// </remarks>
    [Fact]
    public void APacketWrittenIntoAV210LineIsParsedBackOutOfIt()
    {
        byte[] payload = [0x51, 0x52, 0x53, 0x54];

        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.V210, Width)
            ?? throw new InvalidOperationException("v210 at 1920 pixels has to be a supported VBI geometry.");

        Assert.True(encoder.AddAncillary(false, DID, SDID, payload));

        byte[] line = new byte[V210Stride];
        encoder.WriteLine(line);

        using VideoVBIParser parser = VideoVBIParser.New(VideoFormat.V210, Width)
            ?? throw new InvalidOperationException("v210 at 1920 pixels has to be a supported VBI geometry.");

        parser.AddLine(line);

        Assert.Equal(VideoVBIParserResult.Ok, parser.GetAncillary(out VideoAncillary ancillary));
        Assert.Equal(DID, ancillary.DID);
        Assert.Equal(SDID, ancillary.SDIDBlockNumber);
        Assert.Equal(payload.Length, ancillary.DataCount);

        for (int i = 0; i < payload.Length; i++)
        {
            Assert.Equal(payload[i], ancillary.Data[i]);
        }

        Assert.Equal(VideoVBIParserResult.Done, parser.GetAncillary(out _));
    }

    /// <summary>
    /// A <c>v210</c> line one byte shorter than that stride is refused, which
    /// is what pins the measurement to 5120 bytes rather than to anything
    /// smaller the round trip would also survive.
    /// </summary>
    [Fact]
    public void AShortV210LineIsRefused()
    {
        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.V210, Width)
            ?? throw new InvalidOperationException("v210 at 1920 pixels has to be a supported VBI geometry.");

        byte[] tooShort = new byte[V210Stride - 1];

        _ = Assert.Throws<ArgumentException>(() => encoder.WriteLine(tooShort));
    }

    /// <summary>
    /// A copy of either half keeps the geometry of the original, so the round
    /// trip works through the copies as well.
    /// </summary>
    [Fact]
    public void ACopyOfEitherHalfKeepsTheGeometryOfTheOriginal()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04];

        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");
        using VideoVBIEncoder encoderCopy = encoder.Copy();

        Assert.True(encoderCopy.AddAncillary(false, DID, SDID, payload));

        byte[] line = new byte[UyvyStride];
        encoderCopy.WriteLine(line);

        using VideoVBIParser parser = VideoVBIParser.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");
        using VideoVBIParser parserCopy = parser.Copy();

        parserCopy.AddLine(line);

        Assert.Equal(VideoVBIParserResult.Ok, parserCopy.GetAncillary(out VideoAncillary ancillary));
        Assert.Equal(DID, ancillary.DID);

        // The copy carries its own pending packets, so the original still
        // encodes a line of its own after the copy has written one.
        Assert.True(encoder.AddAncillary(false, DID, SDID, payload));

        byte[] originalLine = new byte[UyvyStride];
        encoder.WriteLine(originalLine);

        using VideoVBIParser originalParser = VideoVBIParser.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");

        originalParser.AddLine(originalLine);
        Assert.Equal(VideoVBIParserResult.Ok, originalParser.GetAncillary(out VideoAncillary fromOriginal));
        Assert.Equal(DID, fromOriginal.DID);
    }

    /// <summary>A line shorter than the stride of the geometry is refused by both halves.</summary>
    [Fact]
    public void AShortLineIsRefused()
    {
        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");
        using VideoVBIParser parser = VideoVBIParser.New(VideoFormat.Uyvy, Width)
            ?? throw new InvalidOperationException("UYVY at 1920 pixels has to be a supported VBI geometry.");

        byte[] tooShort = new byte[UyvyStride - 1];

        _ = Assert.Throws<ArgumentException>(() => encoder.WriteLine(tooShort));
        _ = Assert.Throws<ArgumentException>(() => parser.AddLine(tooShort));
    }

    /// <summary>
    /// A geometry the library itself walks off the end of is refused before the
    /// call: below six pixels the unsigned loop bound of the conversion
    /// routines wraps.
    /// </summary>
    [Fact]
    public void AWidthBelowSixIsRefused()
    {
        using VideoVBIEncoder encoder = VideoVBIEncoder.New(VideoFormat.Uyvy, 4)
            ?? throw new InvalidOperationException("UYVY at 4 pixels is still a geometry the C accepts.");
        using VideoVBIParser parser = VideoVBIParser.New(VideoFormat.Uyvy, 4)
            ?? throw new InvalidOperationException("UYVY at 4 pixels is still a geometry the C accepts.");

        byte[] line = new byte[256];

        _ = Assert.Throws<InvalidOperationException>(() => encoder.WriteLine(line));
        _ = Assert.Throws<InvalidOperationException>(() => parser.AddLine(line));
    }

    /// <summary>A geometry the library does not support is still answered with null.</summary>
    [Fact]
    public void AnUnsupportedFormatIsAnsweredWithNull()
    {
        Assert.Null(VideoVBIEncoder.New(VideoFormat.I420, Width));
        Assert.Null(VideoVBIParser.New(VideoFormat.I420, Width));
    }
}
