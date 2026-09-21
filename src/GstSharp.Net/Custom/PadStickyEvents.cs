using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// The function <see cref="Gst.Pad.StickyEventsForeach"/> calls for every
/// sticky event of a pad.
/// </summary>
/// <param name="pad">The pad the walk was started on.</param>
/// <param name="event">
/// The sticky event, in a slot the function may change.
/// <para>
/// The wrapper owns one reference for the length of the invocation and is dead
/// once the function returns, so copy anything that has to outlive it. Leave
/// the slot alone to keep the event. Assign <see langword="null"/> to remove
/// it; the binding releases the reference the slot lent. Assign another event
/// to replace it: that wrapper is handed over to the library and cannot be used
/// afterwards (a wrapper that only borrows its event has a reference minted for
/// the library instead), and the old event is released. Disposing the wrapper,
/// or passing it to a member that consumes it, without clearing the slot counts
/// as a removal.
/// </para>
/// </param>
/// <returns>
/// <see langword="true"/> to go on with the next sticky event,
/// <see langword="false"/> to end the walk.
/// </returns>
/// <remarks>
/// <para>
/// The event is never editable in place, because the pad holds a second
/// reference to it for as long as it is stored: <see cref="Gst.Event.MakeWritable"/>
/// on the wrapper therefore always copies, and the copy becomes the entry, which
/// is the same answer as assigning a modified copy to the slot.
/// </para>
/// <para>
/// The object lock of the pad is not held while the function runs, so the
/// sticky events may change underneath the walk; the library then restarts it
/// from the first event, which is why a function can be shown the same event
/// twice. The answer of the invocation that saw the change is discarded, so a
/// replacement handed over in it is released rather than stored.
/// </para>
/// <para>
/// An exception that leaves the function is reported through
/// <see cref="Gst.Interop.ExceptionTrap"/> and ends the walk. The slot is still
/// settled by the rules above, so an event the function had already removed or
/// replaced stays removed or replaced.
/// </para>
/// </remarks>
public delegate bool PadStickyEventsForeachFunction(Gst.Pad pad, ref Gst.Event? @event);

/// <content>
/// The walk over the sticky events of a pad, which the generator cannot emit.
/// </content>
public unsafe partial class Pad
{
    /// <summary>
    /// Calls a function for every sticky event of this pad.
    /// </summary>
    /// <param name="foreachFunc">
    /// The function to call for each event. It may keep, remove or replace each
    /// event through its slot, and it ends the walk by answering
    /// <see langword="false"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_pad_sticky_events_foreach</c>, written by hand because its
    /// callback is lent a reference through a <c>GstEvent**</c> the function may
    /// keep, clear or replace (<c>gstpad.c:607-667</c>,
    /// <c>gstpad.c:6441-6455</c>). The only <c>inout</c> handle projection the
    /// generator has is the identity one of
    /// <see cref="Gst.PadGetRangeFunction"/>, which cannot express that
    /// ownership.
    /// </para>
    /// <para>
    /// The function is called on the calling thread and every call it gets has
    /// happened before this method returns; nothing is kept of it afterwards.
    /// The object lock of the pad is released around each invocation, so the
    /// function may call back into the pad.
    /// </para>
    /// <para>
    /// An exception thrown by the function is reported through
    /// <see cref="Gst.Interop.ExceptionTrap"/> rather than thrown here, because
    /// it would otherwise unwind through the native frames of the walk.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="foreachFunc"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void StickyEventsForeach(Gst.PadStickyEventsForeachFunction foreachFunc)
    {
        ArgumentNullException.ThrowIfNull(foreachFunc);
        nint instanceHandle = Handle;
        Gst.Interop.CallbackHandle funcState =
            Gst.Interop.CallbackHandle.Alloc(new StickyEventsForeachState(this, foreachFunc));
        try
        {
            GstPadStickyEventsForeach(instanceHandle, StickyEventsForeachTrampoline.Pointer, funcState.UserData);
            System.GC.KeepAlive(this);
        }
        finally
        {
            funcState.Free();
        }
    }

    /// <summary>The state one walk over the sticky events of a pad carries.</summary>
    /// <remarks>
    /// The pad is carried along so that the function is handed the very wrapper
    /// the walk was started on rather than a second wrapper of the same pad.
    /// </remarks>
    private sealed class StickyEventsForeachState
    {
        internal StickyEventsForeachState(Gst.Pad pad, Gst.PadStickyEventsForeachFunction function)
        {
            Pad = pad;
            Function = function;
        }

        /// <summary>Gets the pad the walk was started on.</summary>
        internal Gst.Pad Pad { get; }

        /// <summary>Gets the function to call for each sticky event.</summary>
        internal Gst.PadStickyEventsForeachFunction Function { get; }

        /// <summary>
        /// Gets or sets a value indicating whether an invocation has ended the
        /// walk.
        /// </summary>
        /// <remarks>
        /// An invocation that removes its event is answered by a
        /// <c>continue</c> that skips the <c>if (!ret) break;</c> of the loop
        /// (<c>gstpad.c:644-651</c>), so the library goes on asking however the
        /// function answered. The next invocation therefore has to decline with
        /// the slot untouched, which is the answer the library does break on.
        /// </remarks>
        internal bool Stopped { get; set; }
    }

    /// <summary>
    /// The native entry point of
    /// <see cref="Gst.PadStickyEventsForeachFunction"/>.
    /// </summary>
    /// <remarks>
    /// The slot holds one reference that belongs to the function while it runs,
    /// so the wrapper adopts it rather than borrowing the event, and
    /// <see cref="Gst.Interop.ReplacedSlot.Settle"/> turns whatever the function
    /// left behind back into the pointer the library reads.
    /// </remarks>
    internal static class StickyEventsForeachTrampoline
    {
        /// <summary>Gets the address that is handed to native code.</summary>
        internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint*, nint, int>)&Invoke;

        /// <summary>Runs the managed function on one sticky event.</summary>
        /// <param name="pad">The pad that is walked.</param>
        /// <param name="event">The slot that carries the event.</param>
        /// <param name="userData">The <c>GCHandle</c> of the state of the walk.</param>
        /// <returns>Non zero to go on, zero to end the walk.</returns>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static int Invoke(nint pad, nint* @event, nint userData)
        {
            _ = pad;

            StickyEventsForeachState state;
            try
            {
                if (Gst.Interop.CallbackHandle.GetState<StickyEventsForeachState>(userData) is not { } read)
                {
                    // Without the state nothing can decide anything, and ending the
                    // walk with the slot untouched changes nothing about the pad.
                    return 0;
                }

                state = read;
            }
            catch (Exception exception)
            {
                // Reading the state is a handle lookup, and a handle that was
                // freed under the walk may throw there - what it does is
                // undefined. There is nothing to settle
                // over the slot then - the reference it lends was never taken -
                // so the walk ends with the slot as the library left it.
                Gst.Interop.ExceptionTrap.Report(exception);
                return 0;
            }

            if (state.Stopped)
            {
                // An earlier invocation ended the walk while it removed its
                // event, which the library answers by going on. Leaving the
                // slot untouched is what it does break on, and it takes the
                // reference the slot lends straight back.
                return 0;
            }

            Gst.Event? entry;
            try
            {
                // The library never passes a NULL slot (gstpad.c:623, :6446); the
                // check is here so that there is no unchecked dereference.
                entry = @event is null ? null : Gst.Event.FromNative(*@event, Gst.Interop.Transfer.Full);
            }
            catch (Exception exception)
            {
                // The wrapper was never built, so the reference the slot lends
                // was never taken over either. Settling over the slot here
                // would settle a null - a removal of an event that is still
                // perfectly good - so the slot is left untouched and the walk
                // declines instead.
                Gst.Interop.ExceptionTrap.Report(exception);
                state.Stopped = true;
                return 0;
            }

            Gst.Event? current = entry;
            int answer = 0;

            try
            {
                answer = state.Function(state.Pad, ref current) ? 1 : 0;
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                answer = 0;
            }

            try
            {
                nint settled = Gst.Interop.ReplacedSlot.Settle(entry, current);
                if (@event is not null)
                {
                    *@event = settled;
                }
            }
            catch (Exception exception)
            {
                Gst.Interop.ExceptionTrap.Report(exception);
                if (@event is not null)
                {
                    *@event = nint.Zero;
                }

                answer = 0;
            }

            if (answer == 0)
            {
                state.Stopped = true;
            }

            return answer;
        }
    }

    /// <summary>The <c>gst_pad_sticky_events_foreach</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_pad_sticky_events_foreach")]
    private static partial void GstPadStickyEventsForeach(nint pad, nint foreachFunc, nint userData);
}
