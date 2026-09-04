using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed aggregator whose <c>create_new_pad</c> override answers a pad of
/// a managed type, so that a requested pad is a
/// <see cref="ProbeManagedAggregatorPad"/> rather than a plain one.
/// </summary>
/// <remarks>
/// The templates name the plain <c>GstAggregatorPad</c> type: the always
/// present source pad is built by the base class during construction, and
/// nothing in this test wants a managed wrapper for it. The sink pads are the
/// ones the override creates.
/// </remarks>
internal sealed class ProbeCreateNewPadAggregator : Aggregator
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestCreateNewPadAggregator";

    private static readonly PadTemplate SrcTemplate = NewTemplate("src", PadDirection.Src, PadPresence.Always);

    private static readonly PadTemplate SinkTemplate = NewTemplate("sink_%u", PadDirection.Sink, PadPresence.Request);

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        AggregateOverride,
        CreateNewPadOverride);

    private readonly List<ProbeManagedAggregatorPad> _pads = [];

    private int _created;

    /// <summary>Creates a managed aggregator.</summary>
    internal ProbeCreateNewPadAggregator()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the aggregator is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the sink template of the class.</summary>
    internal static PadTemplate SinkPadTemplate => SinkTemplate;

    /// <summary>Gets the pads the <c>create_new_pad</c> override built.</summary>
    internal IReadOnlyList<ProbeManagedAggregatorPad> CreatedPads
    {
        get
        {
            lock (_pads)
            {
                return _pads.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    protected override AggregatorPad? OnCreateNewPad(PadTemplate templ, string? reqName, Caps? caps)
    {
        ArgumentNullException.ThrowIfNull(templ);

        string name = reqName ?? FormattableString.Invariant(
            $"sink_{Interlocked.Increment(ref _created) - 1}");

        ProbeManagedAggregatorPad pad = ProbeManagedAggregatorPad.New(name, templ);

        lock (_pads)
        {
            _pads.Add(pad);
        }

        return pad;
    }

    /// <inheritdoc/>
    protected override FlowReturn OnAggregate(bool timeout)
    {
        ProbeManagedAggregatorPad[] pads;

        lock (_pads)
        {
            pads = _pads.ToArray();
        }

        Gst.Buffer? forward = null;
        bool draining = true;

        foreach (ProbeManagedAggregatorPad pad in pads)
        {
            using Gst.Buffer? popped = pad.PopBuffer();

            if (popped is not null)
            {
                draining = false;

                // The pushed buffer needs a reference of its own: the wrapper
                // of the popped one is disposed at the end of this iteration.
                forward ??= popped.Copy();
            }
        }

        if (forward is null)
        {
            return draining ? FlowReturn.Eos : FlowReturn.Ok;
        }

        // FinishBuffer consumes the buffer it is given.
        return FinishBuffer(forward);
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp probe create_new_pad aggregator",
            "Generic/Testing",
            "An aggregator whose sink pads are of a managed pad type",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SrcTemplate);
        config.AddPadTemplate(SinkTemplate);
    }

    private static PadTemplate NewTemplate(string name, PadDirection direction, PadPresence presence)
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.NewWithGtype(name, direction, presence, caps, new GType(AggregatorPad.GetGType()))
            ?? throw new InvalidOperationException($"The {name} pad template could not be created.");
    }
}
