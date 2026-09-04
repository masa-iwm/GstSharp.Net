using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Video;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed video filter that works in place: it reads the frame it is given
/// and counts the plane the video base class mapped for it.
/// </summary>
/// <remarks>
/// The frame wrapper only holds the address of a <c>GstVideoFrame</c> the base
/// class mapped on its own stack, so nothing is read out of it after the call
/// returns. <c>set_info</c> is not part of the surface - it lends a boxed video
/// info, which has no borrow mode - but the base class fills the info of the
/// filter without it.
/// </remarks>
internal sealed class ProbeVideoFilter : VideoFilter
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeVideoFilter";

    private static readonly PadTemplate SinkTemplate = NewTemplate("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        TransformFrameIpOverride);

    private readonly object _lifecycleLock = new();

    private int _transformed;

    private VideoFrameFlags _flags = (VideoFrameFlags)(-1);

    /// <summary>Creates a managed video filter.</summary>
    internal ProbeVideoFilter()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override transformed.</summary>
    internal int Transformed => Volatile.Read(ref _transformed);

    /// <summary>Gets the flags of the last frame the override saw.</summary>
    internal VideoFrameFlags FrameFlags
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _flags;
            }
        }
    }

    /// <inheritdoc/>
    protected override FlowReturn OnTransformFrameIp(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _ = Interlocked.Increment(ref _transformed);
        lock (_lifecycleLock)
        {
            // Read while the call runs: the wrapper holds the address of a
            // GstVideoFrame the base class mapped on its own stack.
            _flags = frame.Flags;
        }

        return ChainUpTransformFrameIp(frame);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe video filter",
            "Filter/Effect/Video",
            "Reads every frame in place",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction)
    {
        using Caps caps = Caps.FromString("video/x-raw, format=(string){ I420, GRAY8 }")
            ?? throw new InvalidOperationException("The filter caps could not be parsed.");

        return PadTemplate.New(name, direction, PadPresence.Always, caps)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
