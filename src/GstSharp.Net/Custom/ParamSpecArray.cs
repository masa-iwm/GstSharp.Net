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
    /// Builds the specification of an array property, whose elements are all
    /// described by one specification of their own.
    /// </summary>
    /// <param name="name">
    /// The name of the property. It begins with an ASCII letter and carries
    /// ASCII letters, digits, <c>-</c> and <c>_</c> only, and GObject rewrites
    /// every <c>_</c> in it to <c>-</c>.
    /// </param>
    /// <param name="nick">A short label for the property, may be <see langword="null"/>.</param>
    /// <param name="blurb">A description of the property, may be <see langword="null"/>.</param>
    /// <param name="elementSpec">
    /// The description of one element. The array takes a reference of its own on
    /// it and releases that when the array specification itself is finalised, so
    /// the wrapper passed in keeps its reference and may be disposed as usual.
    /// </param>
    /// <param name="flags">
    /// What may be done with the property. The three flags of
    /// <see cref="ParamFlags.StaticStrings"/> are dropped silently: they tell
    /// GObject to keep the caller's pointers, and the three strings are encoded
    /// into buffers this method releases.
    /// </param>
    /// <returns>
    /// The new specification, holding the only reference to it: GStreamer hands
    /// out a floating specification and the wrapper sinks it.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="name"/> or <paramref name="elementSpec"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> does not name a property, or one of the strings
    /// contains a null character.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// GStreamer refused to build the specification and answered nothing.
    /// </exception>
    public static ParamSpecArray New(
        string name,
        string? nick,
        string? blurb,
        ParamSpec elementSpec,
        ParamFlags flags)
    {
        ArgumentNullException.ThrowIfNull(elementSpec);

        using ParamSpecFactory.Strings strings = ParamSpecFactory.Prepare(name, nick, blurb);
        nint handle = GstNative.ParamSpecArray(
            strings.Name,
            strings.Nick,
            strings.Blurb,
            elementSpec.Handle,
            ParamSpecFactory.Sanitize(flags));
        GC.KeepAlive(elementSpec);

        return new ParamSpecArray(ParamSpecFactory.Require(handle), Transfer.None);
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
