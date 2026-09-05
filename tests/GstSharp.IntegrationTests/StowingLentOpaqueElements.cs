using Gst;
using Gst.Audio;
using Gst.GObject;
using Gst.Video;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed video filter that files the frame it is lent away, which is the
/// mistake the detach turns into an exception instead of a read of an address
/// that is gone.
/// </summary>
internal sealed class StowingVideoFilter : VideoFilter
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestStowingVideoFilter";

    private static readonly PadTemplate SinkTemplate = NewTemplate("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        TransformFrameIpOverride);

    private int _transformed;

    /// <summary>Creates a managed video filter.</summary>
    internal StowingVideoFilter()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override transformed.</summary>
    internal int Transformed => Volatile.Read(ref _transformed);

    /// <summary>Gets the wrapper the override filed away.</summary>
    internal VideoFrame? Kept { get; private set; }

    /// <summary>Gets the flags the override read while the call ran.</summary>
    internal VideoFrameFlags FlagsWhileRunning { get; private set; } = (VideoFrameFlags)(-1);

    /// <inheritdoc/>
    protected override FlowReturn OnTransformFrameIp(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _ = Interlocked.Increment(ref _transformed);

        // Read while the call runs, which is the only time the pointer means
        // anything, and then kept, which is what the detach answers for.
        FlagsWhileRunning = frame.Flags;
        Kept = frame;

        return ChainUpTransformFrameIp(frame);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp stowing video filter",
            "Filter/Effect/Video",
            "Files the frame it is lent away",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        using Caps caps = Caps.FromString("video/x-raw, format=(string){ I420, GRAY8 }")
            ?? throw new InvalidOperationException("The filter caps could not be parsed.");

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}

/// <summary>
/// A managed audio sink that files the ring buffer specification it is lent
/// away. The specification is a field of the ring buffer the sink is being
/// prepared for, and the wrapper is detached when <c>prepare</c> returns.
/// </summary>
internal sealed class StowingAudioSink : AudioSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestStowingAudioSink";

    private static readonly PadTemplate SinkTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        PrepareOverride,
        UnprepareOverride,
        WriteOverride);

    /// <summary>Creates a managed audio sink.</summary>
    internal StowingAudioSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the wrapper the override filed away.</summary>
    internal AudioRingBufferSpec? Kept { get; private set; }

    /// <summary>Gets the segment size the override read while the call ran.</summary>
    internal int SegsizeWhileRunning { get; private set; }

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        SegsizeWhileRunning = spec.Segsize;
        Kept = spec;

        return ChainUpPrepare(spec);
    }

    /// <inheritdoc/>
    protected override bool OnUnprepare() => ChainUpUnprepare();

    /// <inheritdoc/>
    protected override int OnWrite(ReadOnlySpan<byte> data) => data.Length;

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp stowing audio sink",
            "Sink/Audio",
            "Files the ring buffer specification it is lent away",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewTemplate()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)[1,MAX], channels=(int)[1,MAX]")
            ?? throw new InvalidOperationException("The sink caps could not be parsed.");

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
