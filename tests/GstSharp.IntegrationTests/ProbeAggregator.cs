using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed aggregator: it pops one buffer from every sink pad that has one
/// and pushes the first of them downstream, and it ends the stream once every
/// pad is at end of stream.
/// </summary>
/// <remarks>
/// <c>aggregate</c> is a required slot — the base class calls it unguarded —
/// so the registration refuses a descriptor that leaves it out. The override
/// is what makes the element bounded: it answers
/// <see cref="FlowReturn.Eos"/> as soon as every sink pad is drained.
/// </remarks>
internal sealed class ProbeAggregator : Aggregator
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestProbeAggregator";

    private static readonly PadTemplate SrcTemplate = NewTemplate(
        "src", PadDirection.Src, PadPresence.Always, new GType(AggregatorPad.GetGType()));

    private static readonly PadTemplate SinkTemplate = NewTemplate(
        "sink_%u", PadDirection.Sink, PadPresence.Request, new GType(AggregatorPad.GetGType()));

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        AggregateOverride);

    private readonly List<AggregatorPad> _pads = [];

    private int _aggregated;

    private int _pushed;

    /// <summary>Creates a managed aggregator.</summary>
    internal ProbeAggregator()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the aggregator is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how many times the <c>aggregate</c> slot ran.</summary>
    internal int Aggregated => Volatile.Read(ref _aggregated);

    /// <summary>Gets how many buffers the override pushed downstream.</summary>
    internal int Pushed => Volatile.Read(ref _pushed);

    /// <summary>Requests a sink pad and remembers it for the override.</summary>
    /// <returns>The pad, which the element owns.</returns>
    internal AggregatorPad RequestSinkPad()
    {
        Pad pad = RequestPadSimple("sink_%u")
            ?? throw new InvalidOperationException("The aggregator refused a sink pad.");

        AggregatorPad sink = pad.As<AggregatorPad>()
            ?? throw new InvalidOperationException("The requested pad is not a GstAggregatorPad.");

        lock (_pads)
        {
            _pads.Add(sink);
        }

        return sink;
    }

    /// <inheritdoc/>
    protected override FlowReturn OnAggregate(bool timeout)
    {
        _ = Interlocked.Increment(ref _aggregated);

        AggregatorPad[] pads;
        lock (_pads)
        {
            pads = _pads.ToArray();
        }

        Gst.Buffer? forward = null;
        bool draining = true;

        foreach (AggregatorPad pad in pads)
        {
            using Gst.Buffer? popped = pad.PopBuffer();

            if (popped is not null)
            {
                draining = false;
            }

            if (forward is null && popped is not null)
            {
                // The pushed buffer needs a reference of its own: the wrapper
                // of the popped one is disposed at the end of this iteration.
                forward = popped.Copy();
            }

            popped?.Dispose();
        }

        if (forward is null)
        {
            return draining ? FlowReturn.Eos : FlowReturn.Ok;
        }

        _ = Interlocked.Increment(ref _pushed);

        // FinishBuffer consumes the buffer it is given.
        return FinishBuffer(forward);
    }

    /// <summary>Describes the class and gives it its pad templates.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe aggregator",
            "Generic/Testing",
            "Forwards one buffer per aggregate call",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
        config.AddPadTemplate(SinkTemplate);
    }

    /// <summary>Builds one pad template of the class.</summary>
    /// <param name="name">The name of the template.</param>
    /// <param name="direction">Which way the pads point.</param>
    /// <param name="presence">Whether the pads are always there.</param>
    /// <param name="padType">The type of the pads created from the template.</param>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewTemplate(
        string name,
        PadDirection direction,
        PadPresence presence,
        GType padType)
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.NewWithGtype(name, direction, presence, caps, padType)
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
