namespace Gst.Interop;

/// <summary>
/// The precondition of the zero copy entry points that wrap memory the caller
/// owns.
/// </summary>
/// <remarks>
/// <c>gst_memory_new_wrapped</c> opens with <c>g_return_val_if_fail</c> guards
/// and answers a call that breaks them with a critical warning and a null
/// pointer. <c>gst_buffer_new_wrapped_full</c> hands that null pointer straight
/// to <c>gst_memory_lock</c> without checking it, so the process dies rather
/// than reporting anything. Both hand written members therefore validate the
/// range themselves, before they allocate the handle of the notification.
/// </remarks>
internal static class WrappedMemory
{
    /// <summary>Validates the block a caller lends to the library.</summary>
    /// <param name="data">The address of the block.</param>
    /// <param name="maxsize">How many bytes the block holds.</param>
    /// <param name="offset">Where the valid data starts inside the block.</param>
    /// <param name="size">How many valid bytes there are.</param>
    /// <exception cref="ArgumentException">
    /// The address is <c>0</c>, or the range does not fit into the block.
    /// </exception>
    internal static void ValidateRange(nint data, nuint maxsize, nuint offset, nuint size)
    {
        if (data == 0)
        {
            throw new ArgumentException("The wrapped memory must not be a null pointer.", nameof(data));
        }

        if (offset > maxsize)
        {
            throw new ArgumentException(
                "The wrapped range does not start inside the block: offset must not exceed maxsize.",
                nameof(offset));
        }

        // Written as a subtraction rather than as offset + size so that a sum
        // that wraps around cannot pass for a range that fits. The check above
        // has already ruled out an offset past the end of the block, so the
        // difference cannot underflow.
        if (size > maxsize - offset)
        {
            throw new ArgumentException(
                "The wrapped range does not fit into the block: offset plus size must not exceed maxsize.",
                nameof(size));
        }
    }
}
