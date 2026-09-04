using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstElement</c> subclass that is registered as an element
/// factory without stating how its wrapper is built, which is what keeps it
/// what it always was: a type that only C# creates instances of.
/// </summary>
/// <remarks>
/// An instance an element factory makes of it arrives as the nearest wrapped
/// ancestor, and <see cref="TypeRegistry.Fallback"/> is what says so.
/// </remarks>
internal sealed class ProbePlainFactoryElement : Element
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestPlainFactoryElement";

    /// <summary>The name the element factory is registered under.</summary>
    internal const string FactoryName = "gstsharptestplainfactoryelement";

    private static readonly SubclassType Definition = DefineSubclass(GTypeName, ConfigureClass);

    private static readonly bool Registered =
        Element.Register(null, FactoryName, (uint)Rank.None, Definition.GType);

    /// <summary>Creates an instance of the managed type from C#.</summary>
    internal ProbePlainFactoryElement()
        : base(Definition.NewInstance())
    {
    }

    /// <summary>Gets the type the element is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets a value indicating whether the factory was registered.</summary>
    internal static bool IsRegistered => Registered;

    private static void ConfigureClass(ClassConfig config) =>
        config.SetMetadata(
            "GstSharp probe plain factory element",
            "Generic/Testing",
            "A managed element registered without a wrapper factory",
            "GstSharp.Net integration tests");
}
