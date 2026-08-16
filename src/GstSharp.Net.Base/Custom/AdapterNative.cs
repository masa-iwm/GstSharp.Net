using System.Runtime.InteropServices;

namespace Gst.Base;

/// <summary>
/// Raw entry points of <c>libgstbase-1.0</c> that the hand written adapter glue
/// needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_adapter_map</c> hands out a borrowed pointer whose length is the
/// <c>size</c> argument, and the gir describes that argument as an output rather
/// than as the length of the returned array. No annotation override can turn
/// that into an array with a length, so the function is on the skip list of
/// <c>girs/overlays/fixups.json</c> and is bound here instead, as the
/// <see cref="Adapter.MapScope"/> that ties the pointer to an unmap call.
/// </para>
/// <para>
/// <c>gst_adapter_unmap</c> is on that skip list too, and for the opposite
/// reason: its signature is perfectly generatable, but a public <c>Unmap()</c>
/// next to the scope is a second, unscoped way to release a mapping that the
/// scope also releases. Skipping it makes the scope the only mapping surface an
/// adapter has, which is the policy <c>BufferNative</c> states.
/// </para>
/// <para>
/// Both signatures are blittable: the adapter is passed as its handle and the
/// size is a <c>gsize</c>.
/// </para>
/// </remarks>
internal static partial class AdapterNative
{
    /// <summary>
    /// Returns a pointer to the first <paramref name="size"/> bytes of an
    /// adapter, merging its buffers into one block if there is more than one.
    /// </summary>
    /// <param name="adapter">The adapter to map.</param>
    /// <param name="size">The number of bytes to map.</param>
    /// <returns>
    /// The mapped memory, which the adapter owns, or <c>0</c> when it holds
    /// fewer than <paramref name="size"/> bytes.
    /// </returns>
    [LibraryImport("GstBase", EntryPoint = "gst_adapter_map")]
    internal static partial nint Map(nint adapter, nuint size);

    /// <summary>
    /// Releases the memory that the last <see cref="Map"/> of an adapter handed
    /// out.
    /// </summary>
    /// <param name="adapter">The adapter that was mapped.</param>
    [LibraryImport("GstBase", EntryPoint = "gst_adapter_unmap")]
    internal static partial void Unmap(nint adapter);
}
