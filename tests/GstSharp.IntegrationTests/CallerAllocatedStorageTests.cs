using System.Diagnostics;
using Gst;
using Gst.App;
using Gst.Audio;
using Gst.Base;
using Gst.Rtsp;
using Gst.Sdp;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members whose out parameter is storage that C expects the caller to
/// provide, measured against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// None of these could be bound at all while the planner had no way of
/// producing a structure of the size the callee writes. Ten of them are
/// generated now, because the record can allocate one of itself; the rest are
/// hand written, because their storage is a scope that has to be released, a
/// span the caller already owns, or a range the C function does not check.
/// </para>
/// <para>
/// Every expectation here is what the C implementation documents. What the
/// tests really measure is ownership: that the storage reaches the caller, that
/// it is released again on the paths where the call filled nothing, and that a
/// mapping ends where its scope does.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class CallerAllocatedStorageTests
{
    private const uint Width = 320;
    private const uint Height = 240;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public CallerAllocatedStorageTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <c>gst_base_src_get_allocator</c> answers with the allocator the base
    /// class settled on and the parameters that go with it. A source that was
    /// never started has neither, and the parameters still have to arrive: they
    /// are storage the binding allocated, so an unwritten one is a zeroed
    /// record and not a missing answer.
    /// </summary>
    [Fact]
    public void ABaseSourceHandsBackItsAllocatorAndItsParameters()
    {
        using Element element = ElementFactory.Make("fakesrc", "source")
            ?? throw new InvalidOperationException("fakesrc is a core element and has to exist.");

        BaseSrc source = Assert.IsAssignableFrom<BaseSrc>(element);

        source.GetAllocator(out Allocator? allocator, out AllocationParams parameters);

        using (parameters)
        {
            _output.WriteLine($"allocator: {allocator?.Name ?? "<none>"}, params: {parameters.Handle:X}");

            // The wrapper owns the record and hands out a live pointer, which
            // is the whole of what "the caller owns it" means here.
            Assert.NotEqual(nint.Zero, parameters.Handle);
            Assert.False(parameters.IsDisposed);
        }

        allocator?.Dispose();
    }

    /// <summary>
    /// <c>gst_buffer_pool_config_get_allocator</c> reads back what
    /// <c>gst_buffer_pool_config_set_allocator</c> wrote. Its allocator is
    /// <c>transfer none</c> and the gir says so, so the wrapper must not drop a
    /// reference it never took — which is what disposing it twice over would
    /// do, and what the pool would notice.
    /// </summary>
    [Fact]
    public void APoolConfigurationRoundTripsItsAllocationParameters()
    {
        using BufferPool pool = BufferPool.New();
        using Structure config = pool.GetConfig();

        Assert.True(BufferPool.ConfigGetAllocator(config, out Allocator? allocator, out AllocationParams? parameters));

        Assert.NotNull(parameters);
        _output.WriteLine($"allocator: {allocator?.Name ?? "<none>"}, params: {parameters!.Handle:X}");

        // A fresh configuration names no allocator, which the gir declares
        // nullable, and the parameters are the zeroed defaults.
        Assert.Null(allocator);
        Assert.NotEqual(nint.Zero, parameters.Handle);

        parameters.Dispose();
        Assert.True(parameters.IsDisposed);

        // The configuration is unharmed by the read, and reading it again
        // answers the same way. Nothing was consumed and nothing was unreffed.
        Assert.True(BufferPool.ConfigGetAllocator(config, out Allocator? again, out AllocationParams? second));
        Assert.Null(again);
        second?.Dispose();
    }

    /// <summary>
    /// <c>gst_query_parse_nth_allocation_param</c> is <c>void</c> and leaves
    /// both of its out parameters untouched when the index is past the end of
    /// the array, so the binding checks the range itself. The in-range read is
    /// what the check is worth nothing without.
    /// </summary>
    [Fact]
    public void AnAllocationQueryReadsItsParametersAndRefusesAnIndexPastTheEnd()
    {
        using Caps caps = Caps.NewEmptySimple("video/x-raw");
        using Query query = Query.NewAllocation(caps, needPool: false);

        Assert.Equal(0u, query.GetNAllocationParams());
        Assert.Throws<ArgumentOutOfRangeException>(() => query.ParseNthAllocationParam(0, out _, out _));

        // The C function refuses an entry that names neither an allocator nor
        // any parameters, so the entry is the default parameters.
        using (AllocationParams defaults = AllocationParams.New())
        {
            query.AddAllocationParam(allocator: null, defaults);
        }

        Assert.Equal(1u, query.GetNAllocationParams());

        query.ParseNthAllocationParam(0, out Allocator? allocator, out AllocationParams parameters);

        using (parameters)
        {
            _output.WriteLine($"allocator: {allocator?.Name ?? "<none>"}, params: {parameters.Handle:X}");
            Assert.Null(allocator);
            Assert.NotEqual(nint.Zero, parameters.Handle);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => query.ParseNthAllocationParam(1, out _, out _));
    }

    /// <summary>
    /// <c>gst_video_info_dma_drm_from_video_info</c> and
    /// <c>gst_video_info_dma_drm_to_video_info</c> are the pair that proves the
    /// boolean shape: the first answers false for a format that has no DRM
    /// fourcc and must not hand back the storage it was given, and the round
    /// trip of a format that has one has to come back where it started.
    /// </summary>
    [Fact]
    public void ADrmVideoInfoRoundTripsAndFreesItsStorageOnFailure()
    {
        using VideoInfo info = VideoInfo.New();
        Assert.True(info.SetFormat(VideoFormat.Nv12, Width, Height));

        // DRM_FORMAT_MOD_LINEAR, the modifier every linear layout carries.
        Assert.True(VideoInfoDmaDrm.FromVideoInfo(info, modifier: 0, out VideoInfoDmaDrm? drm));
        Assert.NotNull(drm);

        using (drm)
        {
            Assert.True(drm!.ToVideoInfo(out VideoInfo? converted));
            Assert.NotNull(converted);

            using (converted)
            {
                using Caps original = info.ToCaps();
                using Caps roundTripped = converted!.ToCaps();

                _output.WriteLine($"{original} -> {roundTripped}");
                Assert.True(converted.IsEqual(info));
            }
        }

        // An encoded format has no DRM fourcc, so the call fills nothing. The
        // out parameter is null rather than a zeroed record, and the storage
        // the binding allocated went back through the boxed free.
        using VideoInfo encoded = VideoInfo.New();
        Assert.True(encoded.SetFormat(VideoFormat.Encoded, Width, Height));

        Assert.False(VideoInfoDmaDrm.FromVideoInfo(encoded, modifier: 0, out VideoInfoDmaDrm? none));
        Assert.Null(none);
    }

    /// <summary>
    /// <c>gst_buffer_extract</c> copies out of a buffer into memory the caller
    /// owns, which the binding spells as a span. A short destination is filled
    /// and says so, and an offset past the end copies nothing.
    /// </summary>
    [Fact]
    public void ABufferExtractsIntoASpanTheCallerOwns()
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

        using Buffer buffer = Buffer.NewAllocate(allocator: null, size: (nuint)payload.Length, @params: null)
            ?? throw new InvalidOperationException("gst_buffer_new_allocate answered nothing.");

        using (Buffer.MapScope mapping = buffer.Map(MapFlags.Write))
        {
            payload.CopyTo(mapping.Span);
        }

        byte[] destination = new byte[payload.Length];
        Assert.Equal((nuint)payload.Length, buffer.Extract(offset: 0, destination));
        Assert.Equal(payload, destination);

        byte[] tail = new byte[4];
        Assert.Equal((nuint)4, buffer.Extract(offset: 4, tail));
        Assert.Equal(new byte[] { 5, 6, 7, 8 }, tail);

        // The buffer holds less than the destination asks for from here on, and
        // the answer is how much really arrived.
        byte[] tooMuch = new byte[16];
        Assert.Equal((nuint)2, buffer.Extract(offset: 6, tooMuch));

        Assert.Equal((nuint)0, buffer.Extract(offset: 64, destination));
    }

    /// <summary>
    /// <c>gst_video_frame_map</c> fills a <c>GstVideoFrame</c> the caller
    /// declares, which the binding declares as the storage of a scope. The
    /// planes have to reach the caller, the frame view has to be usable by the
    /// generated members, and the release has to be safe to ask for twice. What
    /// the library wrote into the frame is read back as well, which is what
    /// measures the mirror against the installed structure.
    /// </summary>
    [RequiresElementFact("videotestsrc")]
    public void AVideoFrameMapsThePlanesOfAFrameAndReleasesThemOnce()
    {
        using Sample sample = PullOne(
            "videotestsrc num-buffers=1 ! " +
            $"video/x-raw,format=I420,width={Width},height={Height} ! appsink name=sink sync=false");

        using Caps caps = sample.GetCaps() ?? throw new InvalidOperationException("The sample carries no caps.");
        using Buffer buffer = sample.GetBuffer() ?? throw new InvalidOperationException("The sample carries no buffer.");
        using VideoInfo info = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("The caps of the sample are not video caps.");

        VideoFrame.MapScope frame = VideoFrame.Map(info, buffer, MapFlags.Read);
        try
        {
            Span<byte> luma = frame.Plane(0);
            Span<byte> blue = frame.Plane(1);
            Span<byte> red = frame.Plane(2);

            _output.WriteLine($"planes: {luma.Length}, {blue.Length}, {red.Length}; flags: {frame.Flags}");

            // I420 at 320x240 is 76800 bytes of luma followed by two quarter
            // sized chroma planes. Every plane starts inside the mapping and
            // reaches at least to the end of its own rows.
            Assert.True(luma.Length >= (int)(Width * Height));
            Assert.True(blue.Length >= (int)(Width * Height / 4));
            Assert.True(red.Length >= (int)(Width * Height / 4));
            Assert.True(blue.Length < luma.Length);
            Assert.True(red.Length < blue.Length);

            Assert.Same(buffer, frame.Buffer);

            // What the library wrote into the three fields the planes do not
            // reach is what pins their offsets in the mirror against the
            // installed GstVideoFrame. gst_video_frame_map_id assigns
            // frame->buffer unconditionally and frame->meta whatever it found,
            // which is the meta this asks the library for; the identifier is
            // the one the call was made with only when there was no meta, so
            // that is the one case it can be checked in.
            (nint rawBuffer, nint rawMeta, int rawId) = frame.RawFields;
            VideoMeta? meta = VideoGlobal.BufferGetVideoMeta(buffer);

            _output.WriteLine($"buffer: {rawBuffer:X}, meta: {rawMeta:X}, id: {rawId}");

            Assert.Equal(buffer.Handle, rawBuffer);
            Assert.Equal(meta?.Handle ?? nint.Zero, rawMeta);

            if (meta is null)
            {
                Assert.Equal(-1, rawId);
            }

            // The view is what the generated members take, and it addresses the
            // storage of the scope rather than a copy of it.
            using VideoInfo mapped = frame.Info;
            Assert.True(mapped.IsEqual(info));
            Assert.NotEqual(nint.Zero, frame.Frame.Handle);

            // A ref struct cannot be captured, so the throwing calls are made
            // here rather than through Assert.Throws.
            bool outOfRange = false;
            try
            {
                _ = frame.Plane(4);
            }
            catch (ArgumentOutOfRangeException)
            {
                outOfRange = true;
            }

            Assert.True(outOfRange, "Plane(4) answered instead of refusing.");
        }
        finally
        {
            frame.Dispose();
        }

        // The release is one way and idempotent, so a scope that was disposed
        // by hand inside a using declaration stays correct.
        frame.Dispose();

        bool disposed = false;
        try
        {
            _ = frame.Flags;
        }
        catch (ObjectDisposedException)
        {
            disposed = true;
        }

        Assert.True(disposed, "The released scope still answered.");
    }

    /// <summary>
    /// <c>gst_audio_buffer_map</c> fills a <c>GstAudioBuffer</c> the caller
    /// declares. An interleaved buffer maps to a single plane holding every
    /// channel, and the mapping takes no reference of the buffer, which is why
    /// the scope holds the wrapper. What the library wrote into the structure
    /// is read back as well, including the two array fields it pointed at the
    /// structure itself.
    /// </summary>
    [RequiresElementFact("audiotestsrc")]
    public void AnAudioBufferMapsAnInterleavedBufferAsOnePlane()
    {
        using Sample sample = PullOne(
            "audiotestsrc num-buffers=1 ! " +
            "audio/x-raw,format=S16LE,channels=2,rate=44100,layout=interleaved ! appsink name=sink sync=false");

        using Caps caps = sample.GetCaps() ?? throw new InvalidOperationException("The sample carries no caps.");
        using Buffer buffer = sample.GetBuffer() ?? throw new InvalidOperationException("The sample carries no buffer.");
        using AudioInfo info = AudioInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("The caps of the sample are not audio caps.");

        AudioBuffer.MapScope audio = AudioBuffer.Map(info, buffer, MapFlags.Read);
        try
        {
            _output.WriteLine($"planes: {audio.NPlanes}, samples: {audio.NSamples}");

            Assert.Equal(1, audio.NPlanes);
            Assert.True(audio.NSamples > 0);

            Span<byte> interleaved = audio.Plane(0);

            // Two channels of signed 16 bit samples: four bytes per frame.
            Assert.Equal((int)audio.NSamples * 4, interleaved.Length);
            Assert.Same(buffer, audio.Buffer);

            // gst_audio_buffer_map assigns buffer->buffer unconditionally and,
            // for eight planes or fewer, points planes at priv_planes_arr,
            // which is the address of a field of the mirror. Both readings
            // measure the mirror against the installed GstAudioBuffer rather
            // than against itself.
            (nint rawBuffer, bool inline) = audio.RawFields;

            _output.WriteLine($"buffer: {rawBuffer:X}, inline planes: {inline}");

            Assert.Equal(buffer.Handle, rawBuffer);
            Assert.True(inline, "The mapping did not point its planes at the inline array of the structure.");

            using AudioInfo mapped = audio.Info;
            Assert.True(mapped.IsEqual(info));

            bool outOfRange = false;
            try
            {
                _ = audio.Plane(1);
            }
            catch (ArgumentOutOfRangeException)
            {
                outOfRange = true;
            }

            Assert.True(outOfRange, "Plane(1) answered for an interleaved buffer.");
        }
        finally
        {
            audio.Dispose();
        }

        audio.Dispose();

        bool disposed = false;
        try
        {
            _ = audio.NPlanes;
        }
        catch (ObjectDisposedException)
        {
            disposed = true;
        }

        Assert.True(disposed, "The released scope still answered.");
    }

    /// <summary>
    /// <c>gst_rtsp_transport_parse</c> fills a transport the caller allocated.
    /// A header that parses hands the transport over, and one that does not is
    /// answered with nothing at all rather than with a half filled record the
    /// caller would have to free.
    /// </summary>
    [Fact]
    public void ATransportHeaderIsParsedIntoATransportTheCallerOwns()
    {
        Assert.Equal(
            RTSPResult.Ok,
            RTSPTransport.Parse("RTP/AVP;unicast;client_port=5000-5001", out RTSPTransport? transport));

        Assert.NotNull(transport);

        string? text = transport!.AsText();
        _output.WriteLine($"parsed: {text}");

        Assert.NotNull(text);
        Assert.Contains("RTP/AVP", text, StringComparison.Ordinal);
        Assert.Equal(RTSPResult.Ok, transport.Free());

        Assert.Equal(RTSPResult.Einval, RTSPTransport.Parse("not a transport", out RTSPTransport? refused));
        Assert.Null(refused);
    }

    /// <summary>
    /// <c>gst_sdp_media_set_media_from_caps</c> is annotated as filling storage
    /// the caller provides, and really requires an initialised media: it frees
    /// the media string that is already there and appends to the format list.
    /// The overlay corrects it onto an in parameter, which is what this
    /// measures — a zeroed media would not survive the call.
    /// </summary>
    [Fact]
    public void CapsAreWrittenIntoAMediaThatWasAlreadyInitialised()
    {
        Assert.Equal(SDPResult.Ok, SDPMedia.New(out SDPMedia? media));
        Assert.NotNull(media);

        using Caps caps = Caps.FromString(
            "application/x-unknown, media=(string)video, payload=(int)96, "
            + "encoding-name=(string)H264, clock-rate=(int)90000")
            ?? throw new InvalidOperationException("The caps description did not parse.");

        Assert.Equal(SDPResult.Ok, SDPMedia.SetMediaFromCaps(media!, caps));

        _output.WriteLine($"media: {media!.GetMedia()}, formats: {media.FormatsLen()}");

        Assert.Equal("video", media.GetMedia());
        Assert.True(media.FormatsLen() > 0);

        // Calling it twice is what the free-then-write of the C function is
        // about: the second call has to replace the first without leaking it.
        Assert.Equal(SDPResult.Ok, SDPMedia.SetMediaFromCaps(media, caps));
        Assert.Equal("video", media.GetMedia());

        Assert.Equal(SDPResult.Ok, media.Free());
    }

    /// <summary>
    /// <c>gst_sdp_media_add_media_from_structure</c> is the 1.28 half of the
    /// same pair, and adds to a media rather than replacing it.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void AStructureIsAddedToAMediaThatWasAlreadyInitialised()
    {
        Assert.Equal(SDPResult.Ok, SDPMedia.New(out SDPMedia? media));
        Assert.NotNull(media);

        using Caps caps = Caps.FromString(
            "application/x-unknown, media=(string)audio, payload=(int)96, "
            + "encoding-name=(string)OPUS, clock-rate=(int)48000")
            ?? throw new InvalidOperationException("The caps description did not parse.");

        using Structure structure = caps.GetStructure(0);

        Assert.Equal(SDPResult.Ok, SDPMedia.AddMediaFromStructure(media!, structure));

        _output.WriteLine($"media: {media!.GetMedia()}, formats: {media.FormatsLen()}");

        Assert.Equal("audio", media.GetMedia());
        Assert.True(media.FormatsLen() > 0);

        Assert.Equal(SDPResult.Ok, media.Free());
    }

    /// <summary>Runs a pipeline until its appsink produced one sample.</summary>
    /// <param name="description">The pipeline description, with an appsink named <c>sink</c>.</param>
    /// <returns>The sample.</returns>
    private static Sample PullOne(string description)
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(description));
        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < RunTimeout)
            {
                if (sink.TryPullSample(ClockTime.FromMilliseconds(100)) is { } sample)
                {
                    return sample;
                }

                if (sink.IsEos())
                {
                    break;
                }
            }

            throw new InvalidOperationException("The pipeline produced no sample.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
