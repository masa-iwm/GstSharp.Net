namespace Gst.Interop;

/// <summary>
/// Settles the mini object slot that a walk lends to its function.
/// </summary>
/// <remarks>
/// <para>
/// A handful of C walks hand their function a <c>T**</c> rather than a
/// <c>T*</c>: the slot holds one reference that belongs to the function while
/// it runs, and what the function leaves in the slot decides what the library
/// does with the entry. An unchanged slot means the library takes the
/// reference back and keeps the entry; NULL means the function has released
/// the reference and the entry is removed; another pointer means the function
/// has released the old reference and hands a full reference of the new object
/// over, which replaces the entry
/// (<c>gstpad.c:607-667</c>, <c>gstpad.c:6441-6455</c>,
/// <c>gstbufferlist.c:239-320</c>, <c>gstbufferlist.h:51-54</c>).
/// </para>
/// <para>
/// The binding shows that slot to managed code as a <c>ref</c> parameter of a
/// wrapper, so the three C answers become: leave the wrapper alone, assign
/// <see langword="null"/>, assign another wrapper. This class turns the state
/// of the two wrappers - the one the trampoline handed out and the one the
/// function left behind - back into the pointer the slot has to carry.
/// </para>
/// <para>
/// A wrapper the function disposed, or handed to a member that consumes it,
/// owns nothing afterwards, which is exactly what the C calls "released", so it
/// settles as a removal.
/// </para>
/// </remarks>
internal static class ReplacedSlot
{
    /// <summary>
    /// Answers the pointer to write back into the slot of a walk.
    /// </summary>
    /// <param name="entry">
    /// The wrapper the trampoline handed to the function, which adopted the
    /// reference the slot lent, or <see langword="null"/> when the slot was
    /// NULL.
    /// </param>
    /// <param name="current">
    /// The wrapper the function left in the slot, which is
    /// <paramref name="entry"/> when it kept the entry,
    /// <see langword="null"/> when it removed it, and another wrapper when it
    /// replaced it.
    /// </param>
    /// <returns>
    /// The handle the slot has to carry: <see cref="nint.Zero"/> for a removal,
    /// otherwise a handle that carries one reference the library takes over.
    /// </returns>
    internal static nint Settle(Gst.MiniObject? entry, Gst.MiniObject? current)
    {
        if (ReferenceEquals(current, entry))
        {
            // The entry was kept. A wrapper that owns nothing any more - it was
            // disposed, or a consuming member took it over - has released the
            // lent reference, which is the removal answer. Otherwise the
            // wrapper hands its reference back; the handle it holds now is the
            // one to write, because an in-place MakeWritable() may have
            // replaced the object behind the wrapper, and that is the C replace.
            return entry is null || entry.IsDisposed ? nint.Zero : entry.HandOver();
        }

        // The entry was removed or replaced, so the lent reference has to go.
        // Dispose is idempotent and a no-op on a wrapper the function already
        // consumed, so this releases the reference exactly when it is still held.
        entry?.Dispose();

        return current is null || current.IsDisposed ? nint.Zero : current.HandOver();
    }
}
