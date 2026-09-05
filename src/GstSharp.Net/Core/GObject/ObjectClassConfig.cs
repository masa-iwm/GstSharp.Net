using System.Globalization;
using Gst.Interop;

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

    private readonly HashSet<uint> _propertyIds = [];

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

    /// <summary>Gets the type that is being initialised.</summary>
    internal unsafe GType OwnType => new(((GTypeClassRaw*)_gClass)->GType);

    /// <summary>
    /// Installs a property on the class that is being initialised.
    /// </summary>
    /// <param name="propertyId">
    /// The identifier the property slots are given, which has to be greater
    /// than zero and unique within this class.
    /// </param>
    /// <param name="spec">
    /// The specification of the property, built with one of the
    /// <c>ParamSpecX.New</c> factories. The class takes its own references, so
    /// the wrapper may be disposed once this method has returned.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="propertyId"/> is zero.</exception>
    /// <exception cref="ArgumentException">
    /// The specification asks for <see cref="ParamFlags.Construct"/> or
    /// <see cref="ParamFlags.ConstructOnly"/>, it has already been installed
    /// somewhere, the identifier is one this class already used, or this class
    /// already has a property of that name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The subclass did not declare
    /// <see cref="Object.SetPropertyOverride"/> or
    /// <see cref="Object.GetPropertyOverride"/> although the property is
    /// writable or readable. GObject would answer such a property out of the
    /// implementation of <c>GObject</c> itself, which knows nothing about it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Construct properties are refused.</b> GObject delivers every
    /// construct property — the value the caller named, or the default of the
    /// specification when it named none — from inside <c>g_object_new</c>,
    /// before anything managed can exist for an instance a C# constructor is
    /// building. The value would reach the managed setter of an instance native
    /// code created and vanish for one C# created, which is not a property but
    /// a coin toss. Take construct-time state through the constructor of the
    /// subclass instead.
    /// </para>
    /// <para>
    /// Redefining a property of an ancestor is legal and is what GObject calls
    /// an override by shadowing: the specification this class installs wins for
    /// every instance of this class, and the implementation of the ancestor is
    /// never consulted again — there is no chain up out of a property slot.
    /// </para>
    /// </remarks>
    public unsafe void InstallProperty(uint propertyId, ParamSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfZero(propertyId);

        ParamFlags flags = spec.Flags;

        if ((flags & (ParamFlags.Construct | ParamFlags.ConstructOnly)) != 0)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' asks for CONSTRUCT or CONSTRUCT_ONLY, which a managed subclass cannot answer: "
                        + "GObject delivers construct properties before the wrapper of the instance exists.",
                    spec.Name),
                nameof(spec));
        }

        if (ParamSpec.ParamIdOf(spec.Handle) != 0)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property specification '{0}' has already been installed on '{1}'.",
                    spec.Name,
                    spec.OwnerType.IsValid ? spec.OwnerType.Name : "another class"),
                nameof(spec));
        }

        if (!_propertyIds.Add(propertyId))
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property identifier {0} was already used by this class.",
                    propertyId),
                nameof(propertyId));
        }

        GType ownType = OwnType;
        string canonical = Canonicalise(spec.Name);

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using (Utf8Scope nameScope = GMarshal.StackUtf8(canonical, nameBuffer))
        {
            nint existing = GObjectNative.ObjectClassFindProperty(_gClass, nameScope.Pointer);
            if (existing != nint.Zero && ParamSpec.OwnerTypeOf(existing).Value == ownType.Value)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "This class already has a property called '{0}'.",
                        canonical),
                    nameof(spec));
            }
        }

        GObjectClassRaw* raw = (GObjectClassRaw*)_gClass;

        if ((flags & ParamFlags.Writable) != 0 && raw->SetProperty != Object.SetPropertyOverride.Function)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' is writable, so the subclass has to declare Object.SetPropertyOverride.",
                    canonical));
        }

        if ((flags & ParamFlags.Readable) != 0 && raw->GetProperty != Object.GetPropertyOverride.Function)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' is readable, so the subclass has to declare Object.GetPropertyOverride.",
                    canonical));
        }

        GObjectNative.ObjectClassInstallProperty(_gClass, propertyId, spec.Handle);

        // The installed specification lives as long as the class does, and the
        // property slots need a wrapper they may hand out on every call: one
        // that is built here, once, rather than one per call that nothing would
        // ever release.
        SubclassRegistry.RememberInstalledSpec(ownType, ParamSpec.FromNative(spec.Handle, Transfer.None));
    }

    /// <summary>
    /// Canonicalises a property name the way GObject does internally, so that
    /// the duplicate check looks the name up in the form it will be stored
    /// under.
    /// </summary>
    private static string Canonicalise(string name) => name.Replace('_', '-');
}
