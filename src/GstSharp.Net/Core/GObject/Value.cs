using System.Globalization;
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
    /// <summary>The smallest number a C <c>long</c> holds on this platform.</summary>
    private static readonly long CLongMinimum = IsNarrowCLong() ? int.MinValue : long.MinValue;

    /// <summary>The largest number a C <c>long</c> holds on this platform.</summary>
    private static readonly long CLongMaximum = IsNarrowCLong() ? int.MaxValue : long.MaxValue;

    /// <summary>The largest number a C <c>unsigned long</c> holds on this platform.</summary>
    private static readonly ulong CULongMaximum = IsNarrowCLong() ? uint.MaxValue : ulong.MaxValue;

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

    /// <summary>
    /// Creates a value of an expected type from a managed object, which is how
    /// the arguments of a dynamically emitted signal are converted.
    /// </summary>
    /// <param name="content">
    /// The value to store. <see langword="null"/> is only accepted by the types
    /// that have a null value: strings, objects, boxed values, parameter
    /// specifications, variants and pointers.
    /// </param>
    /// <param name="expected">The type the value has to hold.</param>
    /// <returns>The new value, which the caller has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// The conversion is a switch over the fundamental type of
    /// <paramref name="expected"/>, so it needs neither reflection nor a
    /// generated table:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// the numeric types accept their own managed type and the ones that widen
    /// into it without loss, so an <see cref="int"/> is accepted for a
    /// <c>gint64</c> but not the other way round;
    /// </description></item>
    /// <item><description>
    /// an enumeration or a set of flags accepts any managed
    /// <see cref="System.Enum"/> whose value fits, as well as a plain
    /// <see cref="int"/> or <see cref="uint"/>. The generated enumerations of
    /// the bindings are not known here, so their <c>GType</c> is not checked
    /// and the numeric value is what is passed on;
    /// </description></item>
    /// <item><description>
    /// an object accepts an <see cref="Object"/> whose type derives from
    /// <paramref name="expected"/>, and a boxed value accepts a
    /// <see cref="Boxed"/> wrapper or the raw handle of one. A mini object,
    /// which is a boxed type as far as GObject is concerned, is passed as its
    /// <c>Handle</c>: the value takes a reference of its own, so the wrapper
    /// stays the owner of the one it holds.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="expected"/> is not a type a value can hold, or
    /// <paramref name="content"/> is not compatible with it.
    /// </exception>
    public static Value CreateFor(object? content, GType expected)
    {
        if (!expected.IsValid || expected == GType.None)
        {
            throw new ArgumentException(
                $"A value cannot hold anything of type {expected.Name}.",
                nameof(expected));
        }

        Value value = New(expected);

        try
        {
            value.Fill(content, expected);
        }
        catch
        {
            value.Dispose();
            throw;
        }

        return value;
    }

    /// <summary>Stores a boolean.</summary>
    /// <param name="content">The value to store.</param>
    public void SetBoolean(bool content) => GObjectNative.ValueSetBoolean(ref NativeValue, content ? 1 : 0);

    /// <summary>Stores a signed 8 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetSChar(sbyte content) => GObjectNative.ValueSetSChar(ref NativeValue, content);

    /// <summary>Reads a signed 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly sbyte GetSChar() => GObjectNative.ValueGetSChar(ref AsMutable());

    /// <summary>Stores an unsigned 8 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    public void SetUChar(byte content) => GObjectNative.ValueSetUChar(ref NativeValue, content);

    /// <summary>Reads an unsigned 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly byte GetUChar() => GObjectNative.ValueGetUChar(ref AsMutable());

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
    /// Stores a parameter specification. The value takes its own reference.
    /// </summary>
    /// <param name="content">The <c>GParamSpec</c> to store.</param>
    public void SetParam(nint content) => GObjectNative.ValueSetParam(ref NativeValue, content);

    /// <summary>
    /// Reads a parameter specification, which stays owned by this value.
    /// </summary>
    /// <returns>The stored <c>GParamSpec</c>.</returns>
    public readonly nint GetParam() => GObjectNative.ValueGetParam(ref AsMutable());

    /// <summary>
    /// Stores a variant. The value takes its own reference.
    /// </summary>
    /// <param name="content">The <c>GVariant</c> to store.</param>
    public void SetVariant(nint content) => GObjectNative.ValueSetVariant(ref NativeValue, content);

    /// <summary>
    /// Reads a variant, which stays owned by this value.
    /// </summary>
    /// <returns>The stored <c>GVariant</c>.</returns>
    public readonly nint GetVariant() => GObjectNative.ValueGetVariant(ref AsMutable());

    /// <summary>
    /// Reads the content of the value as a managed object, based on its
    /// fundamental type.
    /// </summary>
    /// <returns>
    /// The content: a primitive for the numeric types, a
    /// <see cref="string"/>, an <see cref="Object"/> wrapper, or the raw
    /// pointer for boxed, parameter, variant and pointer types.
    /// </returns>
    public readonly object? GetContent()
    {
        GType type = Type;
        nuint fundamental = GObjectNative.TypeFundamental(NativeValue.TypeValue);

        return fundamental switch
        {
            GType.InvalidValue => null,
            GType.BooleanValue => GetBoolean(),
            GType.CharValue => GetSChar(),
            GType.UCharValue => GetUChar(),
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

            // Every one of these has an accessor of its own; g_value_get_pointer
            // on them is a fatal warning rather than a cast.
            GType.ParamValue => GetParam(),
            GType.VariantValue => GetVariant(),

            // An interface with GObject among its prerequisites holds an
            // object. One without is not something a value can be read as: it
            // has no accessor of its own and g_value_get_pointer on it is an
            // assertion failure in GLib rather than a cast.
            GType.InterfaceValue => Type.IsA(GType.Object) ? GetObject() : throw Unreadable(type, fundamental),
            GType.PointerValue => GetPointer(),

            // A fundamental type that GLib itself does not define. Reading it
            // as a pointer would be a guess about a layout nothing here knows,
            // and a wrong guess is a crash rather than a wrong answer.
            _ => throw Unreadable(type, fundamental),
        };

        static NotSupportedException Unreadable(GType type, nuint fundamental) => new(
            $"A value of type {type.Name} cannot be read as a managed object: " +
            $"its fundamental type {new GType(fundamental).Name} has no accessor here. " +
            "Read the value with the accessor of its own type.");
    }

    /// <summary>
    /// Reads the content the way the dynamic signal layer hands it to
    /// application code: as <see cref="GetContent"/> does, except that a null
    /// boxed value, pointer, parameter specification or variant reads as
    /// <see langword="null"/> rather than as a zero pointer.
    /// </summary>
    /// <returns>The content.</returns>
    internal readonly object? GetDynamicContent()
    {
        object? content = GetContent();

        if (content is nint pointer && pointer == nint.Zero)
        {
            nuint fundamental = GObjectNative.TypeFundamental(NativeValue.TypeValue);
            if (fundamental is GType.BoxedValue or GType.PointerValue or GType.ParamValue or GType.VariantValue)
            {
                return null;
            }
        }

        return content;
    }

    /// <summary>
    /// Reads the content the way an emission hands it back to the caller: as
    /// <see cref="GetDynamicContent"/> does, except that the caller becomes an
    /// owner of a boxed value, of a parameter specification and of a variant.
    /// </summary>
    /// <returns>The content.</returns>
    /// <remarks>
    /// The value that holds the result of an emission is unset as soon as the
    /// emission is over, which would release a boxed return value with it, so
    /// what is handed out is a reference of its own. For a mini object, whose
    /// boxed copy function is <c>gst_mini_object_ref</c>, that is the reference
    /// the wrapper of the caller adopts.
    /// </remarks>
    internal readonly object? TakeDynamicContent()
    {
        nuint fundamental = GObjectNative.TypeFundamental(NativeValue.TypeValue);

        switch (fundamental)
        {
            case GType.BoxedValue:
                return Owned(GObjectNative.ValueDupBoxed(ref AsMutable()));
            case GType.ParamValue:
                return Owned(GObjectNative.ValueDupParam(ref AsMutable()));
            case GType.VariantValue:
                return Owned(GObjectNative.ValueDupVariant(ref AsMutable()));
            default:
                return GetDynamicContent();
        }

        static object? Owned(nint handle) => handle == nint.Zero ? null : handle;
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

    /// <summary>
    /// Tells whether a C <c>long</c> is 32 bits wide here, which it is on
    /// Windows and on every 32 bit platform.
    /// </summary>
    /// <returns><see langword="true"/> for a 32 bit C <c>long</c>.</returns>
    private static bool IsNarrowCLong() => OperatingSystem.IsWindows() || nint.Size == 4;

    private static ArgumentException Mismatch(object? content, GType expected) =>
        new($"A value of type {expected.Name} cannot be created from {Describe(content)}.");

    private static string Describe(object? content) => content is null ? "null" : content.GetType().ToString();

    /// <summary>
    /// Converts anything that widens into a signed 64 bit integer without
    /// losing information.
    /// </summary>
    /// <param name="content">The value to convert.</param>
    /// <param name="result">The converted value.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    private static bool TryToInt64(object? content, out long result)
    {
        switch (content)
        {
            case sbyte number:
                result = number;
                return true;
            case byte number:
                result = number;
                return true;
            case short number:
                result = number;
                return true;
            case ushort number:
                result = number;
                return true;
            case int number:
                result = number;
                return true;
            case uint number:
                result = number;
                return true;
            case long number:
                result = number;
                return true;
            case ulong number when number <= long.MaxValue:
                result = (long)number;
                return true;
            case nint number:
                result = number;
                return true;
            case nuint number when number <= long.MaxValue:
                result = (long)number;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Converts anything that widens into an unsigned 64 bit integer without
    /// losing information, which includes the non negative signed values.
    /// </summary>
    /// <param name="content">The value to convert.</param>
    /// <param name="result">The converted value.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    private static bool TryToUInt64(object? content, out ulong result)
    {
        switch (content)
        {
            case ulong number:
                result = number;
                return true;
            case nuint number:
                result = number;
                return true;
            default:
                if (TryToInt64(content, out long signed) && signed >= 0)
                {
                    result = (ulong)signed;
                    return true;
                }

                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Converts anything numeric into a double precision number.
    /// </summary>
    /// <param name="content">The value to convert.</param>
    /// <param name="result">The converted value.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    private static bool TryToDouble(object? content, out double result)
    {
        switch (content)
        {
            case double number:
                result = number;
                return true;
            case float number:
                result = number;
                return true;
            default:
                if (TryToInt64(content, out long integer))
                {
                    result = integer;
                    return true;
                }

                result = 0;
                return false;
        }
    }

    /// <summary>
    /// Reads the numeric value of an enumeration member, of a set of flags or
    /// of a plain integer.
    /// </summary>
    /// <param name="content">The value to convert.</param>
    /// <param name="result">The numeric value.</param>
    /// <returns><see langword="true"/> when the conversion is possible.</returns>
    /// <remarks>
    /// A boxed enumeration is not an <see cref="int"/> to a type pattern, and
    /// the <c>GType</c> of a generated enumeration is not known down here, so
    /// the member travels as its number. <see cref="System.Enum"/> implements
    /// <see cref="IConvertible"/>, which is a plain interface call rather than
    /// reflection.
    /// </remarks>
    private static bool TryToEnumValue(object? content, out long result)
    {
        if (content is Enum member)
        {
            try
            {
                result = Convert.ToInt64(member, CultureInfo.InvariantCulture);
                return true;
            }
            catch (OverflowException)
            {
                // An enumeration whose underlying type is a 64 bit unsigned
                // integer holding a value above long.MaxValue: no GObject
                // enumeration is that wide, so it is a mismatch.
                result = 0;
                return false;
            }
        }

        return TryToInt64(content, out result);
    }

    /// <summary>
    /// Stores a managed object in this value, which is already initialised to
    /// <paramref name="expected"/>.
    /// </summary>
    /// <param name="content">The value to store.</param>
    /// <param name="expected">The type of this value.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="content"/> does not fit <paramref name="expected"/>.
    /// </exception>
    private void Fill(object? content, GType expected)
    {
        nuint fundamental = GObjectNative.TypeFundamental(expected.Value);

        // An interface that has GObject among its prerequisites holds an object
        // as well, and its values are collected the same way.
        if (fundamental == GType.ObjectValue ||
            (fundamental == GType.InterfaceValue && expected.IsA(GType.Object)))
        {
            switch (content)
            {
                case null:
                    SetObject(null);
                    return;
                case Object instance when instance.NativeType.IsA(expected):
                    SetObject(instance);
                    return;
                default:
                    throw Mismatch(content, expected);
            }
        }

        switch (fundamental)
        {
            case GType.BooleanValue when content is bool boolean:
                SetBoolean(boolean);
                return;

            case GType.CharValue when TryToInt64(content, out long number) &&
                number is >= sbyte.MinValue and <= sbyte.MaxValue:
                SetSChar((sbyte)number);
                return;

            case GType.UCharValue when TryToUInt64(content, out ulong number) && number <= byte.MaxValue:
                SetUChar((byte)number);
                return;

            case GType.IntValue when TryToInt64(content, out long number) &&
                number is >= int.MinValue and <= int.MaxValue:
                SetInt((int)number);
                return;

            case GType.UIntValue when TryToUInt64(content, out ulong number) && number <= uint.MaxValue:
                SetUInt((uint)number);
                return;

            case GType.LongValue when TryToInt64(content, out long number) &&
                number >= CLongMinimum && number <= CLongMaximum:
                SetLong((nint)number);
                return;

            case GType.ULongValue when TryToUInt64(content, out ulong number) && number <= CULongMaximum:
                SetULong((nuint)number);
                return;

            case GType.Int64Value when TryToInt64(content, out long number):
                SetInt64(number);
                return;

            case GType.UInt64Value when TryToUInt64(content, out ulong number):
                SetUInt64(number);
                return;

            case GType.FloatValue when TryToDouble(content, out double number):
                SetFloat((float)number);
                return;

            case GType.DoubleValue when TryToDouble(content, out double number):
                SetDouble(number);
                return;

            case GType.StringValue when content is null or string:
                SetString(content as string);
                return;

            case GType.EnumValue when TryToEnumValue(content, out long number) &&
                number is >= int.MinValue and <= int.MaxValue:
                SetEnum((int)number);
                return;

            case GType.FlagsValue when TryToEnumValue(content, out long number) &&
                number is >= 0 and <= uint.MaxValue:
                SetFlags((uint)number);
                return;

            case GType.BoxedValue when content is null:
                SetBoxed(nint.Zero);
                return;

            case GType.BoxedValue when content is Boxed boxed && boxed.BoxedType.IsA(expected):
                SetBoxed(boxed.Handle);
                return;

            // A mini object is a boxed type, and its wrapper lives in the Gst
            // assembly rather than here, so it is passed as its handle. The
            // value takes a reference of its own through the copy function of
            // the boxed type, which for a mini object is gst_mini_object_ref.
            case GType.BoxedValue when content is nint boxed:
                SetBoxed(boxed);
                return;

            case GType.ParamValue when content is null:
                SetParam(nint.Zero);
                return;

            case GType.ParamValue when content is ParamSpec specification:
                SetParam(specification.Handle);
                return;

            case GType.ParamValue when content is nint specification:
                SetParam(specification);
                return;

            case GType.VariantValue when content is null:
                SetVariant(nint.Zero);
                return;

            case GType.VariantValue when content is nint variant:
                SetVariant(variant);
                return;

            case GType.PointerValue when content is null:
                SetPointer(nint.Zero);
                return;

            case GType.PointerValue when content is nint pointer:
                SetPointer(pointer);
                return;

            default:
                throw Mismatch(content, expected);
        }
    }
}
