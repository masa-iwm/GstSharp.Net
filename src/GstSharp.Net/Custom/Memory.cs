namespace Gst;

public sealed partial class Memory
{
    /// <summary>
    /// Creates a memory block over memory the caller owns, without copying it.
    /// </summary>
    /// <param name="flags">
    /// The flags of the wrapped memory. <see cref="Gst.MemoryFlags.Readonly"/>
    /// is what keeps the pipeline from writing through the block.
    /// </param>
    /// <param name="data">The address of the block, which must not be <c>0</c>.</param>
    /// <param name="maxsize">How many bytes the block holds.</param>
    /// <param name="offset">Where the valid data starts inside the block.</param>
    /// <param name="size">How many valid bytes there are from <paramref name="offset"/> on.</param>
    /// <param name="notify">
    /// What to run once the pipeline has released the memory, or
    /// <see langword="null"/> when the caller releases it some other way.
    /// </param>
    /// <returns>
    /// The memory, which the caller owns and disposes, or <see langword="null"/>
    /// when the library refused the block. <paramref name="notify"/> does not
    /// run in that case, and the state it carried is released here.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_memory_new_wrapped</c>, and it is zero copy: the block
    /// that is passed in is the memory. The caller keeps owning it and has to
    /// keep it alive, and unmoved, until <paramref name="notify"/> runs. A
    /// managed array therefore has to be pinned by a
    /// <see cref="System.Runtime.InteropServices.GCHandle"/> of its own for
    /// exactly that long.
    /// </para>
    /// <para>
    /// <paramref name="notify"/> runs on an arbitrary streaming thread —
    /// whichever one drops the last reference of the memory — and it runs
    /// exactly once.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="data"/> is <c>0</c>, or <paramref name="offset"/> and
    /// <paramref name="size"/> do not fit into <paramref name="maxsize"/>. The
    /// C function answers such a call with a critical warning, which the
    /// binding turns into an exception before it allocates anything.
    /// </exception>
    public static unsafe Memory? NewWrapped(
        Gst.MemoryFlags flags,
        nint data,
        nuint maxsize,
        nuint offset,
        nuint size,
        Action? notify)
    {
        Gst.Interop.WrappedMemory.ValidateRange(data, maxsize, offset, size);

        Gst.Interop.CallbackHandle state = notify is null
            ? default
            : Gst.Interop.CallbackHandle.Alloc(notify);

        nint result = MemoryNative.NewWrapped(
            flags,
            data,
            maxsize,
            offset,
            size,
            state.UserData,
            notify is null ? 0 : (nint)Gst.Interop.CallbackHandle.InvokeAndFreeNotify);

        if (result == 0)
        {
            // Nothing was created, so nothing will ever run the notification.
            state.Free();
            return null;
        }

        return Gst.Memory.FromNative(result, Gst.Interop.Transfer.Full);
    }
}
