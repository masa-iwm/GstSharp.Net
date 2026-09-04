using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstElement</c> subclass that states how its wrapper is built
/// and is registered as an element factory, so that GStreamer itself is what
/// creates its instances.
/// </summary>
/// <remarks>
/// The rank is <see cref="Rank.None"/> on purpose: a non zero rank makes the
/// type eligible for the autoplugging of <c>decodebin</c> and <c>playbin</c>,
/// which would construct it on a streaming thread nobody asked.
/// </remarks>
internal sealed class ProbeFactoryElement : Element, IManagedSubclass<ProbeFactoryElement>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestFactoryElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestfactoryelement";

    private static readonly SubclassType Definition = DefineSubclass<ProbeFactoryElement>(
        GTypeName,
        ConfigureClass,
        ChangeStateOverride);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    private readonly List<StateChange> _transitions = [];

    /// <summary>Creates an instance of the managed type from C#.</summary>
    internal ProbeFactoryElement()
        : this(Definition.NewInstance())
    {
    }

    private ProbeFactoryElement(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    /// <summary>Gets the transitions the override has seen, oldest first.</summary>
    internal IReadOnlyList<StateChange> Transitions
    {
        get
        {
            lock (_transitions)
            {
                return _transitions.ToArray();
            }
        }
    }

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeFactoryElement CreateWrapper(SubclassCtorArgs args) => new(args);

    /// <inheritdoc/>
    protected override StateChangeReturn OnChangeState(StateChange transition)
    {
        lock (_transitions)
        {
            _transitions.Add(transition);
        }

        return ChainUpChangeState(transition);
    }

    private static void ConfigureClass(ClassConfig config) =>
        config.SetMetadata(
            "GstSharp probe factory element",
            "Generic/Testing",
            "A managed element that an element factory creates",
            "GstSharp.Net integration tests");
}
