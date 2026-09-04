// A GStreamer audio sink written in C#: the ring buffer of GstAudioSink hands
// its write slot a raw pointer and a byte count, which the binding projects
// onto a span. The sample counts the bytes so that the smoke test can say the
// samples really reached managed code.
using Gst;
using Gst.Audio;
using Gst.GObject;

/// <summary>
/// A managed audio sink that opens no device and counts what it is written.
/// </summary>
internal sealed class ManagedAudioSink : AudioSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedAudioSink";

    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        OpenOverride,
        PrepareOverride,
        WriteOverride,
        UnprepareOverride,
        CloseOverride);

    private long _written;

    /// <summary>Creates a managed audio sink.</summary>
    internal ManagedAudioSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the sink is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how many bytes the ring buffer wrote through the span.</summary>
    internal long Written => Interlocked.Read(ref _written);

    /// <inheritdoc/>
    protected override bool OnOpen() => ChainUpOpen();

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return ChainUpPrepare(spec);
    }

    /// <inheritdoc/>
    protected override int OnWrite(ReadOnlySpan<byte> data)
    {
        _ = Interlocked.Add(ref _written, data.Length);

        // A write is allowed to block until the block has been played, which
        // is what keeps a real device from being asked for the next one at
        // memory speed. This one has no device to wait for, so it waits for a
        // millisecond instead of spinning for as long as the pipeline runs.
        Thread.Sleep(1);
        return data.Length;
    }

    /// <inheritdoc/>
    protected override bool OnUnprepare() => ChainUpUnprepare();

    /// <inheritdoc/>
    protected override bool OnClose() => ChainUpClose();

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed audio sink",
            "Sink/Audio",
            "Counts the samples the ring buffer writes, in C#",
            "GstSharp.Net");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)[1,MAX], channels=(int)[1,MAX]")
            ?? throw new InvalidOperationException("The sink caps could not be parsed.");

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
