using Gst;
using Gst.Audio;
using Gst.Rtsp;
using Gst.Sdp;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated accessors of the <c>const gchar *</c> fields of a record,
/// measured against the library that is installed.
/// </summary>
/// <remarks>
/// Every value read here was written by a native function on the other side of
/// the field — <c>gst_video_info_from_caps</c>, <c>gst_audio_info_from_caps</c>,
/// <c>gst_element_factory_get_static_pad_templates</c>,
/// <c>gst_rtsp_url_parse</c> and <c>gst_sdp_message_parse_buffer</c> — so a
/// mirror whose offsets are wrong reads a different string or no string at all.
/// The accessor copies the bytes on every read, which is why the values outlive
/// nothing and are compared as ordinary managed strings.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RecordStringFieldTests
{
    /// <summary>
    /// The name and the description of a format come out of the static table
    /// the library keeps for the life of the process, which is why the overlays
    /// state them non nullable: reading them answers rather than throws.
    /// </summary>
    [Fact]
    public void AVideoFormatDescribesItselfByNameAndDescription()
    {
        using Caps caps = Caps.FromString("video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of an I420 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused I420 caps.");

        VideoFormatInfo format = info.FormatInfo;

        Assert.Equal("I420", format.Name);
        Assert.NotEmpty(format.Description);

        // The unknown format is a row of the same table rather than a hole in
        // it, so the accessors answer there too.
        using VideoInfo empty = VideoInfo.New();
        Assert.Equal("UNKNOWN", empty.FormatInfo.Name);
    }

    /// <summary>The audio table is the same shape as the video one.</summary>
    [Fact]
    public void AnAudioFormatDescribesItselfByNameAndDescription()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw,format=S16LE,layout=interleaved,rate=44100,channels=2")
            ?? throw new InvalidOperationException("The caps of interleaved S16LE audio have to parse.");

        using AudioInfo info = AudioInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_audio_info_from_caps refused S16LE caps.");

        AudioFormatInfo format = info.FormatInfo;

        Assert.Equal("S16LE", format.Name);
        Assert.NotEmpty(format.Description);
        Assert.Equal("UNKNOWN", AudioInfo.New().FormatInfo.Name);
    }

    /// <summary>
    /// The templates of a factory sit in the static storage of the plugin, so
    /// the name template is a compile time literal the accessor copies out.
    /// </summary>
    [RequiresElementFact("videotestsrc")]
    public void AStaticPadTemplateCarriesTheNameItWasDeclaredWith()
    {
        using ElementFactory? factory = ElementFactory.Find("videotestsrc");

        Assert.NotNull(factory);

        StaticPadTemplate template = Assert.Single(factory.GetStaticPadTemplates());

        Assert.Equal("src", template.NameTemplate);
    }

    /// <summary>
    /// A parsed RTSP URL fills its host and its absolute path on every
    /// successful parse and leaves the optional parts NULL, which is exactly
    /// the split the overlays state.
    /// </summary>
    [Fact]
    public void AParsedRtspUrlSplitsIntoItsStringFields()
    {
        Assert.Equal(
            RTSPResult.Ok,
            RTSPUrl.Parse("rtsp://user:pw@host.example:8554/stream?probe=1", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            Assert.Equal("host.example", url.Host);
            Assert.Equal("/stream", url.Abspath);
            Assert.Equal("user", url.User);
            Assert.Equal("pw", url.Passwd);
            Assert.Equal("probe=1", url.Query);
        }

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://host.example/stream", out RTSPUrl? bare));
        Assert.NotNull(bare);

        using (bare)
        {
            // No userinfo and no query, which the C parser leaves NULL rather
            // than empty.
            Assert.Null(bare.User);
            Assert.Null(bare.Passwd);
            Assert.Null(bare.Query);
        }
    }

    /// <summary>
    /// The payload structures of a session description carry their strings in
    /// fields and, for the origin, in nothing else: the gir declares no method
    /// on <see cref="SDPOrigin"/> at all.
    /// </summary>
    [Fact]
    public void ASessionDescriptionReadsItsStringsOutOfItsPayloadStructures()
    {
        const string Description =
            "v=0\r\n" +
            "o=alice 3735928559 2 IN IP4 127.0.0.1\r\n" +
            "s=GstSharp string fields\r\n" +
            "c=IN IP4 127.0.0.1\r\n" +
            "t=0 0\r\n" +
            "m=audio 49170 RTP/AVP 0\r\n" +
            "a=rtpmap:0 PCMU/8000\r\n";

        Assert.Equal(SDPResult.Ok, SDPMessage.NewFromText(Description, out SDPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal("0", message.Version);
            Assert.Equal("GstSharp string fields", message.SessionName);
            Assert.Null(message.Uri);

            SDPOrigin origin = message.GetOrigin();
            Assert.Equal("alice", origin.Username);
            Assert.Equal("3735928559", origin.SessId);
            Assert.Equal("IN", origin.Nettype);
            Assert.Equal("IP4", origin.Addrtype);
            Assert.Equal("127.0.0.1", origin.Addr);

            SDPMedia media = message.GetMedia(0);
            Assert.Equal("audio", media.Media);
            Assert.Equal("RTP/AVP", media.Proto);

            SDPAttribute attribute = media.GetAttribute(0);
            Assert.Equal("rtpmap", attribute.Key);
            Assert.Equal("0 PCMU/8000", attribute.Value);
        }
    }
}
