// A GStreamer element whose wrapper the binding builds for an instance
// GStreamer itself created. The smoke test registers it as an element factory
// and makes one, so that ILC has to compile the fabrication path: the static
// abstract CreateWrapper of the subclass, instantiated for this type.
using Gst;
using Gst.GObject;

/// <summary>
/// A managed <c>GstElement</c> subclass that states how its wrapper is built,
/// counts the transitions it is asked to perform and lets the parent class
/// perform them.
/// </summary>
internal sealed class ManagedFactoryElement : Element, IManagedSubclass<ManagedFactoryElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "AotSmokeManagedFactoryElement";

    /// <summary>The name of the element factory it is registered under.</summary>
    internal const string FactoryName = "aotsmokemanagedfactoryelement";

    private static readonly SubclassType Definition = DefineSubclass<ManagedFactoryElement>(
        GTypeName,
        static config => config.SetMetadata(
            "AotSmoke managed factory element",
            "Generic/Testing",
            "A managed element that an element factory creates",
            "GstSharp.Net"),
        ChangeStateOverride);

    private int _transitions;

    private ManagedFactoryElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets how often the managed override has run.</summary>
    internal int Transitions => Volatile.Read(ref _transitions);

    /// <summary>
    /// Registers the element factory. The rank is <see cref="Rank.None"/>: a
    /// higher one would make the type eligible for autoplugging, and nothing
    /// here asks for that.
    /// </summary>
    /// <returns><see langword="true"/> when the factory was registered.</returns>
    internal static bool RegisterFactory() =>
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ManagedFactoryElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override StateChangeReturn OnChangeState(StateChange transition)
    {
        Interlocked.Increment(ref _transitions);
        return ChainUpChangeState(transition);
    }
}
