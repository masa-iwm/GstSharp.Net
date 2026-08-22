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

        VideoMeta added = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            Width,
            Height);

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
