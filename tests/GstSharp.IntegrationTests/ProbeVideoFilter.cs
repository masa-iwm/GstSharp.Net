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
/// returns. <c>set_info</c> is declared for its chain-up alone: the slot is
/// empty below the filter, and what the override records is the answer the base
/// class reads an empty one as.
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
        TransformFrameIpOverride,
        SetInfoOverride);

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

    /// <summary>Gets what the chain-up of each lifecycle slot answered.</summary>
    internal ChainUpRecord ChainUpAnswers { get; } = new();

    /// <summary>Chains the <c>set_info</c> slot up from outside a slot call.</summary>
    /// <param name="incaps">The caps of the sink pad.</param>
    /// <param name="inInfo">The info the base class read out of them.</param>
    /// <param name="outcaps">The caps of the src pad.</param>
    /// <param name="outInfo">The info the base class read out of those.</param>
    /// <returns>What the class below the override answers.</returns>
    internal bool ChainUpSetInfoForTest(Caps incaps, VideoInfo inInfo, Caps outcaps, VideoInfo outInfo) =>
        ChainUpSetInfo(incaps, inInfo, outcaps, outInfo);

    /// <inheritdoc/>
    protected override bool OnSetInfo(Caps incaps, VideoInfo inInfo, Caps outcaps, VideoInfo outInfo)
    {
        // GstVideoFilter leaves the slot empty and stores both infos on a true
        // answer; what this records is the answer of the empty one.
        return ChainUpAnswers.Record("set_info", ChainUpSetInfo(incaps, inInfo, outcaps, outInfo));
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
