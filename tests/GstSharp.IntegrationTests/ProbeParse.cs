using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed parser: it cuts whatever reaches it into fixed size frames,
/// writes the frame it produced into the borrowed <c>GstBaseParseFrame</c> and
/// finishes it, which is the whole contract of <c>handle_frame</c>.
/// </summary>
internal sealed class ProbeParse : BaseParse
{
    /// <summary>How many bytes one frame of the probe holds.</summary>
    internal const int FrameSize = 9000;

    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeParse";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetSinkCapsOverride,
        StartOverride,
        StopOverride,
        SinkEventOverride);

    private readonly List<EventType> _events = [];

    private int _framed;

    private int _fedBack;

    /// <summary>Creates a managed parser.</summary>
    internal ProbeParse()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override finished.</summary>
    internal int Framed => Volatile.Read(ref _framed);

    /// <summary>
    /// Gets how often the override asked for more data instead of finishing a
    /// frame, which is the branch that writes a skip size of zero.
    /// </summary>
    internal int FedBack => Volatile.Read(ref _fedBack);

    /// <summary>Gets whether every call was handed a usable frame.</summary>
    internal bool EveryFrameCarriedABuffer { get; private set; } = true;

    /// <summary>Gets what the chain-up of each lifecycle slot answered.</summary>
    internal ChainUpRecord ChainUpAnswers { get; } = new();

    /// <summary>Chains the <c>start</c> slot up from outside a slot call.</summary>
    /// <returns>What the class below the override answers.</returns>
    internal bool ChainUpStartForTest() => ChainUpStart();

    /// <summary>Chains the <c>stop</c> slot up from outside a slot call.</summary>
    /// <returns>What the class below the override answers.</returns>
    internal bool ChainUpStopForTest() => ChainUpStop();

    /// <summary>Chains the <c>set_sink_caps</c> slot up from outside a slot call.</summary>
    /// <param name="caps">The caps the answer is about.</param>
    /// <returns>What the class below the override answers.</returns>
    internal bool ChainUpSetSinkCapsForTest(Caps caps) => ChainUpSetSinkCaps(caps);

    /// <summary>Gets the events the sink pad of the parser saw, oldest first.</summary>
    internal IReadOnlyList<EventType> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override bool OnStart() => ChainUpAnswers.Record("start", ChainUpStart());

    /// <inheritdoc/>
    protected override bool OnStop() => ChainUpAnswers.Record("stop", ChainUpStop());

    /// <inheritdoc/>
    protected override bool OnSetSinkCaps(Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

        // GstBaseParse leaves the slot empty, so this is the value the library
        // reads an empty one as rather than an implementation being extended.
        _ = ChainUpAnswers.Record("set_sink_caps", ChainUpSetSinkCaps(caps));

        // GstBaseParse pushes nothing until the src pad has caps of its own; a
        // parser that does not change the format says so by handing the sink
        // caps on.
        using Pad? src = GetStaticPad("src");

        return src is not null && src.PushEvent(Event.NewCaps(caps));
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(BaseParseFrame frame, out int skipsize)
    {
        ArgumentNullException.ThrowIfNull(frame);

        skipsize = 0;

        using Gst.Buffer? input = frame.GetBuffer();

        if (input is null)
        {
            EveryFrameCarriedABuffer = false;
            return FlowReturn.Error;
        }

        if (input.GetSize() < (nuint)FrameSize)
        {
            // Not enough for a frame yet: leaving the skip size at zero and
            // finishing nothing is how a parser asks for more data.
            _ = Interlocked.Increment(ref _fedBack);
            return FlowReturn.Ok;
        }

        Gst.Buffer output = Gst.Buffer.NewAllocate(null, (nuint)FrameSize, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        _ = output.Memset(0, (byte)(Volatile.Read(ref _framed) + 1), (nuint)FrameSize);

        // Both writers go through the borrowed frame: the flag tells the base
        // class to clip the frame to the segment, the buffer replaces the input
        // as what is pushed.
        frame.AddFlags(BaseParseFrameFlags.Clip);
        frame.SetOutBuffer(output);

        _ = Interlocked.Increment(ref _framed);
        return FinishFrame(frame, FrameSize);
    }

    /// <inheritdoc/>
    protected override bool OnSinkEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        lock (_events)
        {
            _events.Add(@event.Type);
        }

        // The event is adopted by the trampoline; chaining up hands it on, so
        // the stream start, the caps and the segment still reach the base
        // class and the parser keeps working.
        return ChainUpSinkEvent(@event);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe parser",
            "Codec/Parser",
            "Cuts its input into fixed size frames",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}
