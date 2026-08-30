using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A writable view of a <c>GValue</c> that somebody else owns, which is what a
/// callback of the binding is handed for a non <c>const</c> <c>GValue*</c>
/// argument.
/// </summary>
/// <remarks>
/// <para>
/// This is <see cref="ValueView"/> plus the setters, for the callbacks that are
/// invited to change what they are shown in place:
/// <c>gst_structure_map_in_place</c> hands each field of a structure over this
/// way, and <c>gst_iterator_fold</c> hands over the accumulator.
/// </para>
/// <para>
/// <b>The type of the value cannot be changed.</b> Every setter first asks
/// whether the value already holds the type it is about to write, and throws an
/// <see cref="InvalidOperationException"/> when it does not; there is no
/// <c>Unset</c> and no way to re-initialise the value as another type. That is a
/// contract of the callers rather than a preference. <c>gst_structure_map_in_place</c>
/// writes the field back without checking anything, so a callback that unset the
/// value and answered <see langword="true"/> would leave the structure holding a
/// field with no type at all — every later read of that structure, including
/// <c>gst_structure_to_string</c>, walks into it. A field that should go away is
/// removed by answering <see langword="false"/> from the filtering variant,
/// <c>gst_structure_filter_and_map_in_place</c>, which is the supported way to
/// say so.
/// </para>
/// <para>
/// <b>The content is checked too where it carries a type.</b>
/// <see cref="SetBoxed(Boxed?)"/> and <see cref="SetMiniObject(Gst.MiniObject?)"/>
/// are handed a wrapper that knows what it is, and they refuse one of another
/// type than the value holds. Asking only the value would not be enough for
/// them: every boxed type answers to <c>G_TYPE_BOXED</c>, and
/// <c>g_value_set_boxed</c> copies what it is given with the copy function of
/// the type the <em>value</em> was initialised as, which on a wrapper of
/// another type is a wrong function over a foreign pointer rather than a
/// refused write.
/// </para>
/// <para>
/// <b>The reference is only valid while the callback runs</b>, for the same
/// reason and with the same enforcement as <see cref="ValueView"/>: it is a
/// <see langword="ref"/> <see langword="struct"/>, so the compiler refuses to
/// let one escape the call it arrived on. Use <see cref="ToValue"/> to keep a
/// copy.
/// </para>
/// </remarks>
public ref struct ValueRef
{
    private readonly ref GValueNative _native;

    /// <summary>
    /// Initializes a new instance of the <see cref="ValueRef"/> struct over
    /// storage somebody else owns.
    /// </summary>
    /// <param name="native">The value to work on.</param>
    internal ValueRef(ref GValueNative native) => _native = ref native;

    /// <summary>
    /// Gets the type of the value, or <see cref="GType.Invalid"/> when the value
    /// is not initialised.
    /// </summary>
    public readonly GType Type => _native.Type;

    /// <summary>Gets a value indicating whether the value holds nothing at all.</summary>
    public readonly bool IsEmpty => _native.TypeValue == GType.InvalidValue;

    /// <summary>
    /// Returns the read only view of the same value.
    /// </summary>
    /// <returns>The view, which is valid exactly as long as this reference is.</returns>
    /// <remarks>
    /// This is what a handler passes on to code that only reads, so that the
    /// permission to write stops where the reading starts. It copies nothing.
    /// </remarks>
    public readonly ValueView AsView() => new(ref _native);

    /// <summary>Reads a signed 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly sbyte GetSChar() => ValueAccess.GetSChar(ref _native);

    /// <summary>Reads an unsigned 8 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly byte GetUChar() => ValueAccess.GetUChar(ref _native);

    /// <summary>Reads a boolean.</summary>
    /// <returns>The stored value.</returns>
    public readonly bool GetBoolean() => ValueAccess.GetBoolean(ref _native);

    /// <summary>Reads a 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly int GetInt() => ValueAccess.GetInt(ref _native);

    /// <summary>Reads an unsigned 32 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly uint GetUInt() => ValueAccess.GetUInt(ref _native);

    /// <summary>Reads a 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly long GetInt64() => ValueAccess.GetInt64(ref _native);

    /// <summary>Reads an unsigned 64 bit integer.</summary>
    /// <returns>The stored value.</returns>
    public readonly ulong GetUInt64() => ValueAccess.GetUInt64(ref _native);

    /// <summary>Reads a C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetLong() => ValueAccess.GetLong(ref _native);

    /// <summary>Reads an unsigned C <c>long</c>.</summary>
    /// <returns>The stored value.</returns>
    public readonly nuint GetULong() => ValueAccess.GetULong(ref _native);

    /// <summary>Reads a single precision number.</summary>
    /// <returns>The stored value.</returns>
    public readonly float GetFloat() => ValueAccess.GetFloat(ref _native);

    /// <summary>Reads a double precision number.</summary>
    /// <returns>The stored value.</returns>
    public readonly double GetDouble() => ValueAccess.GetDouble(ref _native);

    /// <summary>Reads a string.</summary>
    /// <returns>
    /// A copy of the stored string, or <see langword="null"/> when the value
    /// holds none. The copy is the caller's and outlives the reference.
    /// </returns>
    public readonly string? GetString() => ValueAccess.GetString(ref _native);

    /// <summary>Reads an untyped pointer.</summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetPointer() => ValueAccess.GetPointer(ref _native);

    /// <summary>Reads a type.</summary>
    /// <returns>The stored value.</returns>
    public readonly GType GetGType() => ValueAccess.GetGType(ref _native);

    /// <summary>Reads an enumeration member.</summary>
    /// <returns>The stored value.</returns>
    public readonly int GetEnum() => ValueAccess.GetEnum(ref _native);

    /// <summary>Reads a set of flags.</summary>
    /// <returns>The stored value.</returns>
    public readonly uint GetFlags() => ValueAccess.GetFlags(ref _native);

    /// <summary>Reads an object.</summary>
    /// <returns>
    /// The wrapper of the stored object, or <see langword="null"/> when the
    /// value holds nothing.
    /// </returns>
    public readonly Object? GetObject() => ValueAccess.GetObject(ref _native);

    /// <summary>Reads a boxed value, which stays owned by the value.</summary>
    /// <returns>The stored value.</returns>
    public readonly nint GetBoxed() => ValueAccess.GetBoxed(ref _native);

    /// <summary>
    /// Reads a boxed value as the wrapper of the binding, as a copy of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">
    /// The wrapper type of the boxed value, for example <see cref="Gst.Structure"/>.
    /// </typeparam>
    /// <returns>
    /// The wrapper, which the caller has to dispose, or <see langword="null"/>
    /// when the value holds no boxed value at all.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value, or the wrapper of its type is not
    /// a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value, which normally means
    /// that the module that binds it has not been initialised.
    /// </exception>
    public readonly T? GetBoxed<T>()
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
    /// when the value holds nothing.
    /// </returns>
    /// <exception cref="InvalidCastException">
    /// The value does not hold a boxed value at all, or the wrapper of its type
    /// is not a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the value, which normally means
    /// that the module that binds it has not been initialised.
    /// </exception>
    public readonly T? GetMiniObject<T>()
        where T : Gst.MiniObject
        => ValueAccess.GetMiniObject<T>(ref _native);

    /// <summary>Reads a parameter specification, which stays owned by the value.</summary>
    /// <returns>The stored <c>GParamSpec</c>.</returns>
    public readonly nint GetParam() => ValueAccess.GetParam(ref _native);

    /// <summary>Reads a variant, which stays owned by the value.</summary>
    /// <returns>The stored <c>GVariant</c>.</returns>
    public readonly nint GetVariant() => ValueAccess.GetVariant(ref _native);

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
    public readonly object? GetContent() => ValueAccess.GetContent(ref _native);

    /// <summary>
    /// Copies what the reference points at into a value of the caller's own.
    /// </summary>
    /// <returns>
    /// An independent copy, which the caller disposes. An empty value copies as
    /// an empty value.
    /// </returns>
    public readonly Value ToValue() => Value.CopyFrom(ref _native);

    /// <summary>Stores a boolean.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a boolean.</exception>
    public readonly void SetBoolean(bool content)
    {
        Require(GType.Boolean);
        GObjectNative.ValueSetBoolean(ref _native, content ? 1 : 0);
    }

    /// <summary>Stores a signed 8 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a signed 8 bit integer.</exception>
    public readonly void SetSChar(sbyte content)
    {
        Require(GType.Char);
        GObjectNative.ValueSetSChar(ref _native, content);
    }

    /// <summary>Stores an unsigned 8 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an unsigned 8 bit integer.</exception>
    public readonly void SetUChar(byte content)
    {
        Require(GType.UChar);
        GObjectNative.ValueSetUChar(ref _native, content);
    }

    /// <summary>Stores a 32 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a 32 bit integer.</exception>
    public readonly void SetInt(int content)
    {
        Require(GType.Int);
        GObjectNative.ValueSetInt(ref _native, content);
    }

    /// <summary>Stores an unsigned 32 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an unsigned 32 bit integer.</exception>
    public readonly void SetUInt(uint content)
    {
        Require(GType.UInt);
        GObjectNative.ValueSetUInt(ref _native, content);
    }

    /// <summary>Stores a 64 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a 64 bit integer.</exception>
    public readonly void SetInt64(long content)
    {
        Require(GType.Int64);
        GObjectNative.ValueSetInt64(ref _native, content);
    }

    /// <summary>Stores an unsigned 64 bit integer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an unsigned 64 bit integer.</exception>
    public readonly void SetUInt64(ulong content)
    {
        Require(GType.UInt64);
        GObjectNative.ValueSetUInt64(ref _native, content);
    }

    /// <summary>Stores a C <c>long</c>.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a C <c>long</c>.</exception>
    public readonly void SetLong(nint content)
    {
        Require(GType.Long);
        GObjectNative.ValueSetLong(ref _native, new CLong(content));
    }

    /// <summary>Stores an unsigned C <c>long</c>.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an unsigned C <c>long</c>.</exception>
    public readonly void SetULong(nuint content)
    {
        Require(GType.ULong);
        GObjectNative.ValueSetULong(ref _native, new CULong(content));
    }

    /// <summary>Stores a single precision number.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a single precision number.</exception>
    public readonly void SetFloat(float content)
    {
        Require(GType.Float);
        GObjectNative.ValueSetFloat(ref _native, content);
    }

    /// <summary>Stores a double precision number.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a double precision number.</exception>
    public readonly void SetDouble(double content)
    {
        Require(GType.Double);
        GObjectNative.ValueSetDouble(ref _native, content);
    }

    /// <summary>Stores a copy of a string.</summary>
    /// <param name="content">The value to store, may be <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a string.</exception>
    public readonly unsafe void SetString(string? content)
    {
        Require(GType.String);
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(content, buffer);
        GObjectNative.ValueSetString(ref _native, scope.Pointer);
    }

    /// <summary>Stores an untyped pointer.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a pointer.</exception>
    public readonly void SetPointer(nint content)
    {
        Require(GType.Pointer);
        GObjectNative.ValueSetPointer(ref _native, content);
    }

    /// <summary>Stores a type.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a type.</exception>
    public readonly void SetGType(GType content)
    {
        // G_TYPE_GTYPE has no compile time value: GLib registers it at run
        // time, so the type to compare against is asked for rather than named.
        Require(new GType(GObjectNative.GTypeGetType()));
        GObjectNative.ValueSetGType(ref _native, content.Value);
    }

    /// <summary>Stores an enumeration member.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an enumeration member.</exception>
    public readonly void SetEnum(int content)
    {
        Require(GType.Enum);
        GObjectNative.ValueSetEnum(ref _native, content);
    }

    /// <summary>Stores a set of flags.</summary>
    /// <param name="content">The value to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a set of flags.</exception>
    public readonly void SetFlags(uint content)
    {
        Require(GType.Flags);
        GObjectNative.ValueSetFlags(ref _native, content);
    }

    /// <summary>Stores an object. The value takes its own reference.</summary>
    /// <param name="content">The object to store, may be <see langword="null"/>.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold an object.</exception>
    /// <remarks>
    /// The wrapper is kept alive across the call, as it is on
    /// <see cref="Value.SetObject"/>: between the read of its handle and the
    /// reference the value takes, nothing else necessarily uses it.
    /// </remarks>
    public readonly void SetObject(Object? content)
    {
        Require(GType.Object);
        GObjectNative.ValueSetObject(ref _native, content?.Handle ?? nint.Zero);
        GC.KeepAlive(content);
    }

    /// <summary>Stores a boxed value through its wrapper. The value takes its own copy.</summary>
    /// <param name="content">
    /// The wrapper of the value to store, or <see langword="null"/> to clear the
    /// content without changing the type.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The value does not already hold a boxed value, or it holds one of another
    /// type than <paramref name="content"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The wrapper stays the caller's: what the value holds afterwards is a
    /// <c>g_boxed_copy</c> of what the wrapper owns. It is kept alive across the
    /// call for the reason <see cref="Value.SetBoxed(Boxed?)"/> states.
    /// </para>
    /// <para>
    /// The type of the content is checked as well as the type of the value, and
    /// that is not a nicety: <c>g_value_set_boxed</c> copies what it is given
    /// with the copy function of the <b>value's</b> type, so a wrapper of
    /// another boxed type would be handed to the wrong copy function and the
    /// result written into the field. There is no warning from GLib on that
    /// path, only a corrupt value.
    /// </para>
    /// </remarks>
    public readonly void SetBoxed(Boxed? content)
    {
        Require(GType.Boxed);
        if (content is not null)
        {
            RequireContent(content.BoxedType);
        }

        GObjectNative.ValueSetBoxed(ref _native, content?.Handle ?? nint.Zero);
        GC.KeepAlive(content);
    }

    /// <summary>Stores a mini object through its wrapper. The value takes a reference of its own.</summary>
    /// <param name="content">
    /// The wrapper of the mini object to store, or <see langword="null"/> to
    /// clear the content without changing the type.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The value does not already hold a boxed value, or it holds one of another
    /// type than <paramref name="content"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A mini object is a boxed type as far as GObject is concerned and its copy
    /// function is <c>gst_mini_object_ref</c>, so the value ends up holding a
    /// reference of its own and the wrapper stays the caller's to dispose.
    /// </para>
    /// <para>
    /// The type of the content is checked for the reason
    /// <see cref="SetBoxed(Boxed?)"/> states, and it matters more here: the copy
    /// function of a mini object type is <c>gst_mini_object_ref</c>, which would
    /// increment a word of a plain boxed value that is not a reference count at
    /// all.
    /// </para>
    /// </remarks>
    public readonly void SetMiniObject(Gst.MiniObject? content)
    {
        Require(GType.Boxed);
        if (content is not null)
        {
            RequireContent(Value.MiniObjectTypeOf(content.Handle));
        }

        GObjectNative.ValueSetBoxed(ref _native, content?.Handle ?? nint.Zero);
        GC.KeepAlive(content);
    }

    /// <summary>Stores a parameter specification. The value takes its own reference.</summary>
    /// <param name="content">The <c>GParamSpec</c> to store.</param>
    /// <exception cref="InvalidOperationException">
    /// The value does not already hold a parameter specification.
    /// </exception>
    public readonly void SetParam(nint content)
    {
        Require(GType.Param);
        GObjectNative.ValueSetParam(ref _native, content);
    }

    /// <summary>Stores a variant. The value takes its own reference.</summary>
    /// <param name="content">The <c>GVariant</c> to store.</param>
    /// <exception cref="InvalidOperationException">The value does not already hold a variant.</exception>
    public readonly void SetVariant(nint content)
    {
        Require(GType.Variant);
        GObjectNative.ValueSetVariant(ref _native, content);
    }

    /// <summary>
    /// Throws unless the value already holds the type that is about to be
    /// written.
    /// </summary>
    /// <param name="expected">The type the setter writes.</param>
    /// <exception cref="InvalidOperationException">
    /// The value holds something else, or holds nothing at all.
    /// </exception>
    /// <remarks>
    /// The question is <c>G_VALUE_HOLDS</c>: an exact match for a fundamental
    /// type such as <c>G_TYPE_INT</c>, and derivation for the type families —
    /// any registered enumeration holds against <c>G_TYPE_ENUM</c>, any boxed
    /// type against <c>G_TYPE_BOXED</c>. An uninitialised value holds nothing
    /// and fails every one of them, and it is reported on its own: it is not a
    /// value of the wrong type but a value with no type yet, which is what an
    /// uninitialised fold seed is.
    /// </remarks>
    private readonly void Require(GType expected)
    {
        if (IsEmpty)
        {
            throw new InvalidOperationException(
                "The value is not initialized; initialize it before the call.");
        }

        if (!Type.IsA(expected))
        {
            throw new InvalidOperationException(
                $"A value that holds a {Type.Name} cannot be given a {expected.Name}. " +
                "The type of a value a callback is handed cannot be changed in place; " +
                "only its content can.");
        }
    }

    /// <summary>
    /// Throws unless the content a setter was handed is of the type the value
    /// holds.
    /// </summary>
    /// <param name="actual">The type of the content.</param>
    /// <exception cref="InvalidOperationException">The content is of another type.</exception>
    /// <remarks>
    /// This is the second half of the question for the two setters whose content
    /// carries a type of its own. <see cref="Require"/> only asks whether the
    /// value belongs to the family — every boxed type answers yes to
    /// <c>G_TYPE_BOXED</c> — and that is as far as a setter of a scalar can go,
    /// because an <see cref="int"/> carries no type. A wrapper does carry one,
    /// and <c>g_value_set_boxed</c> copies whatever it is given with the copy
    /// function registered for the type of the <em>value</em>, so a mismatch is
    /// not a rejected write but a wrong function over a foreign pointer.
    /// </remarks>
    private readonly void RequireContent(GType actual)
    {
        if (!actual.IsA(Type))
        {
            throw new InvalidOperationException(
                $"A value that holds a {Type.Name} cannot be given a {actual.Name}. " +
                "The content of a value a callback is handed has to be of the type the " +
                "value already holds: it is copied with the copy function of that type.");
        }
    }
}
