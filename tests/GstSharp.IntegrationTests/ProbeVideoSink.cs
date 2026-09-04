using Gst;
using Gst.GObject;
using Gst.Video;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed video sink: it counts the frames <c>show_frame</c> hands it and
/// remembers how large the last one was.
/// </summary>
/// <remarks>
/// The buffer is lent for the duration of the call. Chaining up reaches the
/// implementation below the override, which is NULL for a direct subclass of
/// <c>GstVideoSink</c> - the base class renders the frame itself in that case,
/// so the chain-up answers <see cref="FlowReturn.Ok"/>.
/// </remarks>
internal sealed class ProbeVideoSink : VideoSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeVideoSink";

    private static readonly PadTemplate SinkTemplate = NewTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        ShowFrameOverride);

    private int _shown;

    private long _bytes;

    /// <summary>Creates a managed video sink.</summary>
    internal ProbeVideoSink()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override was shown.</summary>
    internal int Shown => Volatile.Read(ref _shown);

    /// <summary>Gets how many bytes those frames carried.</summary>
    internal long Bytes => Interlocked.Read(ref _bytes);

    /// <inheritdoc/>
    protected override FlowReturn OnShowFrame(Gst.Buffer buf)
    {
        ArgumentNullException.ThrowIfNull(buf);

        _ = Interlocked.Increment(ref _shown);
        _ = Interlocked.Add(ref _bytes, (long)buf.GetSize());
        return ChainUpShowFrame(buf);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe video sink",
            "Sink/Video",
            "Counts the frames it is shown",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewTemplate()
    {
        using Caps caps = Caps.FromString("video/x-raw")
            ?? throw new InvalidOperationException("The sink caps could not be parsed.");

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }
}
