using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GParamSpecValueArray</c>: a property that carries a
/// <c>GValueArray</c>, whose members may all be described by one specification
/// of their own.
/// </summary>
/// <remarks>
/// <para>
/// The field is read at the offset of the public structure, because GObject
/// exposes it through the structure alone and declares no accessor for it. The
/// layout is the one <see cref="Gst.ParamSpecArray"/> has, which is where the
/// offset comes from: a <c>GParamSpec</c> and then the specification of one
/// element.
/// </para>
/// <para>
/// GLib deprecates <c>GValueArray</c> in favour of <c>GArray</c>, and GStreamer
/// elements declare <c>GstValueArray</c> properties instead, but the older type
/// is what several plugins of gst-plugins-good and gst-plugins-bad still
/// install -- <c>audiofirfilter</c>, <c>vp8enc</c> and <c>audiointerleave</c>
/// among them -- so a reader that describes a property has to be able to name
/// what such an array holds.
/// </para>
/// </remarks>
public sealed unsafe class ParamSpecValueArray : ParamSpec
{
    internal ParamSpecValueArray(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the description of one member of the array, or
    /// <see langword="null"/> when the property was declared without one and
    /// its members may be anything.
    /// </summary>
    /// <remarks>
    /// The specification belongs to this one, which holds a reference of its
    /// own on it. What comes back is the caller's wrapper and holds a further
    /// reference, so it has to be disposed like everything else a member of the
    /// binding hands out; it outlives this wrapper either way.
    /// </remarks>
    public ParamSpec? ElementSpec
    {
        get
        {
            nint element = *(nint*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return element == nint.Zero ? null : FromNative(element, Transfer.None);
        }
    }
}
