using Gst.GObject;
using Gst.Interop;

namespace Gst;

/// <summary>
/// A <c>GstParamSpecArray</c>: a property that carries an array of values, all
/// of them described by one specification of their own.
/// </summary>
/// <remarks>
/// <para>
/// The field is read at the offset of the public structure, because GStreamer
/// exposes it through the structure alone and declares no accessor for it.
/// </para>
/// <para>
/// The class lives in <c>Gst</c> rather than in <c>Gst.GObject</c> because the
/// type is GStreamer's: <c>Gst.ParamFlags</c>, the other half of what a
/// GStreamer property description carries, is beside it.
/// </para>
/// </remarks>
public sealed unsafe class ParamSpecArray : ParamSpec
{
    internal ParamSpecArray(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the description of one element of the array, or
    /// <see langword="null"/> when the property was declared without one and
    /// its elements may be anything.
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
