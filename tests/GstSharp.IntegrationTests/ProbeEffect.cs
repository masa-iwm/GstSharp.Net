using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESEffect</c> that adds no slot of its own beyond
/// <c>set_parent</c>, so that the element behind it is the one the inherited
/// <c>create_element</c> builds out of the bin description of its asset.
/// </summary>
/// <remarks>
/// <para>
/// This is the ordinary shape of a managed effect: the asset carries the
/// description, <c>GESEffect::create_element</c> parses it, and the managed class
/// is there for the behaviour around it. An instance is therefore only ever
/// built through <c>GES.Asset.Request(GType, "video &lt;description&gt;")</c> and
/// <see cref="GES.Asset.Extract{T}"/> — a <c>null</c> id takes the process down
/// (<c>ges-effect-asset.c:390-391</c>), and an instance built with <c>new</c>
/// takes it down when a clip adopts it (<c>ges-clip.c:1786-1790</c>), so neither
/// appears here.
/// </para>
/// <para>
/// The <c>set_parent</c> override answers <see langword="true"/> without chaining
/// up, which is not a choice: the slot has no implementation anywhere between
/// <c>GESTimelineElement</c> and <c>GESEffect</c>, so <c>ChainUpSetParent</c>
/// throws — and a refusal there costs the clip its add.
/// </para>
/// <para>
/// The wrapper counter is what a native copy is observed through: the library
/// copies a top effect itself when a clip is split or pasted
/// (<c>ges-clip.c:2385-2407</c>), which fabricates a wrapper the test never
/// asked for.
/// </para>
/// </remarks>
internal sealed class ProbeEffect : GES.Effect, IManagedSubclass<ProbeEffect>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesEffect";

    private static readonly SubclassType Definition = DefineSubclass<ProbeEffect>(
        GTypeName,
        null,
        SetParentOverride);

    private static int _wrappersBuilt;

    private int _setParentCalls;

    private int _lastParentWasNull;

    private ProbeEffect(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the effect.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how many wrappers were fabricated since the last reset.</summary>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets how often the <c>set_parent</c> override ran.</summary>
    internal int SetParentCalls => Volatile.Read(ref _setParentCalls);

    /// <summary>Gets whether the last parent the override saw was none.</summary>
    internal bool LastParentWasNull => Volatile.Read(ref _lastParentWasNull) != 0;

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset() => Volatile.Write(ref _wrappersBuilt, 0);

    /// <summary>Builds an effect out of an asset whose id is a bin description.</summary>
    /// <param name="id">The asset id, which is never <see langword="null"/>.</param>
    /// <returns>The new effect, which has an asset.</returns>
    internal static ProbeEffect NewFromDescription(string id)
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, id)
            ?? throw new InvalidOperationException("The effect asset could not be requested.");

        return asset.Extract<ProbeEffect>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeEffect CreateWrapper(SubclassCtorArgs args)
    {
        ProbeEffect wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override bool OnSetParent(GES.TimelineElement? newParent)
    {
        _ = Interlocked.Increment(ref _setParentCalls);
        Volatile.Write(ref _lastParentWasNull, newParent is null ? 1 : 0);

        // No class between GESTimelineElement and GESEffect implements
        // set_parent, so there is nothing to chain up into:
        // ChainUpSetParent throws here, the trap turns that into a refusal, and
        // the refusal costs the clip its add. Answering true is what the
        // exception of the chain-up itself asks for, and is the documented shape
        // for a slot whose default implementation is NULL.
        return true;
    }
}
