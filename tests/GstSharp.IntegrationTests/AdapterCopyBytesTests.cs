using Gst.Base;
using Gst.GLib;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="Adapter.Copy(nuint, nuint)"/> against a real adapter: the copy
/// reaches across the buffers the adapter holds, and the two ranges the C
/// answers with a critical are answered here instead.
/// </summary>
/// <remarks>
/// <c>gst_adapter_copy_bytes</c> is on the skip list of
/// <c>girs/overlays/fixups.json</c> and hand written because it validates
/// nothing it can recover from: a size of zero raises a critical, and a range
/// the adapter does not hold raises one and hands out the uninitialised block
/// it allocated all the same.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AdapterCopyBytesTests
{
    private static readonly byte[] First = [0x10, 0x20, 0x30, 0x40];
    private static readonly byte[] Second = [0x50, 0x60, 0x70, 0x80];

    /// <summary>Initialises one test.</summary>
    public AdapterCopyBytesTests() =>

        // See Gst.Base.GstBase: the type registry only knows GstAdapter once
        // the module initialiser of GstSharp.Net.Base has run.
        GstBase.Initialize();

    /// <summary>
    /// A copy that spans the boundary of two pushed buffers reads the bytes of
    /// both.
    /// </summary>
    [Fact]
    public void ACopyReachesAcrossTheBuffersOfTheAdapter()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, First);
        Push(adapter, Second);

        Assert.Equal((nuint)8, adapter.Available());

        using Bytes bytes = adapter.Copy(2, 4);

        Assert.Equal((nuint)4, bytes.Size);
        Assert.Equal(new byte[] { 0x30, 0x40, 0x50, 0x60 }, bytes.ToArray());

        // The copy is the caller's own: flushing the adapter afterwards leaves
        // it untouched.
        adapter.Flush(8);
        Assert.Equal(new byte[] { 0x30, 0x40, 0x50, 0x60 }, bytes.ToArray());
    }

    /// <summary>A size of zero answers an empty block.</summary>
    [Fact]
    public void ASizeOfZeroAnswersAnEmptyBlock()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, First);

        using Bytes bytes = adapter.Copy(0, 0);

        Assert.Equal((nuint)0, bytes.Size);
        Assert.Empty(bytes.ToArray());
    }

    /// <summary>
    /// A range the adapter does not hold is refused rather than answered with
    /// uninitialised memory.
    /// </summary>
    [Fact]
    public void ARangeTheAdapterDoesNotHoldIsRefused()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, First);

        ArgumentOutOfRangeException size = Assert.Throws<ArgumentOutOfRangeException>(
            () => adapter.Copy(0, 5));
        Assert.Equal("size", size.ParamName);

        ArgumentOutOfRangeException offset = Assert.Throws<ArgumentOutOfRangeException>(
            () => adapter.Copy(2, 4));
        Assert.Equal("offset", offset.ParamName);
    }

    /// <summary>
    /// An offset near the top of the address space is refused rather than
    /// wrapping around into a range that looks valid.
    /// </summary>
    [Fact]
    public void AnOffsetThatWouldWrapAroundIsRefused()
    {
        using Adapter adapter = Adapter.New();
        Push(adapter, First);

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => adapter.Copy(nuint.MaxValue, 4));
        Assert.Equal("offset", thrown.ParamName);
    }

    /// <summary>
    /// Puts a buffer with the given bytes into the adapter.
    /// </summary>
    /// <param name="adapter">The adapter to push into.</param>
    /// <param name="payload">The bytes of the buffer.</param>
    /// <remarks>
    /// <c>gst_adapter_push</c> takes the reference of the buffer over, so the
    /// push is given one of its own and the wrapper gives its own back as
    /// usual; this is the shape <c>AdapterMapTests</c> uses.
    /// </remarks>
    private static void Push(Adapter adapter, ReadOnlySpan<byte> payload)
    {
        using Buffer buffer = Buffer.NewMemdup(payload);

        TestNatives.MiniObjectRef(buffer.Handle);
        TestNatives.AdapterPush(adapter.Handle, buffer.Handle);
        GC.KeepAlive(adapter);
    }
}
