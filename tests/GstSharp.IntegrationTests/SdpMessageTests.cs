using System.Text;
using Gst;
using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstSdp</c> binding against the library that is installed: a session
/// description is parsed, read through the wrappers of its payload structures
/// and written back out.
/// </summary>
/// <remarks>
/// <para>
/// The payload structures of a message — media, attribute, origin, connection,
/// bandwidth, time, zone and key — are plain C structures that the library
/// hands out by pointer, and fixups.json keeps them behind a pointer for that
/// reason. What the message hands out is borrowed: the wrapper addresses
/// storage the message owns, so it is read while the message is alive and its
/// <c>Free</c> and <c>Uninit</c> are never called on it. Disposing the message
/// releases the lot.
/// </para>
/// <para>
/// <see cref="SDPOrigin"/> carries no method at all in the gir, so the origin
/// is asserted through the text the message serialises back to rather than
/// through the wrapper.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SdpMessageTests
{
    /// <summary>
    /// A minimal session description with two media, one attribute each. SDP
    /// lines are terminated with CRLF, which is what RFC 4566 asks for and what
    /// the library writes back out.
    /// </summary>
    private const string Description =
        "v=0\r\n" +
        "o=- 3735928559 2 IN IP4 127.0.0.1\r\n" +
        "s=GstSharp round trip\r\n" +
        "c=IN IP4 127.0.0.1\r\n" +
        "t=0 0\r\n" +
        "m=audio 49170 RTP/AVP 0\r\n" +
        "a=rtpmap:0 PCMU/8000\r\n" +
        "m=video 51372 RTP/AVP 96\r\n" +
        "a=rtpmap:96 H264/90000\r\n";

    /// <summary>
    /// The session level of a parsed description reads back the way it was
    /// written, and the message serialises to text that carries the origin the
    /// wrapper of <see cref="SDPOrigin"/> cannot be asked for.
    /// </summary>
    [Fact]
    public void ASessionDescriptionParsesIntoItsFields()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Description, out SDPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal("0", message.GetVersion());
            Assert.Equal("GstSharp round trip", message.GetSessionName());
            Assert.Equal(2u, message.MediasLen());

            // The origin is there and is addressable, which is all the wrapper
            // of a structure without methods can say.
            Assert.NotNull(message.GetOrigin());

            string text = message.AsText();

            Assert.Contains("o=- 3735928559 2 IN IP4 127.0.0.1", text, StringComparison.Ordinal);
            Assert.Contains("s=GstSharp round trip", text, StringComparison.Ordinal);
            Assert.Contains("m=audio 49170 RTP/AVP 0", text, StringComparison.Ordinal);
            Assert.Contains("m=video 51372 RTP/AVP 96", text, StringComparison.Ordinal);
            Assert.Contains("a=rtpmap:96 H264/90000", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The media of a message are read through the opaque wrappers the message
    /// owns. Every accessor here is one that the emitter drops while the
    /// structure is projected by value, so this is what the <c>forceOpaque</c>
    /// entries of fixups.json buy.
    /// </summary>
    [Fact]
    public void AMediaIsReadThroughTheWrapperTheMessageOwns()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Description, out SDPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            SDPMedia video = message.GetMedia(1);

            Assert.Equal("video", video.GetMedia());
            Assert.Equal(51372u, video.GetPort());
            Assert.Equal("RTP/AVP", video.GetProto());
            Assert.Equal(1u, video.FormatsLen());
            Assert.Equal("96", video.GetFormat(0));
            Assert.Equal("96 H264/90000", video.GetAttributeVal("rtpmap"));

            // The connection is inherited from the session level, so the media
            // itself declares none.
            Assert.Equal(0u, video.ConnectionsLen());
            Assert.Equal(1u, video.AttributesLen());
            Assert.NotNull(video.GetAttribute(0));

            // Reading the second media as well proves that the first wrapper
            // did not take ownership of anything: both point into the same live
            // message.
            SDPMedia audio = message.GetMedia(0);

            Assert.Equal("audio", audio.GetMedia());
            Assert.Equal("0 PCMU/8000", audio.GetAttributeVal("rtpmap"));
            Assert.Equal("video", video.GetMedia());
        }
    }

    /// <summary>
    /// The other way into a message: an empty one is allocated and the bytes of
    /// a description are parsed into it. It has to end up equal to what
    /// <c>NewFromText</c> produces, which is also what proves that the text a
    /// message writes is a description the library parses again.
    /// </summary>
    [Fact]
    public void ABufferParsesIntoAnAllocatedMessage()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.New(out SDPMessage? parsed));
        Assert.NotNull(parsed);

        using (parsed)
        {
            Assert.Equal(
                SDPResult.Ok,
                SDPMessage.ParseBuffer(Encoding.UTF8.GetBytes(Description), parsed));
            Assert.Equal(2u, parsed.MediasLen());

            Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(parsed.AsText(), out SDPMessage? again));
            Assert.NotNull(again);

            using (again)
            {
                Assert.Equal(parsed.AsText(), again.AsText(), StringComparer.Ordinal);
            }
        }
    }

    /// <summary>
    /// A media of a description describes a payload type, and the library turns
    /// it into the <c>Gst.Caps</c> a receiver is configured with. The call
    /// crosses from <c>GstSdp</c> into the core module, which is the only place
    /// this module hands a core type out.
    /// </summary>
    [Fact]
    public void TheCapsOfAPayloadTypeAreBuiltFromTheMedia()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Description, out SDPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            using Caps? caps = message.GetMedia(1).GetCapsFromMedia(96);

            Assert.NotNull(caps);

            string description = caps.ToString();

            Assert.Contains("H264", description, StringComparison.Ordinal);
            Assert.Contains("payload=(int)96", description, StringComparison.Ordinal);
        }
    }
}
