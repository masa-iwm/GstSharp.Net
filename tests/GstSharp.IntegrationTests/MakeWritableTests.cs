using Gst;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// First class <c>MakeWritable</c> on buffers and caps, and the <c>All</c>
/// composite of <see cref="BufferCopyFlags"/> that a metadata copy is built
/// from.
/// </summary>
/// <remarks>
/// <c>gst_mini_object_make_writable</c> consumes the reference it is given, so
/// the interesting question is not whether the object comes back writable — it
/// always does — but what happens to the wrapper. These tests pin that down:
/// one wrapper goes in, the same wrapper comes out, and the native object
/// behind it may have been replaced.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MakeWritableTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MakeWritableTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A buffer nobody else holds is writable already, so the call is a no-op
    /// and the wrapper keeps standing for the same native buffer.
    /// </summary>
    [Fact]
    public void MakeWritableKeepsTheHandleOfAnExclusiveBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        buffer.SetPts(new ClockTime(11));

        nint before = buffer.Handle;
        Buffer returned = buffer.MakeWritable();

        Assert.Same(buffer, returned);
        Assert.Equal(before, buffer.Handle);
        Assert.True(buffer.IsWritable);
        Assert.Equal(new ClockTime(11), buffer.Pts);
    }

    /// <summary>
    /// A second reference makes the buffer shared, so the call copies it. The
    /// wrapper is the same object afterwards and now stands for the copy, which
    /// carries the state of the original and is writable.
    /// </summary>
    [Fact]
    public void MakeWritableSwapsTheHandleOfASharedBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        buffer.SetPts(new ClockTime(11));

        // Somebody else takes a reference, the way a queue or a pad probe does.
        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        bool swapped = false;
        try
        {
            Assert.False(buffer.IsWritable);

            Buffer returned = buffer.MakeWritable();

            Assert.Same(buffer, returned);
            Assert.NotEqual(shared, buffer.Handle);
            swapped = true;

            _output.WriteLine(FormattableString.Invariant(
                $"make_writable: {shared:X} -> {buffer.Handle:X}"));

            // The wrapper owns the copy alone, so the setters that refused
            // before work again, and the copy inherited the timestamp.
            Assert.True(buffer.IsWritable);
            Assert.Equal(new ClockTime(11), buffer.Pts);
            buffer.SetPts(new ClockTime(22));
            Assert.Equal(new ClockTime(22), buffer.Pts);
        }
        finally
        {
            // The call consumed the reference of the wrapper, so what is left
            // on the original buffer is the reference this test took, and only
            // when the copy really happened.
            if (swapped)
            {
                TestNatives.MiniObjectUnref(shared);
            }
        }
    }

    /// <summary>
    /// The wrapper of a disposed buffer owns nothing to make writable.
    /// </summary>
    [Fact]
    public void MakeWritableOnADisposedBufferThrows()
    {
        Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.MakeWritable());
    }

    /// <summary>
    /// Caps behave exactly like buffers: exclusive caps keep their handle,
    /// shared caps are copied under the same wrapper, and the copy can be
    /// rewritten.
    /// </summary>
    [Fact]
    public void MakeWritableOnCapsMatchesTheBufferRule()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,width=(int)320,height=(int)240"));

        nint exclusive = caps.Handle;
        Assert.Same(caps, caps.MakeWritable());
        Assert.Equal(exclusive, caps.Handle);

        nint shared = caps.Handle;
        TestNatives.MiniObjectRef(shared);

        bool swapped = false;
        try
        {
            Assert.False(caps.IsWritable);

            Caps returned = caps.MakeWritable();

            Assert.Same(caps, returned);
            Assert.NotEqual(shared, caps.Handle);
            swapped = true;

            Assert.True(caps.IsWritable);

            // Rewriting caps is what this exists for, and the structure of the
            // copy is what carries the fields. The getter hands out a copy of
            // the boxed value, so this only shows that the copy kept the
            // content of the original.
            using Structure structure = caps.GetStructure(0);
            Assert.Equal("video/x-raw", structure.GetName());
        }
        finally
        {
            if (swapped)
            {
                TestNatives.MiniObjectUnref(shared);
            }
        }
    }

    /// <summary>
    /// The composites are the ones the C header defines, values included.
    /// </summary>
    [Fact]
    public void BufferCopyCompositesMatchTheHeader()
    {
        Assert.Equal(7, (int)BufferCopy.Metadata);
        Assert.Equal(15, (int)BufferCopy.All);

        // GST_BUFFER_COPY_MERGE is deliberately absent from GST_BUFFER_COPY_ALL.
        Assert.False(BufferCopy.All.HasFlag(BufferCopyFlags.Merge));
        Assert.False(BufferCopy.All.HasFlag(BufferCopyFlags.Deep));
    }

    /// <summary>
    /// A region copy with the <c>All</c> composite carries the metadata over
    /// and references the memory of the original instead of duplicating it.
    /// </summary>
    [Fact]
    public unsafe void CopyRegionWithAllCopiesMetadataAndSharesMemory()
    {
        using Buffer source = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        source.SetPts(new ClockTime(1_000_000_000));
        source.SetDts(new ClockTime(500_000_000));
        source.SetDuration(new ClockTime(40_000_000));
        source.SetOffset(7);
        source.SetOffsetEnd(8);

        using (Buffer.MapScope write = source.Map(MapFlags.Write))
        {
            write.Span.Fill(0xAB);
        }

        nuint size = source.GetSize();
        using Buffer copy = Assert.IsType<Buffer>(source.CopyRegion(BufferCopy.All, 0, size));

        // A full region copy carries all five timestamp and offset fields.
        Assert.Equal(source.Pts, copy.Pts);
        Assert.Equal(source.Dts, copy.Dts);
        Assert.Equal(source.Duration, copy.Duration);
        Assert.Equal(source.Offset, copy.Offset);
        Assert.Equal(source.OffsetEnd, copy.OffsetEnd);
        Assert.Equal(size, copy.GetSize());

        // GST_BUFFER_COPY_MEMORY refs the memory rather than duplicating it, so
        // both buffers map to the very same bytes. Neither of them is writable
        // while the other one lives, which is why both mappings are read only.
        using Buffer.MapScope original = source.Map(MapFlags.Read);
        using Buffer.MapScope shared = copy.Map(MapFlags.Read);

        fixed (byte* originalData = original.Span)
        fixed (byte* sharedData = shared.Span)
        {
            _output.WriteLine(FormattableString.Invariant(
                $"copy_region(ALL): {(nint)originalData:X} == {(nint)sharedData:X}"));
            Assert.Equal((nint)originalData, (nint)sharedData);
        }

        Assert.Equal(0xAB, shared.Span[0]);
    }
}
