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
/// <c>gst_buffer_copy</c> is imported by hand for a different reason: the gir
/// marks it <c>introspectable="0"</c>, so the generator skips it and no
/// overlay can bring it back. For C consumers it is a static inline function
/// of <c>gst/gstbuffer.h</c> that forwards to <c>gst_mini_object_copy</c>, but
/// the library itself is built with the inline functions disabled and exports
/// it as a real symbol, which is what makes it importable at all. Should a
/// build ever be met that does not export it,
/// <c>gst_mini_object_copy</c> is the fallback entry point that the inline
/// version calls.
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

    /// <summary>
    /// Creates a copy of a buffer: its fields and its metadata are copied, its
    /// memory is shared.
    /// </summary>
    /// <param name="buffer">The buffer to copy.</param>
    /// <returns>
    /// The copy, which the caller owns, or <c>0</c> when the copy failed.
    /// </returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_copy")]
    internal static partial nint Copy(nint buffer);

    /// <summary>
    /// Copies bytes out of a buffer into memory the caller provides.
    /// </summary>
    /// <param name="buffer">The buffer to read from.</param>
    /// <param name="offset">Where in the buffer to start reading.</param>
    /// <param name="dest">The destination, pinned by the caller.</param>
    /// <param name="size">How many bytes to copy at most.</param>
    /// <returns>
    /// How many bytes were copied, which is less than <paramref name="size"/>
    /// when the buffer held less than that from <paramref name="offset"/> on.
    /// </returns>
    /// <remarks>
    /// The gir describes the destination as a caller allocated out array, whose
    /// length is a second parameter the caller states; the C# spelling of that
    /// is a span, whose length says the same thing and cannot disagree with it.
    /// The generated marshalling has no such projection, which is why the entry
    /// point is on the skip list and this is imported by hand.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_extract")]
    internal static partial nuint Extract(nint buffer, nuint offset, byte* dest, nuint size);

    /// <summary>
    /// Creates a buffer over memory the caller owns, without copying it.
    /// </summary>
    /// <param name="flags">The flags of the memory that is wrapped.</param>
    /// <param name="data">The block the buffer reads and writes through.</param>
    /// <param name="maxsize">How many bytes the block holds.</param>
    /// <param name="offset">Where the valid data starts inside the block.</param>
    /// <param name="size">How many valid bytes there are.</param>
    /// <param name="userData">The state of the notification, or <c>0</c>.</param>
    /// <param name="notify">The notification that runs when the memory is released, or <c>0</c>.</param>
    /// <returns>The buffer, which the caller owns.</returns>
    /// <remarks>
    /// The gir describes <c>data</c> as an array of <c>size</c> bytes, which is
    /// upstream's own doc comment and wrong twice over: the block is
    /// <paramref name="maxsize"/> bytes and it has to outlive the call. That is
    /// why the entry point is on the skip list and this is imported by hand.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_new_wrapped_full")]
    internal static partial nint NewWrappedFull(
        MemoryFlags flags,
        nint data,
        nuint maxsize,
        nuint offset,
        nuint size,
        nint userData,
        nint notify);
}
