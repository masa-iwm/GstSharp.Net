using System.Runtime.CompilerServices;
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
/// <remarks>
/// <para>
/// GObject describes a property with a class of its own for every kind of
/// value — <c>GParamSpecInt</c> carries a range, <c>GParamSpecEnum</c> carries
/// a table of members — and the binding mirrors that: this is the surface every
/// specification has, and <see cref="FromNative"/> hands out the derived class
/// that matches the native one, so a caller can pattern match on
/// <see cref="ParamSpecInt"/> and read <c>Minimum</c> from it. Every member
/// declared here means the same thing on every derived class.
/// </para>
/// <para>
/// The GStreamer specific bits of <see cref="Flags"/> — <c>controllable</c> and
/// the three <c>mutable</c> states — are not members of
/// <see cref="ParamFlags"/>: cast the value to <c>Gst.ParamFlags</c> to read
/// them.
/// </para>
/// </remarks>
public class ParamSpec : IDisposable
{
    /// <summary>
    /// The offset of the first field a derived <c>GParamSpec</c> declares,
    /// that is <c>sizeof (GParamSpec)</c> on a 64 bit platform: the
    /// <c>GTypeInstance</c>, <c>name</c>, the padded <c>flags</c>,
    /// <c>value_type</c>, <c>owner_type</c>, <c>_nick</c>, <c>_blurb</c> and
    /// <c>qdata</c> take one slot each, and <c>ref_count</c> and
    /// <c>param_id</c> share the ninth.
    /// </summary>
    private protected const int FieldsOffset = 72;

    private nint _handle;

    /// <summary>
    /// Wraps a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="handle">The parameter specification to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    /// <remarks>
    /// This wraps the specification in <see cref="ParamSpec"/> itself, whatever
    /// the native class is: it is the constructor of the base class and does
    /// not look at the type of what it is given. <see cref="FromNative"/> is
    /// the one that picks the derived class, and it is what every member of the
    /// binding that hands a specification out calls.
    /// </remarks>
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
            // collect it from there on. Collecting it releases nothing - this
            // class has no finalizer, and the reference is dropped by Dispose
            // alone - so the keep alive states the extent of the borrow rather
            // than guarding against a finalizer that could end it.
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
    /// Gets the nickname of the property: the short label a user interface
    /// puts beside it, for example <c>Number of buffers</c>.
    /// </summary>
    /// <remarks>
    /// GObject always answers a nickname here. A specification installed
    /// without one falls back to the nickname of its redirect target, and then
    /// to <see cref="Name"/>.
    /// </remarks>
    public string Nick
    {
        get
        {
            string? nick = GMarshal.PtrToStringUtf8(GObjectNative.ParamSpecGetNick(Handle));
            GC.KeepAlive(this);
            return nick ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets the description of the property, the sentence a tool prints under
    /// its name, or <see langword="null"/> when the property was installed
    /// without one.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Nick"/> this really is absent for some properties:
    /// GObject has no fallback for it and answers the null pointer.
    /// </remarks>
    public string? Blurb
    {
        get
        {
            string? blurb = GMarshal.PtrToStringUtf8(GObjectNative.ParamSpecGetBlurb(Handle));
            GC.KeepAlive(this);
            return blurb;
        }
    }

    /// <summary>
    /// Gets the type that installed the property, which is the class the
    /// property is declared on rather than the one it was looked up through.
    /// </summary>
    public GType OwnerType
    {
        get
        {
            GType type = OwnerTypeOf(Handle);
            GC.KeepAlive(this);
            return type;
        }
    }

    /// <summary>
    /// Gets the class of the specification itself, that is
    /// <c>G_PARAM_SPEC_TYPE</c>: <c>GParamInt</c> for a property that carries a
    /// range of integers, <c>GParamEnum</c> for one that carries a member of an
    /// enumeration.
    /// </summary>
    /// <remarks>
    /// This is the type <see cref="FromNative"/> reads to choose the derived
    /// class, and it is not <see cref="ValueType"/>: an integer property has
    /// <c>GParamInt</c> here and <c>gint</c> there.
    /// </remarks>
    public GType NativeType
    {
        get
        {
            GType type = NativeTypeOf(Handle);
            GC.KeepAlive(this);
            return type;
        }
    }

    /// <summary>
    /// Gets the default value of the property, as the value GObject built for
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The view is <b>borrowed</b>. The value belongs to the specification,
    /// which builds it once on first use and keeps it for as long as it lives,
    /// so the view is only valid while this wrapper holds its reference. To
    /// keep the value, copy it into a <see cref="Value"/> of your own with
    /// <c>ToValue</c>; to write to it, copy it first, because writing through
    /// the view would change what every later reader of this specification
    /// sees.
    /// </para>
    /// <para>
    /// Every class of specification answers here, including the ones the
    /// binding wraps in <see cref="ParamSpec"/> itself: a class that installed
    /// no default gets the reset value of its type.
    /// </para>
    /// </remarks>
    public unsafe ValueView DefaultValue
    {
        get
        {
            GValueNative* value = GObjectNative.ParamSpecGetDefaultValue(Handle);
            GC.KeepAlive(this);
            return new ValueView(ref Unsafe.AsRef<GValueNative>(value));
        }
    }

    /// <summary>
    /// Gets the specification this one stands for, or <see langword="null"/>
    /// when it stands for nothing.
    /// </summary>
    /// <remarks>
    /// Only a <c>GParamSpecOverride</c> — what a class installs when it
    /// redeclares a property of an interface it implements — answers something
    /// here; every other class answers <see langword="null"/>. The wrapper that
    /// comes back is the caller's own and has to be disposed, as everything a
    /// member of the binding hands out does.
    /// </remarks>
    public ParamSpec? RedirectTarget
    {
        get
        {
            nint target = GObjectNative.ParamSpecGetRedirectTarget(Handle);
            GC.KeepAlive(this);
            return target == nint.Zero ? null : FromNative(target, Transfer.None);
        }
    }

    /// <summary>
    /// Wraps a native <c>GParamSpec</c> in the class that matches it.
    /// </summary>
    /// <param name="handle">The parameter specification to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> when the wrapper has to take its own.
    /// </param>
    /// <returns>
    /// A <see cref="ParamSpec"/> whose class is the derived one that matches
    /// <c>G_PARAM_SPEC_TYPE</c>, or <see cref="ParamSpec"/> itself when the
    /// binding declares no class for that type.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="handle"/> is <c>0</c>. Unlike
    /// <see cref="Object.FromNative{T}"/> this does not map the null pointer
    /// onto <see langword="null"/>: a specification is never optional at the
    /// point one is wrapped, and the callers that can be handed nothing test
    /// for it themselves.
    /// </exception>
    public static ParamSpec FromNative(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A parameter specification must not be null.");
        }

        return ParamSpecKinds.Of(NativeTypeOf(handle)) switch
        {
            ParamSpecKind.Boolean => new ParamSpecBoolean(handle, transfer),
            ParamSpecKind.Char => new ParamSpecChar(handle, transfer),
            ParamSpecKind.UChar => new ParamSpecUChar(handle, transfer),
            ParamSpecKind.Int => new ParamSpecInt(handle, transfer),
            ParamSpecKind.UInt => new ParamSpecUInt(handle, transfer),
            ParamSpecKind.Long => new ParamSpecLong(handle, transfer),
            ParamSpecKind.ULong => new ParamSpecULong(handle, transfer),
            ParamSpecKind.Int64 => new ParamSpecInt64(handle, transfer),
            ParamSpecKind.UInt64 => new ParamSpecUInt64(handle, transfer),
            ParamSpecKind.Float => new ParamSpecFloat(handle, transfer),
            ParamSpecKind.Double => new ParamSpecDouble(handle, transfer),
            ParamSpecKind.Unichar => new ParamSpecUnichar(handle, transfer),
            ParamSpecKind.Enum => new ParamSpecEnum(handle, transfer),
            ParamSpecKind.Flags => new ParamSpecFlags(handle, transfer),
            ParamSpecKind.String => new ParamSpecString(handle, transfer),
            ParamSpecKind.GType => new ParamSpecGType(handle, transfer),
            ParamSpecKind.Param => new ParamSpecParam(handle, transfer),
            ParamSpecKind.Boxed => new ParamSpecBoxed(handle, transfer),
            ParamSpecKind.Pointer => new ParamSpecPointer(handle, transfer),
            ParamSpecKind.Object => new ParamSpecObject(handle, transfer),
            ParamSpecKind.Variant => new ParamSpecVariant(handle, transfer),
            ParamSpecKind.ValueArray => new ParamSpecValueArray(handle, transfer),
            ParamSpecKind.Fraction => new Gst.ParamSpecFraction(handle, transfer),
            ParamSpecKind.Array => new Gst.ParamSpecArray(handle, transfer),
            _ => new ParamSpec(handle, transfer),
        };
    }

    /// <summary>
    /// Reads the class of a native <c>GParamSpec</c>, that is
    /// <c>G_PARAM_SPEC_TYPE</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>The type of the specification itself.</returns>
    /// <remarks>
    /// The macro is <c>G_TYPE_FROM_INSTANCE</c>, which reads the class pointer
    /// of the instance and the type out of that class: both are the first field
    /// of their structure.
    /// </remarks>
    internal static unsafe GType NativeTypeOf(nint pspec) =>
        pspec == nint.Zero ? GType.Invalid : new GType(*(nuint*)*(nint*)pspec);

    /// <summary>
    /// Reads the owner type out of a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>The type that installed the property.</returns>
    /// <remarks>
    /// GObject exposes the field through the <c>GParamSpec</c> structure only,
    /// so the offset is used, as <see cref="ValueTypeOf"/> does:
    /// <c>owner_type</c> is the slot behind <c>value_type</c>.
    /// </remarks>
    internal static unsafe GType OwnerTypeOf(nint pspec) =>
        pspec == nint.Zero ? GType.Invalid : new GType(*(nuint*)((byte*)pspec + (4 * sizeof(nint))));

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
    /// Reads the property identifier out of a native <c>GParamSpec</c>.
    /// </summary>
    /// <param name="pspec">The parameter specification to read.</param>
    /// <returns>
    /// The identifier the installing class gave it, or zero when it has never
    /// been installed.
    /// </returns>
    /// <remarks>
    /// <c>param_id</c> lives in the private part of <c>GParamSpec</c>, behind
    /// <c>GTypeInstance</c>, <c>name</c>, the padded <c>flags</c>,
    /// <c>value_type</c>, <c>owner_type</c>, <c>_nick</c>, <c>_blurb</c>,
    /// <c>qdata</c> and <c>ref_count</c>: eight pointer sized slots and the
    /// <c>guint</c> reference count, so the identifier is the <c>guint</c> that
    /// follows it. There is no accessor for it, and the installed-or-not
    /// question it answers has none either.
    /// </remarks>
    internal static unsafe uint ParamIdOf(nint pspec) =>
        pspec == nint.Zero ? 0u : *(uint*)((byte*)pspec + (8 * sizeof(nint)) + sizeof(uint));

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
