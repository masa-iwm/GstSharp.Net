namespace Gst.GObject;

/// <summary>
/// A read only view of a <c>GValue</c> that somebody else owns, which is what a
/// callback of the binding is handed for a <c>const GValue*</c> argument.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is the struct a caller allocates: it owns an inline
/// <c>GValue</c> and has to be disposed. A callback is handed something else — a
/// pointer to a <c>GValue</c> that lives in a <c>GstStructure</c> field or on
/// the stack of the caller — which no owning type can wrap without claiming a
/// payload it does not own. This is that pointer, projected onto the readers of
/// <see cref="Value"/>, under the same names and with the same meanings.
/// </para>
/// <para>
/// <b>The view is only valid while the callback runs.</b> It is a
/// <see langword="ref"/> <see langword="struct"/> for that reason and not for
/// performance: the compiler refuses to store one in a field, in an array, in a
/// closure or in an <c>async</c> state machine, so a view cannot outlive the
/// call it arrived on. The storage behind it really does go away — the item
/// <c>gst_iterator_fold</c> hands out is a stack <c>GValue</c> that is reset
/// after every callback, and a <c>GstStructure</c> field is gone as soon as the
/// structure is. To keep what a view holds, copy it with
/// <see cref="ToValue"/> and dispose the copy.
/// </para>
/// <para>
/// Nothing here disposes anything: the view owns no payload, so there is no
/// <c>Dispose</c> and no <c>using</c>. The wrappers that
/// <see cref="GetObject"/>, <see cref="GetBoxed{T}"/> and
/// <see cref="GetMiniObject{T}"/> hand out are the caller's own, exactly as they
/// are on <see cref="Value"/>.
/// </para>
/// </remarks>
public readonly ref struct ValueView
{
    private readonly ref GValueNative _native;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueView"/> struct over
    /// storage somebody else owns.
    /// </summary>
    /// <param name="native">The value to look at.</param>
    internal ValueView(ref GValueNative native) => _native = ref native;

    /// <summary>
    /// Gets the type of the value, or <see cref="GType.Invalid"/> when the value
    /// is not initialised.
    /// </summary>
    public GType Type => _native.Type;

    /// <summary>Gets a value indicating whether the value holds nothing at all.</summary>
    public bool IsEmpty => _native.TypeValue == GType.InvalidValue;

    /// <summary>Reads a signed 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public sbyte GetSChar() => ValueAccess.GetSChar(ref _native);

    /// <summary>Reads an unsigned 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public byte GetUChar() => ValueAccess.GetUChar(ref _native);

    /// <summary>Reads a boolean.</summary>
    /// <returns>The stored value.</returns>
    public bool GetBoolean() => ValueAccess.GetBoolean(ref _native);

    /// <summary>Reads a 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public int GetInt() => ValueAccess.GetInt(ref _native);

    /// <summary>Reads an unsigned 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public uint GetUInt() => ValueAccess.GetUInt(ref _native);

    /// <summary>Reads a 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public long GetInt64() => ValueAccess.GetInt64(ref _native);

    /// <summary>Reads an unsigned 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public ulong GetUInt64() => ValueAccess.GetUInt64(ref _native);

    /// <summary>Reads a C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public nint GetLong() => ValueAccess.GetLong(ref _native);

    /// <summary>Reads an unsigned C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public nuint GetULong() => ValueAccess.GetULong(ref _native);

    /// <summary>Reads a single precision number.</summary>
    /// <returns>The stored value.</returns>
    public float GetFloat() => ValueAccess.GetFloat(ref _native);

    /// <summary>Reads a double precision number.</summary>
    /// <returns>The stored value.</returns>
    public double GetDouble() => ValueAccess.GetDouble(ref _native);

    /// <summary>Reads a string.</summary>
    /// <returns>
    /// A copy of the stored string, or <see langword="null"/> when the value
    /// holds none. The copy is the caller's and outlives the view.
    /// </returns>
    public string? GetString() => ValueAccess.GetString(ref _native);

    /// <summary>Reads an untyped pointer.</summary>
    /// <returns>The stored value.</returns>
    public nint GetPointer() => ValueAccess.GetPointer(ref _native);

    /// <summary>Reads a type.</summary>
    /// <returns>The stored value.</returns>
    public GType GetGType() => ValueAccess.GetGType(ref _native);

    /// <summary>Reads an enumeration member.</summary>
    /// <returns>The stored value.</returns>
    public int GetEnum() => ValueAccess.GetEnum(ref _native);

    /// <summary>Reads a set of flags.</summary>
    /// <returns>The stored value.</returns>
    public uint GetFlags() => ValueAccess.GetFlags(ref _native);

    /// <summary>Reads an object.</summary>
    /// <returns>
    /// The wrapper of the stored object, or <see langword="null"/> when the
    /// value holds nothing.
    /// </returns>
    public Object? GetObject() => ValueAccess.GetObject(ref _native);

    /// <summary>Reads a boxed value, which stays owned by the value.</summary>
    /// <returns>The stored value.</returns>
    public nint GetBoxed() => ValueAccess.GetBoxed(ref _native);

    /// <summary>
    /// Reads a boxed value as the wrapper of the binding, as a copy of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">
    /// The wrapper type of the boxed value, for example <see cref="Gst.Structure"/>.
    /// </typeparam>
    /// <returns>
    /// The wrapper, which the caller has to dispose, or <see langword="null"/>
    /// when the value holds no boxed value at all. The copy outlives the view.
    /// </returns>
    /// <remarks>
    /// This is <see cref="Value.GetBoxed{T}"/>, with the same registration rule:
    /// the <c>GType</c> of the value says what the pointer is, and the module
    /// that binds the type has to have been initialised.
    /// </remarks>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value, or the wrapper of its type is not
    /// a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value, which normally means
    /// that the module that binds it has not been initialised.
    /// </exception>
    public T? GetBoxed<T>()
        where T : Boxed
        => ValueAccess.GetBoxed<T>(ref _native);

    /// <summary>
    /// Reads a mini object as the wrapper of the binding, as a reference of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">
    /// The wrapper type of the mini object, for example <see cref="Gst.Caps"/>.
    /// </typeparam>
    /// <returns>
    /// The wrapper, which the caller has to dispose, or <see langword="null"/>
    /// when the value holds nothing. The reference outlives the view.
    /// </returns>
    /// <remarks>
    /// This is <see cref="Value.GetMiniObject{T}"/>, with the same contract: the
    /// wrapper takes a reference of its own and the caller disposes it.
    /// </remarks>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value at all, or the wrapper of its type
    /// is not a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value, which normally means
    /// that the module that binds it has not been initialised.
    /// </exception>
    public T? GetMiniObject<T>()
        where T : Gst.MiniObject
        => ValueAccess.GetMiniObject<T>(ref _native);

    /// <summary>Reads a parameter specification, which stays owned by the value.</summary>
    /// <returns>The stored <c>GParamSpec</c>.</returns>
    public nint GetParam() => ValueAccess.GetParam(ref _native);

    /// <summary>Reads a variant, which stays owned by the value.</summary>
    /// <returns>The stored <c>GVariant</c>.</returns>
    public nint GetVariant() => ValueAccess.GetVariant(ref _native);

    /// <summary>
    /// Reads the content of the value as a managed object, based on its
    /// fundamental type.
    /// </summary>
    /// <returns>
    /// The content: a primitive for the numeric types, a <see cref="string"/>,
    /// an <see cref="Object"/> wrapper, or the raw pointer for boxed, parameter,
    /// variant and pointer types.
    /// </returns>
    /// <exception cref="NotSupportedException">
    /// The fundamental type of the value has no accessor here.
    /// </exception>
    public object? GetContent() => ValueAccess.GetContent(ref _native);

    /// <summary>
    /// Copies what the view looks at into a value of the caller's own.
    /// </summary>
    /// <returns>
    /// An independent copy, which the caller disposes. An empty value copies as
    /// an empty value.
    /// </returns>
    /// <remarks>
    /// This is the way to keep anything past the callback. The copy is a
    /// <c>g_value_copy</c> into fresh storage, so it owns its payload and the
    /// value the view pointed at is untouched.
    /// </remarks>
    public Value ToValue() => Value.CopyFrom(ref _native);
}
