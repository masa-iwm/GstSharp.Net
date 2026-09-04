// A GStreamer video sink written in C#: GstVideoSink hands its show_frame slot
// the buffer the base sink was going to render, borrowed for the call.
using Gst;
using Gst.GObject;
using Gst.Video;

/// <summary>
/// A managed video sink that counts the frames it is shown.
/// </summary>
internal sealed class ManagedVideoSink : VideoSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedVideoSink";

    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        ShowFrameOverride);

    private int _shown;

    /// <summary>Creates a managed video sink.</summary>
    internal ManagedVideoSink()
        : base(Definition.NewInstance())
    {
        using Value sync = Value.New(GType.Boolean);
        sync.SetBoolean(false);
        SetProperty("sync", sync);
    }

    /// <summary>Gets the type the sink is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how many frames the override was shown.</summary>
    internal int Shown => Volatile.Read(ref _shown);

    /// <inheritdoc/>
    protected override FlowReturn OnShowFrame(Gst.Buffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);

        _ = Interlocked.Increment(ref _shown);
        return ChainUpShowFrame(buf);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "AotSmoke managed video sink",
            "Sink/Video",
            "Counts the frames it is shown, in C#",
            "GstSharp.Net");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.FromString("video/x-raw")
            ?? throw new InvalidOperationException("The sink caps could not be parsed.");

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
