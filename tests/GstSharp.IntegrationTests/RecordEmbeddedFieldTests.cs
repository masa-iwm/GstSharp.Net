using Gst;
using Gst.Rtsp;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated accessors of a record field that embeds another record by
/// value, measured against the library that is installed.
/// </summary>
/// <remarks>
/// An embedded field sits at a fixed offset inside the C structure, so a
/// mirror that is short or wide by one field reads a different structure
/// altogether. Every value here was written by a native function on the other
/// side of the field, and the assertions name values that only come out right
/// for one layout.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RecordEmbeddedFieldTests
{
    /// <summary>
    /// A parsed transport carries four port ranges by value. The accessor of
    /// one copies it out of the structure, so what comes back is the caller's
    /// own and writing into it changes nothing native.
    /// </summary>
    [Fact]
    public void AParsedTransportCopiesOutThePortRangesItHolds()
    {
        Assert.Equal(
            RTSPResult.Ok,
            RTSPTransport.Parse(
                "RTP/AVP;unicast;client_port=5000-5001;server_port=6000-6001",
                out RTSPTransport? transport));
        Assert.NotNull(transport);

        try
        {
            RTSPRange client = transport.ClientPort;
            RTSPRange server = transport.ServerPort;

            Assert.Equal(5000, client.Min);
            Assert.Equal(5001, client.Max);
            Assert.Equal(6000, server.Min);
            Assert.Equal(6001, server.Max);

            // Not requested, so they keep what gst_rtsp_transport_init wrote,
            // which is -1 rather than the zero of the allocation. Reading them
            // is what says the ranges the parser did not fill sit where the
            // mirror puts them.
            Assert.Equal(-1, transport.Port.Min);
            Assert.Equal(-1, transport.Interleaved.Min);

            // The copy is the caller's: writing into it leaves the structure
            // alone, which the next read proves.
            client.Min = 1;
            Assert.Equal(5000, transport.ClientPort.Min);
        }
        finally
        {
            // The wrapper of an opaque record owns nothing, so the storage
            // gst_rtsp_transport_new allocated is released by hand.
            Assert.Equal(RTSPResult.Ok, transport.Free());
        }
    }

    /// <summary>
    /// The colorimetry of a video info is filled from the caps by
    /// <c>gst_video_info_from_caps</c>, and the alignment of a video meta is
    /// zeroed until <c>gst_video_meta_set_alignment</c> writes it.
    /// </summary>
    [Fact]
    public void AVideoInfoAndAVideoMetaCopyOutTheStructuresTheyEmbed()
    {
        using Caps caps = Caps.FromString(
            "video/x-raw,format=I420,width=320,height=240,framerate=30/1,colorimetry=bt709")
            ?? throw new InvalidOperationException("The caps of an I420 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused I420 caps.");

        VideoColorimetry colorimetry = info.Colorimetry;

        Assert.Equal(VideoColorMatrix.Bt709, colorimetry.Matrix);
        Assert.Equal(VideoTransferFunction.Bt709, colorimetry.Transfer);
        Assert.Equal(VideoColorPrimaries.Bt709, colorimetry.Primaries);
        Assert.True(colorimetry.Matches("bt709"));

        using Buffer buffer = AbiProbeTests.NewBuffer();
        VideoMeta meta = VideoGlobal.BufferAddVideoMeta(buffer, VideoFrameFlags.None, VideoFormat.I420, 320, 240)
            ?? throw new InvalidOperationException("gst_buffer_add_video_meta refused an exclusively held buffer.");

        VideoAlignment alignment = meta.Alignment;

        Assert.Equal(0u, alignment.PaddingTop);
        Assert.Equal(0u, alignment.PaddingLeft);
    }

    /// <summary>
    /// A DRM video info embeds a whole <c>GstVideoInfo</c> as its first field.
    /// It is a boxed value, so the accessor hands out a copy the caller owns
    /// rather than a view of the storage the wrapper points at.
    /// </summary>
    [Fact]
    public void ADrmVideoInfoCopiesOutTheVideoInfoItEmbeds()
    {
        using Caps caps = Caps.FromString("video/x-raw,format=NV12,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of an NV12 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused NV12 caps.");

        Assert.True(VideoInfoDmaDrm.FromVideoInfo(info, 0, out VideoInfoDmaDrm? drm));
        Assert.NotNull(drm);

        using (drm)
        {
            using VideoInfo embedded = drm.GetVinfo();

            Assert.NotEqual(drm.Handle, embedded.Handle);
            Assert.Equal(info.Width, embedded.Width);
            Assert.Equal(info.Height, embedded.Height);
            Assert.Equal(VideoFormat.Nv12, embedded.FormatInfo.Format);

            // The copy outlives the structure it was copied out of, which is
            // what makes it the caller's rather than a borrow of the field.
            using VideoInfo kept = drm.GetVinfo();
            drm.Dispose();
            Assert.Equal(info.Width, kept.Width);
        }

        using VideoInfoDmaDrm empty = VideoInfoDmaDrm.New();
        using VideoInfo unknown = empty.GetVinfo();

        Assert.Equal(VideoFormat.Unknown, unknown.FormatInfo.Format);
    }
}
