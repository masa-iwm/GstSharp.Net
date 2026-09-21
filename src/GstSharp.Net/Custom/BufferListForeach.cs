using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// The function <see cref="Gst.BufferList.Foreach"/> calls for every buffer of
/// a buffer list.
/// </summary>
/// <param name="buffer">
/// The buffer, in a slot the function may change.
/// <para>
/// The wrapper owns one reference for the length of the invocation and is dead
/// once the function returns, so copy anything that has to outlive it. Leave
/// the slot alone to keep the entry. Assign <see langword="null"/> to remove
/// it; the binding releases the reference the slot lent. Assign another buffer
/// to replace it: that wrapper is handed over to the library and cannot be used
/// afterwards (a wrapper that only borrows its buffer has a reference minted
/// for the library instead), and the old buffer is released. Disposing the
/// wrapper, or passing it to a member that consumes it, without clearing the
/// slot counts as a removal.
/// </para>
/// </param>
/// <param name="idx">
/// The index of the buffer in the list. A removal moves the buffers behind it
/// down, so the next buffer is shown under the index the removed one had.
/// </param>
/// <returns>
/// <see langword="true"/> to go on with the next buffer,
/// <see langword="false"/> to end the walk.
/// </returns>
/// <remarks>
/// <para>
/// When the list and the buffer are both writable the buffer is writable inside
/// the function, so editing it in place is allowed and
/// <see cref="Gst.Buffer.MakeWritable"/> on the wrapper is a legal replace:
/// the copy it may answer becomes the entry.
/// </para>
/// <para>
/// A change offered while the list itself is not writable is refused by the
/// library with a GLib CRITICAL, and the buffer that was offered is released
/// (<c>gstbufferlist.c:284-292</c>). A removal - clearing the slot, or disposing
/// the wrapper - is a change like any other and is refused the same way.
/// </para>
/// <para>
/// <b>A removal the library refuses does not end: the walk never leaves the
/// entry.</b> The index is advanced only for an entry the function left behind
/// (<c>gstbufferlist.c:318-320</c>), so a refused removal shows the same buffer
/// under the same index again, and the CRITICAL is written once and then
/// silenced - the walk spins with nothing said. Nothing in the binding can stop
/// it, because the library goes on presenting the entry: a function that
/// removes must know the list is writable.
/// <see cref="Gst.MiniObject.IsWritable"/> answers that for any live wrapper,
/// borrowed or not; <see cref="Gst.BufferList.MakeWritable"/> is the way to
/// make it true, and it is available only on a wrapper that owns a reference -
/// a borrowed list throws there. A function walking a list it cannot make
/// writable and that answers <see langword="false"/> has to keep every entry.
/// </para>
/// <para>
/// The function must not touch the list itself while it runs: reading it,
/// inserting into it or removing from it under the walk shows entries the walk
/// is in the middle of settling, and the entry of a writable list is held by
/// the wrapper alone, so reading it back after the wrapper was disposed or
/// replaced reads memory that has been freed.
/// </para>
/// <para>
/// An exception that leaves the function is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and ends the walk. The slot is still
/// settled by the rules above, so a buffer the function had already removed or
/// replaced stays removed or replaced.
/// </para>
/// </remarks>
public delegate bool BufferListFunc(ref Gst.Buffer? buffer, uint idx);

/// <content>
/// The walk over the buffers of a list, which the generator cannot emit.
/// </content>
public sealed unsafe partial class BufferList
{
    /// <summary>
    /// Calls a function for every buffer of this list.
    /// </summary>
    /// <param name="func">
    /// The function to call for each buffer. It may keep, remove or replace
    /// each buffer through its slot, and it ends the walk by answering
    /// <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the function answered
    /// <see langword="true"/> for every buffer, or the list is empty;
    /// <see langword="false"/> when it ended the walk.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_list_foreach</c>, written by hand because its
    /// callback is lent a reference through a <c>GstBuffer**</c> the function
    /// may keep, clear or replace (<c>gstbufferlist.c:239-320</c>). The only
    /// <c>inout</c> handle projection the generator has is the identity one of
    /// <see cref="Gst.PadGetRangeFunction"/>, which cannot express that
    /// ownership.
    /// </para>
    /// <para>
    /// The function is called on the calling thread and every call it gets has
    /// happened before this method returns; nothing is kept of it afterwards.
    /// </para>
    /// <para>
    /// <b>Do not remove entries from a list that is not writable.</b> The
    /// library refuses the removal, keeps the entry and does not advance the
    /// index (<c>gstbufferlist.c:284-292</c>, <c>:318-320</c>), so the same
    /// buffer is shown again under the same index for as long as the function
    /// goes on clearing its slot - and after the first refusal the CRITICAL is
    /// silenced, which leaves a walk that never returns and says nothing. Ask
    /// <see cref="Gst.MiniObject.IsWritable"/> first, which any live wrapper
    /// answers, and call <see cref="MakeWritable"/> before the walk when it is
    /// false - that one needs a wrapper that owns a reference, so a borrowed
    /// list is walked without removing anything.
    /// </para>
    /// <para>
    /// An exception thrown by the function is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> rather than thrown here, because
    /// it would otherwise unwind through the native frames of the walk; the
    /// walk then ends and this answers <see langword="false"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="func"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool Foreach(Gst.BufferListFunc func)
    {
        ArgumentNullException.ThrowIfNull(func);
        nint instanceHandle = Handle;
        Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
        try
        {
            int nativeResult = GstBufferListForeach(instanceHandle, ForeachTrampoline.Pointer, funcState.UserData);
            bool result = nativeResult != 0;
            System.GC.KeepAlive(this);
            return result;
        }
        finally
        {
            funcState.Free();
        }
    }

    /// <summary>The native entry point of <see cref="Gst.BufferListFunc"/>.</summary>
    /// <remarks>
    /// The slot holds one reference that belongs to the function while it runs,
    /// so the wrapper adopts it rather than borrowing the buffer - which is also
    /// what leaves a buffer of a writable list writable inside the function -
    /// and <see cref="Gst.Interop.ReplacedSlot.Settle"/> turns whatever the
    /// function left behind back into the pointer the library reads.
    /// </remarks>
    internal static class ForeachTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint*, uint, nint, int>)&Invoke;

        /// <summary>Runs the managed function on one buffer.</summary>
        /// <param name="buffer">The slot that carries the buffer.</param>
        /// <param name="idx">The index of the buffer in the list.</param>
        /// <param name="userData">The <c>GCHandle</c> of the state of the walk.</param>
        /// <returns>Non zero to go on, zero to end the walk.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Invoke(nint* buffer, uint idx, nint userData)
        {
            Gst.BufferListFunc callback;
            try
            {
                if (Gst.Interop.CallbackHandle.GetState<Gst.BufferListFunc>(userData) is not { } read)
                {
                    // Without the function nothing can decide anything, and ending
                    // the walk with the slot untouched changes nothing about the list.
                    return 0;
                }

                callback = read;
            }
            catch (Exception exception)
            {
                // Reading the function is a handle lookup that throws on a
                // handle that was freed under the walk. There is nothing to
                // settle over the slot then - the reference it lends was never
                // taken - so the walk ends with the slot as the library left it.
                Gst.Interop.ExceptionTrap.Report(exception);
                return 0;
            }

            Gst.Buffer? entry;
            try
            {
                // The library never passes a NULL slot (gstbufferlist.c:275); the
                // check is here so that there is no unchecked dereference.
                entry = buffer is null ? null : Gst.Buffer.FromNative(*buffer, Gst.Interop.Transfer.Full);
            }
            catch (Exception exception)
            {
                // The wrapper was never built, so the reference the slot lends
                // was never taken over either. Settling over the slot here
                // would settle a null - a removal of a buffer that is still
                // perfectly good - so the slot is left untouched and the walk
                // declines instead.
                Gst.Interop.ExceptionTrap.Report(exception);
                return 0;
            }

            Gst.Buffer? current = entry;
            int answer = 0;

            try
            {
                answer = callback(ref current, idx) ? 1 : 0;
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                answer = 0;
            }

            try
            {
                nint settled = Gst.Interop.ReplacedSlot.Settle(entry, current);
                if (buffer is not null)
                {
                    *buffer = settled;
                }
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                if (buffer is not null)
                {
                    *buffer = nint.Zero;
                }

                answer = 0;
            }

            return answer;
        }
    }

    /// <summary>The <c>gst_buffer_list_foreach</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_list_foreach")]
    private static partial int GstBufferListForeach(nint list, nint func, nint userData);
}
