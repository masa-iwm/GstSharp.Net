using Gst;
using Gst.Audio;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed audio source: it opens no device and fills every block the ring
/// buffer asks for with silence, which is the whole contract of
/// <c>GstAudioSrcClass.read</c>.
/// </summary>
/// <remarks>
/// <c>read</c> is a required slot for the same reason <c>write</c> is on the
/// sink side: the thread of the ring buffer stops before it starts when the
/// class leaves it NULL.
/// </remarks>
internal sealed class ProbeAudioSrc : AudioSrc
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAudioSrc";

    private static readonly PadTemplate SrcTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        OpenOverride,
        PrepareOverride,
        ReadOverride,
        UnprepareOverride,
        CloseOverride);

    private long _read;

    private int _opened;

    private int _segsize;

    /// <summary>Creates a managed audio source.</summary>
    internal ProbeAudioSrc()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many bytes of silence the override produced.</summary>
    internal long Read => Interlocked.Read(ref _read);

    /// <summary>Gets how many times the device was opened.</summary>
    internal int Opened => Volatile.Read(ref _opened);

    /// <summary>Gets the segment size the ring buffer was acquired with.</summary>
    internal int Segsize => Volatile.Read(ref _segsize);

    /// <inheritdoc/>
    protected override bool OnOpen()
    {
        _ = Interlocked.Increment(ref _opened);
        return ChainUpOpen();
    }

    /// <inheritdoc/>
    protected override bool OnPrepare(AudioRingBufferSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        Volatile.Write(ref _segsize, spec.Segsize);
        return ChainUpPrepare(spec);
    }

    /// <inheritdoc/>
    protected override uint OnRead(Span<byte> data, out ClockTime timestamp)
    {
        data.Clear();
        _ = Interlocked.Add(ref _read, data.Length);

        // The device knows no time of its own, which is what the ring buffer
        // reads out of an untouched timestamp.
        timestamp = ClockTime.None;
        return (uint)data.Length;
    }

    /// <inheritdoc/>
    protected override bool OnUnprepare() => ChainUpUnprepare();

    /// <inheritdoc/>
    protected override bool OnClose() => ChainUpClose();

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe audio source",
            "Source/Audio",
            "Produces silence through the ring buffer",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewTemplate()
    {
        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)44100, channels=(int)2")
            ?? throw new InvalidOperationException("The source caps could not be parsed.");

        return PadTemplate.New("src", PadDirection.Src, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The source pad template could not be created.");
    }
}
