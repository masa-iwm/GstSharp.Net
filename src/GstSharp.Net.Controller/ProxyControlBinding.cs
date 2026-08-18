using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// Forwards a control binding from one object to another, the
/// <c>GstProxyControlBinding</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the binding that has no control source of its own. Every request it
/// receives — synchronising, reading a value, reading an array of them — is
/// answered by looking the control binding of another property of another
/// object up by name and asking that one instead. It is what an element which
/// merely re-exports a property of something it owns puts on that property, so
/// that controlling either of the two ends up in the same place.
/// </para>
/// <code>
/// // "volume" of proxying follows whatever binding sits on "volume" of real.
/// proxying.AddControlBinding(ProxyControlBinding.New(proxying, "volume", real, "volume"));
/// </code>
/// <para>
/// <b>The forwarding is complete, which includes the object that is written.</b>
/// The reference binding is invoked with the reference object, so
/// synchronising the proxying object moves the property of the reference object
/// and leaves its own alone. The proxy carries the value across; it does not
/// duplicate it.
/// </para>
/// <para>
/// <b>The reference object is held weakly.</b> A proxy binding does not keep it
/// alive, and once it is gone — or while it simply has no control binding under
/// that name — the proxy answers nothing and synchronising through it does
/// nothing at all. Whoever builds the proxy is the one who has to keep the
/// reference object alive.
/// </para>
/// <para>
/// Like <see cref="DirectControlBinding"/> this is a factory rather than a
/// wrapper class, and what it produces is an ordinary
/// <see cref="Gst.ControlBinding"/>. The property it is attached to still has
/// to be writable, controllable and not construct-only.
/// </para>
/// </remarks>
public static unsafe partial class ProxyControlBinding
{
    /// <summary>
    /// Binds a property to the control binding of another property, on another
    /// object.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of that property.</param>
    /// <param name="referenceObject">
    /// The object whose control binding answers, which is held weakly.
    /// </param>
    /// <param name="referencePropertyName">
    /// The name of the property on <paramref name="referenceObject"/> whose
    /// control binding answers. It is looked up on every request, so it does not
    /// have to exist yet.
    /// </param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding New(
        Gst.Object @object,
        string propertyName,
        Gst.Object referenceObject,
        string referencePropertyName)
    {
        ArgumentNullException.ThrowIfNull(@object);
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(referenceObject);
        ArgumentNullException.ThrowIfNull(referencePropertyName);

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        Span<byte> referenceNameBuffer = stackalloc byte[GMarshal.StackBufferSize];

        using Utf8Scope name = GMarshal.StackUtf8(propertyName, nameBuffer);
        using Utf8Scope referenceName = GMarshal.StackUtf8(referencePropertyName, referenceNameBuffer);

        nint handle = GstProxyControlBindingNew(
            @object.Handle,
            name.Pointer,
            referenceObject.Handle,
            referenceName.Pointer);

        // Reading Handle was the last use of both wrappers, and the call above
        // only takes references of its own once it is inside — a weak one, for
        // the reference object.
        GC.KeepAlive(@object);
        GC.KeepAlive(referenceObject);

        return Object.FromNative<Gst.ControlBinding>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_proxy_control_binding_new did not produce a wrapper. GstControlBinding is bound by " +
                "GstSharp.Net itself, so its type registry entry should always be there.");
    }

    [LibraryImport("GstController", EntryPoint = "gst_proxy_control_binding_new")]
    private static partial nint GstProxyControlBindingNew(
        nint @object,
        byte* propertyName,
        nint referenceObject,
        byte* referencePropertyName);
}
