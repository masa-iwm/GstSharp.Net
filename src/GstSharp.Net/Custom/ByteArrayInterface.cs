using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The half of the interface that makes one: a byte array of its own, which is
/// what the record needs before managed code can hand it to anything.
/// </content>
/// <remarks>
/// <para>
/// A <c>GstByteArrayInterface</c> is a sink the library writes a serialisation
/// into, and the array behind it belongs to whoever declared the interface:
/// the record carries the bytes, their length and a resize function, and
/// nothing else — there is no state field a binding could hang an
/// implementation off. The implementation here does what the stock one in
/// <c>gst/gstmeta.c</c> does: it allocates the record with a slot behind it,
/// keeps a <c>GByteArray</c> in that slot, and answers the resize with a single
/// static function that reads the slot back out of the record it is handed.
/// </para>
/// <para>
/// <b>An instance built by the constructor owns its memory and is released by
/// <see cref="Dispose"/>.</b> The wrappers that a
/// <see cref="Gst.MetaSerializeFunction"/> is handed are lent — the record
/// belongs to the caller of the serialisation — and disposing one of those does
/// nothing. There is no finalizer, which is the rule every hand written owner in
/// this binding follows (see the ownership guide, which states it of the
/// parameter specification wrapper: the wrapper has no finalizer, so an instance
/// that is never disposed holds its memory until the process exits).
/// </para>
/// </remarks>
public sealed unsafe partial class ByteArrayInterface : IDisposable
{
    /// <summary>
    /// The record a disposed instance points at, which is zero throughout and
    /// lives as long as the process.
    /// </summary>
    /// <remarks>
    /// The handle of the record is a plain field the generated members read
    /// without a check, so a disposed instance cannot be left pointing at the
    /// memory that was freed and cannot be zeroed either. It is pointed at this
    /// record instead: <see cref="Len"/> answers <c>0</c> and
    /// <see cref="AppendData"/> answers <see langword="false"/>, the way a
    /// record that carries no resize function does, and neither reads freed
    /// memory. The one block it takes is allocated once and deliberately never
    /// released: every disposed instance shares it, nothing ever writes to it,
    /// and it has to outlive whatever still holds a disposed wrapper, so a leak
    /// checker reporting it once per process is reporting the design.
    /// </remarks>
    private static readonly nint DeadRecord = GMarshal.Malloc0((nuint)sizeof(ByteArrayInterfaceRaw));

    /// <summary>Whether this instance allocated the record it points at.</summary>
    private bool _owned;

    /// <summary>Whether <see cref="Dispose"/> released what this instance owned.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates an empty byte array that the serialisation of a metadata item
    /// can be written into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The array grows on demand through a <c>GByteArray</c>, so
    /// <see cref="Gst.Meta.Serialize(Gst.ByteArrayInterface)"/> can be called
    /// more than once on one instance and the results accumulate, which is the
    /// reason to take an instance rather than the <c>byte[]</c> that
    /// <see cref="Gst.Meta.Serialize()"/> answers.
    /// </para>
    /// <para>
    /// The instance owns two native allocations and gives both back in
    /// <see cref="Dispose"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">The byte array could not be allocated.</exception>
    public ByteArrayInterface()
    {
        // The record and one pointer behind it: gst/gstmeta.c does the same
        // with a structure whose first member is the interface, and the cast
        // that recovers the array out of the record is the same cast.
        nint record = GMarshal.Malloc0((nuint)sizeof(ByteArrayInterfaceRaw) + (nuint)sizeof(nint));
        nint array = GLibNative.ByteArrayNew();
        if (array == nint.Zero)
        {
            GMarshal.Free(record);
            throw new InvalidOperationException("g_byte_array_new answered NULL; the byte array was not created.");
        }

        *ArrayOf(record) = array;

        ByteArrayInterfaceRaw* raw = (ByteArrayInterfaceRaw*)record;
        raw->Data = ((GByteArrayRaw*)array)->Data;
        raw->Len = 0;
        raw->Resize = (nint)(delegate* unmanaged[Cdecl]<nint, nuint, int>)&Resize;

        Handle = record;
        _owned = true;
    }

    /// <summary>
    /// Reads the bytes the array holds.
    /// </summary>
    /// <returns>The bytes, which is an empty span while the array is empty.</returns>
    /// <remarks>
    /// <b>The span is only valid until the array is next written to.</b> A
    /// resize may move the bytes, which is why every writer of the interface
    /// reads the data pointer again after it grew the array, and
    /// <see cref="Dispose"/> frees them. Use <see cref="ToArray"/> to keep them.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The array was disposed.</exception>
    /// <exception cref="OverflowException">
    /// The array holds more than <see cref="int.MaxValue"/> bytes, which a span
    /// cannot address: a <c>GByteArray</c> reaches twice that, so read
    /// <see cref="Len"/> when an array that large is possible.
    /// </exception>
    public ReadOnlySpan<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ByteArrayInterfaceRaw* raw = (ByteArrayInterfaceRaw*)Handle;
        nuint length = raw->Len;
        nint data = raw->Data;
        GC.KeepAlive(this);

        return length == 0
            ? default
            : new ReadOnlySpan<byte>((void*)data, checked((int)length));
    }

    /// <summary>
    /// Copies the bytes the array holds.
    /// </summary>
    /// <returns>The copy, which the caller owns.</returns>
    /// <exception cref="ObjectDisposedException">The array was disposed.</exception>
    public byte[] ToArray() => AsSpan().ToArray();

    /// <summary>
    /// Releases the byte array and the record behind it.
    /// </summary>
    /// <remarks>
    /// A second call does nothing, and so does a call on a wrapper the binding
    /// lent out: only the instance the constructor made owns anything. What was
    /// read out of <see cref="AsSpan"/> beforehand points at freed memory
    /// afterwards.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed || !_owned)
        {
            return;
        }

        _disposed = true;

        nint record = Handle;
        nint array = *ArrayOf(record);
        Handle = DeadRecord;

        _ = GLibNative.ByteArrayFree(array, 1);
        GMarshal.Free(record);
        GC.KeepAlive(this);
    }

    /// <summary>
    /// The <c>resize</c> slot of an array this binding allocated.
    /// </summary>
    /// <param name="self">The record, which carries its array behind it.</param>
    /// <param name="length">The length the array is to have.</param>
    /// <returns>Non zero when the array has that length.</returns>
    /// <remarks>
    /// The length the caller asks for is a <c>gsize</c> while a
    /// <c>GByteArray</c> is a <c>guint</c> long, so a length that does not fit
    /// is refused rather than truncated. The length of the record itself is the
    /// caller's to write — <c>gst_byte_array_interface_set_size</c> writes it
    /// after the resize returned — and shrinking is part of the contract:
    /// <c>gst_meta_serialize</c> puts the array back where it was when the
    /// serialisation of an item failed.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int Resize(nint self, nuint length)
    {
        if (length > uint.MaxValue)
        {
            return 0;
        }

        nint array = *ArrayOf(self);
        _ = GLibNative.ByteArraySetSize(array, (uint)length);

        // The bytes may have moved, so the record is pointed at them again,
        // which is what the stock implementation in gst/gstmeta.c does too.
        ((ByteArrayInterfaceRaw*)self)->Data = ((GByteArrayRaw*)array)->Data;
        return 1;
    }

    /// <summary>
    /// Answers the slot behind a record this binding allocated, which is where
    /// the <c>GByteArray</c> of it lives.
    /// </summary>
    /// <param name="record">The record the constructor allocated.</param>
    /// <returns>The address of the array pointer.</returns>
    private static nint* ArrayOf(nint record) =>
        (nint*)((byte*)record + sizeof(ByteArrayInterfaceRaw));

    /// <summary>
    /// Appends bytes to the array, growing it if it has to.
    /// </summary>
    /// <param name="data">The bytes to append.</param>
    /// <returns>
    /// <see langword="true"/> when the bytes were appended;
    /// <see langword="false"/> when the array cannot grow, which is what an
    /// array that carries no resize function and a resize that refused both
    /// produce.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_byte_array_interface_append_data</c> of
    /// <c>gst/gstbytearrayinterface.h</c>, which is a <c>static inline</c>
    /// function that no library exports, so it is written out here rather than
    /// imported: grow the array by the length of the block through its own
    /// resize function, then copy into the memory that grew. The data pointer
    /// is read again after the resize because the resize may have moved the
    /// array.
    /// </para>
    /// <para>
    /// Without it a <see cref="Gst.MetaSerializeFunction"/> would have no way of
    /// writing anything: the sink the library hands a serialisation is this
    /// interface, and the only other member of it says how long the array
    /// already is.
    /// </para>
    /// <para>
    /// The name follows the C one: the header also carries a
    /// <c>gst_byte_array_interface_append</c> that grows the array by a byte
    /// count and answers where the room begins, so the plain <c>Append</c> name
    /// is left for that sibling.
    /// </para>
    /// </remarks>
    public bool AppendData(System.ReadOnlySpan<byte> data)
    {
        ByteArrayInterfaceRaw* raw = (ByteArrayInterfaceRaw*)Handle;
        if (raw->Resize == 0)
        {
            GC.KeepAlive(this);
            return false;
        }

        nuint origin = raw->Len;
        nuint length = origin + (nuint)data.Length;
        delegate* unmanaged[Cdecl]<nint, nuint, int> resize =
            (delegate* unmanaged[Cdecl]<nint, nuint, int>)raw->Resize;
        if (resize(Handle, length) == 0)
        {
            GC.KeepAlive(this);
            return false;
        }

        raw->Len = length;
        if (!data.IsEmpty)
        {
            data.CopyTo(new Span<byte>((byte*)raw->Data + origin, data.Length));
        }

        GC.KeepAlive(this);
        return true;
    }
}
