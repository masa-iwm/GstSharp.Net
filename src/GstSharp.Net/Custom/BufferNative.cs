using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written buffer
/// glue needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_buffer_map</c> and <c>gst_buffer_unmap</c> are imported by hand
/// because the mapped memory has to be handed out as a
/// <see cref="System.Span{T}"/> whose lifetime is tied to the unmap call, which
/// is a shape no generated signature can produce. Once the function emitter
/// covers <c>GstBuffer</c>, both entry points belong on the skip list of
/// <c>girs/overlays/fixups.json</c>, so that the generated bindings do not
/// offer a second, unscoped way to map a buffer.
/// </para>
/// <para>
/// Every signature is blittable: <c>gboolean</c> is an <see cref="int"/> and
/// the <c>GstMapInfo</c> of the caller is passed by address.
/// </para>
/// </remarks>
internal static unsafe partial class BufferNative
{
    /// <summary>
    /// Fills <paramref name="info"/> with the memory of a buffer, merging its
    /// memory blocks into one if there is more than one.
    /// </summary>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="info">Receives the mapped memory.</param>
    /// <param name="flags">The access the caller needs.</param>
    /// <returns>Non zero when the buffer was mapped.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_map")]
    internal static partial int Map(nint buffer, MapInfo* info, MapFlags flags);

    /// <summary>
    /// Releases the memory that <see cref="Map"/> handed out.
    /// </summary>
    /// <param name="buffer">The buffer that was mapped.</param>
    /// <param name="info">The mapping to release.</param>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_unmap")]
    internal static partial void Unmap(nint buffer, MapInfo* info);
}
