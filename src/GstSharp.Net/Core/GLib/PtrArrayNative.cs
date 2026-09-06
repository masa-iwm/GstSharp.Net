using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// The public prefix of <c>GPtrArray</c>, and the two conversions a signal
/// trampoline needs over it.
/// </summary>
/// <remarks>
/// <para>
/// <c>GPtrArray</c> is declared in <c>garray.h</c> with exactly two public
/// fields — <c>gpointer *pdata</c> and <c>guint len</c> — in front of the
/// private ones GLib keeps to itself. Reading a lent array therefore needs no
/// entry point at all: the two fields are the whole contract, and they are the
/// only ones this struct mirrors. Nothing here allocates a
/// <see cref="PtrArrayNative"/>; it is only ever laid over storage GLib owns.
/// </para>
/// <para>
/// The write direction does go through GLib, because the private fields decide
/// how the array grows and how it is freed.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PtrArrayNative
{
    /// <summary>The elements.</summary>
    internal nint Data;

    /// <summary>The number of elements.</summary>
    internal uint Length;
}

/// <summary>
/// The conversions between a <c>GPtrArray</c> of GObjects and a managed array.
/// </summary>
internal static unsafe partial class PtrArray
{
    /// <summary>
    /// Reads a lent <c>GPtrArray</c> of GObjects into an array of wrappers.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the elements.</typeparam>
    /// <param name="array">The array to read, may be <see cref="nint.Zero"/>.</param>
    /// <returns>The wrappers, or the empty array for a null pointer.</returns>
    /// <exception cref="InvalidOperationException">
    /// An element is null, or is an object of a type that is not
    /// <typeparamref name="T"/>.
    /// </exception>
    /// <remarks>
    /// Every element is read out before the method returns, so the answer
    /// outlives the array it came from: an emission that empties or frees its
    /// array once the handler is done takes nothing away from what was read
    /// here. The wrappers are the interned ones and are borrowed exactly as
    /// every other argument of a handler is — no reference is taken and none is
    /// released.
    /// </remarks>
    internal static T[] ToArray<T>(nint array)
        where T : Gst.GObject.Object
    {
        if (array == 0)
        {
            return [];
        }

        ref PtrArrayNative native = ref *(PtrArrayNative*)array;
        if (native.Length == 0)
        {
            return [];
        }

        nint* elements = (nint*)native.Data;
        T[] result = new T[native.Length];
        for (uint i = 0; i < native.Length; i++)
        {
            result[i] = Gst.GObject.Object.FromNative<T>(elements[i], Transfer.None)
                ?? throw new InvalidOperationException(
                    "An element of the array the emission passed is not a " + typeof(T).FullName + ".");
        }

        return result;
    }

    /// <summary>
    /// Builds a <c>GPtrArray</c> that carries one owned reference per element.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the elements.</typeparam>
    /// <param name="items">The objects to hand over, may be <see langword="null"/>.</param>
    /// <returns>
    /// The new array at a reference count of one, or <see cref="nint.Zero"/>
    /// when <paramref name="items"/> is <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// An element is <see langword="null"/> or its wrapper was disposed.
    /// </exception>
    /// <remarks>
    /// No free function is installed: the caller of the emission installs its
    /// own before it releases the array, and installing one here would free
    /// every element twice. When an element cannot be handed over, the
    /// references minted so far are released and the array is freed, so the
    /// failure strands nothing.
    /// </remarks>
    internal static nint FromObjects<T>(T[]? items)
        where T : Gst.GObject.Object
    {
        if (items is null)
        {
            return 0;
        }

        nint array = PtrArraySizedNew((uint)items.Length);
        for (int i = 0; i < items.Length; i++)
        {
            // A disposed wrapper is read as the missing element it stands for
            // rather than through Handle, which would throw before the array
            // built so far could be released.
            if (items[i] is not { IsDisposed: false } item)
            {
                Release(array, i);
                throw new ArgumentNullException(
                    nameof(items),
                    "Element " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " of the array the handler returned is null or was disposed.");
            }

            PtrArrayAdd(array, GObjectNative.ObjectRef(item.Handle));
        }

        GC.KeepAlive(items);
        return array;
    }

    /// <summary>
    /// Releases a partially built array: the references minted for the elements
    /// that are already in it, and then the array itself.
    /// </summary>
    /// <param name="array">The array to free.</param>
    /// <param name="count">The number of elements that were added.</param>
    private static void Release(nint array, int count)
    {
        ref PtrArrayNative native = ref *(PtrArrayNative*)array;
        nint* elements = (nint*)native.Data;
        for (int i = 0; i < count; i++)
        {
            GObjectNative.ObjectUnref(elements[i]);
        }

        PtrArrayUnref(array);
    }

    [LibraryImport("GLib", EntryPoint = "g_ptr_array_sized_new")]
    private static partial nint PtrArraySizedNew(uint reserved);

    [LibraryImport("GLib", EntryPoint = "g_ptr_array_add")]
    private static partial void PtrArrayAdd(nint array, nint data);

    [LibraryImport("GLib", EntryPoint = "g_ptr_array_unref")]
    private static partial void PtrArrayUnref(nint array);
}
