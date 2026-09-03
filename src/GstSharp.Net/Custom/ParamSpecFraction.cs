using Gst.GObject;
using Gst.Interop;

namespace Gst;

/// <summary>
/// A <c>GstParamSpecFraction</c>: a property that carries a fraction out of a
/// range, which is how a frame rate or a pixel aspect ratio is declared.
/// </summary>
/// <remarks>
/// <para>
/// The six fields are read at the offsets of the public structure, because
/// GStreamer exposes them through the structure alone and declares no accessor
/// for any of them. Each bound is a numerator and a denominator of its own: the
/// range of <c>videotestsrc</c> is <c>0/1</c> to <c>2147483647/1</c>, and the
/// two halves are read separately because that is how C stores them.
/// </para>
/// <para>
/// The class lives in <c>Gst</c> rather than in <c>Gst.GObject</c> because the
/// type is GStreamer's: <c>Gst.ParamFlags</c>, the other half of what a
/// GStreamer property description carries, is beside it.
/// </para>
/// </remarks>
public sealed unsafe class ParamSpecFraction : ParamSpec
{
    internal ParamSpecFraction(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Gets the numerator of the smallest fraction the property accepts.</summary>
    public int MinimumNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the denominator of the smallest fraction the property accepts.</summary>
    public int MinimumDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 4);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the numerator of the largest fraction the property accepts.</summary>
    public int MaximumNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 8);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>Gets the denominator of the largest fraction the property accepts.</summary>
    public int MaximumDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 12);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the numerator of the fraction the property has when nothing was
    /// written to it.
    /// </summary>
    public int DefaultNumerator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 16);
            GC.KeepAlive(this);
            return value;
        }
    }

    /// <summary>
    /// Gets the denominator of the fraction the property has when nothing was
    /// written to it.
    /// </summary>
    public int DefaultDenominator
    {
        get
        {
            int value = *(int*)((byte*)Handle + FieldsOffset + 20);
            GC.KeepAlive(this);
            return value;
        }
    }
}
