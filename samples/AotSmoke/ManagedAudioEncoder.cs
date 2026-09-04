// A GStreamer audio encoder written in C#: GstAudioEncoder hands its
// handle_frame slot the samples it has collected and a null buffer for the
// drain at the end of the stream, and its set_format slot lends a GstAudioInfo
// for the length of the call. Both shapes only reach the ahead of time
// compiler through a managed codec.
using Gst;
using Gst.Audio;
using Gst.GObject;

/// <summary>
/// A managed audio encoder that codes nothing and answers one small buffer per
/// block of samples.
/// </summary>
internal sealed class ManagedAudioEncoder : AudioEncoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedAudioEncoder";

    /// <summary>The caps the encoder claims to produce.</summary>
    private const string OutputCaps = "audio/x-gstsharp-aot-smoke";

    private static readonly PadTemplate SinkTemplate = NewTemplate(
        "sink",
        PadDirection.Sink,
        "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
        + "rate=(int)[1,MAX], channels=(int)[1,MAX]");

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src, OutputCaps);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        SetFormatOverride,
        HandleFrameOverride);

    private int _encoded;

    private int _drains;

    /// <summary>Creates a managed audio encoder.</summary>
    internal ManagedAudioEncoder()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the encoder is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how many buffers the encoder answered.</summary>
    internal int Encoded => Volatile.Read(ref _encoded);

    /// <summary>Gets how often the base class asked for a drain with no buffer.</summary>
    internal int Drains => Volatile.Read(ref _drains);

    /// <summary>Gets the sample rate the format it was given carried.</summary>
    internal int Rate { get; private set; }

    /// <inheritdoc/>
    protected override bool OnSetFormat(AudioInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        // The info is lent for the length of the call, so what has to outlive
        // it is read out here rather than kept.
        Rate = info.Rate;

        using Caps caps = Caps.FromString(OutputCaps)
            ?? throw new InvalidOperationException("The output caps could not be parsed.");

        return SetOutputFormat(caps);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(Gst.Buffer? buffer)
    {
        if (buffer is null)
        {
            _ = Interlocked.Increment(ref _drains);
            return FlowReturn.Ok;
        }

        Gst.Buffer output = Gst.Buffer.NewAllocate(null, 4, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        _ = Interlocked.Increment(ref _encoded);

        // -1 consumes every sample the base class is holding for this call.
        return FinishFrame(output, -1);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed audio encoder",
            "Codec/Encoder/Audio",
            "Answers one buffer per block of samples, in C#",
            "GstSharp.Net");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction, string caps)
    {
        using Caps parsed = Caps.FromString(caps)
            ?? throw new InvalidOperationException($"The caps of the {name} template could not be parsed.");

        return PadTemplate.New(name, direction, PadPresence.Always, parsed)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
