using System.Text;
using Gst.GLib;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <see cref="Bytes"/> wrapper of the runtime: an immutable block of bytes
/// that GLib reference counts, wrapped as a boxed value.
/// </summary>
/// <remarks>
/// <para>
/// <c>GBytes</c> is registered as a boxed type whose copy function is
/// <c>g_bytes_ref</c>, so a second wrapper of the same block takes a reference
/// rather than duplicating the data. That is what makes the borrowed case —
/// the block a data channel hands a signal handler — safe to wrap and to
/// dispose without touching what the channel holds, and it is what the second
/// test measures.
/// </para>
/// <para>
/// Nothing here needs a plugin. The data channel that uses the type needs
/// <c>webrtcbin</c>, and the tests of that live beside it.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BytesTests
{
    /// <summary>
    /// A block built from a span holds a copy of it, reads back as the same
    /// bytes, and reports its length.
    /// </summary>
    [Fact]
    public void ABlockHoldsACopyOfTheSpanItWasBuiltFrom()
    {
        byte[] source = Encoding.UTF8.GetBytes("the quick brown fox");

        using Bytes bytes = Bytes.New(source);

        Assert.Equal((nuint)source.Length, bytes.Size);
        Assert.True(bytes.GetData().SequenceEqual(source));
        Assert.Equal(source, bytes.ToArray());

        // g_bytes_new copies, so the array of the caller is its own from here
        // on: rewriting it does not rewrite the block.
        source[0] = (byte)'T';

        Assert.Equal("the quick brown fox", Encoding.UTF8.GetString(bytes.ToArray()));
    }

    /// <summary>
    /// A second wrapper of the same block takes a reference of its own, so
    /// disposing it leaves the first one holding a block that is still there.
    /// </summary>
    /// <remarks>
    /// This is the shape the <c>on-message-data</c> trampoline uses: the signal
    /// borrows the block from the channel, the wrapper takes a reference for
    /// the handler, and the reference goes back when the handler returns.
    /// </remarks>
    [Fact]
    public void ABorrowedBlockIsWrappedByTakingAReference()
    {
        using Bytes owned = Bytes.New([1, 2, 3, 4]);

        Bytes? borrowed = Bytes.FromNative(owned.Handle, Transfer.None);
        Assert.NotNull(borrowed);

        // The same block, not a copy of it.
        Assert.Equal(owned.Handle, borrowed.Handle);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, borrowed.ToArray());

        borrowed.Dispose();
        Assert.True(borrowed.IsDisposed);

        // The reference the second wrapper gave back was its own.
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, owned.ToArray());
    }

    /// <summary>
    /// An empty block is allowed, holds nothing and reads back as an empty
    /// span rather than as a null pointer.
    /// </summary>
    [Fact]
    public void AnEmptyBlockIsAllowed()
    {
        using Bytes empty = Bytes.New([]);

        Assert.Equal((nuint)0, empty.Size);
        Assert.True(empty.GetData().IsEmpty);
        Assert.Empty(empty.ToArray());
    }

    /// <summary>
    /// A disposed wrapper holds nothing, and every accessor says so rather
    /// than reading freed memory.
    /// </summary>
    [Fact]
    public void ADisposedBlockRefusesToBeRead()
    {
        Bytes bytes = Bytes.New([7]);
        bytes.Dispose();

        Assert.True(bytes.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => bytes.Size);
        Assert.Throws<ObjectDisposedException>(() => bytes.ToArray());

        // Disposing twice is allowed and releases nothing a second time.
        bytes.Dispose();
    }

    /// <summary>
    /// A block wrapped as a boxed value carries the type GLib registered it
    /// under, which is what makes the ownership rules of
    /// <see cref="Gst.GObject.Boxed"/> apply to it.
    /// </summary>
    [Fact]
    public void ABlockIsABoxedValueOfTheTypeGLibRegistered()
    {
        using Bytes bytes = Bytes.New([1]);

        Assert.True(bytes.BoxedType.IsValid);
        Assert.Equal("GBytes", bytes.BoxedType.Name);
        Assert.Equal(Gst.GObject.GType.Boxed, bytes.BoxedType.Fundamental);
    }
}
