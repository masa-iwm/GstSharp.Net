using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The two field accessors that carry a <c>GValue</c>, and the typed reader
/// that turns a boxed field into the wrapper of the binding.
/// </content>
/// <remarks>
/// <para>
/// The generated surface of a structure covers the fields whose type C has an
/// accessor for — <see cref="GetInt(string, out int)"/>,
/// <see cref="GetString(string)"/> and their neighbours — and nothing else. The
/// setters of C are variadic and the generator emits none of them, so before
/// these two a structure could be read but never written, and a field of a type
/// with no accessor of its own could not be reached at all.
/// </para>
/// <para>
/// That gap is what stands between an application and half of the WebRTC API:
/// the reply of <c>create-offer</c> is a structure with one field named
/// <c>offer</c>, which holds a <c>GstWebRTCSessionDescription</c>. It is read
/// here with <c>reply.GetBoxed&lt;WebRTCSessionDescription&gt;("offer")</c>.
/// </para>
/// </remarks>
public sealed unsafe partial class Structure
{
    /// <summary>
    /// Reads a field as a value of the caller's own.
    /// </summary>
    /// <param name="fieldName">The name of the field.</param>
    /// <returns>
    /// A copy of the field, which the caller has to dispose. A structure that
    /// has no such field produces an empty value —
    /// <see cref="Gst.GObject.Value.IsEmpty"/> is <see langword="true"/> and
    /// disposing it does nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>gst_structure_get_value</c> hands out the value the structure owns,
    /// borrowed: it stays valid only as long as the field is neither rewritten
    /// nor removed and the structure itself is alive. A
    /// <see cref="Gst.GObject.Value"/> owns what it holds and unsets it when it
    /// is disposed, so what this returns is a copy, taken through
    /// <c>g_value_copy</c>. That is the same shape every boxed value in this
    /// binding has, and it is what makes the result safe to keep after the
    /// structure is gone.
    /// </para>
    /// <para>
    /// A missing field is not an error here. The generated getters of a
    /// structure answer the same way — <see cref="GetString(string)"/> returns
    /// <see langword="null"/> and <see cref="GetInt(string, out int)"/> returns
    /// <see langword="false"/> — because a structure is a bag of optional
    /// fields rather than a type with a fixed shape. Use
    /// <see cref="HasField(string)"/> to ask the question separately.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fieldName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.GObject.Value GetValue(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        // The handle is read first, so that a disposed wrapper throws before
        // anything is encoded.
        nint structure = Handle;

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(fieldName, buffer);

        // Null for a field that is not there, which CopyFrom maps onto the
        // empty value.
        nint borrowed = GstStructureGetValue(structure, scope.Pointer);
        Gst.GObject.Value value = Gst.GObject.Value.CopyFrom(borrowed);

        // The copy is taken while the structure is still known to be alive.
        GC.KeepAlive(this);
        return value;
    }

    /// <summary>
    /// Reads a boxed field as the wrapper of the binding, as a value of the
    /// caller's own.
    /// </summary>
    /// <typeparam name="T">
    /// The wrapper type of the field, for example
    /// <c>Gst.WebRTC.WebRTCSessionDescription</c>.
    /// </typeparam>
    /// <param name="fieldName">The name of the field.</param>
    /// <returns>
    /// The wrapper, which the caller has to dispose, or <see langword="null"/>
    /// when the structure has no such field or the field holds no value.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <see cref="GetValue(string)"/> followed by
    /// <see cref="Gst.GObject.Value.GetBoxed{T}"/>, which is where the
    /// ownership and the type registry are explained. The wrapper owns a copy
    /// of the field, so it outlives the structure and has to be disposed like
    /// every other boxed wrapper of the binding.
    /// </para>
    /// <para>
    /// Two copies are taken on the way — one by the value and one by the
    /// wrapper — and neither is worth avoiding: what travels this way is a
    /// session description or a caps, not media.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fieldName"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidCastException">
    /// The field holds something that is not a boxed value, or one whose
    /// wrapper is not a <typeparamref name="T"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// No wrapper is registered for the type of the field, which normally means
    /// that the module that binds it has not been initialised.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public T? GetBoxed<T>(string fieldName)
        where T : Gst.GObject.Boxed
    {
        using Gst.GObject.Value value = GetValue(fieldName);
        return value.IsEmpty ? null : value.GetBoxed<T>();
    }

    /// <summary>
    /// Writes a field, adding it when the structure does not have it yet.
    /// </summary>
    /// <param name="fieldName">The name of the field.</param>
    /// <param name="value">
    /// The value to store. The structure takes a copy of it, so the caller
    /// keeps and disposes the one it passed.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_structure_set_value</c>, and it is the only way to write
    /// a field from here: the <c>gst_structure_set</c> family is variadic and
    /// the generator emits no variadic call. Anything a
    /// <see cref="Gst.GObject.Value"/> can hold can be stored, which includes
    /// the boxed types that no <c>gst_structure_set_*</c> function covers.
    /// </para>
    /// <code>
    /// using Gst.GObject.Value value = Gst.GObject.Value.New(Gst.GObject.GType.Int);
    /// value.SetInt(96);
    /// structure.SetValue("payload", value);
    /// </code>
    /// <para>
    /// <b>The structure has to be mutable.</b> C ties that to the parent of a
    /// structure: a structure that belongs to a caps, an event or a message has
    /// its parent's reference count attached to it, and writing into it while
    /// somebody else holds the parent is refused. Every structure a wrapper of
    /// this binding holds is a value of its own — a boxed wrapper copies what
    /// it is handed, so <see cref="Caps.GetStructure(uint)"/> and
    /// <see cref="Gst.Promise.GetReply"/> both produce parentless copies — and
    /// a parentless structure is always mutable. The check below is therefore
    /// one that a caller of this binding does not normally meet, and it is here
    /// because the alternative is a GLib warning on the console and a write
    /// that silently did not happen.
    /// </para>
    /// <para>
    /// Because the structure is a copy, writing into it does not write back
    /// into the caps or the message it came from.
    /// <see cref="Caps.MakeWritable"/> does not change that: it makes the
    /// <em>caps</em> writable, which the calls that rewrite the caps
    /// themselves need, while a structure read out of them was a copy of its
    /// own before and after. To change what caps say, write the structure and
    /// then put it back with a call that takes a structure.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fieldName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> holds nothing. An uninitialised value has no
    /// type to store the field as.
    /// </exception>
    /// <exception cref="InvalidOperationException">The structure is not mutable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void SetValue(string fieldName, in Gst.GObject.Value value)
    {
        ArgumentNullException.ThrowIfNull(fieldName);

        if (value.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be stored in a structure: it has no type to store the field as.",
                nameof(value));
        }

        nint structure = Handle;

        if (!IsMutableOrUnanswerable())
        {
            throw new InvalidOperationException(
                $"The \"{GetName()}\" structure is not mutable, so \"{fieldName}\" cannot be written. " +
                "A structure that belongs to a caps, an event or a message is only mutable while its owner is.");
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(fieldName, buffer);

        GstStructureSetValue(structure, scope.Pointer, ref Unsafe.AsRef(in value).NativeValue);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Answers whether the mutability question can be asked and, where it can,
    /// what the answer is: 0 while unknown, 1 when the entry point resolves,
    /// -1 when the installed library predates it.
    /// </summary>
    private static int _isWritableAvailability;

    /// <summary>
    /// Tests whether the structure is mutable, on the installations that can
    /// say.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> only when the installed library answered that
    /// the structure is frozen.
    /// </returns>
    /// <remarks>
    /// <c>gst_structure_is_writable</c> was added in 1.28 and the binding
    /// supports 1.24, so the guard is asked once and remembered. Where the
    /// entry point does not exist the guard passes: every structure a wrapper
    /// of this binding holds is a parentless copy and therefore mutable, so on
    /// an older installation the misuse the guard exists for surfaces the way
    /// it does in C — a GLib warning and a write that did not happen — instead
    /// of an exception the library cannot help to raise.
    /// </remarks>
    private bool IsMutableOrUnanswerable()
    {
        if (Volatile.Read(ref _isWritableAvailability) < 0)
        {
            return true;
        }

        try
        {
            bool result = IsWritable();
            Volatile.Write(ref _isWritableAvailability, 1);
            return result;
        }
        catch (EntryPointNotFoundException)
        {
            Volatile.Write(ref _isWritableAvailability, -1);
            return true;
        }
    }

    /// <summary>The <c>gst_structure_get_value</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_structure_get_value")]
    private static partial nint GstStructureGetValue(nint structure, byte* fieldname);

    /// <summary>The <c>gst_structure_set_value</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_structure_set_value")]
    private static partial void GstStructureSetValue(
        nint structure,
        byte* fieldname,
        ref Gst.GObject.GValueNative value);
}
