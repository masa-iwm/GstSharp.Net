using Gst;
using Gst.Base;
using Gst.GObject;
using Gst.Video;
using Value = Gst.GObject.Value;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What the field setters of a codec frame and a codec state did, recorded on
/// the streaming thread: a vfunc runs where an assertion cannot, so every
/// observation is written here and read back once the pipeline is done.
/// </summary>
internal sealed class FieldSetterObservations
{
    /// <summary>Gets or sets whether the wrapper of the output buffer was detached by the setter.</summary>
    internal bool OutputWrapperDetached { get; set; }

    /// <summary>Gets or sets whether the field answered the very buffer that was set.</summary>
    internal bool OutputHandleMatched { get; set; }

    /// <summary>Gets or sets whether the buffer in the field was unwritable while the test held a reference of its own.</summary>
    internal bool OutputSharedWhileHeld { get; set; }

    /// <summary>Gets or sets whether replacing the buffer gave the held reference the only reference back.</summary>
    internal bool OutputReleasedByReplacement { get; set; }

    /// <summary>Gets or sets whether clearing the field made the getter answer nothing.</summary>
    internal bool OutputCleared { get; set; }

    /// <summary>Gets or sets whether the input buffer field answered the buffer that was set.</summary>
    internal bool InputHandleMatched { get; set; }

    /// <summary>Gets or sets how many references the replaced input buffer lost.</summary>
    internal int InputReferencesDropped { get; set; }

    /// <summary>Gets or sets the caps the state answered after they were set.</summary>
    internal string? CapsAfterSet { get; set; }

    /// <summary>Gets or sets whether clearing the caps made the getter answer nothing.</summary>
    internal bool CapsCleared { get; set; }

    /// <summary>Gets or sets the allocation caps the state answered after they were set.</summary>
    internal string? AllocationCapsAfterSet { get; set; }

    /// <summary>Gets or sets the caps the allocation query carried.</summary>
    internal string? AllocationQueryCaps { get; set; }

    /// <summary>Gets or sets the caps the source pad carried once the negotiation was done.</summary>
    internal string? NegotiatedSrcCaps { get; set; }
}

/// <summary>
/// A managed video decoder that produces its output by writing the frame's
/// output buffer itself instead of asking the base class to allocate one, and
/// that describes its format by writing the caps and the allocation caps of
/// the output state.
/// </summary>
internal sealed unsafe class ProbeSetterVideoDecoder : VideoDecoder
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeSetterVideoDecoder";

    /// <summary>The framerate the decoder writes into the caps of its output state.</summary>
    internal const int OutputFramerate = 7;

    /// <summary>The framerate the decoder writes into the allocation caps, which nothing else uses.</summary>
    internal const int AllocationFramerate = 13;

    private static readonly PadTemplate SinkTemplate = ProbeCodecTemplates.New(
        "sink",
        PadDirection.Sink,
        "video/x-raw");

    private static readonly PadTemplate SrcTemplate = ProbeCodecTemplates.New(
        "src",
        PadDirection.Src,
        "video/x-raw");

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetFormatOverride,
        DecideAllocationOverride);

    private int _decoded;

    private int _recorded;

    private nuint _outputSize = 32;

    /// <summary>Creates a managed video decoder.</summary>
    internal ProbeSetterVideoDecoder()
        : base(Definition.NewInstance()) => SetPacketized(true);

    /// <summary>Gets how many frames the override decoded.</summary>
    internal int Decoded => Volatile.Read(ref _decoded);

    /// <summary>Gets the size of one output buffer of the decoder.</summary>
    internal nuint OutputSize => _outputSize;

    /// <summary>Gets what the setters did, filled on the streaming thread.</summary>
    internal FieldSetterObservations Observations { get; } = new();

    /// <inheritdoc/>
    protected override bool OnSetFormat(VideoCodecState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        using VideoInfo info = state.GetInfo();

        using VideoCodecState? output = SetOutputState(
            info.Format,
            (uint)info.Width,
            (uint)info.Height,
            state);

        if (output is null)
        {
            return false;
        }

        using VideoInfo outputInfo = output.GetInfo();
        _outputSize = outputInfo.Size;

        // The caps are set, read back, cleared and set again: what the state
        // is negotiated with is the last value, and the clearing is only there
        // to show that the field really is emptied. Negotiating an empty field
        // is left alone; the decoder would fill it in from the info.
        output.SetCaps(WithFramerate(outputInfo, OutputFramerate));
        output.SetCaps(null);
        using (Caps? cleared = output.GetCaps())
        {
            Observations.CapsCleared = cleared is null;
        }

        output.SetCaps(WithFramerate(outputInfo, OutputFramerate));
        using (Caps? caps = output.GetCaps())
        {
            Observations.CapsAfterSet = caps?.ToString();
        }

        output.SetAllocationCaps(WithFramerate(outputInfo, AllocationFramerate));
        using (Caps? allocation = output.GetAllocationCaps())
        {
            Observations.AllocationCapsAfterSet = allocation?.ToString();
        }

        if (!Negotiate())
        {
            return false;
        }

        // The caps of the state are what the negotiation puts on the source
        // pad; reading the pad here is reading it while the element is still
        // running.
        using Pad? src = GetStaticPad("src");

        using (Caps? current = src?.GetCurrentCaps())
        {
            Observations.NegotiatedSrcCaps = current?.ToString();
        }

        return true;
    }

    /// <inheritdoc/>
    protected override bool OnDecideAllocation(Query query)
    {
        ArgumentNullException.ThrowIfNull(query);

        query.ParseAllocation(out Caps? caps, out _);

        using (caps)
        {
            Observations.AllocationQueryCaps ??= caps?.ToString();
        }

        // The default implementation is what sets the pool up; skipping it
        // would leave the decoder without one.
        return ChainUpDecideAllocation(query);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnHandleFrame(VideoCodecFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (Interlocked.Exchange(ref _recorded, 1) == 0)
        {
            RecordSetters(frame);
        }

        Gst.Buffer output = Gst.Buffer.NewAllocate(null, _outputSize, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        frame.SetOutputBuffer(output);

        _ = Interlocked.Increment(ref _decoded);
        return FinishFrame(frame);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe field setter video decoder",
            "Codec/Decoder/Video",
            "Writes the frame and state fields a subclass owns",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }

    /// <summary>Builds the caps of a format with a framerate of its own.</summary>
    private static Caps WithFramerate(VideoInfo info, int framerate)
    {
        Caps caps = info.ToCaps();

        // The wrapper of a structure of a caps is a copy of it — a structure is
        // a boxed value — so what is written into it is not written back. The
        // structure is only there to mint a value of the right type; the caps
        // are written through gst_caps_set_value.
        using Structure structure = caps.GetStructure(0);
        Value value = structure.GetValue("framerate");

        try
        {
            Global.ValueSetFraction(ref value, framerate, 1);
            caps.SetValue("framerate", value);
        }
        finally
        {
            value.Dispose();
        }

        return caps;
    }

    /// <summary>Reads how many references a mini object has.</summary>
    private static int ReferencesOf(nint handle) => ((MiniObjectRaw*)handle)->Refcount;

    /// <summary>
    /// Writes the output buffer twice and the input buffer once, recording
    /// what the frame answered and what the replaced values were left with.
    /// </summary>
    private void RecordSetters(VideoCodecFrame frame)
    {
        Gst.Buffer first = Gst.Buffer.NewAllocate(null, _outputSize, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        nint firstHandle = first.Handle;
        frame.SetOutputBuffer(first);
        Observations.OutputWrapperDetached = first.IsDisposed;

        // The getter hands out a reference of its own, so the buffer now has
        // the frame's reference and this one.
        using Gst.Buffer? held = frame.GetOutputBuffer();
        Observations.OutputHandleMatched = held is not null && held.Handle == firstHandle;
        Observations.OutputSharedWhileHeld = held is not null && !held.IsWritable;

        Gst.Buffer second = Gst.Buffer.NewAllocate(null, _outputSize, null)
            ?? throw new InvalidOperationException("The output buffer could not be allocated.");

        frame.SetOutputBuffer(second);
        Observations.OutputReleasedByReplacement = held is not null && held.IsWritable;

        frame.SetOutputBuffer(null);
        using (Gst.Buffer? cleared = frame.GetOutputBuffer())
        {
            Observations.OutputCleared = cleared is null;
        }

        // The input buffer is replaced, not cleared: the base class copies the
        // metas off it when the frame is finished.
        using Gst.Buffer? previousInput = frame.GetInputBuffer();

        if (previousInput is null)
        {
            return;
        }

        int before = ReferencesOf(previousInput.Handle);

        Gst.Buffer replacement = Gst.Buffer.NewAllocate(null, 16, null)
            ?? throw new InvalidOperationException("The input buffer could not be allocated.");

        nint replacementHandle = replacement.Handle;
        frame.SetInputBuffer(replacement);

        using Gst.Buffer? readInput = frame.GetInputBuffer();
        Observations.InputHandleMatched = readInput is not null && readInput.Handle == replacementHandle;
        Observations.InputReferencesDropped = before - ReferencesOf(previousInput.Handle);
    }
}

/// <summary>
/// A managed parser that hands the buffer it was given on as its output the
/// way <c>gst_aac_parse_pre_push_frame</c> does: the frame's buffer moves into
/// the output buffer and the field it came from is cleared.
/// </summary>
internal sealed class ProbeSetterParse : BaseParse
{
    /// <summary>How many bytes one frame of the probe holds.</summary>
    internal const int FrameSize = 9000;

    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeSetterParse";

    private static readonly PadTemplate SinkTemplate = ProbeTemplates.Any("sink", PadDirection.Sink);

    private static readonly PadTemplate SrcTemplate = ProbeTemplates.Any("src", PadDirection.Src);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        HandleFrameOverride,
        SetSinkCapsOverride,
        PrePushFrameOverride);

    private int _framed;

    private int _movedOut;

    /// <summary>Creates a managed parser.</summary>
    internal ProbeSetterParse()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets how many frames the override finished.</summary>
    internal int Framed => Volatile.Read(ref _framed);

    /// <summary>Gets how often the buffer was moved into the output buffer before the push.</summary>
    internal int MovedOut => Volatile.Read(ref _movedOut);

    /// <summary>Gets whether the frame answered no buffer once the field was cleared.</summary>
    internal bool BufferFieldCleared { get; private set; }

    /// <inheritdoc/>
    protected override bool OnSetSinkCaps(Caps caps)
    {
        ArgumentNullException.ThrowIfNull(caps);

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
            return FlowReturn.Error;
        }

        if (input.GetSize() < (nuint)FrameSize)
        {
            return FlowReturn.Ok;
        }

        _ = Interlocked.Increment(ref _framed);
        return FinishFrame(frame, FrameSize);
    }

    /// <inheritdoc/>
    protected override FlowReturn OnPrePushFrame(BaseParseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Gst.Buffer? buffer = frame.GetBuffer();

        if (buffer is null)
        {
            return FlowReturn.Error;
        }

        // The reference the getter minted is handed to the output buffer, and
        // clearing the field releases the one the frame held: the buffer ends
        // up owned once, by the field the base class pushes from.
        frame.SetOutBuffer(buffer);
        frame.SetBuffer(null);

        using (Gst.Buffer? cleared = frame.GetBuffer())
        {
            BufferFieldCleared = cleared is null;
        }

        // The base class only sets the clip flag itself when no subclass takes
        // this slot, so the parser has to ask for the clipping here.
        frame.AddFlags(BaseParseFrameFlags.Clip);

        _ = Interlocked.Increment(ref _movedOut);
        return FlowReturn.Ok;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe field setter parser",
            "Codec/Parser",
            "Moves the frame buffer into the output buffer before the push",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
        config.AddPadTemplate(SrcTemplate);
    }
}
