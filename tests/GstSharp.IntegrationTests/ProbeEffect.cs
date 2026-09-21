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
/// The <c>set_parent</c> override chains up, which is what a managed type of the
/// editing services does with this slot: no class implements it except the video
/// source family (<c>ges-video-source.c:261</c>), and <c>ChainUpSetParent</c>
/// answers <see langword="true"/> for that empty slot, the way the caller of the
/// slot treats one itself (<c>ges-timeline-element.c:995-1000</c>).
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

    /// <summary>
    /// Builds an effect the plain way, which leaves it without an asset.
    /// </summary>
    /// <returns>The new effect, which no clip may take as a top effect.</returns>
    /// <remarks>
    /// This is the shape <c>ges-clip.c:1786-1790</c> dereferences a null pointer
    /// over, and the one the binding refuses before the call. It is built here
    /// so that the refusal can be tested; it is never handed to the library.
    /// </remarks>
    internal static ProbeEffect NewWithoutAsset() => new(Definition.NewInstance());

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

        // The slot is empty below GESEffect, and a chain-up answers the true
        // the library answers for an empty slot, so the same override keeps
        // working once this wrapper is disposed and the static chain-up is all
        // that is left.
        return ChainUpSetParent(newParent);
    }
}
