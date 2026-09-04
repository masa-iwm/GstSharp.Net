using Gst;
using Gst.Audio;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio sink: it opens no device at all and counts the bytes the
/// ring buffer hands it, which is the whole contract of
/// <c>GstAudioSinkClass.write</c>.
/// </summary>
/// <remarks>
/// <c>write</c> is a required slot: the thread of the ring buffer stops before
/// it starts when the class leaves it NULL, so the registration refuses a
/// descriptor without it. Every other slot of the class has a documented
/// answer for a NULL parent, which is what the chain-ups of the overrides
/// below reach.
/// </remarks>
internal sealed class ProbeAudioSink : AudioSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAudioSink";

    private static readonly PadTemplate SinkTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        OpenOverride,
        PrepareOverride,
        WriteOverride,
        UnprepareOverride,
        CloseOverride);

    private readonly List<string> _lifecycle = [];

    private long _written;

    /// <summary>Creates a managed audio sink.</summary>
    internal ProbeAudioSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many bytes the ring buffer wrote through the override.</summary>
    internal long Written => Interlocked.Read(ref _written);

    /// <summary>Gets the calls the device side of the sink saw, in order.</summary>
    internal IReadOnlyList<string> Lifecycle
    {
        get
        {
            lock (_lifecycle)
            {
                return _lifecycle.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnOpen()
    {
        Record("open");
        return ChainUpOpen();
    }

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        // The specification is read out of the wrapper while the call runs:
        // it points at the ring buffer of the element and not at anything the
        // wrapper owns.
        using AudioInfo info = spec.GetInfo();

        Record(FormattableString.Invariant($"prepare rate={info.Rate} segsize={spec.Segsize}"));
        return ChainUpPrepare(spec);
    }

    /// <inheritdoc/>
    protected override int OnWrite(ReadOnlySpan<byte> data)
    {
        _ = Interlocked.Add(ref _written, data.Length);
        return data.Length;
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
            "GstSharp probe audio sink",
            "Sink/Audio",
            "Counts the bytes the ring buffer writes",
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
        lock (_lifecycle)
        {
            _lifecycle.Add(call);
        }
    }
}
