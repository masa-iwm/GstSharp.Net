using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The eight byte payload union of a <c>GValue</c>.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public struct ValueData
{
    [FieldOffset(0)]
    internal long Int64;

    [FieldOffset(0)]
    internal ulong UInt64;

    [FieldOffset(0)]
    internal double Double;

    [FieldOffset(0)]
    internal nint Pointer;
}

/// <summary>
/// The memory layout of a <c>GValue</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GValueNative
{
    internal nuint TypeValue;
    internal ValueData Data0;
    internal ValueData Data1;

    /// <summary>
    /// Gets the type of the value, or <see cref="GType.Invalid"/> when the
    /// value is not initialised.
    /// </summary>
    public readonly GType Type => new(TypeValue);
}

/// <summary>
/// A <c>GValue</c>, the dynamically typed box that GObject uses for properties
/// and signal arguments.
/// </summary>
/// <remarks>
/// <para>
/// The value type owns native resources, so it must be disposed exactly once,
/// normally with <c>using</c>:
/// </para>
/// <code>
/// using Value value = Value.New(GType.Int);
/// value.SetInt(42);
/// </code>
/// <para>
/// It must never be copied while it holds content: a copy shares the same
/// native payload, and disposing both would release it twice. Pass it by
/// <c>ref</c> or <c>in</c> instead, and use <see cref="Copy"/> for an
/// independent second value.
/// </para>
/// </remarks>
public struct Value : IDisposable
{
    internal GValueNative NativeValue;

    /// <summary>
    /// Gets the type of the value, or <see cref="GType.Invalid"/> when the
    /// value is not initialised.
    /// </summary>
    public readonly GType Type => NativeValue.Type;

    /// <summary>
    /// Gets a value indicating whether the value holds nothing at all.
    /// </summary>
    public readonly bool IsEmpty => NativeValue.TypeValue == GType.InvalidValue;

    /// <summary>
    /// Creates an empty value of the given type.
    /// </summary>
    /// <param name="type">The type of the new value.</param>
    /// <returns>The new value.</returns>
    public static Value New(GType type)
    {
        Value value = default;
        if (type.IsValid)
        {
            GObjectNative.ValueInit(ref value.NativeValue, type.Value);
        }

        return value;
    }

    /// <summary>
    /// Creates an independent copy of an existing native <c>GValue</c>.
    /// </summary>
    /// <param name="nativeValue">The <c>GValue</c> to copy.</param>
    /// <returns>The new value, which the caller has to dispose.</returns>
    public static unsafe Value CopyFrom(nint nativeValue)
    {
        if (nativeValue == nint.Zero)
        {
            return default;
        }

        ref GValueNative source = ref Unsafe.AsRef<GValueNative>((void*)nativeValue);
        Value value = New(source.Type);
        GObjectNative.ValueCopy(ref source, ref value.NativeValue);
        return value;
    }

    /// <summary>
    /// Creates an independent copy of this value.
    /// </summary>
    /// <returns>The copy, which the caller has to dispose.</returns>
    public Value Copy()
    {
        Value copy = New(Type);
        if (!IsEmpty)
        {
            GObjectNative.ValueCopy(ref NativeValue, ref copy.NativeValue);
        }

        return copy;
    }

    /// <summary>Stores a boolean.</summary>
    /// <param name="content">The value to store.</param>
    public void SetBoolean(bool content) => GObjectNative.ValueSetBoolean(ref NativeValue, content ? 1 : 0);

    /// <summary>Reads a boolean.</summary>
    /// <returns>The stored value.</returns>
    public readonly bool GetBoolean() => GObjectNative.ValueGetBoolean(ref AsMutable()) != 0;

    /// <summary>Stores a 32 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetInt(int content) => GObjectNative.ValueSetInt(ref NativeValue, content);

    /// <summary>Reads a 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly int GetInt() => GObjectNative.ValueGetInt(ref AsMutable());

    /// <summary>Stores an unsigned 32 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetUInt(uint content) => GObjectNative.ValueSetUInt(ref NativeValue, content);

    /// <summary>Reads an unsigned 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly uint GetUInt() => GObjectNative.ValueGetUInt(ref AsMutable());

    /// <summary>Stores a 64 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetInt64(long content) => GObjectNative.ValueSetInt64(ref NativeValue, content);

    /// <summary>Reads a 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly long GetInt64() => GObjectNative.ValueGetInt64(ref AsMutable());

    /// <summary>Stores an unsigned 64 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetUInt64(ulong content) => GObjectNative.ValueSetUInt64(ref NativeValue, content);

    /// <summary>Reads an unsigned 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly ulong GetUInt64() => GObjectNative.ValueGetUInt64(ref AsMutable());

    /// <summary>Stores a C <c>long</c>.</summary>
    /// <param name="content">The value to store.</param>
    public void SetLong(nint content) => GObjectNative.ValueSetLong(ref NativeValue, new CLong(content));

    /// <summary>Reads a C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetLong() => GObjectNative.ValueGetLong(ref AsMutable()).Value;

    /// <summary>Stores an unsigned C <c>long</c>.</summary>
    /// <param name="content">The value to store.</param>
    public void SetULong(nuint content) => GObjectNative.ValueSetULong(ref NativeValue, new CULong(content));

    /// <summary>Reads an unsigned C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public readonly nuint GetULong() => GObjectNative.ValueGetULong(ref AsMutable()).Value;

    /// <summary>Stores a single precision number.</summary>
    /// <param name="content">The value to store.</param>
    public void SetFloat(float content) => GObjectNative.ValueSetFloat(ref NativeValue, content);

    /// <summary>Reads a single precision number.</summary>
    /// <returns>The stored value.</returns>
    public readonly float GetFloat() => GObjectNative.ValueGetFloat(ref AsMutable());

    /// <summary>Stores a double precision number.</summary>
    /// <param name="content">The value to store.</param>
    public void SetDouble(double content) => GObjectNative.ValueSetDouble(ref NativeValue, content);

    /// <summary>Reads a double precision number.</summary>
    /// <returns>The stored value.</returns>
    public readonly double GetDouble() => GObjectNative.ValueGetDouble(ref AsMutable());

    /// <summary>Stores a copy of a string.</summary>
    /// <param name="content">The value to store, may be <see langword="null"/>.</param>
    public unsafe void SetString(string? content)
    {
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(content, buffer);
        GObjectNative.ValueSetString(ref NativeValue, scope.Pointer);
    }

    /// <summary>Reads a string.</summary>
    /// <returns>The stored value, or <see langword="null"/>.</returns>
    public readonly string? GetString() => GMarshal.PtrToStringUtf8(GObjectNative.ValueGetString(ref AsMutable()));

    /// <summary>Stores an untyped pointer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetPointer(nint content) => GObjectNative.ValueSetPointer(ref NativeValue, content);

    /// <summary>Reads an untyped pointer.</summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetPointer() => GObjectNative.ValueGetPointer(ref AsMutable());

    /// <summary>Stores a type.</summary>
    /// <param name="content">The value to store.</param>
    public void SetGType(GType content) => GObjectNative.ValueSetGType(ref NativeValue, content.Value);

    /// <summary>Reads a type.</summary>
    /// <returns>The stored value.</returns>
    public readonly GType GetGType() => new(GObjectNative.ValueGetGType(ref AsMutable()));

    /// <summary>Stores an enumeration member.</summary>
    /// <param name="content">The value to store.</param>
    public void SetEnum(int content) => GObjectNative.ValueSetEnum(ref NativeValue, content);

    /// <summary>Reads an enumeration member.</summary>
    /// <returns>The stored value.</returns>
    public readonly int GetEnum() => GObjectNative.ValueGetEnum(ref AsMutable());

    /// <summary>Stores a set of flags.</summary>
    /// <param name="content">The value to store.</param>
    public void SetFlags(uint content) => GObjectNative.ValueSetFlags(ref NativeValue, content);

    /// <summary>Reads a set of flags.</summary>
    /// <returns>The stored value.</returns>
    public readonly uint GetFlags() => GObjectNative.ValueGetFlags(ref AsMutable());

    /// <summary>
    /// Stores an object. The value takes its own reference.
    /// </summary>
    /// <param name="content">The object to store, may be <see langword="null"/>.</param>
    public void SetObject(Object? content) =>
        GObjectNative.ValueSetObject(ref NativeValue, content?.Handle ?? nint.Zero);

    /// <summary>
    /// Reads an object.
    /// </summary>
    /// <returns>
    /// The wrapper of the stored object, or <see langword="null"/> when the
    /// value holds nothing.
    /// </returns>
    public readonly Object? GetObject() =>
        Object.FromNative(GObjectNative.ValueGetObject(ref AsMutable()), Transfer.None);

    /// <summary>
    /// Stores a boxed value. The value takes its own copy.
    /// </summary>
    /// <param name="content">The boxed value to store.</param>
    public void SetBoxed(nint content) => GObjectNative.ValueSetBoxed(ref NativeValue, content);

    /// <summary>
    /// Reads a boxed value, which stays owned by this value.
    /// </summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetBoxed() => GObjectNative.ValueGetBoxed(ref AsMutable());

    /// <summary>
    /// Reads the content of the value as a managed object, based on its
    /// fundamental type.
    /// </summary>
    /// <returns>
    /// The content: a primitive for the numeric types, a
    /// <see cref="string"/>, an <see cref="Object"/> wrapper, or the raw
    /// pointer for boxed and pointer types.
    /// </returns>
    public readonly object? GetContent()
    {
        nuint fundamental = GObjectNative.TypeFundamental(NativeValue.TypeValue);

        return fundamental switch
        {
            GType.InvalidValue => null,
            GType.BooleanValue => GetBoolean(),
            GType.CharValue => (sbyte)GetInt(),
            GType.UCharValue => (byte)GetUInt(),
            GType.IntValue => GetInt(),
            GType.UIntValue => GetUInt(),
            GType.LongValue => GetLong(),
            GType.ULongValue => GetULong(),
            GType.Int64Value => GetInt64(),
            GType.UInt64Value => GetUInt64(),
            GType.FloatValue => GetFloat(),
            GType.DoubleValue => GetDouble(),
            GType.StringValue => GetString(),
            GType.EnumValue => GetEnum(),
            GType.FlagsValue => GetFlags(),
            GType.ObjectValue => GetObject(),
            GType.BoxedValue => GetBoxed(),
            GType.ParamValue => GetPointer(),
            _ => GetPointer(),
        };
    }

    /// <summary>
    /// Releases the content of the value.
    /// </summary>
    public void Dispose()
    {
        if (NativeValue.TypeValue != GType.InvalidValue)
        {
            GObjectNative.ValueUnset(ref NativeValue);
            NativeValue = default;
        }
    }

    private readonly ref GValueNative AsMutable() => ref Unsafe.AsRef(in NativeValue);
}
