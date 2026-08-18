using Gst;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members whose whole output is something the C function writes through a
/// pointer the caller provides, measured against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these used to hand the library the address of a local that the
/// member discarded when it returned, so the value the call produced never
/// reached the caller: the alignment of a pool configuration was parsed and
/// dropped, the padding <c>gst_video_info_align</c> raised was dropped, and the
/// mapping <c>gst_video_meta_map</c> performed was dropped. The two array
/// members were worse than that: the library writes four values through a
/// pointer the gir describes as one, so the call wrote past the end of the
/// local it was handed.
/// </para>
/// <para>
/// The tests read the values back, which is the only way of telling a write
/// that reached the caller from one that reached a temporary. Nothing here
/// asserts a value the binding computes; every expectation is what the C
/// implementation documents.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class VideoWriteBackTests
{
    private const uint Width = 320;
    private const uint Height = 240;

    /// <summary>An alignment of 128 bytes, spelled as the mask the C API takes.</summary>
    private const uint StrideAlign128 = 127;

    /// <summary>
    /// What <c>gst_buffer_pool_config_set_video_alignment</c> wrote into a
    /// configuration is what reading it back produces. The getter is the only
    /// way of reading the alignment of a pool configuration, and it answered
    /// <see langword="true"/> without producing anything at all.
    /// </summary>
    [Fact]
    public void APoolConfigurationRoundTripsItsVideoAlignment()
    {
        using BufferPool pool = BufferPool.New();
        using Structure config = pool.GetConfig();

        VideoAlignment written = default;
        written.PaddingTop = 1;
        written.PaddingBottom = 2;
        written.PaddingLeft = 4;
        written.PaddingRight = 8;
        written.StrideAlign[0] = StrideAlign128;

        VideoGlobal.BufferPoolConfigSetVideoAlignment(config, written);

        Assert.True(VideoGlobal.BufferPoolConfigGetVideoAlignment(config, out VideoAlignment read));

        Assert.Equal(1u, read.PaddingTop);
        Assert.Equal(2u, read.PaddingBottom);
        Assert.Equal(4u, read.PaddingLeft);
        Assert.Equal(8u, read.PaddingRight);
        Assert.Equal(StrideAlign128, read.StrideAlign[0]);
    }

    /// <summary>
    /// <c>gst_video_info_align</c> raises the padding on the right until every
    /// stride satisfies the alignment it was asked for, and writes the padding
    /// it settled on back into the alignment it was handed. That write back is
    /// what the negotiation sequence of a pool needs: what comes out of the
    /// align call is what goes into the configuration of the pool.
    /// </summary>
    [Fact]
    public void AligningAVideoInfoWritesThePaddingItRaisedBack()
    {
        using VideoInfo info = VideoInfo.New();
        Assert.True(info.SetFormat(VideoFormat.I420, Width, Height));

        VideoAlignment align = default;
        align.StrideAlign[0] = StrideAlign128;
        align.StrideAlign[1] = StrideAlign128;
        align.StrideAlign[2] = StrideAlign128;

        Assert.True(info.Align(ref align));

        // A 320 wide I420 has a 320 byte luma stride, which is not a multiple
        // of 128, so the call has to widen the frame. The pixels it added are
        // the padding it reports.
        Assert.True(align.PaddingRight > 0);
        Assert.Equal(0u, align.PaddingLeft);
        Assert.Equal(0u, align.PaddingTop);
    }

    /// <summary>
    /// The same call with the plane sizes, which the library writes as four
    /// <c>gsize</c> values through a parameter the gir describes as one. The
    /// three planes of an I420 frame are filled and the fourth is left at zero.
    /// </summary>
    [Fact]
    public void AligningAVideoInfoFillsTheSizeOfEveryPlane()
    {
        using VideoInfo info = VideoInfo.New();
        Assert.True(info.SetFormat(VideoFormat.I420, Width, Height));

        VideoAlignment align = default;
        align.StrideAlign[0] = StrideAlign128;
        align.StrideAlign[1] = StrideAlign128;
        align.StrideAlign[2] = StrideAlign128;

        Assert.True(info.AlignFull(ref align, out VideoInfo.PlaneSizeArray planeSize));

        Assert.True(align.PaddingRight > 0);

        // Luma is one byte per pixel and each chroma plane is a quarter of it,
        // so the first plane is the largest and the two chroma planes agree.
        Assert.True(planeSize[0] >= Width * Height);
        Assert.True(planeSize[1] > 0);
        Assert.Equal(planeSize[1], planeSize[2]);
        Assert.True(planeSize[0] > planeSize[1]);

        // I420 has three planes; the fourth element is the one the shipped
        // member could not have received at all.
        Assert.Equal(0u, (uint)planeSize[3]);
    }

    /// <summary>
    /// <c>gst_video_format_info_component</c> fills the component numbers that
    /// are packed in one plane and terminates the list with <c>-1</c>. It
    /// writes four <c>gint</c> whatever the format is, so the member that took
    /// one <c>out int</c> corrupted twelve bytes of the caller's stack on every
    /// call.
    /// </summary>
    [Fact]
    public void TheComponentsOfAPlanarFormatComeBackWithTheirTerminator()
    {
        VideoFormatInfo info = VideoFormatExtensions.GetInfo(VideoFormat.I420);

        // One component per plane, in order: Y, then U, then V.
        info.Component(0, out VideoFormatInfo.ComponentsArray luma);
        Assert.Equal(0, luma[0]);
        Assert.Equal(-1, luma[1]);
        Assert.Equal(-1, luma[2]);
        Assert.Equal(-1, luma[3]);

        info.Component(1, out VideoFormatInfo.ComponentsArray blue);
        Assert.Equal(1, blue[0]);
        Assert.Equal(-1, blue[1]);

        info.Component(2, out VideoFormatInfo.ComponentsArray red);
        Assert.Equal(2, red[0]);
        Assert.Equal(-1, red[1]);
    }

    /// <summary>
    /// The packed half of the same call: every component of a packed format
    /// lives in plane zero, so three of the four elements carry a component and
    /// only the last one is the terminator.
    /// </summary>
    [Fact]
    public void ThePlaneOfAPackedFormatCarriesEveryComponent()
    {
        VideoFormatInfo info = VideoFormatExtensions.GetInfo(VideoFormat.Rgb);

        info.Component(0, out VideoFormatInfo.ComponentsArray components);

        Assert.Equal(0, components[0]);
        Assert.Equal(1, components[1]);
        Assert.Equal(2, components[2]);
        Assert.Equal(-1, components[3]);
    }

    /// <summary>
    /// <c>gst_video_meta_map</c> fills the <c>GstMapInfo</c> the caller
    /// allocates, exactly as <c>gst_buffer_map</c> does, and the unmap has to be
    /// handed what the map wrote. The member returned a mapping nobody could
    /// see, so the unmap was handed a zeroed structure.
    /// </summary>
    [Fact]
    public void MappingAVideoMetaHandsTheMappingBackToTheCaller()
    {
        // An I420 frame of this size, allocated in one block. The meta is what
        // describes the planes inside it.
        using Buffer? buffer = Buffer.NewAllocate(null, Width * Height * 3 / 2, null);
        Assert.NotNull(buffer);

        VideoMeta meta = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            Width,
            Height);

        Assert.True(meta.Map(0, out MapInfo info, out nint data, out int stride, MapFlags.Read));

        // The mapping is the caller's now: without the write back, all four of
        // these are zero and the unmap below unmaps nothing.
        Assert.NotEqual(0, info.Memory);
        Assert.NotEqual(0, info.Data);
        Assert.True(info.Size > 0);
        Assert.NotEqual(0, data);
        Assert.Equal((int)Width, stride);

        Assert.True(meta.Unmap(0, ref info));

        // The second plane maps as well, which is the whole point of mapping
        // through the metadata rather than through the buffer.
        Assert.True(meta.Map(1, out MapInfo chroma, out nint chromaData, out int chromaStride, MapFlags.Read));
        Assert.NotEqual(0, chromaData);
        Assert.Equal((int)Width / 2, chromaStride);
        Assert.True(meta.Unmap(1, ref chroma));
    }
}
