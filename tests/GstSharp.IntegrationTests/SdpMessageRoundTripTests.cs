using System.Text;
using Gst;
using Gst.Sdp;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A whole SDP message built member by member, written out as text, parsed
/// back and projected into caps: the description half of the Sdp module, as
/// opposed to the key management half the MIKEY tests cover.
/// </summary>
/// <remarks>
/// The out parameter constructors of this module answer a
/// <see cref="SDPResult"/> and leave the message in the parameter, so every
/// construction below is two assertions: the result is
/// <see cref="SDPResult.Ok"/> and the message is there.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class SdpMessageRoundTripTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public SdpMessageRoundTripTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A message built by hand writes the lines it was given and parses back
    /// into a message that writes the same text.
    /// </summary>
    /// <remarks>
    /// A session name is not decoration: an SDP without an <c>s=</c> line is
    /// invalid, and a message that does not carry one does not survive the
    /// round trip.
    /// </remarks>
    [Fact]
    public void AMessageBuiltByHandSerialisesAndParsesBack()
    {
        using SDPMessage message = NewMessage();

        string text = message.AsText();
        _output.WriteLine(text);

        Assert.Contains("m=audio 5004 RTP/AVP 96", text, StringComparison.Ordinal);
        Assert.Contains("a=rtpmap:96 L16/44100/2", text, StringComparison.Ordinal);

        Assert.Equal(SDPResult.Ok, SDPMessage.New(out SDPMessage? parsed));
        Assert.NotNull(parsed);

        using (parsed)
        {
            Assert.Equal(SDPResult.Ok, SDPMessage.ParseBuffer(Encoding.UTF8.GetBytes(text), parsed));

            Assert.Equal(text, parsed.AsText());
            Assert.Equal(1u, parsed.MediasLen());
            Assert.Equal("gstsharp", parsed.GetAttributeVal("tool"));

            SDPMedia media = parsed.GetMedia(0);
            Assert.Equal("audio", media.GetMedia());
            Assert.Equal("96 L16/44100/2", media.GetAttributeVal("rtpmap"));
        }
    }

    /// <summary>
    /// The session attributes of a message land in the caps as fields whose
    /// names carry the <c>a-</c> prefix the library puts there.
    /// </summary>
    /// <remarks>
    /// <c>gst_sdp_message_attributes_to_caps</c>
    /// (gstsdpmessage.c:4784) hands the attributes of the session to
    /// <c>sdp_add_attributes_to_caps</c> (gstsdpmessage.c:4413), which puts the
    /// <c>a-</c> prefix in front of the key at :4454.
    /// </remarks>
    [Fact]
    public void AttributesToCapsWritesTheSessionAttributesIntoTheCaps()
    {
        using SDPMessage message = NewMessage();

        using Caps caps = Caps.NewEmptySimple("application/x-rtp");
        Assert.Equal(SDPResult.Ok, message.AttributesToCaps(caps));

        Structure? structure = caps.GetStructure(0);
        Assert.NotNull(structure);

        _output.WriteLine(structure.ToString());

        Assert.True(structure.HasField("a-tool"));
        Assert.Equal("gstsharp", structure.GetString("a-tool"));
    }

    /// <summary>
    /// Builds the message both tests work on: one session with one audio media
    /// carrying a single dynamic payload type.
    /// </summary>
    /// <returns>The message, which the caller disposes.</returns>
    private static SDPMessage NewMessage()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.New(out SDPMessage? message));
        Assert.NotNull(message);

        Assert.Equal(SDPResult.Ok, message.SetVersion("0"));
        Assert.Equal(SDPResult.Ok, message.SetOrigin("-", "1", "1", "IN", "IP4", "127.0.0.1"));
        Assert.Equal(SDPResult.Ok, message.SetSessionName("gstsharp"));
        Assert.Equal(SDPResult.Ok, message.SetConnection("IN", "IP4", "127.0.0.1", 0, 0));
        Assert.Equal(SDPResult.Ok, message.AddAttribute("tool", "gstsharp"));
        Assert.Equal(SDPResult.Ok, message.AddBandwidth("AS", 128));
        Assert.Equal(1u, message.AttributesLen());

        Assert.Equal(SDPResult.Ok, SDPMedia.New(out SDPMedia? media));
        Assert.NotNull(media);

        Assert.Equal(SDPResult.Ok, media.SetMedia("audio"));
        Assert.Equal(SDPResult.Ok, media.SetPortInfo(5004, 1));
        Assert.Equal(SDPResult.Ok, media.SetProto("RTP/AVP"));
        Assert.Equal(SDPResult.Ok, media.AddFormat("96"));
        Assert.Equal(SDPResult.Ok, media.AddAttribute("rtpmap", "96 L16/44100/2"));

        Assert.Equal(SDPResult.Ok, message.AddMedia(media));
        Assert.Equal(1u, message.MediasLen());

        return message;
    }
}
