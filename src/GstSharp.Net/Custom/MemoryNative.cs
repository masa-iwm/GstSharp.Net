using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written memory
/// glue needs.
/// </summary>
/// <remarks>
/// <c>gst_memory_new_wrapped</c> is imported by hand for the same reason as
/// <see cref="BufferNative.NewWrappedFull"/>: the block it is handed is
/// <c>maxsize</c> bytes and has to outlive the call, which is neither what the
/// gir says nor a shape a generated signature can produce.
/// </remarks>
internal static unsafe partial class MemoryNative
{
    /// <summary>
    /// Creates a memory block over memory the caller owns, without copying it.
    /// </summary>
    /// <param name="flags">The flags of the memory that is wrapped.</param>
    /// <param name="data">The block the memory reads and writes through.</param>
    /// <param name="maxsize">How many bytes the block holds.</param>
    /// <param name="offset">Where the valid data starts inside the block.</param>
    /// <param name="size">How many valid bytes there are.</param>
    /// <param name="userData">The state of the notification, or <c>0</c>.</param>
    /// <param name="notify">The notification that runs when the memory is released, or <c>0</c>.</param>
    /// <returns>The memory, which the caller owns, or <c>0</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_memory_new_wrapped")]
    internal static partial nint NewWrapped(
        MemoryFlags flags,
        nint data,
        nuint maxsize,
        nuint offset,
        nuint size,
        nint userData,
        nint notify);
}
