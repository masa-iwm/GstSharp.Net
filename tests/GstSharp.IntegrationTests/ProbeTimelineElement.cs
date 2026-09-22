using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESTimelineElement</c> directly below the base class, which is
/// the only shape that reaches the empty <c>get_layer_priority</c> slot.
/// </summary>
/// <remarks>
/// <para>
/// Every other subclassable class of the editing services descends from
/// <c>GESClip</c> or <c>GESTrackElement</c>, and both of those fill the slot
/// (<c>ges-clip.c:2678</c>, <c>ges-track-element.c:493</c>), so a chain-up
/// below them runs their implementation. Directly below
/// <c>GESTimelineElement</c> the slot is empty, and the library answers the
/// priority of the element itself for an empty one
/// (<c>ges-timeline-element.c:2439-2440</c>) — which is what
/// <c>ChainUpGetLayerPriority</c> answers here.
/// </para>
/// <para>
/// The <c>set_priority</c> override answers <see langword="true"/> without
/// chaining up: the base class leaves that slot empty too
/// (<c>ges-timeline-element.c:642</c>), and <c>ChainUpSetPriority</c> throws for
/// it, while a true answer is what lets the C write <c>self-&gt;priority</c>
/// (<c>:1476-1481</c>). That is what makes a priority write observable through
/// the layer priority the other slot answers.
/// </para>
/// </remarks>
internal sealed class ProbeTimelineElement : GES.TimelineElement, IManagedSubclass<ProbeTimelineElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestTimelineElement";

    private static readonly SubclassType Definition = DefineSubclass<ProbeTimelineElement>(
        GTypeName,
        null,
        GetLayerPriorityOverride,
        SetPriorityOverride);

    private int _chainedUp;

    private ProbeTimelineElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets whether the layer priority override chained up.</summary>
    internal bool ChainedUp => Volatile.Read(ref _chainedUp) != 0;

    /// <summary>Builds a bare timeline element of the managed type.</summary>
    /// <returns>The new element, which is in no layer.</returns>
    internal static ProbeTimelineElement New() => new(Definition.NewInstance());

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeTimelineElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override uint OnGetLayerPriority()
    {
        uint answer = ChainUpGetLayerPriority();
        Volatile.Write(ref _chainedUp, 1);
        return answer;
    }

    /// <inheritdoc/>
    protected override bool OnSetPriority(uint priority) => true;
}
