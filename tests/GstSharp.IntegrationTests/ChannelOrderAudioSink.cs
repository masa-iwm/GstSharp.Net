using Gst;
using Gst.Audio;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio sink that sets the channel order of its ring buffer from
/// <see cref="OnPrepare"/>, which is where <c>alsasink</c> sets its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_audio_ring_buffer_acquire</c> holds the object lock across the
/// acquire vfunc, and that vfunc is what calls <c>prepare</c>, so everything
/// this sink does in <see cref="OnPrepare"/> runs with the lock held. A
/// wrapper that took the lock to read <c>acquired</c> would hang here, which
/// is the whole point of the sink.
/// </para>
/// <para>
/// The ring buffer is handed to the sink by the test rather than read off the
/// sink: <c>GstAudioBaseSink.ringbuffer</c> is a public C field with no
/// accessor in the binding, and the ring buffer the test acquires is the one
/// it created itself.
/// </para>
/// </remarks>
internal sealed class ChannelOrderAudioSink : AudioSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestChannelOrderAudioSink";

    private static readonly PadTemplate SinkTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        OpenOverride,
        PrepareOverride,
        WriteOverride,
        UnprepareOverride,
        CloseOverride);

    /// <summary>Creates the sink.</summary>
    internal ChannelOrderAudioSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>
    /// Gets or sets the ring buffer whose channel order <see cref="OnPrepare"/>
    /// sets.
    /// </summary>
    internal AudioRingBuffer? OrderTarget { get; set; }

    /// <summary>Gets whether the channel order was set without throwing.</summary>
    internal bool OrderWasSet { get; private set; }

    /// <summary>Gets what setting the channel order threw, if anything.</summary>
    internal Exception? OrderFailure { get; private set; }

    /// <inheritdoc/>
    protected override bool OnOpen() => ChainUpOpen();

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!ChainUpPrepare(spec))
        {
            return false;
        }

        if (OrderTarget is AudioRingBuffer target)
        {
            // An exception must not unwind into the library, and the failure
            // is what the test asserts on anyway.
            try
            {
                target.SetChannelPositions(
                    [AudioChannelPosition.FrontRight, AudioChannelPosition.FrontLeft]);
                OrderWasSet = true;
            }
            catch (Exception failure)
            {
                OrderFailure = failure;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    protected override int OnWrite(ReadOnlySpan<byte> data) => data.Length;

    /// <inheritdoc/>
    protected override bool OnUnprepare() => ChainUpUnprepare();

    /// <inheritdoc/>
    protected override bool OnClose() => ChainUpClose();

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp channel order audio sink",
            "Sink/Audio",
            "Sets the channel order of its ring buffer while it is acquired",
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
