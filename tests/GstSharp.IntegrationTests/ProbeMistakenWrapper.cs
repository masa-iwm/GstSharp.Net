using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed subclass whose <c>CreateWrapper</c> makes the mistake the runtime
/// is meant to catch: it ignores the arguments it is handed and builds a fresh
/// instance instead of wrapping the one that already exists.
/// </summary>
internal sealed class ProbeMistakenWrapper : Element, IManagedSubclass<ProbeMistakenWrapper>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestMistakenWrapper";

    private static readonly SubclassType Definition = DefineSubclass<ProbeMistakenWrapper>(GTypeName, null);

    /// <summary>Creates an instance of the managed type from C#.</summary>
    internal ProbeMistakenWrapper()
        : this(Definition.NewInstance())
    {
    }

    private ProbeMistakenWrapper(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the registration of the type.</summary>
    internal static SubclassType Registration => Definition;

    // Ignoring the arguments is the mistake, and the mistake is the point: the
    // tests pin what the runtime says when a wrapper factory builds a second
    // instance instead of adopting the one it was handed. The rule is
    // suppressed rather than obeyed so the probe keeps making it.
#pragma warning disable GST0005

    /// <summary>
    /// Builds a wrapper of a native instance, wrongly: the arguments are what
    /// name the instance to wrap, and this builds another one.
    /// </summary>
    /// <param name="args">What the runtime says about the instance, ignored.</param>
    /// <returns>A wrapper of a different instance.</returns>
    public static ProbeMistakenWrapper CreateWrapper(SubclassCtorArgs args) => new();
#pragma warning restore GST0005
}
