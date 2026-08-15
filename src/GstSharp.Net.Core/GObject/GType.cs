using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GType</c>: the run time identifier of a GObject type.
/// </summary>
/// <remarks>
/// The fundamental types are the constants that GLib builds with
/// <c>G_TYPE_MAKE_FUNDAMENTAL</c>, that is the index of the type shifted left
/// by two.
/// </remarks>
public readonly struct GType : IEquatable<GType>
{
    internal const nuint InvalidValue = 0;
    internal const nuint NoneValue = 1u << 2;
    internal const nuint InterfaceValue = 2u << 2;
    internal const nuint CharValue = 3u << 2;
    internal const nuint UCharValue = 4u << 2;
    internal const nuint BooleanValue = 5u << 2;
    internal const nuint IntValue = 6u << 2;
    internal const nuint UIntValue = 7u << 2;
    internal const nuint LongValue = 8u << 2;
    internal const nuint ULongValue = 9u << 2;
    internal const nuint Int64Value = 10u << 2;
    internal const nuint UInt64Value = 11u << 2;
    internal const nuint EnumValue = 12u << 2;
    internal const nuint FlagsValue = 13u << 2;
    internal const nuint FloatValue = 14u << 2;
    internal const nuint DoubleValue = 15u << 2;
    internal const nuint StringValue = 16u << 2;
    internal const nuint PointerValue = 17u << 2;
    internal const nuint BoxedValue = 18u << 2;
    internal const nuint ParamValue = 19u << 2;
    internal const nuint ObjectValue = 20u << 2;
    internal const nuint VariantValue = 21u << 2;

    /// <summary>
    /// Initialises a type from its raw value.
    /// </summary>
    /// <param name="value">The raw <c>GType</c>.</param>
    public GType(nuint value) => Value = value;

    /// <summary>Gets <c>G_TYPE_INVALID</c>.</summary>
    public static GType Invalid => new(InvalidValue);

    /// <summary>Gets <c>G_TYPE_NONE</c>.</summary>
    public static GType None => new(NoneValue);

    /// <summary>Gets <c>G_TYPE_INTERFACE</c>.</summary>
    public static GType Interface => new(InterfaceValue);

    /// <summary>Gets <c>G_TYPE_CHAR</c>.</summary>
    public static GType Char => new(CharValue);

    /// <summary>Gets <c>G_TYPE_UCHAR</c>.</summary>
    public static GType UChar => new(UCharValue);

    /// <summary>Gets <c>G_TYPE_BOOLEAN</c>.</summary>
    public static GType Boolean => new(BooleanValue);

    /// <summary>Gets <c>G_TYPE_INT</c>.</summary>
    public static GType Int => new(IntValue);

    /// <summary>Gets <c>G_TYPE_UINT</c>.</summary>
    public static GType UInt => new(UIntValue);

    /// <summary>Gets <c>G_TYPE_LONG</c>.</summary>
    public static GType Long => new(LongValue);

    /// <summary>Gets <c>G_TYPE_ULONG</c>.</summary>
    public static GType ULong => new(ULongValue);

    /// <summary>Gets <c>G_TYPE_INT64</c>.</summary>
    public static GType Int64 => new(Int64Value);

    /// <summary>Gets <c>G_TYPE_UINT64</c>.</summary>
    public static GType UInt64 => new(UInt64Value);

    /// <summary>Gets <c>G_TYPE_ENUM</c>.</summary>
    public static GType Enum => new(EnumValue);

    /// <summary>Gets <c>G_TYPE_FLAGS</c>.</summary>
    public static GType Flags => new(FlagsValue);

    /// <summary>Gets <c>G_TYPE_FLOAT</c>.</summary>
    public static GType Float => new(FloatValue);

    /// <summary>Gets <c>G_TYPE_DOUBLE</c>.</summary>
    public static GType Double => new(DoubleValue);

    /// <summary>Gets <c>G_TYPE_STRING</c>.</summary>
    public static GType String => new(StringValue);

    /// <summary>Gets <c>G_TYPE_POINTER</c>.</summary>
    public static GType Pointer => new(PointerValue);

    /// <summary>Gets <c>G_TYPE_BOXED</c>.</summary>
    public static GType Boxed => new(BoxedValue);

    /// <summary>Gets <c>G_TYPE_PARAM</c>.</summary>
    public static GType Param => new(ParamValue);

    /// <summary>Gets <c>G_TYPE_OBJECT</c>.</summary>
    public static GType Object => new(ObjectValue);

    /// <summary>Gets <c>G_TYPE_VARIANT</c>.</summary>
    public static GType Variant => new(VariantValue);

    /// <summary>
    /// Gets the raw value of the type.
    /// </summary>
    public nuint Value { get; }

    /// <summary>
    /// Gets a value indicating whether this is a valid type.
    /// </summary>
    public bool IsValid => Value != InvalidValue;

    /// <summary>
    /// Gets the registered name of the type.
    /// </summary>
    public string Name =>
        Value == InvalidValue ? "invalid" : GMarshal.PtrToStringUtf8(GObjectNative.TypeName(Value)) ?? "invalid";

    /// <summary>
    /// Gets the type this one derives from, or <see cref="Invalid"/> for a
    /// fundamental type.
    /// </summary>
    public GType Parent => new(GObjectNative.TypeParent(Value));

    /// <summary>
    /// Gets the fundamental type this one is built on.
    /// </summary>
    public GType Fundamental => new(GObjectNative.TypeFundamental(Value));

    /// <summary>Compares two types for equality.</summary>
    /// <param name="left">The first type.</param>
    /// <param name="right">The second type.</param>
    /// <returns><see langword="true"/> when both are the same type.</returns>
    public static bool operator ==(GType left, GType right) => left.Equals(right);

    /// <summary>Compares two types for inequality.</summary>
    /// <param name="left">The first type.</param>
    /// <param name="right">The second type.</param>
    /// <returns><see langword="true"/> when both are different types.</returns>
    public static bool operator !=(GType left, GType right) => !left.Equals(right);

    /// <summary>
    /// Looks a registered type up by name.
    /// </summary>
    /// <param name="name">The name of the type, for example <c>GstElement</c>.</param>
    /// <returns>The type, or <see cref="Invalid"/> when it is not registered.</returns>
    public static unsafe GType FromName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);
        return new GType(GObjectNative.TypeFromName(scope.Pointer));
    }

    /// <summary>
    /// Tests whether this type is <paramref name="other"/> or derives from it.
    /// </summary>
    /// <param name="other">The type to test against.</param>
    /// <returns><see langword="true"/> when the types are compatible.</returns>
    public bool IsA(GType other) => GObjectNative.TypeIsA(Value, other.Value) != 0;

    /// <inheritdoc/>
    public bool Equals(GType other) => Value == other.Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is GType other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode();

    /// <summary>
    /// Returns the name of the type.
    /// </summary>
    /// <returns>The registered name.</returns>
    public override string ToString() => Name;
}
