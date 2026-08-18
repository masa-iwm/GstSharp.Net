using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// Attaches a control source directly to a property of an object, the
/// <c>GstDirectControlBinding</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>GstDirectControlBinding</c> adds no methods of its own to
/// <see cref="Gst.ControlBinding"/> — the C library gives it two constructors
/// and nothing else — so this is a factory rather than a wrapper class, and what
/// it produces is an ordinary <see cref="Gst.ControlBinding"/> that the existing
/// binding already covers: <see cref="Gst.Object.AddControlBinding"/> takes it,
/// <see cref="Gst.Object.GetControlBinding"/> hands it back,
/// <see cref="Gst.ControlBinding.SetDisabled"/> switches it off.
/// </para>
/// <para>
/// <b>The bound property has to be writable, controllable and not
/// construct-only.</b> That is <c>GST_PARAM_CONTROLLABLE</c> in the parameter
/// specification of the element, which is a decision of whoever wrote the
/// element. A binding to a property that is not marked so is built, logs a
/// warning through the GStreamer debug system, and then does nothing.
/// </para>
/// <para>
/// Each of the two methods comes in a pair: one takes any
/// <see cref="Gst.ControlSource"/>, which is what the C function takes and what
/// an <see cref="LFOControlSource"/> is, and one takes a
/// <see cref="TimedValueControlSource"/>, which is the narrower parameter the
/// first release of this module published. Both end up in the same call; the
/// narrow pair is kept because the public surface of a package only ever grows.
/// </para>
/// </remarks>
public static unsafe partial class DirectControlBinding
{
    /// <summary>
    /// Binds a property to a control source, mapping the values of the source
    /// onto the range of the property.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="controlSource">The control source to follow.</param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <remarks>
    /// The source answers values in <c>[0, 1]</c> and this maps them onto the
    /// minimum and maximum the property declares, so <c>0.5</c> on a property
    /// that runs from <c>0</c> to <c>10</c> is <c>5</c>. Use
    /// <see cref="NewAbsolute(Gst.Object, string, Gst.ControlSource)"/> to pass
    /// the values through untouched.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding New(
        Gst.Object @object,
        string propertyName,
        Gst.ControlSource controlSource) =>
        Create(@object, propertyName, controlSource, absolute: false);

    /// <summary>
    /// Binds a property to a control source that is described by control points,
    /// mapping the values of the source onto the range of the property.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="controlSource">The control source to follow.</param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <remarks>
    /// This is <see cref="New(Gst.Object, string, Gst.ControlSource)"/> with the
    /// narrower parameter the first release of this module published; a
    /// <see cref="TimedValueControlSource"/> is a
    /// <see cref="Gst.ControlSource"/>, and either overload does the same thing
    /// with one.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding New(
        Gst.Object @object,
        string propertyName,
        TimedValueControlSource controlSource) =>
        Create(@object, propertyName, controlSource, absolute: false);

    /// <summary>
    /// Binds a property to a control source, taking the values of the source as
    /// they are.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="controlSource">The control source to follow.</param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <remarks>
    /// The values of the source are the values of the property, clamped to the
    /// range the property declares. This is what to use when the numbers in the
    /// control source are meant to be read as the property reads them.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding NewAbsolute(
        Gst.Object @object,
        string propertyName,
        Gst.ControlSource controlSource) =>
        Create(@object, propertyName, controlSource, absolute: true);

    /// <summary>
    /// Binds a property to a control source that is described by control points,
    /// taking the values of the source as they are.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="controlSource">The control source to follow.</param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <remarks>
    /// This is
    /// <see cref="NewAbsolute(Gst.Object, string, Gst.ControlSource)"/> with the
    /// narrower parameter the first release of this module published.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding NewAbsolute(
        Gst.Object @object,
        string propertyName,
        TimedValueControlSource controlSource) =>
        Create(@object, propertyName, controlSource, absolute: true);

    private static Gst.ControlBinding Create(
        Gst.Object @object,
        string propertyName,
        Gst.ControlSource controlSource,
        bool absolute)
    {
        ArgumentNullException.ThrowIfNull(@object);
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(controlSource);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope name = GMarshal.StackUtf8(propertyName, buffer);

        nint handle = absolute
            ? GstDirectControlBindingNewAbsolute(@object.Handle, name.Pointer, controlSource.Handle)
            : GstDirectControlBindingNew(@object.Handle, name.Pointer, controlSource.Handle);

        // Reading Handle was the last use of both wrappers, and the call above
        // only takes references of its own once it is inside.
        GC.KeepAlive(@object);
        GC.KeepAlive(controlSource);

        return Object.FromNative<Gst.ControlBinding>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_direct_control_binding_new did not produce a wrapper. GstControlBinding is bound by " +
                "GstSharp.Net itself, so its type registry entry should always be there.");
    }

    [LibraryImport("GstController", EntryPoint = "gst_direct_control_binding_new")]
    private static partial nint GstDirectControlBindingNew(nint @object, byte* propertyName, nint controlSource);

    [LibraryImport("GstController", EntryPoint = "gst_direct_control_binding_new_absolute")]
    private static partial nint GstDirectControlBindingNewAbsolute(nint @object, byte* propertyName, nint controlSource);
}
