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
/// notification over on the path that succeeds, so the binding has to release
/// the state of the callback itself on every other one. See the <c>skip</c>
/// list of <c>girs/overlays/fixups.json</c> for the ledger entry and
/// <see cref="Gst.Clock.IdWaitAsync"/> for what the binding does with it.
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
    /// function only arranges for on the path that returns
    /// <c>GST_CLOCK_OK</c>.
    /// </param>
    /// <returns>The result of the non blocking wait, as a <c>GstClockReturn</c>.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_clock_id_wait_async")]
    internal static partial int IdWaitAsync(nint id, nint func, nint userData, nint destroyData);
}
