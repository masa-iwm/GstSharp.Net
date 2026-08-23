using System;

namespace Gst;

public sealed unsafe partial class Meta
{
    /// <summary>
    /// Gets the registered implementation this metadata item belongs to.
    /// </summary>
    /// <value>The implementation block of the item.</value>
    /// <remarks>
    /// The block is owned by the metadata registry and outlives every buffer
    /// that carries an item of it, so it is never released and the wrapper of
    /// it takes no part in any ownership.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    /// <exception cref="InvalidOperationException">
    /// The item carries no implementation block, which an item attached to a
    /// buffer never does.
    /// </exception>
    public Gst.MetaInfo Info
    {
        get
        {
            Gst.MetaInfo? info = Gst.MetaInfo.FromNative(((MetaRaw*)RequireHandle())->Info);
            GC.KeepAlive(this);
            return info
                ?? throw new InvalidOperationException(
                    "The metadata item carries no implementation block, so it is not attached to a buffer.");
        }
    }

    /// <summary>
    /// Gets the <c>GType</c> of the metadata API this item implements.
    /// </summary>
    /// <value>The API type of the item.</value>
    /// <remarks>
    /// This is the discriminator GStreamer itself keys on:
    /// <c>gst_buffer_get_meta</c> and <c>gst_buffer_iterate_meta_filtered</c>
    /// both compare <c>meta-&gt;info-&gt;api</c>. Compare it against the
    /// <c>*MetaApiGetType()</c> of the module that declares the metadata, which
    /// is what the <c>FromMeta</c> casts do.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public Gst.GObject.GType ApiType => Info.Api;

    /// <summary>
    /// Serialises this item into the wire format <c>gst_meta_deserialize</c>
    /// reads.
    /// </summary>
    /// <returns>
    /// The bytes, or <see langword="null"/> when the implementation of the item
    /// carries no serialisation function or refused the item.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <c>gst_meta_serialize_simple</c> (gstmeta.c, since GStreamer 1.24)
    /// writes <c>[u32 total][u32 name_len][name\0][u8 version][payload]</c> into
    /// a <c>GByteArray</c> that this member owns for the length of the call and
    /// releases before it returns.
    /// </para>
    /// <para>
    /// This supersedes the generated <c>Serialize(ByteArrayInterface)</c>,
    /// which managed code cannot call because
    /// <see cref="Gst.ByteArrayInterface"/> has no public constructor.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    public byte[]? Serialize()
    {
        nint handle = RequireHandle();
        nint array = Gst.Interop.GLibNative.ByteArrayNew();

        try
        {
            if (MetaNative.SerializeSimple(handle, array) == 0)
            {
                return null;
            }

            Gst.Interop.GByteArrayRaw raw = *(Gst.Interop.GByteArrayRaw*)array;
            byte[] bytes = new byte[raw.Len];
            if (raw.Len != 0)
            {
                new ReadOnlySpan<byte>((void*)raw.Data, checked((int)raw.Len)).CopyTo(bytes);
            }

            return bytes;
        }
        finally
        {
            // The item has to outlive the call that reads it, and the array is
            // released whichever way the call went; the second argument frees
            // the bytes with it, which is what nothing having taken them means.
            _ = Gst.Interop.GLibNative.ByteArrayFree(array, 1);
            GC.KeepAlive(this);
        }
    }

    /// <summary>
    /// Combines the allocation parameters of two elements for one metadata API.
    /// </summary>
    /// <param name="api">The metadata API to aggregate for.</param>
    /// <param name="aggregatedParams">
    /// Receives a newly allocated structure the caller owns, or
    /// <see langword="null"/> when the API has no aggregator and when both
    /// inputs were absent.
    /// </param>
    /// <param name="params0">The first set of parameters, or <see langword="null"/>.</param>
    /// <param name="params1">The second set of parameters, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the parameters were aggregated.
    /// </returns>
    /// <remarks>
    /// <para>Available since GStreamer 1.26.</para>
    /// <para>
    /// The C function takes a <c>GstStructure**</c>, which is why this is the
    /// out parameter shape and why neither input is modified.
    /// </para>
    /// </remarks>
    public static bool ApiTypeAggregateParams(
        Gst.GObject.GType api,
        out Gst.Structure? aggregatedParams,
        Gst.Structure? params0,
        Gst.Structure? params1)
    {
        nint aggregated = 0;
        int nativeResult = MetaNative.ApiTypeAggregateParams(
            api.Value,
            &aggregated,
            params0 is null ? 0 : params0.Handle,
            params1 is null ? 0 : params1.Handle);
        GC.KeepAlive(params0);
        GC.KeepAlive(params1);
        aggregatedParams = Gst.Structure.FromNative(aggregated, Gst.Interop.Transfer.Full);
        return nativeResult != 0;
    }

    /// <summary>
    /// The shape this call shipped with in 1.28.2, which never worked.
    /// </summary>
    /// <param name="api">The metadata API to aggregate for.</param>
    /// <param name="aggregatedParams">Where the result was supposed to go.</param>
    /// <param name="params0">The first set of parameters.</param>
    /// <param name="params1">The second set of parameters.</param>
    /// <returns>Nothing; the call always throws.</returns>
    /// <remarks>
    /// The library writes a pointer through the second argument, so every
    /// historical call overwrote the header of the structure it was handed and
    /// leaked the copy the aggregator had just made. This overload is kept only
    /// so that code compiled against 1.28.2 still links; use the overload whose
    /// second parameter is an <see langword="out"/> parameter.
    /// </remarks>
    /// <exception cref="NotSupportedException">Always.</exception>
    [Obsolete("This overload corrupted the structure it was given; use the out-parameter overload. It " +
        "will be removed in 1.30.", error: false)]
    public static bool ApiTypeAggregateParams(
        Gst.GObject.GType api,
        Gst.Structure aggregatedParams,
        Gst.Structure params0,
        Gst.Structure params1) =>
        throw new NotSupportedException(
            "gst_meta_api_type_aggregate_params writes a GstStructure* through its second argument, so this " +
            "overload overwrote the header of the structure it was given and never produced a result. Use " +
            "the overload whose second parameter is an out parameter.");

    /// <summary>
    /// Returns the handle of a metadata item that is still attached to its
    /// buffer.
    /// </summary>
    /// <returns>The item.</returns>
    /// <remarks>
    /// A metadata wrapper whose handle is zero is dead: the handle is zeroed
    /// exactly where the library frees the item, which is
    /// <see cref="Gst.Buffer.RemoveMeta"/> on <see langword="true"/> and a
    /// removal that <see cref="Gst.Buffer.ForeachMeta"/> honoured.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The item was removed from its buffer.</exception>
    internal nint RequireHandle()
    {
        nint handle = Handle;

        if (handle == 0)
        {
            throw new ObjectDisposedException(
                nameof(Meta),
                "The metadata item was removed from its buffer and freed with it.");
        }

        return handle;
    }
}
