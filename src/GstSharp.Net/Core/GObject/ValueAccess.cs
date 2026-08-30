using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The readers of a <c>GValue</c>, written once against the native layout so
/// that every projection of one answers the same thing.
/// </summary>
/// <remarks>
/// <para>
/// There are three projections. <see cref="Value"/> owns an inline
/// <see cref="GValueNative"/> and is what a caller allocates;
/// <see cref="ValueView"/> and <see cref="ValueRef"/> point at storage somebody
/// else owns and are what a callback receives. They have to read a value the
/// same way — a <c>GstStructure</c> field read through a view and the same
/// field read out of a copy must not disagree — so the readers live here rather
/// than in the three of them.
/// </para>
/// <para>
/// Every method takes the native value by <c>ref</c>. That is what the
/// <c>g_value_get_*</c> imports take, and a <c>GValue</c> is never read by
/// copying it: a copy would share the payload of the original and the two would
/// release it twice.
/// </para>
/// </remarks>
internal static class ValueAccess
{
    /// <summary>Reads a signed 8 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static sbyte GetSChar(ref GValueNative value) => GObjectNative.ValueGetSChar(ref value);

    /// <summary>Reads an unsigned 8 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static byte GetUChar(ref GValueNative value) => GObjectNative.ValueGetUChar(ref value);

    /// <summary>Reads a boolean.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static bool GetBoolean(ref GValueNative value) => GObjectNative.ValueGetBoolean(ref value) != 0;

    /// <summary>Reads a 32 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static int GetInt(ref GValueNative value) => GObjectNative.ValueGetInt(ref value);

    /// <summary>Reads an unsigned 32 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static uint GetUInt(ref GValueNative value) => GObjectNative.ValueGetUInt(ref value);

    /// <summary>Reads a 64 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static long GetInt64(ref GValueNative value) => GObjectNative.ValueGetInt64(ref value);

    /// <summary>Reads an unsigned 64 bit integer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static ulong GetUInt64(ref GValueNative value) => GObjectNative.ValueGetUInt64(ref value);

    /// <summary>Reads a C <c>long</c>.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static nint GetLong(ref GValueNative value) => GObjectNative.ValueGetLong(ref value).Value;

    /// <summary>Reads an unsigned C <c>long</c>.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static nuint GetULong(ref GValueNative value) => GObjectNative.ValueGetULong(ref value).Value;

    /// <summary>Reads a single precision number.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static float GetFloat(ref GValueNative value) => GObjectNative.ValueGetFloat(ref value);

    /// <summary>Reads a double precision number.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static double GetDouble(ref GValueNative value) => GObjectNative.ValueGetDouble(ref value);

    /// <summary>Reads a string.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value, or <see langword="null"/>.</returns>
    internal static string? GetString(ref GValueNative value) =>
        GMarshal.PtrToStringUtf8(GObjectNative.ValueGetString(ref value));

    /// <summary>Reads an untyped pointer.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static nint GetPointer(ref GValueNative value) => GObjectNative.ValueGetPointer(ref value);

    /// <summary>Reads a type.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static GType GetGType(ref GValueNative value) => new(GObjectNative.ValueGetGType(ref value));

    /// <summary>Reads an enumeration member.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static int GetEnum(ref GValueNative value) => GObjectNative.ValueGetEnum(ref value);

    /// <summary>Reads a set of flags.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static uint GetFlags(ref GValueNative value) => GObjectNative.ValueGetFlags(ref value);

    /// <summary>Reads an object.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The wrapper of the stored object, or <see langword="null"/>.</returns>
    internal static Object? GetObject(ref GValueNative value) =>
        Object.FromNative(GObjectNative.ValueGetObject(ref value), Transfer.None);

    /// <summary>Reads a boxed value, which stays owned by the value.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored value.</returns>
    internal static nint GetBoxed(ref GValueNative value) => GObjectNative.ValueGetBoxed(ref value);

    /// <summary>Reads a parameter specification, which stays owned by the value.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored <c>GParamSpec</c>.</returns>
    internal static nint GetParam(ref GValueNative value) => GObjectNative.ValueGetParam(ref value);

    /// <summary>Reads a variant, which stays owned by the value.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The stored <c>GVariant</c>.</returns>
    internal static nint GetVariant(ref GValueNative value) => GObjectNative.ValueGetVariant(ref value);

    /// <summary>
    /// Reads a boxed value as the wrapper of the binding, as a copy of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the boxed value.</typeparam>
    /// <param name="value">The value to read.</param>
    /// <returns>The wrapper, or <see langword="null"/> when the value holds nothing.</returns>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value, or the wrapper of its type is not
    /// a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value.
    /// </exception>
    internal static T? GetBoxed<T>(ref GValueNative value)
        where T : Boxed
    {
        GType type = value.Type;

        // g_value_get_boxed on a value of another fundamental type is a GLib
        // assertion failure rather than a cast, so the question is asked here.
        if (GObjectNative.TypeFundamental(type.Value) != GType.BoxedValue)
        {
            throw new InvalidCastException(
                $"A value of type {type.Name} does not hold a boxed value.");
        }

        nint boxed = GetBoxed(ref value);
        if (boxed == nint.Zero)
        {
            return null;
        }

        if (!TypeRegistry.TryCreateWrapper(type, boxed, Transfer.None, out object? wrapper))
        {
            throw new InvalidOperationException(
                $"No wrapper is registered for the boxed type {type.Name}. " +
                "Initialise the binding module that covers it — for example " +
                "Gst.WebRTC.GstWebRTC.Initialize() — before reading the value.");
        }

        if (wrapper is T typed)
        {
            return typed;
        }

        // The factory built a copy of its own, and nothing else holds it.
        (wrapper as IDisposable)?.Dispose();

        throw new InvalidCastException(
            $"The value holds a {type.Name}, whose wrapper is " +
            $"{wrapper?.GetType().ToString() ?? "nothing"} and not a {typeof(T)}.");
    }

    /// <summary>
    /// Reads a mini object as the wrapper of the binding, as a reference of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the mini object.</typeparam>
    /// <param name="value">The value to read.</param>
    /// <returns>The wrapper, or <see langword="null"/> when the value holds nothing.</returns>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value at all, or the wrapper of its type
    /// is not a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value.
    /// </exception>
    internal static T? GetMiniObject<T>(ref GValueNative value)
        where T : Gst.MiniObject
    {
        GType type = value.Type;

        // g_value_get_boxed on a value of another fundamental type is a GLib
        // assertion failure rather than a cast, so the question is asked here.
        if (GObjectNative.TypeFundamental(type.Value) != GType.BoxedValue)
        {
            throw new InvalidCastException(
                $"A value of type {type.Name} does not hold a mini object.");
        }

        nint boxed = GetBoxed(ref value);
        if (boxed == nint.Zero)
        {
            return null;
        }

        if (!TypeRegistry.TryCreateWrapper(type, boxed, Transfer.None, out object? wrapper))
        {
            throw new InvalidOperationException(
                $"No wrapper is registered for the mini object type {type.Name}. " +
                "Initialise the binding module that covers it — for example " +
                "Gst.Video.GstVideo.Initialize() — before reading the value.");
        }

        if (wrapper is T typed)
        {
            return typed;
        }

        // The factory took a reference of its own, and nothing else holds it.
        (wrapper as IDisposable)?.Dispose();

        throw new InvalidCastException(
            $"The value holds a {type.Name}, whose wrapper is " +
            $"{wrapper?.GetType().ToString() ?? "nothing"} and not a {typeof(T)}.");
    }

    /// <summary>
    /// Reads the content of the value as a managed object, based on its
    /// fundamental type.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The content.</returns>
    /// <exception cref="NotSupportedException">
    /// The fundamental type of the value has no accessor here.
    /// </exception>
    internal static object? GetContent(ref GValueNative value)
    {
        GType type = value.Type;
        nuint fundamental = GObjectNative.TypeFundamental(value.TypeValue);

        return fundamental switch
        {
            GType.InvalidValue => null,
            GType.BooleanValue => GetBoolean(ref value),
            GType.CharValue => GetSChar(ref value),
            GType.UCharValue => GetUChar(ref value),
            GType.IntValue => GetInt(ref value),
            GType.UIntValue => GetUInt(ref value),
            GType.LongValue => GetLong(ref value),
            GType.ULongValue => GetULong(ref value),
            GType.Int64Value => GetInt64(ref value),
            GType.UInt64Value => GetUInt64(ref value),
            GType.FloatValue => GetFloat(ref value),
            GType.DoubleValue => GetDouble(ref value),
            GType.StringValue => GetString(ref value),
            GType.EnumValue => GetEnum(ref value),
            GType.FlagsValue => GetFlags(ref value),
            GType.ObjectValue => GetObject(ref value),
            GType.BoxedValue => GetBoxed(ref value),

            // Every one of these has an accessor of its own; g_value_get_pointer
            // on them is a fatal warning rather than a cast.
            GType.ParamValue => GetParam(ref value),
            GType.VariantValue => GetVariant(ref value),

            // An interface with GObject among its prerequisites holds an
            // object. One without is not something a value can be read as: it
            // has no accessor of its own and g_value_get_pointer on it is an
            // assertion failure in GLib rather than a cast.
            GType.InterfaceValue => type.IsA(GType.Object)
                ? GetObject(ref value)
                : throw Unreadable(type, fundamental),
            GType.PointerValue => GetPointer(ref value),

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
}
