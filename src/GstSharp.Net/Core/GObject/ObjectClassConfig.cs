namespace Gst.GObject;

/// <summary>
/// The class of a managed subclass while it is being initialised, at the
/// <c>GObject</c> level.
/// </summary>
/// <remarks>
/// <para>
/// An instance is handed to the <c>configureClass</c> delegate of
/// <c>DefineSubclass</c>, from inside <c>class_init</c>, and it is only usable
/// for the length of that call. A subclassable class whose ancestry reaches
/// <c>Gst.Element</c> is given the richer <see cref="ClassConfig"/>, which
/// derives from this one and adds the <c>GstElementClass</c> operations;
/// everything else — <c>Gst.Pad</c> and <c>GstBase.AggregatorPad</c> — is given
/// this facade, because the element operations would be written into a class
/// struct that has no such fields.
/// </para>
/// <para>
/// <b>Never create a wrapper from a class initialiser.</b> <c>class_init</c>
/// runs while GObject holds its type lock, and the wrapper interning table of
/// this binding is a lock that is taken <em>around</em> native calls, so
/// wrapping an object here would take the two locks in the reverse of the order
/// every other path takes them in. See <c>docs/subclassing.md</c> §5.5.
/// </para>
/// </remarks>
public class ObjectClassConfig
{
    private readonly nint _gClass;

    /// <summary>Wraps the class that is being initialised.</summary>
    /// <param name="gClass">The <c>GObjectClass</c> under construction.</param>
    /// <remarks>
    /// The constructor is internal: the facade is handed out by
    /// <c>class_init</c> and stands for a class that is being built at that
    /// very moment, so there is no state an application could construct one
    /// from.
    /// </remarks>
    internal ObjectClassConfig(nint gClass) => _gClass = gClass;

    /// <summary>Gets the class that is being initialised.</summary>
    internal nint GClass => _gClass;
}
