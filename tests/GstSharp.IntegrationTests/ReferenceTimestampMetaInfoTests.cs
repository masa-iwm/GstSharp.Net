using System.Buffers.Binary;
using System.Text;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>ReferenceTimestampMeta.GetInfoStructure()</c>, the one field of the tree
/// that the gir marks with a version above the floor of the binding and that a
/// runtime version check hands out anyway.
/// </summary>
/// <remarks>
/// <para>
/// A structure that grew a field is allocated at the size the library
/// registered it with (<c>sizeof (GstReferenceTimestampMeta)</c>,
/// gstbuffer.c:3004 at 1.28.6 against :2925 at 1.24.13), so on 1.24 and 1.26
/// the field is not there at all and the accessor throws
/// <see cref="EntryPointNotFoundException"/> rather than reading past the
/// allocation. Both legs are measured here, because the one that runs on this
/// machine is not the one that runs on the floor leg of the matrix.
/// </para>
/// <para>
/// 1.28 has no public call that attaches an <c>info</c> structure:
/// <c>gst_buffer_add_reference_timestamp_meta</c> sets the field to
/// <c>NULL</c> (gstbuffer.c:2783), the library writes it directly, and the one
/// public path that fills it is the deserialisation
/// (gstbuffer.c:2980). So the non-null case is read off a hand written
/// serialisation, whose two formats are both documented in the C: the envelope
/// of <c>gst_meta_serialize</c> (gstmeta.c, <c>[u32 total][u32 name_len]</c>
/// <c>[name\0][u8 version][payload]</c>) and the payload of the item itself
/// (gstbuffer.c: timestamp and duration as little endian 64 bit, the caps as a
/// NUL terminated string, and the optional structure as a second one).
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ReferenceTimestampMetaInfoTests
{
    /// <summary>The implementation name the deserialisation is keyed on.</summary>
    private const string ImplementationName = "GstReferenceTimestampMeta";

    /// <summary>
    /// An item attached by the public call carries no information, and on a
    /// library that has no such field at all the accessor says so.
    /// </summary>
    [Fact]
    public void AnItemAttachedByTheCallCarriesNoInformation()
    {
        using Gst.Buffer buffer = Gst.Buffer.New();
        using Caps reference = Caps.NewEmptySimple("timestamp/x-gstsharp-test");

        ReferenceTimestampMeta? meta = buffer.AddReferenceTimestampMeta(
            reference,
            ClockTime.FromNanoseconds(7),
            ClockTime.FromNanoseconds(11));
        Assert.NotNull(meta);

        // The accessor gates on 1.28.0 while this branches on the symbol
        // NativeAvailability probes, whose first tag is 1.27.90, so the
        // unstable 1.27 band is knowingly unmeasured here: no release series
        // lives there.
        if (NativeAvailability.Has128)
        {
            Assert.Null(meta.GetInfoStructure());
        }
        else
        {
            Assert.Throws<EntryPointNotFoundException>(() => meta.GetInfoStructure());
        }
    }

    /// <summary>
    /// An item whose serialisation carries a structure hands that structure
    /// back: its name and its one field read the way they were written.
    /// </summary>
    [RequiresGStreamerFact(28)]
    public void AnItemDeserialisedWithInformationHandsItBack()
    {
        // The deserialisation looks the implementation up by name, which needs
        // it registered; asking for the block is what registers it.
        Assert.NotNull(ReferenceTimestampMeta.GetInfo());

        using Caps reference = Caps.NewEmptySimple("timestamp/x-gstsharp-test");
        byte[] serialized = Serialize(reference, 7, 11, "meta-info, index=(int)3");

        using Gst.Buffer buffer = Gst.Buffer.New();
        Meta? deserialized = Meta.Deserialize(buffer, serialized, out uint consumed);

        Assert.NotNull(deserialized);
        Assert.Equal((uint)serialized.Length, consumed);

        ReferenceTimestampMeta? meta = buffer.GetReferenceTimestampMeta(reference);
        Assert.NotNull(meta);
        Assert.Equal(7ul, meta.Timestamp.Nanoseconds);

        using Structure? info = meta.GetInfoStructure();
        Assert.NotNull(info);
        Assert.Equal("meta-info", info.GetName());
        Assert.True(info.GetInt("index", out int index));
        Assert.Equal(3, index);

        // The structure that came back is a copy of the one the item owns, so
        // the item keeps its own: a second read answers the same content.
        using Structure? again = meta.GetInfoStructure();
        Assert.NotNull(again);
        Assert.Equal("meta-info", again.GetName());
    }

    /// <summary>
    /// Writes the serialisation of one reference timestamp item, with or
    /// without the optional information structure.
    /// </summary>
    /// <param name="reference">The caps that name what the timestamp refers to.</param>
    /// <param name="timestamp">The timestamp.</param>
    /// <param name="duration">The duration.</param>
    /// <param name="info">The structure to serialise, or <see langword="null"/>.</param>
    /// <returns>The bytes <c>gst_meta_deserialize</c> reads.</returns>
    private static byte[] Serialize(Caps reference, ulong timestamp, ulong duration, string? info)
    {
        byte[] name = Encoding.UTF8.GetBytes(ImplementationName);
        byte[] caps = Encoding.UTF8.GetBytes(reference.ToString() ?? string.Empty);
        byte[] information = info is null ? [] : Encoding.UTF8.GetBytes(info);

        // [u32 total][u32 name_len][name\0][u8 version], the version last,
        // which is what puts the two extra bytes in the header size.
        int header = (2 * sizeof(uint)) + name.Length + 2;
        int payload = (2 * sizeof(ulong)) + caps.Length + 1
            + (info is null ? 0 : information.Length + 1);

        byte[] bytes = new byte[header + payload];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)name.Length);
        name.CopyTo(bytes, 8);
        bytes[header - 1] = 0;

        int at = header;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), timestamp);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + 8), duration);
        at += 2 * sizeof(ulong);
        caps.CopyTo(bytes, at);
        at += caps.Length + 1;
        if (info is not null)
        {
            information.CopyTo(bytes, at);
        }

        return bytes;
    }
}
