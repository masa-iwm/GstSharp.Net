namespace Gst.GObject;

/// <summary>
/// The optional parts of a managed subclass registration, everything beyond the
/// type name, the class initialiser and the overridden slots.
/// </summary>
/// <remarks>
/// <para>
/// The type is an extension point: a later version adds init properties to it
/// rather than another <c>DefineSubclass</c> overload, so code that passes one
/// keeps compiling.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// SubclassType type = PushSrc.DefineSubclass&lt;MySource&gt;(
///     "MySource",
///     config => { /* metadata, pad template */ },
///     new SubclassOptions { Interfaces = [URIHandlerImplementation.For&lt;MySource&gt;()] },
///     PushSrc.CreateOverride);
/// </code>
/// </example>
public sealed class SubclassOptions
{
    /// <summary>
    /// Gets the GObject interfaces the subclass implements, empty by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Interfaces can only be declared here, when the type is registered:
    /// <c>g_type_add_interface_static</c> refuses a type whose class
    /// initialisation has begun, so there is no way to add one from the class
    /// initialiser or later. See <c>docs/subclassing.md</c> §5.7.
    /// </para>
    /// <para>
    /// Each entry is built by the binding — <see cref="InterfaceImplementation"/>
    /// cannot be derived from outside it — for instance by
    /// <c>Gst.URIHandlerImplementation.For&lt;TSelf&gt;()</c>.
    /// </para>
    /// </remarks>
    public IReadOnlyList<InterfaceImplementation> Interfaces { get; init; } = [];
}

/// <summary>
/// One GObject interface a managed subclass implements, together with the way
/// its vtable is filled in.
/// </summary>
/// <remarks>
/// <para>
/// The class cannot be derived from outside the binding: filling an interface
/// vtable means writing raw function pointers into memory GLib owns, and the
/// binding hands out one ready made implementation per interface it supports.
/// </para>
/// <para>
/// <see cref="InitializeVTable"/> runs from <c>interface_init</c>, which GLib
/// calls after the class initialiser of the implementing type, on the thread
/// that registered the subclass. The vtable it is given lives for the process
/// and is never freed.
/// </para>
/// </remarks>
public abstract class InterfaceImplementation
{
    /// <summary>
    /// Initialises the base class of an implementation.
    /// </summary>
    /// <param name="interfaceType">The type of the interface being implemented.</param>
    internal InterfaceImplementation(GType interfaceType) => InterfaceType = interfaceType;

    /// <summary>Gets the type of the interface being implemented.</summary>
    public GType InterfaceType { get; }

    /// <summary>
    /// Writes the vtable of the interface for one implementing type.
    /// </summary>
    /// <param name="iface">The vtable, as GLib handed it to <c>interface_init</c>.</param>
    /// <param name="instanceType">The type that implements the interface.</param>
    /// <remarks>
    /// The rules of a class initialiser apply: no wrapper may be created here,
    /// and nothing may wait on another thread. See
    /// <c>docs/subclassing.md</c> §5.7.
    /// </remarks>
    internal abstract unsafe void InitializeVTable(void* iface, GType instanceType);
}
