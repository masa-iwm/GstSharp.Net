using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The <c>GParamFlags</c> of a property: what may be done with it and when.
/// </summary>
/// <remarks>
/// Only the members that GObject itself defines are listed. A property that is
/// both <see cref="Readable"/> and <see cref="Writable"/> is what C spells
/// <c>G_PARAM_READWRITE</c>, and that pair is the filter a tool applies before
/// it prints a property as something a pipeline description could carry.
/// </remarks>
[Flags]
public enum ParamFlags : uint
{
    /// <summary>No flag at all.</summary>
    None = 0,

    /// <summary>The property can be read.</summary>
    Readable = 1 << 0,

    /// <summary>The property can be written.</summary>
    Writable = 1 << 1,

    /// <summary>The property can only be written while the object is built.</summary>
    Construct = 1 << 2,

    /// <summary>The property can only be given to the constructor.</summary>
    ConstructOnly = 1 << 3,

    /// <summary>A value outside the range of the property is corrected rather than refused.</summary>
    LaxValidation = 1 << 4,

    /// <summary>The name, the nickname and the blurb are static strings.</summary>
    StaticStrings = (1 << 5) | (1 << 6) | (1 << 7),

    /// <summary>The name is a static string.</summary>
    StaticName = 1 << 5,

    /// <summary>The nickname is a static string.</summary>
    StaticNick = 1 << 6,

    /// <summary>The blurb is a static string.</summary>
    StaticBlurb = 1 << 7,

    /// <summary>
    /// <c>notify</c> is not emitted for every write: the implementation emits
    /// it itself, when the value really changed.
    /// </summary>
    ExplicitNotify = 1u << 30,

    /// <summary>The property is deprecated.</summary>
    Deprecated = 1u << 31,
}

/// <summary>
/// A <c>GParamSpec</c>: the description of one property of a class.
/// </summary>
public sealed class ParamSpec : IDisposable
{
    private nint _handle;

    /// <summary>
    /// Wraps a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="handle">The parameter specification to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    public ParamSpec(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A parameter specification must not be null.");
        }

        _handle = transfer == Transfer.Full ? handle : GObjectNative.ParamSpecRefSink(handle);
    }

    /// <summary>
    /// Gets the native <c>GParamSpec</c>.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets the name of the property, for example <c>uri</c>.
    /// </summary>
    public string Name
    {
        get
        {
            string? name = GMarshal.PtrToStringUtf8(GObjectNative.ParamSpecGetName(Handle));

            // The string belongs to the specification, so it is only there for
            // as long as this wrapper holds its reference: reading Handle is
            // the last use of the wrapper, and the collector is free to
            // finalize it from there on.
            GC.KeepAlive(this);
            return name ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets the type of the values of the property.
    /// </summary>
    public GType ValueType
    {
        get
        {
            GType type = ValueTypeOf(Handle);
            GC.KeepAlive(this);
            return type;
        }
    }

    /// <summary>
    /// Gets what may be done with the property.
    /// </summary>
    /// <remarks>
    /// This is what tells a property a pipeline description could set from one
    /// it could not: <c>Readable | Writable</c> is the test the C tools apply,
    /// and <see cref="ParamFlags.ConstructOnly"/> is the one that has to be
    /// given to the constructor rather than assigned afterwards.
    /// </remarks>
    public ParamFlags Flags
    {
        get
        {
            ParamFlags flags = FlagsOf(Handle);
            GC.KeepAlive(this);
            return flags;
        }
    }

    /// <summary>
    /// Reads the value type out of a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>The type of the values of the property.</returns>
    /// <remarks>
    /// GObject exposes the field through the <c>G_PARAM_SPEC_VALUE_TYPE</c>
    /// macro only, so the offset of the public structure is used:
    /// <c>GTypeInstance</c>, <c>name</c> and the padded <c>flags</c> take three
    /// pointer sized slots.
    /// </remarks>
    internal static unsafe GType ValueTypeOf(nint pspec) =>
        pspec == nint.Zero ? GType.Invalid : new GType(*(nuint*)((byte*)pspec + (3 * sizeof(nint))));

    /// <summary>
    /// Reads the flags out of a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>What may be done with the property.</returns>
    /// <remarks>
    /// GObject exposes the field through the <c>GParamSpec</c> structure only
    /// and has no accessor for it, so the offset is used, as
    /// <see cref="ValueTypeOf"/> does: <c>GTypeInstance</c> and <c>name</c>
    /// take one pointer sized slot each, and <c>flags</c> is the <c>guint</c>
    /// that follows them. It is padded up to the alignment of the
    /// <c>GType</c> behind it, which is what puts <c>value_type</c> at three
    /// slots rather than at two and a half.
    /// </remarks>
    internal static unsafe ParamFlags FlagsOf(nint pspec) =>
        pspec == nint.Zero ? ParamFlags.None : (ParamFlags)(*(uint*)((byte*)pspec + (2 * sizeof(nint))));

    /// <summary>
    /// Releases the reference this wrapper holds.
    /// </summary>
    public void Dispose()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            GObjectNative.ParamSpecUnref(handle);
        }
    }
}
