using System.Text;
using Gst;
using Gst.GLib;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="Buffer.NewWrappedBytes(Bytes)"/> against a real
/// <c>GBytes</c>: the buffer aliases the block, holds a reference of its own,
/// and an empty block answers an empty buffer instead of a critical.
/// </summary>
/// <remarks>
/// <c>gst_buffer_new_wrapped_bytes</c> is on the skip list of
/// <c>girs/overlays/fixups.json</c> and hand written for the empty case: the
/// C reads the data pointer of the block and refuses a null one, which is what
/// every empty block carries. Everything else about the call is what the
/// generated member would have been, so what is measured here is the wrapping
/// itself as much as the guard.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BufferWrappedBytesTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("wrapped bytes");

    /// <summary>The buffer reads back the bytes of the block.</summary>
    [Fact]
    public void TheBufferReadsBackTheBytesOfTheBlock()
    {
        using Bytes bytes = Bytes.New(Payload);
        using Buffer buffer = Buffer.NewWrappedBytes(bytes);

        Assert.Equal((nuint)Payload.Length, buffer.GetSize());

        using Buffer.MapScope map = buffer.Map(MapFlags.Read);
        Assert.Equal(Payload, map.Span.ToArray());
    }

    /// <summary>
    /// The memory of the buffer is read only, which is what the immutability of
    /// a block means to a pipeline.
    /// </summary>
    /// <remarks>
    /// The flag is measured through a mapping rather than read back, because
    /// nothing of the shipped surface hands the flags of a memory out. A write
    /// mapping is what GST_MEMORY_FLAG_READONLY refuses - the flag is the lock
    /// state of the mini object, so the write lock the mapping takes fails -
    /// while a read mapping is granted. What it is not is
    /// gst_buffer_is_all_memory_writable: that one asks about the reference
    /// count and the exclusive lock of the block, not about the flag, and it
    /// answers true for this buffer.
    /// </remarks>
    [Fact]
    public void TheMemoryOfTheBufferIsReadOnly()
    {
        using Bytes bytes = Bytes.New(Payload);
        using Buffer buffer = Buffer.NewWrappedBytes(bytes);
        using Gst.Memory? memory = buffer.PeekMemory(0);

        Assert.NotNull(memory);
        Assert.False(memory.Map(out _, MapFlags.Write));

        Assert.True(memory.Map(out MapInfo read, MapFlags.Read));
        try
        {
            Assert.Equal((nuint)Payload.Length, read.Size);
        }
        finally
        {
            memory.Unmap(read);
        }
    }

    /// <summary>
    /// Disposing the wrapper of the block leaves the buffer readable: the
    /// buffer took a reference of its own.
    /// </summary>
    [Fact]
    public void TheBufferKeepsTheBlockAliveOnItsOwn()
    {
        Bytes bytes = Bytes.New(Payload);
        using Buffer buffer = Buffer.NewWrappedBytes(bytes);

        bytes.Dispose();
        Assert.True(bytes.IsDisposed);

        using Buffer.MapScope map = buffer.Map(MapFlags.Read);
        Assert.Equal(Payload, map.Span.ToArray());
    }

    /// <summary>
    /// An empty block answers an empty buffer, and the library is never asked
    /// to wrap the null data pointer it would refuse.
    /// </summary>
    [Fact]
    public void AnEmptyBlockAnswersAnEmptyBuffer()
    {
        using Bytes bytes = Bytes.New(ReadOnlySpan<byte>.Empty);

        // An empty block has no data pointer at all, which is the case the C
        // answers with a critical and no buffer.
        Assert.Equal((nuint)0, bytes.Size);

        using Buffer buffer = Buffer.NewWrappedBytes(bytes);

        Assert.Equal((nuint)0, buffer.GetSize());
    }

    /// <summary>A null block is an argument error, not an empty buffer.</summary>
    [Fact]
    public void ANullBlockIsRefused() =>
        Assert.Throws<ArgumentNullException>(static () => Buffer.NewWrappedBytes(null!));

    /// <summary>A disposed wrapper throws before anything is allocated.</summary>
    [Fact]
    public void ADisposedBlockIsRefused()
    {
        Bytes bytes = Bytes.New(Payload);
        bytes.Dispose();

        Assert.Throws<ObjectDisposedException>(() => Buffer.NewWrappedBytes(bytes));
    }
}
