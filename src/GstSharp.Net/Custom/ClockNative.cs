using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written clock glue
/// needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_clock_id_wait_async</c> is imported by hand because the member that
/// stands for it is written by hand: the C function only takes the destroy
/// notification over once it has written it onto the entry, which it does right
/// before it dispatches to the clock, so the binding has to release the state of
/// the callback itself on the refusals in front of that. See the <c>skip</c>
/// list of <c>girs/overlays/fixups.json</c> for the ledger entry and
/// <see cref="Gst.Clock.IdWaitAsync"/> for how the binding tells those apart
/// from the refusals that come back from the clock.
/// </para>
/// <para>
/// <c>gst_clock_id_get_clock</c> is imported here as well, next to the call
/// that needs it, even though <see cref="Gst.Clock.IdGetClock"/> is generated:
/// that member answers a wrapper, and wrappers are interned, so it would hand
/// back the one the caller already holds and releasing it would dispose the
/// clock of the caller. The reference that member takes has to stay a bare
/// handle to be a reference of its own.
/// </para>
/// <para>
/// The signature is the one of the C function, with the callback and the
/// destroy notification as plain addresses: both are
/// <c>[UnmanagedCallersOnly]</c> methods, which have no delegate type to
/// marshal. A <c>GstClockReturn</c> is an <see cref="int"/>.
/// </para>
/// </remarks>
internal static partial class ClockNative
{
    /// <summary>
    /// Registers a callback on a clock entry and returns without waiting.
    /// </summary>
    /// <param name="id">The clock entry to wait on.</param>
    /// <param name="func">The callback to run when the entry expires.</param>
    /// <param name="userData">The state handed to the callback.</param>
    /// <param name="destroyData">
    /// Called when <paramref name="userData"/> is no longer used, which the C
    /// function only arranges for once it has reached the clock: the refusals
    /// it answers before that never store it.
    /// </param>
    /// <returns>The result of the non blocking wait, as a <c>GstClockReturn</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_clock_id_wait_async")]
    internal static partial int IdWaitAsync(nint id, nint func, nint userData, nint destroyData);

    /// <summary>Takes a reference on the clock a clock entry was made from.</summary>
    /// <param name="id">The clock entry to read.</param>
    /// <returns>
    /// The clock, which the caller owns a reference of, or zero when the entry
    /// has outlived it: an entry holds nothing but a weak reference.
    /// </returns>
    [LibraryImport("Gst", EntryPoint = "gst_clock_id_get_clock")]
    internal static partial nint IdGetClock(nint id);
}
