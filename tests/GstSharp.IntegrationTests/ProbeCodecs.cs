using Gst;
using Gst.Audio;
using Gst.GObject;
using Gst.Video;

namespace GstSharp.IntegrationTests;

/// <summary>What a <c>pre_push</c> override does with the buffer it is lent.</summary>
internal enum PrePushBehaviour
{
    /// <summary>Hand the very buffer back, which is what a real encoder does.</summary>
    Unchanged,

    /// <summary>Answer a buffer of its own, which releases the lent one.</summary>
    Replace,

    /// <summary>Drop the buffer by answering none.</summary>
    NullOut,

    /// <summary>Throw, so the trap answers for the override.</summary>
    Throw,

    /// <summary>Hand the buffer to the parent slot and answer what it answers.</summary>
    ChainUp,
}

/// <summary>
/// A managed audio encoder: it answers one output buffer per block of samples
/// and counts the drain the base class asks for at the end of the stream.
/// </summary>
internal sealed class ProbeAudioEncoder : AudioEncoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAudioEncoder";

    /// <summary>The caps the encoder claims to produce.</summary>
    internal const string OutputCaps = "audio/x-gstsharp-test";

    private static readonly PadTemplate SinkTemplate = ProbeCodecTemplates.New(
        "sink",
        PadDirection.Sink,
        "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
        + "rate=(int)[1,MAX], channels=(int)[1,MAX]");

    private static readonly PadTemplate SrcTemplate = ProbeCodecTemplates.New(
        "src",
        PadDirection.Src,
        OutputCaps);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetFormatOverride,
        PrePushOverride,
        SinkEventOverride);

    private readonly List<EventType> _events = [];

    private int _encoded;

    private int _drains;

    /// <summary>Creates a managed audio encoder.</summary>
    internal ProbeAudioEncoder()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets or sets what the <c>pre_push</c> override does.</summary>
    internal PrePushBehaviour PrePush { get; set; } = PrePushBehaviour.Unchanged;

    /// <summary>Gets how many buffers the override encoded.</summary>
    internal int Encoded => Volatile.Read(ref _encoded);

    /// <summary>Gets how often the base class asked for a drain with no buffer.</summary>
    internal int Drains => Volatile.Read(ref _drains);

    /// <summary>Gets the sample rate the format the encoder was given carried.</summary>
    internal int Rate { get; private set; }

    /// <summary>Gets the channel count the format the encoder was given carried.</summary>
    internal int Channels { get; private set; }

    /// <summary>Gets the events the sink pad of the encoder saw, oldest first.</summary>
    internal IReadOnlyList<EventType> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnSetFormat(AudioInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        // The info is lent for the length of the call; what has to outlive it
        // is read out here.
        Rate = info.Rate;
        Channels = info.Channels;

        using Caps caps = Caps.FromString(OutputCaps)
            ?? throw new InvalidOperationException("The output caps could not be parsed.");

        return SetOutputFormat(caps);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(Gst.Buffer? buffer)
    {
        if (buffer is null)
        {
            // The drain at the end of the stream: the base class asks for
            // whatever is left with no buffer of its own.
            _ = Interlocked.Increment(ref _drains);
            return FlowReturn.Ok;
        }

        Gst.Buffer output = Gst.Buffer.NewAllocate(null, 4, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        _ = Interlocked.Increment(ref _encoded);

        // -1 consumes every sample the base class is holding for this call.
        return FinishFrame(output, -1);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnPrePush(ref Gst.Buffer? buffer)
    {
        switch (PrePush)
        {
            case PrePushBehaviour.Replace:
                buffer = Gst.Buffer.NewAllocate(null, 8, null);
                break;

            case PrePushBehaviour.NullOut:
                buffer = null;
                break;

            case PrePushBehaviour.Throw:
                throw new InvalidOperationException("The managed encoder refuses this buffer.");

            case PrePushBehaviour.ChainUp:
                return ChainUpPrePush(ref buffer);

            default:
                break;
        }

        return FlowReturn.Ok;
    }

    /// <inheritdoc/>
    protected override bool OnSinkEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_events)
        {
            _events.Add(@event.Type);
        }

        return ChainUpSinkEvent(@event);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe audio encoder",
            "Codec/Encoder/Audio",
            "Answers one buffer per block of samples",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}

/// <summary>
/// A managed audio decoder: it treats the raw audio it is given as the coded
/// stream and hands it on, which is enough to exercise <c>handle_frame</c> and
/// the drain at the end of the stream.
/// </summary>
internal sealed class ProbeAudioDecoder : AudioDecoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAudioDecoder";

    private const string RawCaps =
        "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
        + "rate=(int)[1,MAX], channels=(int)[1,MAX]";

    private static readonly PadTemplate SinkTemplate = ProbeCodecTemplates.New(
        "sink",
        PadDirection.Sink,
        RawCaps);

    private static readonly PadTemplate SrcTemplate = ProbeCodecTemplates.New(
        "src",
        PadDirection.Src,
        RawCaps);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetFormatOverride);

    private int _decoded;

    private int _drains;

    /// <summary>Creates a managed audio decoder.</summary>
    internal ProbeAudioDecoder()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many buffers the override decoded.</summary>
    internal int Decoded => Volatile.Read(ref _decoded);

    /// <summary>Gets how often the base class asked for a drain with no buffer.</summary>
    internal int Drains => Volatile.Read(ref _drains);

    /// <inheritdoc/>
    protected override bool OnSetFormat(Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        using AudioInfo? info = AudioInfo.NewFromCaps(caps);

        return info is not null && SetOutputFormat(info);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(Gst.Buffer? buffer)
    {
        if (buffer is null)
        {
            _ = Interlocked.Increment(ref _drains);
            return FlowReturn.Ok;
        }

        _ = Interlocked.Increment(ref _decoded);
        return FinishFrame(buffer, 1);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe audio decoder",
            "Codec/Decoder/Audio",
            "Hands its input on as if it had decoded it",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}

/// <summary>
/// A managed video encoder: the frame it is handed is adopted by the
/// trampoline and consumed by <c>FinishFrame</c>, which is the whole
/// ownership story of <c>handle_frame</c>.
/// </summary>
internal sealed class ProbeVideoEncoder : VideoEncoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeVideoEncoder";

    /// <summary>The caps the encoder claims to produce.</summary>
    internal const string OutputCaps = "video/x-gstsharp-test";

    private static readonly PadTemplate SinkTemplate = ProbeCodecTemplates.New(
        "sink",
        PadDirection.Sink,
        "video/x-raw");

    private static readonly PadTemplate SrcTemplate = ProbeCodecTemplates.New(
        "src",
        PadDirection.Src,
        OutputCaps);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetFormatOverride);

    private int _encoded;

    private int _released;

    /// <summary>Creates a managed video encoder.</summary>
    internal ProbeVideoEncoder()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override encoded.</summary>
    internal int Encoded => Volatile.Read(ref _encoded);

    /// <summary>Gets how many of those frames the library has freed again.</summary>
    internal int Released => Volatile.Read(ref _released);

    /// <summary>Gets the width the format the encoder was given carried.</summary>
    internal int Width { get; private set; }

    /// <inheritdoc/>
    protected override bool OnSetFormat(VideoCodecState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using VideoInfo info = state.GetInfo();
        Width = info.Width;

        using Caps caps = Caps.FromString(OutputCaps)
            ?? throw new InvalidOperationException("The output caps could not be parsed.");

        using VideoCodecState? output = SetOutputState(caps, state);

        return output is not null && Negotiate();
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(VideoCodecFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        // The frame has no reference count of its own on the surface, so the
        // observable proxy for "it was freed" is the notify of its user data.
        frame.SetUserData(() => Interlocked.Increment(ref _released));

        FlowReturn allocated = AllocateOutputFrame(frame, 8);

        if (allocated != FlowReturn.Ok)
        {
            return allocated;
        }

        _ = Interlocked.Increment(ref _encoded);
        return FinishFrame(frame);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe video encoder",
            "Codec/Encoder/Video",
            "Answers one buffer per frame",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}

/// <summary>
/// A managed video decoder: it is fed raw video and treats it as packetised
/// coded data, so every buffer is one frame the override adopts, fills and
/// finishes.
/// </summary>
internal sealed class ProbeVideoDecoder : VideoDecoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeVideoDecoder";

    private static readonly PadTemplate SinkTemplate = ProbeCodecTemplates.New(
        "sink",
        PadDirection.Sink,
        "video/x-raw");

    private static readonly PadTemplate SrcTemplate = ProbeCodecTemplates.New(
        "src",
        PadDirection.Src,
        "video/x-raw");

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetFormatOverride,
        SinkEventOverride);

    private readonly List<EventType> _events = [];

    private int _decoded;

    private int _released;

    /// <summary>Creates a managed video decoder.</summary>
    internal ProbeVideoDecoder()
        : base(Definition.NewInstance())
    {
        // Without this the base class routes everything through the parse
        // slot, which the probe does not declare, and handle_frame is never
        // reached.
        SetPacketized(true);
    }

    /// <summary>Gets how many frames the override decoded.</summary>
    internal int Decoded => Volatile.Read(ref _decoded);

    /// <summary>Gets how many of those frames the library has freed again.</summary>
    internal int Released => Volatile.Read(ref _released);

    /// <summary>Gets the events the sink pad of the decoder saw, oldest first.</summary>
    internal IReadOnlyList<EventType> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnSetFormat(VideoCodecState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using VideoInfo info = state.GetInfo();

        using VideoCodecState? output = SetOutputState(
            info.Format,
            (uint)info.Width,
            (uint)info.Height,
            state);

        return output is not null && Negotiate();
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(VideoCodecFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        frame.SetUserData(() => Interlocked.Increment(ref _released));

        FlowReturn allocated = AllocateOutputFrame(frame);

        if (allocated != FlowReturn.Ok)
        {
            return allocated;
        }

        _ = Interlocked.Increment(ref _decoded);
        return FinishFrame(frame);
    }

    /// <inheritdoc/>
    protected override bool OnSinkEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_events)
        {
            _events.Add(@event.Type);
        }

        return ChainUpSinkEvent(@event);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe video decoder",
            "Codec/Decoder/Video",
            "Copies every frame it is given through",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}

/// <summary>The pad templates the codec probes are described with.</summary>
internal static class ProbeCodecTemplates
{
    /// <summary>Builds a template from a caps string.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="direction">Which way the pads point.</param>
    /// <param name="caps">The caps the template carries.</param>
    /// <returns>The template, which lives for the process.</returns>
    internal static PadTemplate New(string name, PadDirection direction, string caps)
    {
        using Caps parsed = Caps.FromString(caps)
            ?? throw new InvalidOperationException($"The caps of the {name} template could not be parsed.");

        return PadTemplate.New(name, direction, PadPresence.Always, parsed)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}

/// <summary>
/// A managed sink that accepts anything and counts what it is given, so that a
/// codec test can say how much reached the end of its pipeline.
/// </summary>
internal sealed class ProbeAnySink : Gst.Base.BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAnySink";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        RenderOverride);

    private int _rendered;

    private long _bytes;

    /// <summary>Creates a managed sink.</summary>
    internal ProbeAnySink()
        : base(Definition.NewInstance()) => SetProperty("sync", false);

    /// <summary>Gets how many buffers reached the sink.</summary>
    internal int Rendered => Volatile.Read(ref _rendered);

    /// <summary>Gets how many bytes those buffers carried.</summary>
    internal long Bytes => Interlocked.Read(ref _bytes);

    /// <inheritdoc/>
    protected override FlowReturn OnRender(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        _ = Interlocked.Increment(ref _rendered);
        _ = Interlocked.Add(ref _bytes, (long)buffer.GetSize());
        return FlowReturn.Ok;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe any sink",
            "Sink/Testing",
            "Counts everything it is given",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }
}
