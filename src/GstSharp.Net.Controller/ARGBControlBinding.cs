using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.Controller;

/// <summary>
/// Attaches four control sources to the four channels of a packed colour
/// property, the <c>GstARGBControlBinding</c> of <c>libgstcontroller</c>.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="DirectControlBinding"/> this is a factory rather than a
/// wrapper class — <c>GstARGBControlBinding</c> adds no methods of its own to
/// <see cref="Gst.ControlBinding"/> — so what it produces is an ordinary
/// <see cref="Gst.ControlBinding"/> that the core binding already covers.
/// </para>
/// <para>
/// <b>The bound property has to be a <c>guint</c></b>, on top of being writable,
/// controllable and not construct-only. A binding to a property of any other
/// type is built and then does nothing, exactly as one to a property that is
/// not marked controllable does, and
/// <see cref="Gst.Object.AddControlBinding"/> refuses it. The <c>guint</c> is
/// read as big-endian ARGB, which is what <c>videotestsrc</c> means by
/// <c>foreground-color</c> and what the <c>alpha</c> and <c>textoverlay</c>
/// colour properties are.
/// </para>
/// <para>
/// Each source answers a value in <c>[0, 1]</c>, which is clamped and scaled to
/// the <c>[0, 255]</c> of its channel — <c>0.5</c> is <c>127</c>, not
/// <c>128</c>, because the product is truncated — and the four bytes are packed
/// as <c>(a &lt;&lt; 24) | (r &lt;&lt; 16) | (g &lt;&lt; 8) | b</c>. A
/// timestamp at which any one of the four has no value leaves the property
/// alone.
/// </para>
/// </remarks>
public static unsafe partial class ARGBControlBinding
{
    /// <summary>
    /// Binds a packed ARGB property to one control source per channel.
    /// </summary>
    /// <param name="object">The object whose property is bound.</param>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="alpha">The control source of the alpha channel.</param>
    /// <param name="red">The control source of the red channel.</param>
    /// <param name="green">The control source of the green channel.</param>
    /// <param name="blue">The control source of the blue channel.</param>
    /// <returns>
    /// The binding, which is not attached yet:
    /// <see cref="Gst.Object.AddControlBinding"/> is what attaches it.
    /// </returns>
    /// <remarks>
    /// All four sources are required, which is what the C function documents:
    /// <c>gst_argb_control_binding_new</c> annotates none of its four control
    /// source parameters as nullable. Its implementation happens to skip a
    /// channel whose source is <c>NULL</c> — falling back to an opaque alpha and
    /// a black colour — but that is not part of what it promises, so this does
    /// not promise it either. A channel that should stay where it is takes a
    /// control source of a constant value.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">One of the wrappers was disposed.</exception>
    public static Gst.ControlBinding New(
        Gst.Object @object,
        string propertyName,
        Gst.ControlSource alpha,
        Gst.ControlSource red,
        Gst.ControlSource green,
        Gst.ControlSource blue)
    {
        ArgumentNullException.ThrowIfNull(@object);
        ArgumentNullException.ThrowIfNull(propertyName);
        ArgumentNullException.ThrowIfNull(alpha);
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(green);
        ArgumentNullException.ThrowIfNull(blue);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope name = GMarshal.StackUtf8(propertyName, buffer);

        nint handle = GstARGBControlBindingNew(
            @object.Handle,
            name.Pointer,
            alpha.Handle,
            red.Handle,
            green.Handle,
            blue.Handle);

        // Reading Handle was the last use of each wrapper, and the call above
        // only takes references of its own once it is inside.
        GC.KeepAlive(@object);
        GC.KeepAlive(alpha);
        GC.KeepAlive(red);
        GC.KeepAlive(green);
        GC.KeepAlive(blue);

        return Object.FromNative<Gst.ControlBinding>(handle, Transfer.Full)
            ?? throw new InvalidOperationException(
                "gst_argb_control_binding_new did not produce a wrapper. GstControlBinding is bound by " +
                "GstSharp.Net itself, so its type registry entry should always be there.");
    }

    [LibraryImport("GstController", EntryPoint = "gst_argb_control_binding_new")]
    private static partial nint GstARGBControlBindingNew(
        nint @object,
        byte* propertyName,
        nint alpha,
        nint red,
        nint green,
        nint blue);
}
