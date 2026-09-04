using Gst;
using Gst.Audio;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio filter. It overrides both its own slot, <c>setup</c>,
/// which is lent the negotiated audio info for the length of the call, and the
/// in place transform of <c>GstBaseTransform</c>, which is what an audio
/// filter is for.
/// </summary>
internal sealed class ProbeAudioFilter : AudioFilter
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAudioFilter";

    private static readonly PadTemplate SinkTemplate = NewTemplate("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        SetupOverride,
        BaseTransform.TransformIpOverride);

    private int _transformed;

    private long _bytes;

    /// <summary>Creates a managed audio filter.</summary>
    internal ProbeAudioFilter()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many buffers the override transformed.</summary>
    internal int Transformed => Volatile.Read(ref _transformed);

    /// <summary>Gets how many bytes those buffers carried.</summary>
    internal long Bytes => Interlocked.Read(ref _bytes);

    /// <summary>Gets the sample rate the info lent to <c>setup</c> carried.</summary>
    internal int SetupRate { get; private set; }

    /// <summary>Gets the channel count the info lent to <c>setup</c> carried.</summary>
    internal int SetupChannels { get; private set; }

    /// <summary>
    /// Gets the wrapper of the info <c>setup</c> was lent, kept past the end
    /// of the call on purpose: the borrow is scoped to the call, so the
    /// wrapper is detached by the time a test looks at it.
    /// </summary>
    internal Gst.Audio.AudioInfo? EscapedInfo { get; private set; }

    /// <inheritdoc/>
    protected override bool OnSetup(Gst.Audio.AudioInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        SetupRate = info.Rate;
        SetupChannels = info.Channels;
        EscapedInfo = info;
        return true;
    }

    /// <inheritdoc/>
    protected override FlowReturn OnTransformIp(Gst.Buffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);

        _ = Interlocked.Increment(ref _transformed);
        _ = Interlocked.Add(ref _bytes, (long)buf.GetSize());
        return ChainUpTransformIp(buf);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe audio filter",
            "Filter/Effect/Audio",
            "Counts every buffer in place",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)[1,MAX], channels=(int)[1,MAX]")
            ?? throw new InvalidOperationException("The filter caps could not be parsed.");

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
