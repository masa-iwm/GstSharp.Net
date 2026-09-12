using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <c>GVariant</c>: a value of the serialisation format of GLib, together
/// with the type that describes it.
/// </summary>
/// <remarks>
/// <para>
/// What is bound here is what the one GVariant user of the whole GStreamer API
/// needs — <c>Gst.Pbutils.DiscovererInfo.ToVariant</c> and
/// <c>FromVariant</c>, which serialise a discovery result into a cache or a
/// message and read it back — rather than the container, the reader and the
/// text format of the C API. A variant of this binding is therefore an opaque
/// value: it says what its type string is, it turns into bytes, and bytes turn
/// back into it.
/// </para>
/// <para>
/// <c>GVariant</c> is a fundamental of GLib and not a boxed type, so this is
/// not a <see cref="Gst.GObject.Boxed"/>. It is a handle with a reference of
/// its own, disposed like a main context, and its finalizer is the safety net
/// for a wrapper that was dropped instead: releasing one is an atomic
/// decrement that reaches no GStreamer state, so the finalizer thread may do
/// it.
/// </para>
/// <para>
/// <b>Construction in GLib produces a floating reference</b>, which is a
/// reference nobody owns yet and which the first owner claims rather than
/// adds. Everything that hands one to this binding goes through the same
/// constructor, and the constructor claims it with <c>g_variant_take_ref</c>
/// for a transferred value — the API GLib provides for exactly the return
/// value that may or may not be floating — and with
/// <c>g_variant_ref_sink</c> for a borrowed one. See
/// <c>docs/ownership.md</c>.
/// </para>
/// </remarks>
public sealed unsafe partial class Variant : IDisposable
{
    private nint _handle;

    /// <summary>
    /// Wraps a native <c>GVariant</c>.
    /// </summary>
    /// <param name="handle">The value to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// which may be a floating one, <see cref="Transfer.None"/> when the
    /// wrapper has to take one of its own.
    /// </param>
    internal Variant(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A variant handle must not be null.");
        }

        // take_ref is what GLib documents for a value that was transferred and
        // may still be floating: it claims the floating reference when there is
        // one and adds nothing when there is not. ref_sink on a borrowed value
        // sinks a floating one and adds a reference to an owned one, which is
        // the reference this wrapper then owns either way.
        _handle = transfer == Transfer.Full ? GVariantTakeRef(handle) : GVariantRefSink(handle);
    }

    /// <summary>Releases the reference this wrapper holds.</summary>
    ~Variant() => Release();

    /// <summary>
    /// Gets the native <c>GVariant</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets the type of the value, as the string GLib spells it with — for
    /// instance <c>"v"</c> for a value that wraps another one.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string TypeString
    {
        get
        {
            string text = GMarshal.PtrToStringUtf8(GVariantGetTypeString(Handle)) ?? string.Empty;
            GC.KeepAlive(this);
            return text;
        }
    }

    /// <summary>
    /// Builds a value out of its serialised form.
    /// </summary>
    /// <param name="typeString">
    /// The type the bytes are read as, which has to be the type they were
    /// written from and which has to be a definite one.
    /// </param>
    /// <param name="data">The bytes, which the value keeps a reference to.</param>
    /// <returns>The value, which the caller has to dispose.</returns>
    /// <remarks>
    /// <para>
    /// This is <c>g_variant_new_from_bytes</c> with <c>trusted</c> false,
    /// which is the only answer that is safe for bytes this process did not
    /// produce itself: GLib then validates the contents on use instead of
    /// believing them. Bytes that do not describe a value of
    /// <paramref name="typeString"/> are not an error here — the value reads
    /// as the default of its type — so a round trip has to be through a type
    /// the caller knows.
    /// </para>
    /// <para>
    /// The bytes are referenced rather than copied, so the block stays alive
    /// for as long as the value does whatever the caller does with its own
    /// wrapper.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="typeString"/> or <paramref name="data"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="typeString"/> is not a valid type string, or is an
    /// indefinite one such as <c>"*"</c> or <c>"a*"</c>.
    /// </exception>
    public static Variant FromBytes(string typeString, Bytes data)
    {
        ArgumentNullException.ThrowIfNull(typeString);
        ArgumentNullException.ThrowIfNull(data);

        // A value may not have an indefinite type, and the path this takes
        // does not report one: it asserts over it. Hence definite: true.
        nint type = NewType(typeString, nameof(typeString), definite: true);
        try
        {
            nint handle = GVariantNewFromBytes(type, data.Handle, 0);
            GC.KeepAlive(data);

            return handle == nint.Zero
                ? throw new InvalidOperationException("g_variant_new_from_bytes returned no value.")
                : new Variant(handle, Transfer.Full);
        }
        finally
        {
            GVariantTypeFree(type);
        }
    }

    /// <summary>
    /// Serialises the value.
    /// </summary>
    /// <returns>The bytes, which the caller has to dispose.</returns>
    /// <remarks>
    /// This is <c>g_variant_get_data_as_bytes</c>. The block is the
    /// serialised form of the value and is what
    /// <see cref="FromBytes"/> reads back.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Bytes ToBytes()
    {
        nint bytes = GVariantGetDataAsBytes(Handle);
        GC.KeepAlive(this);

        return bytes == nint.Zero
            ? throw new InvalidOperationException("g_variant_get_data_as_bytes returned no value.")
            : new Bytes(bytes, Transfer.Full);
    }

    /// <summary>
    /// Releases the reference this wrapper holds.
    /// </summary>
    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Answers whether the value is of a type.
    /// </summary>
    /// <param name="typeString">The type to test against.</param>
    /// <returns>
    /// <see langword="true"/> when the value has that type or is of a subtype
    /// of it.
    /// </returns>
    /// <remarks>
    /// This is the guard in front of a C function that dereferences what a
    /// type check refused: it is internal because the only thing that needs it
    /// is the binding itself, and a public predicate over a surface that does
    /// not let a caller build a variant of their own would answer a question
    /// nobody can act on.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="typeString"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="typeString"/> is not a valid type string.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    internal bool IsOfType(string typeString)
    {
        ArgumentNullException.ThrowIfNull(typeString);

        // An indefinite type is meaningful here: g_variant_is_of_type asks
        // whether the value is of a subtype of it (gvariant.c:2128-2133).
        nint type = NewType(typeString, nameof(typeString), definite: false);
        try
        {
            bool result = GVariantIsOfType(Handle, type) != 0;
            GC.KeepAlive(this);
            return result;
        }
        finally
        {
            GVariantTypeFree(type);
        }
    }

    /// <summary>
    /// Builds a native <c>GVariantType</c> out of a type string, refusing one
    /// GLib would raise a critical over and, when the caller needs a type a
    /// value can have, one that is indefinite.
    /// </summary>
    /// <param name="typeString">The type string to parse.</param>
    /// <param name="parameterName">The parameter it arrived through.</param>
    /// <param name="definite">
    /// <see langword="true"/> when the type has to be a definite one, which is
    /// what a type a value is built of has to be.
    /// </param>
    /// <returns>The type, which the caller has to free.</returns>
    /// <remarks>
    /// <c>g_variant_type_string_is_valid</c> answers true for the indefinite
    /// type characters as well — <c>*</c>, <c>?</c> and <c>r</c>, alone or
    /// inside a container such as <c>"a*"</c> (gvarianttype.c:273) — and a
    /// value cannot have such a type: the allocator asks for the type
    /// information of the character and reaches a <c>g_assert</c> over it
    /// rather than a return (gvarianttypeinfo.c:847-850), which aborts the
    /// process. The check is therefore made here, before any call that would
    /// build a value.
    /// </remarks>
    private static nint NewType(string typeString, string parameterName, bool definite)
    {
        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope text = GMarshal.StackUtf8(typeString, buffer);

        if (GVariantTypeStringIsValid(text.Pointer) == 0)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"'{typeString}' is not a GVariant type string."),
                parameterName);
        }

        nint type = GVariantTypeNew(text.Pointer);
        if (definite && GVariantTypeIsDefinite(type) == 0)
        {
            GVariantTypeFree(type);

            throw new ArgumentException(
                FormattableString.Invariant(
                    $"'{typeString}' is an indefinite GVariant type string, which no value can have."),
                parameterName);
        }

        return type;
    }

    /// <summary>Releases the reference once, from either the dispose or the finalizer.</summary>
    private void Release()
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);
        if (handle != nint.Zero)
        {
            GVariantUnref(handle);
        }
    }

    /// <summary>The <c>g_variant_take_ref</c> entry point.</summary>
    /// <param name="value">The value to claim.</param>
    /// <returns>The same value.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_take_ref")]
    private static partial nint GVariantTakeRef(nint value);

    /// <summary>The <c>g_variant_ref_sink</c> entry point.</summary>
    /// <param name="value">The value to sink and reference.</param>
    /// <returns>The same value.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_ref_sink")]
    private static partial nint GVariantRefSink(nint value);

    /// <summary>The <c>g_variant_unref</c> entry point.</summary>
    /// <param name="value">The value to release.</param>
    [LibraryImport("GLib", EntryPoint = "g_variant_unref")]
    private static partial void GVariantUnref(nint value);

    /// <summary>The <c>g_variant_get_type_string</c> entry point.</summary>
    /// <param name="value">The value to read.</param>
    /// <returns>The type string, which belongs to the value.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_get_type_string")]
    private static partial nint GVariantGetTypeString(nint value);

    /// <summary>The <c>g_variant_get_data_as_bytes</c> entry point.</summary>
    /// <param name="value">The value to serialise.</param>
    /// <returns>The bytes, which the caller owns.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_get_data_as_bytes")]
    private static partial nint GVariantGetDataAsBytes(nint value);

    /// <summary>The <c>g_variant_new_from_bytes</c> entry point.</summary>
    /// <param name="type">The type the bytes are read as.</param>
    /// <param name="bytes">The bytes, which the value references.</param>
    /// <param name="trusted">Non-zero when the bytes are known to be well formed.</param>
    /// <returns>The new value, with a floating reference.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_new_from_bytes")]
    private static partial nint GVariantNewFromBytes(nint type, nint bytes, int trusted);

    /// <summary>The <c>g_variant_is_of_type</c> entry point.</summary>
    /// <param name="value">The value to test.</param>
    /// <param name="type">The type to test against.</param>
    /// <returns>Non-zero when the value is of that type.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_is_of_type")]
    private static partial int GVariantIsOfType(nint value, nint type);

    /// <summary>The <c>g_variant_type_string_is_valid</c> entry point.</summary>
    /// <param name="text">The type string to check.</param>
    /// <returns>Non-zero when the string is a type string.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_type_string_is_valid")]
    private static partial int GVariantTypeStringIsValid(byte* text);

    /// <summary>The <c>g_variant_type_new</c> entry point.</summary>
    /// <param name="text">The type string, which has to be a valid one.</param>
    /// <returns>The new type, which the caller frees.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_type_new")]
    private static partial nint GVariantTypeNew(byte* text);

    /// <summary>The <c>g_variant_type_is_definite</c> entry point.</summary>
    /// <param name="type">The type to check.</param>
    /// <returns>Non-zero when the type carries no indefinite character.</returns>
    [LibraryImport("GLib", EntryPoint = "g_variant_type_is_definite")]
    private static partial int GVariantTypeIsDefinite(nint type);

    /// <summary>The <c>g_variant_type_free</c> entry point.</summary>
    /// <param name="type">The type to free.</param>
    [LibraryImport("GLib", EntryPoint = "g_variant_type_free")]
    private static partial void GVariantTypeFree(nint type);
}
