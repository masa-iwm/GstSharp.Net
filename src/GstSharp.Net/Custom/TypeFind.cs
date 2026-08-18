using System.Runtime.InteropServices;

namespace Gst;

/// <content>
/// The reader of a typefind function: the bytes of the stream it has been
/// asked to identify.
/// </content>
/// <remarks>
/// <para>
/// <c>gst_type_find_peek</c> is on the skip list of
/// <c>girs/overlays/fixups.json</c> and is bound here instead. Its gir marks
/// the return value as an array of bytes with no length, because the length is
/// not part of the value at all: the pointer that comes back is exactly the
/// <c>size</c> argument long. An annotation override cannot add a
/// length-from-argument contract, so following the gir would hand out a pointer
/// of unknown extent. The entry point therefore stays skipped and
/// <see cref="TryPeek(long, uint, out ReadOnlySpan{byte})"/> is the only way to
/// read from a <see cref="TypeFind"/>.
/// </para>
/// <para>
/// This is the same shape that makes <c>gst_adapter_map</c> a hand written
/// binding — see <c>Gst.Base.Adapter.Map</c> and the convention described in
/// <c>Custom/BufferNative.cs</c> — with one difference: what a peek hands out
/// is released by nobody, so there is no scope to dispose and no
/// <c>ref struct</c> to hold. The stream reader that lent the memory takes it
/// back on its own schedule, which is what makes the lifetime of the span a
/// documented contract rather than something the type system enforces.
/// </para>
/// </remarks>
public sealed unsafe partial class TypeFind
{
    /// <summary>
    /// Reads <paramref name="size"/> bytes of the stream at
    /// <paramref name="offset"/> without consuming them.
    /// </summary>
    /// <param name="offset">
    /// Where to read from, counted from the start of the stream. A negative
    /// offset counts back from the end of the stream, which only the readers
    /// that know the length support; the others answer
    /// <see langword="false"/>.
    /// </param>
    /// <param name="size">The number of bytes to read.</param>
    /// <param name="data">
    /// The bytes that were read, borrowed from the stream reader, or an empty
    /// span when this returns <see langword="false"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the whole range was available and
    /// <paramref name="data"/> holds it; <see langword="false"/> when it was
    /// not.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_type_find_peek</c>. Asking for more than there is is the
    /// normal way a typefind function finds the end of the data — it reads what
    /// it needs and stops when a read fails — so a refused range is a
    /// <see langword="false"/> and not an exception. So is a
    /// <paramref name="size"/> of zero, which the readers refuse outright.
    /// </para>
    /// <para>
    /// <b>The span is borrowed, and only for the length of the call that handed
    /// out this <see cref="TypeFind"/>.</b> The memory belongs to whoever is
    /// driving the typefinding — the <c>typefind</c> element, or one of the
    /// <c>gst_type_find_helper_*</c> functions — and it is released as soon as
    /// the typefind function returns. It may also be released, moved or
    /// overwritten by the next call to
    /// <see cref="TryPeek(long, uint, out ReadOnlySpan{byte})"/>, because a
    /// reader is free to re-map its buffers to serve the new range. Read what
    /// is needed out of the span before peeking again, and copy anything that
    /// has to outlive the callback; storing the span, or the
    /// <see cref="TypeFind"/> it came from, in a field or a closure hands the
    /// next reader of it memory that is gone.
    /// </para>
    /// <para>
    /// A <see cref="TypeFind"/> belongs to the thread that is running the
    /// typefind function. Nothing here is synchronised, and neither the span
    /// nor the wrapper may be handed to another thread.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="size"/> is larger than <see cref="int.MaxValue"/> and
    /// does not fit into a single span.
    /// </exception>
    public bool TryPeek(long offset, uint size, out ReadOnlySpan<byte> data)
    {
        // A guint is wider than the length of a span, and a range that large
        // could never be handed back: this is caught before the call rather
        // than as an overflow of the cast below.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(size, (uint)int.MaxValue);

        byte* peeked = GstTypeFindPeek(Handle, offset, size);
        GC.KeepAlive(this);

        if (peeked is null)
        {
            data = default;
            return false;
        }

        // The length is the size that was asked for. That is the whole reason
        // this binding is written by hand; see the remarks on the type.
        data = new ReadOnlySpan<byte>(peeked, (int)size);
        return true;
    }

    /// <summary>The <c>gst_type_find_peek</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_type_find_peek")]
    private static partial byte* GstTypeFindPeek(nint find, long offset, uint size);
}
