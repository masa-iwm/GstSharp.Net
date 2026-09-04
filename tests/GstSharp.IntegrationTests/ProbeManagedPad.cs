using Gst;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed <c>GstPad</c> subclass. Nothing in C# ever creates one: a base
/// class builds it from the pad template of an element, which is what makes it
/// the proof that a wrapper is fabricated for an instance native code created.
/// </summary>
internal sealed class ProbeManagedPad : Pad, IManagedSubclass<ProbeManagedPad>
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestManagedPad";

    private static readonly SubclassType Definition = DefineSubclass<ProbeManagedPad>(
        GTypeName,
        null,
        LinkedOverride);

    private static int _wrappersBuilt;

    private int _linked;

    private ProbeManagedPad(SubclassCtorArgs args)
        : base(args)
    {
    }

    /// <summary>Gets the type the pad is registered as.</summary>
    internal static GType RegisteredType => Definition.GType;

    /// <summary>Gets the registration of the pad.</summary>
    internal static SubclassType Registration => Definition;

    /// <summary>
    /// Gets how many wrappers of this type were fabricated in this process.
    /// </summary>
    /// <remarks>
    /// A count that does not move is how a test says "nothing was fabricated
    /// here", which no other observation of the pad can say.
    /// </remarks>
    internal static int WrappersBuilt => Volatile.Read(ref _wrappersBuilt);

    /// <summary>Gets how often the <c>linked</c> override ran for this pad.</summary>
    internal int LinkedCalls => Volatile.Read(ref _linked);

    /// <summary>Builds the wrapper of an instance native code created.</summary>
    /// <param name="args">What the runtime says about the instance.</param>
    /// <returns>The wrapper, which adopts the instance.</returns>
    public static ProbeManagedPad CreateWrapper(SubclassCtorArgs args)
    {
        ProbeManagedPad wrapper = new(args);
        _ = Interlocked.Increment(ref _wrappersBuilt);
        return wrapper;
    }

    /// <inheritdoc/>
    protected override void OnLinked(Pad peer)
    {
        _ = Interlocked.Increment(ref _linked);
        ChainUpLinked(peer);
    }
}
