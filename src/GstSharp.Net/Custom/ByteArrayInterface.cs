using System;

namespace Gst;

public sealed unsafe partial class ByteArrayInterface
{
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
