using Gst;
using Gst.Audio;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio sink that records the device calls it sees, in order, and
/// whose subject is <c>GstAudioSinkClass.stop</c> — the slot the binding emits
/// as <see cref="AudioSink.OnStopDevice"/>, because its gir name collides with
/// the <c>stop</c> of <c>GstBaseSink</c>.
/// </summary>
/// <remarks>
/// The sink opens no device: <c>OnWrite</c> counts the bytes and answers that
/// it wrote them all. <c>OnOpen</c> can be made to fail, which is the path in
/// which the ring buffer never starts and the stop slot is therefore never
/// reached.
/// </remarks>
internal sealed class StopDeviceAudioSink : AudioSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestStopDeviceAudioSink";

    private static readonly PadTemplate SinkTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        OpenOverride,
        PrepareOverride,
        WriteOverride,
        StopDeviceOverride,
        ResetOverride,
        UnprepareOverride,
        CloseOverride);

    private readonly List<string> _calls = [];

    /// <summary>Creates a managed audio sink whose device calls are recorded.</summary>
    internal StopDeviceAudioSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>
    /// Gets or sets whether <c>OnOpen</c> answers that the device was opened.
    /// </summary>
    internal bool OpenSucceeds { get; set; } = true;

    /// <summary>Gets the device calls the sink saw, in order.</summary>
    internal IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Counts how often one call was recorded.</summary>
    /// <param name="call">The call to count.</param>
    /// <returns>How many times it was seen.</returns>
    internal int CountOf(string call) => Calls.Count(seen => string.Equals(seen, call, StringComparison.Ordinal));

    /// <inheritdoc/>
    protected override bool OnOpen()
    {
        Record("open");

        return OpenSucceeds && ChainUpOpen();
    }

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Record("prepare");
        return ChainUpPrepare(spec);
    }

    /// <inheritdoc/>
    protected override int OnWrite(ReadOnlySpan<byte> data)
    {
        Record("write");
        return data.Length;
    }

    /// <inheritdoc/>
    protected override void OnStopDevice()
    {
        Record("stop");
        ChainUpStopDevice();
    }

    /// <inheritdoc/>
    protected override void OnReset()
    {
        Record("reset");
        ChainUpReset();
    }

    /// <inheritdoc/>
    protected override bool OnUnprepare()
    {
        Record("unprepare");
        return ChainUpUnprepare();
    }

    /// <inheritdoc/>
    protected override bool OnClose()
    {
        Record("close");
        return ChainUpClose();
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp stop device audio sink",
            "Sink/Audio",
            "Records the device calls of an audio sink",
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

    private void Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }
}
