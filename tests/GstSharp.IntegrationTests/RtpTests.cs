using System.Text;
using Gst;
using Gst.Rtp;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstRtp</c> module: the two mapped helper structures over a
/// <see cref="Gst.Buffer"/>, the packet cursor of a compound RTCP buffer, the
/// source meta and the header extension factory.
/// </summary>
/// <remarks>
/// <para>
/// Every mapped structure below is a local variable that is unmapped once and
/// never copied, which is the contract <c>docs/ownership.md</c> states: a
/// <see cref="RTCPPacket"/> borrows the address of the
/// <see cref="RTCPBuffer"/> it was taken from, and the buffer wrapper stays
/// alive until after the unmap because the library keeps a bare pointer to it
/// rather than a reference.
/// </para>
/// <para>
/// Nothing here needs a version gate: every member of the module shipped
/// before 1.24, which is the floor of the binding.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtpTests
{
    /// <summary>The payload of the RTP packet the facts below build.</summary>
    private const uint PayloadLength = 16;

    /// <summary>The MTU of the RTCP buffer the facts below build.</summary>
    private const uint Mtu = 1400;

    /// <summary>The name of the APP packet, four ASCII bytes exactly.</summary>
    private const string AppName = "TEST";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtpTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A freshly allocated RTP buffer maps for writing, carries every header
    /// field back that was written into it, and hands its payload out as a
    /// sub buffer.
    /// </summary>
    [Fact]
    public void RtpBufferRoundTripsItsHeaderAndPayload()
    {
        using Gst.Buffer buffer = RTPBuffer.NewAllocate(PayloadLength, 0, 0);

        Assert.True(RTPBuffer.MapBuffer(buffer, MapFlags.Read | MapFlags.Write, out RTPBuffer rtp));

        rtp.SetSeq(0x1234);
        rtp.SetTimestamp(0xDEADBEEF);
        rtp.SetSsrc(0x0BADF00D);
        rtp.SetPayloadType((byte)RTPPayload.Fs1016);
        rtp.SetMarker(true);

        Assert.Equal(0x1234, rtp.GetSeq());
        Assert.Equal(0xDEADBEEFu, rtp.GetTimestamp());
        Assert.Equal(0x0BADF00Du, rtp.GetSsrc());
        Assert.Equal(1, rtp.GetPayloadType());
        Assert.True(rtp.GetMarker());
        Assert.Equal(PayloadLength, rtp.GetPayloadLen());

        ReadOnlySpan<byte> extension = [0x01, 0x02, 0x03, 0x04];
        Assert.True(rtp.AddExtensionOnebyteHeader(5, extension));
        Assert.True(rtp.GetExtensionOnebyteHeader(5, 0, out byte[]? read));
        Assert.Equal(extension.ToArray(), read);

        using (Gst.Buffer? whole = rtp.GetPayloadSubbuffer(0, uint.MaxValue))
        {
            Assert.NotNull(whole);
            Assert.Equal(PayloadLength, (uint)whole.GetSize());
        }

        // The offset is past the end of the payload, so the library answers
        // NULL after a deliberate g_warning (gstrtpbuffer.c:1181). It is a
        // warning and not a critical, and the test expects it.
        Assert.Null(rtp.GetPayloadSubbuffer(PayloadLength + 1, uint.MaxValue));

        rtp.Unmap();
    }

    /// <summary>
    /// A compound RTCP buffer built through the packet cursor reads back as the
    /// four packets it was written as, with the hand written name, data and FCI
    /// members answering what was put in.
    /// </summary>
    [Fact]
    public void RtcpBufferRoundTripsACompoundPacket()
    {
        using Gst.Buffer buffer = RTCPBuffer.New(Mtu);

        Assert.True(RTCPBuffer.MapBuffer(buffer, MapFlags.Read | MapFlags.Write, out RTCPBuffer rtcp));

        Assert.True(rtcp.AddPacket(RTCPType.Sr, out RTCPPacket sr));
        sr.SrSetSenderInfo(0x0BADF00D, 0x1122334455667788, 0x99AABBCC, 42, 4200);

        Assert.True(rtcp.AddPacket(RTCPType.Sdes, out RTCPPacket sdes));
        Assert.True(sdes.SdesAddItem(0x0BADF00D));
        Assert.True(sdes.SdesAddEntry(RTCPSDESType.Cname, "gstsharp@test"u8));

        Assert.True(rtcp.AddPacket(RTCPType.App, out RTCPPacket app));

        // The data pointer of an APP packet is NULL until its length is set,
        // and the empty span is what this binding answers for it.
        Assert.True(app.AppGetData().IsEmpty);

        app.AppSetName(AppName);
        Assert.True(app.AppSetDataLength(1));

        Span<byte> appData = app.AppGetData();
        Assert.Equal(4, appData.Length);
        appData[0] = 0x0A;
        appData[1] = 0x0B;
        appData[2] = 0x0C;
        appData[3] = 0x0D;

        Assert.True(rtcp.AddPacket(RTCPType.Rtpfb, out RTCPPacket fb));

        // The same emptiness on the feedback side, before a length is set.
        Assert.True(fb.FbGetFci().IsEmpty);

        fb.FbSetType(RTCPFBType.RtpfbTypeNack);
        fb.FbSetSenderSsrc(0x0BADF00D);
        Assert.True(fb.FbSetFciLength(1));
        Assert.Equal(4, fb.FbGetFci().Length);

        Assert.True(rtcp.Unmap());

        Assert.True(RTCPBuffer.MapBuffer(buffer, MapFlags.Read, out RTCPBuffer reread));

        Assert.True(reread.GetFirstPacket(out RTCPPacket packet));
        Assert.Equal(RTCPType.Sr, packet.GetPacketType());

        Assert.True(packet.MoveToNext());
        Assert.Equal(RTCPType.Sdes, packet.GetPacketType());

        Assert.True(packet.MoveToNext());
        Assert.Equal(RTCPType.App, packet.GetPacketType());
        Assert.Equal(AppName, packet.AppGetName());
        Assert.Equal(new byte[] { 0x0A, 0x0B, 0x0C, 0x0D }, packet.AppGetData().ToArray());

        Assert.True(packet.MoveToNext());
        Assert.Equal(RTCPType.Rtpfb, packet.GetPacketType());
        Assert.Equal(4, packet.FbGetFci().Length);

        Assert.False(packet.MoveToNext());

        _output.WriteLine($"packets = {reread.GetPacketCount()}");
        Assert.True(reread.Unmap());
    }

    /// <summary>
    /// The four byte name of an APP packet is exactly four ASCII characters,
    /// because the library copies four bytes and reads no terminator.
    /// </summary>
    [Fact]
    public void AppSetNameRefusesANameThatIsNotFourAsciiCharacters()
    {
        using Gst.Buffer buffer = RTCPBuffer.New(Mtu);

        Assert.True(RTCPBuffer.MapBuffer(buffer, MapFlags.Read | MapFlags.Write, out RTCPBuffer rtcp));
        Assert.True(rtcp.AddPacket(RTCPType.App, out RTCPPacket app));

        Assert.Throws<ArgumentException>(() => app.AppSetName("AB"));
        Assert.Throws<ArgumentException>(() => app.AppSetName("ABCDE"));
        Assert.Throws<ArgumentException>(() => app.AppSetName("ABCé"));

        Assert.True(rtcp.Unmap());
    }

    /// <summary>
    /// The source meta carries an SSRC that may be absent, which is a NULL
    /// pointer in C and <see langword="null"/> here.
    /// </summary>
    [Fact]
    public void SourceMetaCarriesAnOptionalSsrcAndItsCsrcs()
    {
        using Gst.Buffer buffer = Gst.Buffer.New();

        RTPSourceMeta? meta = RTPSourceMeta.Add(buffer, 0x1234, [1, 2]);
        Assert.NotNull(meta);
        Assert.True(meta.SsrcValid);
        Assert.Equal(0x1234u, meta.Ssrc);
        Assert.Equal(2u, meta.CsrcCount);

        // The count the library answers folds the SSRC in, so two contributing
        // sources plus a valid SSRC are three sources.
        Assert.Equal(3u, meta.GetSourceCount());

        Assert.True(meta.AppendCsrc([3]));
        Assert.Equal(3u, meta.CsrcCount);
        Assert.Equal(4u, meta.GetSourceCount());

        RTPSourceMeta? found = RtpGlobal.BufferGetRtpSourceMeta(buffer);
        Assert.NotNull(found);
        Assert.Equal(meta.Ssrc, found.Ssrc);
        Assert.Equal(meta.CsrcCount, found.CsrcCount);

        Assert.True(meta.SetSsrc(null));
        Assert.False(meta.SsrcValid);
    }

    /// <summary>
    /// A source meta added without an SSRC says so, and one asked for more
    /// contributing sources than an RTP header can hold is refused before the
    /// library sees it.
    /// </summary>
    [Fact]
    public void SourceMetaRefusesMoreCsrcsThanTheHeaderHolds()
    {
        using Gst.Buffer buffer = Gst.Buffer.New();

        RTPSourceMeta? meta = RTPSourceMeta.Add(buffer, null, []);
        Assert.NotNull(meta);
        Assert.False(meta.SsrcValid);
        Assert.Equal(0u, meta.CsrcCount);

        using Gst.Buffer other = Gst.Buffer.New();
        // One more than GST_RTP_SOURCE_META_MAX_CSRC_COUNT.
        uint[] tooMany = new uint[16];
        Assert.Throws<ArgumentOutOfRangeException>(() => RTPSourceMeta.Add(other, null, tooMany));
    }

    /// <summary>
    /// The built in header extension of the audio level is created from its
    /// URI, and answers the URI and the SDP caps field name of the id it was
    /// given.
    /// </summary>
    /// <remarks>
    /// <c>gst_rtp_header_extension_get_sdp_caps_field_name</c> answers NULL
    /// with a CRITICAL while the extension has no id, so the id is set first
    /// and the NULL path is left to the documentation.
    /// </remarks>
    [Fact]
    public void HeaderExtensionIsCreatedFromItsUri()
    {
        const string Uri = "urn:ietf:params:rtp-hdrext:ssrc-audio-level";

        RTPHeaderExtension? extension = RTPHeaderExtension.CreateFromUri(Uri);
        if (extension is null)
        {
            _output.WriteLine($"no header extension implements {Uri}");
            return;
        }

        using (extension)
        {
            Assert.Equal(Uri, extension.GetUri());
            Assert.NotEqual(default, extension.GetSupportedFlags());

            extension.SetId(1);
            Assert.Equal(1u, extension.GetId());
            Assert.NotNull(extension.GetSdpCapsFieldName());
        }
    }

    /// <summary>
    /// A payloader is an <see cref="RTPBasePayload"/>, whose properties round
    /// trip, and takes a header extension through the action signal the
    /// generator does not bind.
    /// </summary>
    [RequiresElementFact("rtpL16pay")]
    public void PayloaderRoundTripsItsPropertiesAndTakesAnExtension()
    {
        using Element element = ElementFactory.Make("rtpL16pay", null)
            ?? throw new InvalidOperationException("rtpL16pay could not be created.");

        RTPBasePayload payloader = Assert.IsAssignableFrom<RTPBasePayload>(element);

        payloader.Pt = 96;
        payloader.Ssrc = 0x0BADF00D;
        payloader.Mtu = 1200;

        Assert.Equal(96u, payloader.Pt);
        Assert.Equal(0x0BADF00Du, payloader.Ssrc);
        Assert.Equal(1200u, payloader.Mtu);

        RTPHeaderExtension? extension =
            RTPHeaderExtension.CreateFromUri("urn:ietf:params:rtp-hdrext:ssrc-audio-level");
        if (extension is null)
        {
            _output.WriteLine("no header extension to add");
            return;
        }

        using (extension)
        {
            extension.SetId(1);
            payloader.EmitSignal("add-extension", extension);
        }
    }
}
