using Gst;
using Gst.Audio;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio filter. Its own slot, <c>setup</c>, is not part of the
/// surface - it lends a boxed audio info, which has no borrow mode - so what
/// the class overrides is the in place transform of <c>GstBaseTransform</c>,
/// which is what an audio filter is for.
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
