using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GESEffect</c> that takes over <c>create_element</c> and answers an
/// <c>identity</c> of its own, ignoring the bin description its asset carries.
/// </summary>
/// <remarks>
/// <para>
/// The description is still parsed on every request: <c>check_id</c> instantiates
/// it to decide whether the id is legal at all (<c>ges-effect-asset.c:405</c>,
/// <c>ges-asset.c:1263</c>), so an override that builds something else is still
/// requested with an id that names installed elements.
/// </para>
/// <para>
/// The inherited slot registers the child properties of the bin it builds
/// (<c>ges-effect.c:338</c>); an override replaces that too, so it calls
/// <see cref="GES.TrackElement.AddChildrenProps"/> itself.
/// </para>
/// </remarks>
internal sealed class ProbeElementEffect : GES.Effect, IManagedSubclass<ProbeElementEffect>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestGesElementEffect";

    /// <summary>The message the throwing mode raises.</summary>
    internal const string RefusalMessage = "The probe refuses to build an element.";

    private static readonly SubclassType Definition = DefineSubclass<ProbeElementEffect>(
        GTypeName,
        null,
        CreateElementOverride);

    private static int _throws;

    private static int _createElementCalls;

    private static int _managedThreadId;

    private static int _sawNoParent;

    private static ProbeElementEffect? _caller;

    private ProbeElementEffect(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the effect.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>Gets how often the <c>create_element</c> override ran.</summary>
    internal static int CreateElementCalls => Volatile.Read(ref _createElementCalls);

    /// <summary>Gets the managed thread the override last ran on.</summary>
    internal static int ManagedThreadId => Volatile.Read(ref _managedThreadId);

    /// <summary>Gets whether the instance had no parent when the override ran.</summary>
    internal static bool SawNoParent => Volatile.Read(ref _sawNoParent) != 0;

    /// <summary>Gets the instance the override last ran for.</summary>
    internal static ProbeElementEffect? Caller => Volatile.Read(ref _caller);

    /// <summary>
    /// Gets or sets a value indicating whether the override throws instead of
    /// answering an element.
    /// </summary>
    internal static bool Throws
    {
        get => Volatile.Read(ref _throws) != 0;
        set => Volatile.Write(ref _throws, value ? 1 : 0);
    }

    /// <summary>Forgets what the previous test observed.</summary>
    internal static void Reset()
    {
        Volatile.Write(ref _createElementCalls, 0);
        Volatile.Write(ref _managedThreadId, 0);
        Volatile.Write(ref _sawNoParent, 0);
        Volatile.Write(ref _caller, null);
    }

    /// <summary>Builds an effect out of an asset whose id is a bin description.</summary>
    /// <param name="id">The asset id, which is never <see langword="null"/>.</param>
    /// <returns>The new effect, which has an asset.</returns>
    internal static ProbeElementEffect NewFromDescription(string id)
    {
        GES.Asset asset = GES.Asset.Request(Definition.GType, id)
            ?? throw new InvalidOperationException("The effect asset could not be requested.");

        return asset.Extract<ProbeElementEffect>();
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeElementEffect CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override Gst.Element OnCreateElement()
    {
        _ = Interlocked.Increment(ref _createElementCalls);
        Volatile.Write(ref _managedThreadId, Environment.CurrentManagedThreadId);
        Volatile.Write(ref _sawNoParent, Parent is null ? 1 : 0);
        Volatile.Write(ref _caller, this);

        if (Throws)
        {
            throw new InvalidOperationException(RefusalMessage);
        }

        Gst.Element element = Gst.ElementFactory.Make("identity", null)
            ?? throw new InvalidOperationException("identity is not installed.");

        // The inherited slot would have done this for the bin it built; an
        // override that wants the properties of its element reachable as child
        // properties asks for them itself.
        AddChildrenProps(element, null, null, null);

        return element;
    }
}
