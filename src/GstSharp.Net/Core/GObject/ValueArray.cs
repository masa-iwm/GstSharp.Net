using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// A <c>GValueArray</c>: a growable array of <c>GValue</c> elements that
/// GObject registers as a boxed type.
/// </summary>
/// <remarks>
/// <para>
/// This is the container the <c>GST_TYPE_ARRAY</c> and <c>GST_TYPE_LIST</c>
/// bridge functions speak — <see cref="Gst.Structure.GetArray"/>,
/// <see cref="Gst.Structure.SetArray"/> and the
/// <c>gst_util_*_object_array</c> pair exist precisely so that a binding can
/// reach those value types through a <c>GValueArray</c>. What is bound here is
/// what that bridge needs — build an array, append, count, read an element
/// back — rather than the whole of the C API.
/// </para>
/// <para>
/// The wrapper is a <see cref="Boxed"/> like every other boxed wrapper of the
/// binding and follows the same rule: it owns a value of its own and has to be
/// disposed. Disposing frees the array and unsets every element in it, which
/// is what the registered <c>G_TYPE_VALUE_ARRAY</c> free function —
/// <c>g_value_array_free</c> — does.
/// </para>
/// <para>
/// GLib deprecates <c>GValueArray</c> in favour of <c>GArray</c>, but that
/// deprecation is a C compile time attribute on an API that GStreamer still
/// requires for the functions above; it says nothing about calling the entry
/// points at run time, so the wrapper carries no <see cref="ObsoleteAttribute"/>
/// and raises no warning.
/// </para>
/// </remarks>
public sealed unsafe partial class ValueArray : Boxed
{
    /// <summary>
    /// Wraps a native <c>GValueArray</c>.
    /// </summary>
    /// <param name="handle">The array to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands the array over,
    /// <see cref="Transfer.None"/> to adopt a copy of it.
    /// </param>
    internal ValueArray(nint handle, Transfer transfer)
        : base(handle, new GType(GetGType()), transfer)
    {
    }

    /// <summary>
    /// Gets the number of elements in the array.
    /// </summary>
    /// <remarks>
    /// The count is the <c>n_values</c> field of the public structure, which is
    /// its first member; <c>GValueArray</c> has no accessor function for it.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public uint Count
    {
        get
        {
            uint count = *(uint*)Handle;
            GC.KeepAlive(this);
            return count;
        }
    }

    /// <summary>
    /// Creates an empty array.
    /// </summary>
    /// <param name="preallocated">
    /// The number of elements to preallocate space for. The new array contains
    /// zero elements whatever the value; it only sizes the storage that
    /// <see cref="Append"/> grows into.
    /// </param>
    /// <returns>The new array, which the caller has to dispose.</returns>
    public static ValueArray New(uint preallocated = 0)
    {
        nint handle = GValueArrayNew(preallocated);

        return handle == nint.Zero
            ? throw new InvalidOperationException("g_value_array_new returned no value.")
            : new ValueArray(handle, Transfer.Full);
    }

    /// <summary>
    /// Wraps a native <c>GValueArray</c>, mapping the null pointer onto
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="handle">The array to wrap, or <c>0</c>.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The wrapper, or <see langword="null"/> for a null handle.</returns>
    internal static ValueArray? FromNative(nint handle, Transfer transfer) =>
        handle == nint.Zero ? null : new ValueArray(handle, transfer);

    /// <summary>
    /// Reads one element of the array.
    /// </summary>
    /// <param name="index">The position to read, counted from zero.</param>
    /// <returns>
    /// An independent copy of the element, which the caller has to dispose.
    /// </returns>
    /// <remarks>
    /// The copy is deliberate: <c>g_value_array_get_nth</c> hands out a
    /// borrowed pointer into the array's own storage, which the next
    /// <see cref="Append"/> may reallocate and <see cref="Boxed.Dispose()"/>
    /// frees, so a value read out of the array has to be the caller's own.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="index"/> is not below <see cref="Count"/>.
    /// </exception>
    public Value Get(uint index)
    {
        nint handle = Handle;
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, *(uint*)handle);

        Value copy = Value.CopyFrom(GValueArrayGetNth(handle, index));
        GC.KeepAlive(this);
        return copy;
    }

    /// <summary>
    /// Appends a copy of a value to the array.
    /// </summary>
    /// <param name="value">
    /// The value to append. The array stores a copy of its contents, so the
    /// caller keeps ownership of <paramref name="value"/> and still disposes
    /// it.
    /// </param>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is empty.
    /// </exception>
    public void Append(in Value value)
    {
        if (value.IsEmpty)
        {
            throw new ArgumentException(
                "An empty value cannot be passed: it has no type for the call to read.",
                nameof(value));
        }

        fixed (GValueNative* valuePointer = &Unsafe.AsRef(in value).NativeValue)
        {
            _ = GValueArrayAppend(Handle, valuePointer);
            GC.KeepAlive(this);
        }
    }

    /// <summary>The <c>g_value_array_new</c> entry point.</summary>
    /// <remarks>
    /// The imports of this type live next to it rather than in a shared
    /// interop class, as the hand written wrappers of the binding do —
    /// <see cref="Gst.GLib.Bytes"/> is the precedent.
    /// </remarks>
    [LibraryImport("GObject", EntryPoint = "g_value_array_new")]
    private static partial nint GValueArrayNew(uint nPrealloced);

    /// <summary>The <c>g_value_array_get_nth</c> entry point, which returns a borrowed interior pointer.</summary>
    [LibraryImport("GObject", EntryPoint = "g_value_array_get_nth")]
    private static partial nint GValueArrayGetNth(nint valueArray, uint index);

    /// <summary>The <c>g_value_array_append</c> entry point, which copies the contents of the value.</summary>
    [LibraryImport("GObject", EntryPoint = "g_value_array_append")]
    private static partial nint GValueArrayAppend(nint valueArray, GValueNative* value);

    /// <summary>The <c>g_value_array_get_type</c> entry point.</summary>
    /// <returns>The boxed type of the instances of this wrapper.</returns>
    [LibraryImport("GObject", EntryPoint = "g_value_array_get_type")]
    private static partial nuint GetGType();
}
