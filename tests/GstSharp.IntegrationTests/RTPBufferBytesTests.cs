using Gst;
using Gst.GLib;
using Gst.Rtp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The three <c>GBytes</c> members of <see cref="RTPBuffer"/>: the payload and
/// the extension data are copied out of the packet into a block of their own,
/// and the one byte header reader walks such a block.
/// </summary>
/// <remarks>
/// <para>
/// Both readers copy - <c>g_bytes_new</c> is what they end in - so the block
/// outlives the mapping it was read through and is unaffected by a later write
/// into the packet. That is what the second test measures.
/// </para>
/// <para>
/// The empty payload is deliberately not exercised: whether the C answers NULL
/// or an empty block for one depends on how many memory blocks the buffer has
/// and whether it carries padding, so neither answer would be a fact about the
/// binding.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RTPBufferBytesTests
{
    /// <summary>The payload of the packets below.</summary>
    private const uint PayloadLength = 8;

    /// <summary>The length of an RTP header with no CSRC and no extension.</summary>
    private const int HeaderLength = 12;

    /// <summary>The id of the one byte header extension the packets carry.</summary>
    private const byte ExtensionId = 5;

    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8];

    private static readonly byte[] Extension = [0x0A, 0x0B, 0x0C, 0x0D];

    /// <summary>
    /// The payload reader answers the bytes of the packet, in a block that is
    /// the caller's own.
    /// </summary>
    [Fact]
    public void ThePayloadIsCopiedIntoABlockOfItsOwn()
    {
        using Gst.Buffer buffer = NewPacket();

        Bytes? payload;
        Assert.True(RTPBuffer.MapBuffer(buffer, MapFlags.Read, out RTPBuffer rtp));
        try
        {
            payload = rtp.GetPayload();
        }
        finally
        {
            rtp.Unmap();
        }

        Assert.NotNull(payload);
        using (payload)
        {
            Assert.Equal(Payload, payload.ToArray());

            // The block is a copy: rewriting the payload of the packet does not
            // reach it.
            using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Write))
            {
                map.Span[HeaderLength] = 0xFF;
            }

            Assert.Equal(Payload, payload.ToArray());
        }
    }

    /// <summary>
    /// A packet with no extension answers no block, and leaves the header bits
    /// at zero rather than at whatever was in the caller's variable.
    /// </summary>
    /// <remarks>
    /// The C returns before it writes the bits, so the local of the generated
    /// member is default initialised for it; without that the caller would read
    /// an uninitialised number.
    /// </remarks>
    [Fact]
    public void APacketWithNoExtensionAnswersNoBlockAndNoBits()
    {
        using Gst.Buffer buffer = NewPacket();

        Assert.True(RTPBuffer.MapBuffer(buffer, MapFlags.Read, out RTPBuffer rtp));
        try
        {
            Assert.Null(rtp.GetExtensionData(out ushort bits));
            Assert.Equal(0, bits);
        }
        finally
        {
            rtp.Unmap();
        }
    }

    /// <summary>
    /// A one byte header extension is read back out of the block the extension
    /// reader answers, by the id it was written under and by no other.
    /// </summary>
    [Fact]
    public void AOneByteHeaderIsReadBackOutOfTheExtensionBlock()
    {
        using Gst.Buffer buffer = NewPacket();

        Bytes? extension;
        ushort bits;
        Assert.True(RTPBuffer.MapBuffer(buffer, MapFlags.Read | MapFlags.Write, out RTPBuffer rtp));
        try
        {
            Assert.True(rtp.AddExtensionOnebyteHeader(ExtensionId, Extension));
            extension = rtp.GetExtensionData(out bits);
        }
        finally
        {
            rtp.Unmap();
        }

        Assert.NotNull(extension);
        using (extension)
        {
            // 0xBEDE is the profile of a one byte header extension.
            Assert.Equal(0xBEDE, bits);

            Assert.True(RTPBuffer.GetExtensionOnebyteHeaderFromBytes(
                extension,
                0xBEDE,
                ExtensionId,
                0,
                out byte[]? data));
            Assert.Equal(Extension, data);

            // An id the block does not carry is answered with false, and the
            // C leaves the out parameter untouched for it.
            Assert.False(RTPBuffer.GetExtensionOnebyteHeaderFromBytes(
                extension,
                0xBEDE,
                ExtensionId + 1,
                0,
                out byte[]? missing));
            Assert.Null(missing);
        }
    }

    /// <summary>
    /// Allocates an RTP packet and writes the payload bytes into it.
    /// </summary>
    /// <returns>The buffer, which the caller disposes.</returns>
    /// <remarks>
    /// The payload is written through a plain mapping of the buffer rather than
    /// through the RTP view, because the module offers no payload writer: the
    /// header of a freshly allocated packet is twelve bytes, since it carries
    /// no CSRC and no extension yet.
    /// </remarks>
    private static Gst.Buffer NewPacket()
    {
        Gst.Buffer buffer = RTPBuffer.NewAllocate(PayloadLength, 0, 0);

        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            Payload.CopyTo(map.Span[HeaderLength..]);
        }

        return buffer;
    }
}
