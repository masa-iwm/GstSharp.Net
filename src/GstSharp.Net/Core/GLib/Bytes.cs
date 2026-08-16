using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <c>GBytes</c>: an immutable block of bytes that GLib reference counts.
/// </summary>
/// <remarks>
/// <para>
/// GLib registers <c>GBytes</c> as a boxed type whose copy function is
/// <c>g_bytes_ref</c> and whose free function is <c>g_bytes_unref</c>, so this
/// is a <see cref="Gst.GObject.Boxed"/> like every other boxed wrapper of the
/// binding and follows the same rule: the wrapper owns a reference of its own
/// and has to be disposed. Taking a reference is what "copying" a
/// <c>GBytes</c> costs, because the bytes themselves are immutable and never
/// need to be duplicated.
/// </para>
/// <para>
/// What is bound here is what the WebRTC data channels need — build a block
/// from a span, read one back — rather than the whole of the C API. The
/// slicing, comparing and hashing functions of <c>GBytes</c> have managed
/// equivalents that work on the span.
/// </para>
/// <para>
/// The type is not in the <c>GType</c> registry, so
/// <see cref="Gst.GObject.Value.GetBoxed{T}"/> does not build one. Nothing
/// hands a <c>GBytes</c> out inside a <c>GValue</c> today: the one signal that
/// carries one, <c>on-message-data</c>, delivers it as a wrapper already.
/// </para>
/// </remarks>
public sealed unsafe partial class Bytes : Boxed
{
    /// <summary>
    /// Wraps a native <c>GBytes</c>.
    /// </summary>
    /// <param name="handle">The block to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> to take one of our own.
    /// </param>
    internal Bytes(nint handle, Transfer transfer)
        : base(handle, new GType(GetGType()), transfer)
    {
    }

    /// <summary>
    /// Gets the number of bytes in the block.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public nuint Size
    {
        get
        {
            nuint size = GBytesGetSize(Handle);
            GC.KeepAlive(this);
            return size;
        }
    }

    /// <summary>
    /// Creates a block that holds a copy of a span.
    /// </summary>
    /// <param name="data">The bytes to copy. An empty span is allowed.</param>
    /// <returns>The new block, which the caller has to dispose.</returns>
    /// <remarks>
    /// <c>g_bytes_new</c> copies what it is given, which is the shape that is
    /// always safe from managed code: the span may point into the managed heap,
    /// into a stack frame or into a buffer the caller reuses, and none of those
    /// survive being handed to native code that keeps them. The variants that
    /// take memory over — <c>g_bytes_new_take</c> and
    /// <c>g_bytes_new_static</c> — are not bound for that reason.
    /// </remarks>
    public static Bytes New(ReadOnlySpan<byte> data)
    {
        fixed (byte* first = data)
        {
            nint handle = GBytesNew((nint)first, (nuint)data.Length);

            return handle == nint.Zero
                ? throw new InvalidOperationException("g_bytes_new returned no value.")
                : new Bytes(handle, Transfer.Full);
        }
    }

    /// <summary>
    /// Wraps a native <c>GBytes</c>, mapping the null pointer onto
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="handle">The block to wrap, or <c>0</c>.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The wrapper, or <see langword="null"/> for a null handle.</returns>
    internal static Bytes? FromNative(nint handle, Transfer transfer) =>
        handle == nint.Zero ? null : new Bytes(handle, transfer);

    /// <summary>
    /// Reads the bytes of the block.
    /// </summary>
    /// <returns>
    /// The bytes, as a window into the block rather than a copy. The window is
    /// empty for a block of length zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>The span is only valid while this wrapper holds the block.</b> It
    /// points into memory that <c>g_bytes_unref</c> frees, so it must not
    /// outlive the wrapper and must not be stored. That is the same rule a
    /// mapped <see cref="Gst.Buffer"/> follows, and the <c>using</c> that the
    /// ownership rules of the binding ask for around a boxed wrapper is what
    /// keeps it: read what is needed inside the scope, or take a copy with
    /// <see cref="ToArray"/>.
    /// </para>
    /// <para>
    /// The bytes are immutable, which is why this is a
    /// <see cref="ReadOnlySpan{T}"/> and why no scope object is needed to give
    /// them back: unlike a buffer, a block is not mapped and unmapped.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    /// <exception cref="InvalidOperationException">
    /// The block is larger than a span can describe, which needs more than two
    /// gigabytes of data in one message.
    /// </exception>
    public ReadOnlySpan<byte> GetData()
    {
        nuint size;
        nint data = GBytesGetData(Handle, &size);
        GC.KeepAlive(this);

        if (data == nint.Zero || size == 0)
        {
            // g_bytes_get_data answers NULL for an empty block.
            return default;
        }

        if (size > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"The block holds {size} bytes, which is more than a span can describe.");
        }

        return new ReadOnlySpan<byte>((void*)data, (int)size);
    }

    /// <summary>
    /// Copies the bytes of the block into an array of the caller's own.
    /// </summary>
    /// <returns>The copy, which is empty for a block of length zero.</returns>
    /// <remarks>
    /// This is how a handler keeps what it was handed. The wrapper a signal
    /// hands out is released when the handler returns, and the span of
    /// <see cref="GetData"/> points into it, so anything that outlives the
    /// handler has to be a copy.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public byte[] ToArray() => GetData().ToArray();

    /// <summary>The <c>g_bytes_new</c> entry point.</summary>
    /// <remarks>
    /// The imports of this type live next to it rather than in
    /// <see cref="GLibNative"/>, which collects what the runtime itself needs —
    /// the loader, the main loop and the marshalling helpers. This is a public
    /// wrapper, and the hand written wrappers of the binding carry their own
    /// entry points, as <c>Gst.Gio.Socket</c> does.
    /// </remarks>
    [LibraryImport("GLib", EntryPoint = "g_bytes_new")]
    private static partial nint GBytesNew(nint data, nuint size);

    /// <summary>The <c>g_bytes_get_data</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_bytes_get_data")]
    private static partial nint GBytesGetData(nint bytes, nuint* size);

    /// <summary>The <c>g_bytes_get_size</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_bytes_get_size")]
    private static partial nuint GBytesGetSize(nint bytes);

    /// <summary>
    /// The <c>g_bytes_get_type</c> entry point, which lives in GObject rather
    /// than in GLib: the boxed registration of <c>GBytes</c> is part of the
    /// type system, not of the data structure.
    /// </summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("GObject", EntryPoint = "g_bytes_get_type")]
    private static partial nuint GetGType();
}
