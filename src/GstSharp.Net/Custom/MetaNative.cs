using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written metadata
/// glue needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_meta_serialize_simple</c> writes into a <c>GByteArray</c> the caller
/// owns. The generated sibling of it, <c>Meta.Serialize(ByteArrayInterface)</c>,
/// is unreachable surface because <see cref="Gst.ByteArrayInterface"/> has no
/// public constructor, so <see cref="Gst.Meta.Serialize()"/> owns a byte array
/// for the length of the call and answers a <c>byte[]</c> instead. See the
/// <c>skip</c> list of <c>girs/overlays/fixups.json</c> for the ledger entry.
/// </para>
/// <para>
/// <c>gst_meta_api_type_aggregate_params</c> takes a <c>GstStructure**</c> that
/// the gir declares as a <c>GstStructure*</c>, which is why the entry point is
/// on the skip list and the out parameter overload of
/// <c>Gst.Meta.ApiTypeAggregateParams</c> is written by hand. The signature
/// here is the one of the C function.
/// </para>
/// <para>
/// Every signature is blittable: <c>gboolean</c> is an <see cref="int"/> and a
/// <c>GType</c> is an <see cref="nuint"/>.
/// </para>
/// </remarks>
internal static unsafe partial class MetaNative
{
    /// <summary>
    /// Serialises a metadata item into a byte array the caller owns.
    /// </summary>
    /// <param name="meta">The item to serialise.</param>
    /// <param name="data">The <c>GByteArray</c> the bytes are appended to.</param>
    /// <returns>
    /// Non zero when the item was serialised; zero when its implementation
    /// carries no serialisation function or refused the item.
    /// </returns>
    [LibraryImport("Gst", EntryPoint = "gst_meta_serialize_simple")]
    internal static partial int SerializeSimple(nint meta, nint data);

    /// <summary>
    /// Combines the allocation parameters of two elements for one metadata API.
    /// </summary>
    /// <param name="api">The metadata API to aggregate for.</param>
    /// <param name="aggregatedParams">
    /// Receives a newly allocated structure the caller owns, or <c>0</c>.
    /// </param>
    /// <param name="params0">The first set of parameters, or <c>0</c>.</param>
    /// <param name="params1">The second set of parameters, or <c>0</c>.</param>
    /// <returns>Non zero when the parameters were aggregated.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_meta_api_type_aggregate_params")]
    internal static partial int ApiTypeAggregateParams(
        nuint api,
        nint* aggregatedParams,
        nint params0,
        nint params1);
}
