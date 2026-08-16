using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written bus glue
/// needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_bus_set_sync_handler</c> is imported by hand because the whole sync
/// handler surface is written by hand; see the <c>skip</c> list of
/// <c>girs/overlays/fixups.json</c> for why, and
/// <see cref="Gst.Bus.SetSyncHandler"/> for what the binding does with it. The
/// signature is the one of the C function, with the handler and the destroy
/// notification as plain addresses: both are <c>[UnmanagedCallersOnly]</c>
/// methods, which have no delegate type to marshal.
/// </para>
/// </remarks>
internal static partial class BusNative
{
    /// <summary>
    /// Installs, replaces or removes the synchronous handler of a bus.
    /// </summary>
    /// <param name="bus">The bus to install the handler on.</param>
    /// <param name="func">The handler, or <c>0</c> to remove the one installed.</param>
    /// <param name="userData">The state handed to the handler.</param>
    /// <param name="notify">Called when <paramref name="userData"/> is no longer used.</param>
    [LibraryImport("Gst", EntryPoint = "gst_bus_set_sync_handler")]
    internal static partial void SetSyncHandler(nint bus, nint func, nint userData, nint notify);
}
