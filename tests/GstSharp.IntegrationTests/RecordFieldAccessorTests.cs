using Gst;
using Gst.Audio;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated field accessors of a boxed or an opaque record, measured
/// against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// A mirror with wrong offsets agrees with itself, so none of these tests
/// writes a field and reads it back. Every one of them has a native function on
/// the other side of the field — <c>gst_allocation_params_new</c>,
/// <c>gst_video_info_from_caps</c>, <c>gst_audio_info_from_caps</c> and
/// <c>gst_buffer_add_video_meta</c> — so the bytes the accessors read are bytes
/// the library wrote, and the values are ones that only come out right for one
/// layout.
/// </para>
/// <para>
/// <see cref="Gst.Video.VideoMeta"/> is the case that pins the embedded record
/// support: its fields sit behind a <c>GstMeta</c> header embedded by value, so
/// they are only where the mirror says they are if the header is exactly as
/// large as the C one.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RecordFieldAccessorTests
{
    private const uint Width = 320;

    private const uint Height = 240;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RecordFieldAccessorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <c>gst_allocation_params_new</c> hands out a structure that
    /// <c>gst_allocation_params_init</c> zeroed, so every field of it reads as
    /// the default the C API documents. A field at a wrong offset would read
    /// into the reserved tail or past the end of the structure.
    /// </summary>
    [Fact]
    public void AFreshAllocationParamsReadsAsTheDocumentedDefault()
    {
        using AllocationParams parameters = AllocationParams.New();

        _output.WriteLine(FormattableString.Invariant(
            $"gst_allocation_params_new: flags={parameters.Flags} align={parameters.Align} prefix={parameters.Prefix} padding={parameters.Padding}"));

        Assert.Equal(default, parameters.Flags);
        Assert.Equal(0u, (uint)parameters.Align);
        Assert.Equal(0u, (uint)parameters.Prefix);
        Assert.Equal(0u, (uint)parameters.Padding);
    }

    /// <summary>
    /// <c>gst_video_info_from_caps</c> fills the structure in from the caps, so
    /// the accessors read back what the caps said. The framerate sits 64 bytes
    /// into the structure, behind the embedded <c>GstVideoColorimetry</c>, and
    /// is the field a wrong size for that would move.
    /// </summary>
    [Fact]
    public void VideoInfoReadsWhatTheCapsSaid()
    {
        using Caps caps = Caps.FromString(
            "video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of a raw I420 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused raw I420 caps.");

        _output.WriteLine(FormattableString.Invariant(
            $"gst_video_info_from_caps: {info.Width}x{info.Height} @ {info.FpsN}/{info.FpsD}, size={info.Size} par={info.ParN}/{info.ParD}"));

        Assert.Equal((int)Width, info.Width);
        Assert.Equal((int)Height, info.Height);
        Assert.Equal(30, info.FpsN);
        Assert.Equal(1, info.FpsD);

        // I420 is one byte of luma and half a byte of chroma per pixel.
        Assert.Equal((nuint)(Width * Height * 3 / 2), info.Size);
    }

    /// <summary>
    /// <c>gst_audio_info_from_caps</c> fills the structure in from the caps.
    /// <c>bpf</c> is the bytes per frame the library computes rather than
    /// something the caps state, so it is what says the read reached the
    /// structure the library wrote.
    /// </summary>
    [Fact]
    public void AudioInfoReadsWhatTheCapsSaid()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw,format=S16LE,layout=interleaved,rate=44100,channels=2")
            ?? throw new InvalidOperationException("The caps of interleaved S16LE audio have to parse.");

        using AudioInfo info = AudioInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_audio_info_from_caps refused S16LE caps.");

        _output.WriteLine(FormattableString.Invariant(
            $"gst_audio_info_from_caps: rate={info.Rate} channels={info.Channels} bpf={info.Bpf} layout={info.Layout}"));

        Assert.Equal(44100, info.Rate);
        Assert.Equal(2, info.Channels);
        Assert.Equal(4, info.Bpf);
        Assert.Equal(AudioLayout.Interleaved, info.Layout);
    }

    /// <summary>
    /// <c>gst_buffer_add_video_meta</c> writes the metadata into the buffer,
    /// and the accessors read it back. Every field of a <c>GstVideoMeta</c>
    /// sits behind the <c>GstMeta</c> header it embeds by value, so this is
    /// what says that the embedded mirror is the size of the C header.
    /// </summary>
    [Fact]
    public void AVideoMetaReadsBackThroughTheEmbeddedMetaHeader()
    {
        using Buffer buffer = AbiProbeTests.NewBuffer();

        VideoMeta? added = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            Width,
            Height);

        // The return is nullable since the overlay of item E3-13; this buffer
        // is exclusively held, so the adder answers an item.
        Assert.NotNull(added);

        _output.WriteLine(FormattableString.Invariant(
            $"gst_buffer_add_video_meta: {added.Width}x{added.Height} format={added.Format} planes={added.NPlanes} id={added.Id}"));

        Assert.Equal(Width, added.Width);
        Assert.Equal(Height, added.Height);
        Assert.Equal(VideoFormat.I420, added.Format);
        Assert.Equal(3u, added.NPlanes);

        // gst_buffer_add_video_meta numbers the first view of a frame zero.
        Assert.Equal(0, added.Id);

        // And the metadata the library hands back for the same buffer is the
        // same item, read through the same offsets.
        VideoMeta found = VideoGlobal.BufferGetVideoMeta(buffer)
            ?? throw new InvalidOperationException("The buffer was just given a video meta.");

        Assert.Equal(Width, found.Width);
        Assert.Equal(Height, found.Height);
    }

    /// <summary>
    /// The fixed size fields of <c>GstVideoInfo</c>, which the library filled
    /// in for the caps. The plane strides and offsets of an I420 frame are
    /// arithmetic on the width and the height, so a storage type at the wrong
    /// offset, or one element too short, reads numbers that are nothing.
    /// </summary>
    [Fact]
    public void VideoInfoAnswersThePlaneOffsetsAndStrides()
    {
        using Caps caps = Caps.FromString(
            "video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of a raw I420 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused raw I420 caps.");

        VideoInfo.StrideArray strides = info.Stride;
        VideoInfo.OffsetArray offsets = info.Offset;

        _output.WriteLine(FormattableString.Invariant(
            $"gst_video_info_from_caps: strides={strides[0]},{strides[1]},{strides[2]} offsets={offsets[0]},{offsets[1]},{offsets[2]}"));

        // I420 carries a full size luma plane and two half size chroma planes.
        Assert.Equal((int)Width, strides[0]);
        Assert.Equal((int)Width / 2, strides[1]);
        Assert.Equal((int)Width / 2, strides[2]);

        Assert.Equal((nuint)0, offsets[0]);
        Assert.Equal((nuint)(Width * Height), offsets[1]);
        Assert.Equal((nuint)(Width * Height * 5 / 4), offsets[2]);
    }

    /// <summary>
    /// The same two fields on <c>GstVideoMeta</c>, which sit behind the
    /// <c>GstMeta</c> header the record embeds by value.
    /// </summary>
    [Fact]
    public void AVideoMetaAnswersThePlaneOffsetsAndStrides()
    {
        using Buffer buffer = AbiProbeTests.NewBuffer();

        VideoMeta added = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            Width,
            Height)
            ?? throw new InvalidOperationException("The buffer is exclusively held, so the meta is added.");

        VideoMeta.StrideArray strides = added.Stride;
        VideoMeta.OffsetArray offsets = added.Offset;

        _output.WriteLine(FormattableString.Invariant(
            $"gst_buffer_add_video_meta: strides={strides[0]},{strides[1]} offsets={offsets[0]},{offsets[1]}"));

        Assert.Equal((int)Width, strides[0]);
        Assert.Equal((int)Width / 2, strides[1]);
        Assert.Equal((nuint)0, offsets[0]);
        Assert.Equal((nuint)(Width * Height), offsets[1]);
    }

    /// <summary>
    /// The channel positions of a <c>GstAudioInfo</c>, which the library
    /// derives from the channel count of the caps.
    /// </summary>
    [Fact]
    public void AudioInfoAnswersItsChannelPositions()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw,format=S16LE,layout=interleaved,rate=44100,channels=2")
            ?? throw new InvalidOperationException("The caps of interleaved S16LE audio have to parse.");

        using AudioInfo info = AudioInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_audio_info_from_caps refused S16LE caps.");

        AudioInfo.PositionArray positions = info.Position;

        _output.WriteLine(FormattableString.Invariant(
            $"gst_audio_info_from_caps: positions={positions[0]},{positions[1]}"));

        Assert.Equal(AudioChannelPosition.FrontLeft, positions[0]);
        Assert.Equal(AudioChannelPosition.FrontRight, positions[1]);
    }

    /// <summary>
    /// The per component tables of a <c>GstVideoFormatInfo</c>, which the
    /// library keeps one of per format. They are read through the borrowed
    /// description a <see cref="Gst.Video.VideoInfo"/> points at.
    /// </summary>
    [Fact]
    public void VideoFormatInfoAnswersItsPerComponentTables()
    {
        using Caps caps = Caps.FromString(
            "video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of a raw I420 frame have to parse.");

        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused raw I420 caps.");

        VideoFormatInfo format = info.FormatInfo;

        VideoFormatInfo.DepthArray depth = format.Depth;
        VideoFormatInfo.PixelStrideArray pixelStride = format.PixelStride;
        VideoFormatInfo.WSubArray wsub = format.WSub;
        VideoFormatInfo.HSubArray hsub = format.HSub;

        _output.WriteLine(FormattableString.Invariant(
            $"I420: components={format.NComponents} depth={depth[0]},{depth[1]} pixel_stride={pixelStride[0]} w_sub={wsub[1]} h_sub={hsub[1]}"));

        Assert.Equal(3u, format.NComponents);
        Assert.Equal(8u, depth[0]);
        Assert.Equal(8u, depth[1]);
        Assert.Equal(1, pixelStride[0]);

        // The chroma planes of I420 are halved in both directions, which the
        // subsampling tables state as one shift.
        Assert.Equal(1u, wsub[1]);
        Assert.Equal(1u, hsub[1]);
        Assert.Equal(0u, wsub[0]);
    }

    /// <summary>
    /// The hand written <c>Format</c> and <c>FormatInfo</c> of the two
    /// <c>*Info</c> records, which read through the <c>finfo</c> pointer the
    /// library assigns in <c>gst_video_info_init</c> and
    /// <c>gst_audio_info_init</c>.
    /// </summary>
    [Fact]
    public void TheInfoRecordsAnswerTheirFormatThroughTheirDescription()
    {
        using Caps videoCaps = Caps.FromString(
            "video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of a raw I420 frame have to parse.");

        using VideoInfo video = VideoInfo.NewFromCaps(videoCaps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused raw I420 caps.");

        Assert.Equal(VideoFormat.I420, video.Format);
        Assert.Equal(VideoFormat.I420, video.FormatInfo.Format);

        // A fresh structure carries the description of the unknown format
        // rather than no description at all, which is why FormatInfo is not
        // nullable: reading through it here answers rather than throws.
        using VideoInfo emptyVideo = VideoInfo.New();
        Assert.Equal(VideoFormat.Unknown, emptyVideo.Format);
        Assert.Equal(VideoFormat.Unknown, emptyVideo.FormatInfo.Format);

        using Caps audioCaps = Caps.FromString(
            "audio/x-raw,format=S16LE,layout=interleaved,rate=44100,channels=2")
            ?? throw new InvalidOperationException("The caps of interleaved S16LE audio have to parse.");

        using AudioInfo audio = AudioInfo.NewFromCaps(audioCaps)
            ?? throw new InvalidOperationException("gst_audio_info_from_caps refused S16LE caps.");

        Assert.Equal(AudioFormat.S16le, audio.Format);
        Assert.Equal(AudioFormat.S16le, audio.FormatInfo.Format);

        using AudioInfo emptyAudio = AudioInfo.New();
        Assert.Equal(AudioFormat.Unknown, emptyAudio.Format);
        Assert.Equal(AudioFormat.Unknown, emptyAudio.FormatInfo.Format);
    }

    /// <summary>
    /// A field accessor of a boxed record reads through the handle of the
    /// wrapper, which a disposed wrapper refuses to hand out. Without that the
    /// read would dereference the null pointer.
    /// </summary>
    [Fact]
    public void ADisposedBoxedWrapperRefusesToReadItsFields()
    {
        AllocationParams parameters = AllocationParams.New();
        parameters.Dispose();

        Assert.Throws<ObjectDisposedException>(() => parameters.Align);
    }
}
