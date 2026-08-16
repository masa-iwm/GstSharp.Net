using Gst.Sdp;
using Gst.WebRTC;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstWebRTC</c> binding against the library that is installed: a
/// session description is built around an SDP message, read back through the
/// fields of the structure and copied.
/// </summary>
/// <remarks>
/// <para>
/// <c>GstWebRTCSessionDescription</c> is the type the whole module turns on: it
/// is what an application hands <c>webrtcbin</c> as an offer or an answer, and
/// what <c>webrtcbin</c> hands back. It is also the one type here that the
/// generator cannot construct — <c>gst_webrtc_session_description_new</c> takes
/// its message over, which is a shape the emitter refuses — so its factory is
/// hand written and this is where it is measured.
/// </para>
/// <para>
/// Both fields of the structure are public API in C with no accessor function,
/// so the binding reads them through a raw mirror of the layout. Reading the
/// type back as what was passed in, and the message back as the text it was
/// parsed from, is what says that the mirror agrees with the library: a wrong
/// offset would read the pointer as the type, or a byte of the message as the
/// pointer.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class WebRTCSessionDescriptionTests
{
    /// <summary>
    /// A minimal offer. It is a valid session description rather than a
    /// realistic one: what is under test is the ownership of the message, not
    /// what a peer would negotiate from it.
    /// </summary>
    private const string Offer =
        "v=0\r\n" +
        "o=- 3735928559 2 IN IP4 127.0.0.1\r\n" +
        "s=GstSharp offer\r\n" +
        "t=0 0\r\n" +
        "m=audio 9 UDP/TLS/RTP/SAVPF 111\r\n" +
        "c=IN IP4 0.0.0.0\r\n" +
        "a=rtpmap:111 OPUS/48000/2\r\n" +
        "a=setup:actpass\r\n" +
        "a=mid:audio0\r\n";

    /// <summary>
    /// <c>gst_webrtc_session_description_new</c> is
    /// <c>transfer-ownership="full"</c> in its <c>sdp</c> parameter: the
    /// description owns the message afterwards, and the wrapper of the caller
    /// is left owning nothing.
    /// </summary>
    [Fact]
    public void ADescriptionTakesTheMessageOver()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Offer, out SDPMessage? message));
        Assert.NotNull(message);

        using WebRTCSessionDescription description =
            WebRTCSessionDescription.New(WebRTCSDPType.Offer, message);

        // What the caller had is gone: the call consumed the value of the
        // wrapper, so the wrapper is disposed and its handle is unreachable.
        // This is the whole pattern — parse, hand over, forget; never dispose a
        // message that was handed over, and never touch it again.
        Assert.True(message.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => message.Handle);

        // And the description has one of its own, which is what the fields of
        // the structure say.
        Assert.Equal(WebRTCSDPType.Offer, description.Type);

        using SDPMessage carried = description.GetSdp();

        Assert.Equal("GstSharp offer", carried.GetSessionName());
        Assert.Equal(1u, carried.MediasLen());
        Assert.Contains("a=rtpmap:111 OPUS/48000/2", carried.AsText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The message a description hands out is the caller's own: disposing it
    /// leaves the description intact, which is what a copy buys over the
    /// pointer the structure holds.
    /// </summary>
    [Fact]
    public void TheMessageADescriptionHandsOutIsIndependentOfIt()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Offer, out SDPMessage? message));
        Assert.NotNull(message);

        using WebRTCSessionDescription description =
            WebRTCSessionDescription.New(WebRTCSDPType.Answer, message);

        string text;

        using (SDPMessage first = description.GetSdp())
        {
            text = first.AsText();
        }

        // The first copy is gone. Reading the description again has to produce
        // the same text, which it cannot if the copy had freed what the
        // description points at.
        using SDPMessage second = description.GetSdp();

        Assert.Equal(text, second.AsText(), StringComparer.Ordinal);
        Assert.Equal(WebRTCSDPType.Answer, description.Type);
    }

    /// <summary>
    /// <c>gst_webrtc_session_description_copy</c> deep copies: the copy carries
    /// the same type and the same message, and outlives the original.
    /// </summary>
    [Fact]
    public void ACopyCarriesTheSameDescription()
    {
        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Offer, out SDPMessage? message));
        Assert.NotNull(message);

        WebRTCSessionDescription original =
            WebRTCSessionDescription.New(WebRTCSDPType.Pranswer, message);
        WebRTCSessionDescription copy;

        using (original)
        {
            copy = original.Copy();
        }

        // The original is freed, and with it the message it owned. The copy has
        // its own of both.
        using (copy)
        {
            Assert.Equal(WebRTCSDPType.Pranswer, copy.Type);

            using SDPMessage carried = copy.GetSdp();

            Assert.Equal("GstSharp offer", carried.GetSessionName());
        }
    }

    /// <summary>
    /// The enumerations of the module reach the library: the names the C
    /// function answers pin the values of <see cref="WebRTCSDPType"/> against
    /// the header rather than against the gir they were read from.
    /// </summary>
    [Theory]
    [InlineData(WebRTCSDPType.Offer, "offer")]
    [InlineData(WebRTCSDPType.Pranswer, "pranswer")]
    [InlineData(WebRTCSDPType.Answer, "answer")]
    [InlineData(WebRTCSDPType.Rollback, "rollback")]
    public void EverySdpTypeNamesItself(WebRTCSDPType type, string name)
    {
        Assert.Equal(name, WebRTCSDPTypeExtensions.ToString(type));
    }
}
