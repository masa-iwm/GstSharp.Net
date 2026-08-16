using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The timestamps and offsets of a buffer can be written, and only while the
/// buffer belongs to the caller alone.
/// </summary>
/// <remarks>
/// The five fields are written straight into the <c>GstBuffer</c>, which is
/// what the <c>GST_BUFFER_PTS</c> family of macros does in C. What C leaves to
/// the discipline of the programmer — never write a buffer somebody else holds
/// — is a check here, because the failure is silent otherwise: the other holder
/// simply sees different timestamps than the ones it was given.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BufferFieldTests
{
    private const int PayloadSize = 16;

    private const byte Pattern = 0x5A;

    /// <summary>
    /// A buffer that nobody else holds takes every field and reads it back.
    /// </summary>
    [Fact]
    public void FieldsRoundTripOnAWritableBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Assert.True(buffer.IsWritable);

        buffer.SetPts(new ClockTime(1_000_000_000));
        buffer.SetDts(new ClockTime(500_000_000));
        buffer.SetDuration(new ClockTime(40_000_000));
        buffer.SetOffset(7);
        buffer.SetOffsetEnd(8);

        Assert.Equal(new ClockTime(1_000_000_000), buffer.Pts);
        Assert.Equal(new ClockTime(500_000_000), buffer.Dts);
        Assert.Equal(new ClockTime(40_000_000), buffer.Duration);
        Assert.Equal(7UL, buffer.Offset);
        Assert.Equal(8UL, buffer.OffsetEnd);

        // The unset time is a value like any other and has to survive the round
        // trip as itself, rather than as the largest possible duration.
        buffer.SetPts(ClockTime.None);
        Assert.True(buffer.Pts.IsNone);
    }

    /// <summary>
    /// A second reference makes the buffer shared, and a shared buffer is
    /// refused: writing a field of it would change what the other holder sees.
    /// </summary>
    [Fact]
    public void FieldsAreRefusedOnASharedBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        buffer.SetPts(new ClockTime(11));

        // Somebody else takes a reference, the way a queue or a pad probe does.
        TestNatives.MiniObjectRef(buffer.Handle);

        try
        {
            Assert.False(buffer.IsWritable);

            Assert.Throws<InvalidOperationException>(() => buffer.SetPts(new ClockTime(22)));
            Assert.Throws<InvalidOperationException>(() => buffer.SetDts(new ClockTime(22)));
            Assert.Throws<InvalidOperationException>(() => buffer.SetDuration(new ClockTime(22)));
            Assert.Throws<InvalidOperationException>(() => buffer.SetOffset(22));
            Assert.Throws<InvalidOperationException>(() => buffer.SetOffsetEnd(22));

            // Nothing was written: the refusal is complete, not partial.
            Assert.Equal(new ClockTime(11), buffer.Pts);
        }
        finally
        {
            TestNatives.MiniObjectUnref(buffer.Handle);
        }

        // And the buffer is writable again once the other reference is gone.
        Assert.True(buffer.IsWritable);
        buffer.SetPts(new ClockTime(33));
        Assert.Equal(new ClockTime(33), buffer.Pts);
    }

    /// <summary>
    /// A copy carries the fields, the metadata and the bytes of the original,
    /// and it carries them as its own: writing a field of the copy leaves the
    /// original alone.
    /// </summary>
    /// <remarks>
    /// The bytes are shared rather than duplicated, which is why they are
    /// written before the copy is made: a write mapping needs the only
    /// reference to the memory, and after the copy neither buffer has it. The
    /// fields are a different matter — they belong to the buffer, and the copy
    /// holds the only reference to itself.
    /// </remarks>
    [Fact]
    public void CopyCarriesTheFieldsAndTheBytes()
    {
        using Buffer original = Assert.IsType<Buffer>(Buffer.NewAllocate(null, PayloadSize, null));

        using (Buffer.MapScope map = original.Map(MapFlags.Write))
        {
            map.Span.Fill(Pattern);
        }

        original.SetPts(new ClockTime(1234));
        original.SetOffset(7);

        using Buffer copy = Assert.IsType<Buffer>(original.Copy());

        Assert.NotEqual(original.Handle, copy.Handle);
        Assert.Equal(original.Pts, copy.Pts);
        Assert.Equal(original.Offset, copy.Offset);

        using (Buffer.MapScope map = copy.Map(MapFlags.Read))
        {
            Assert.Equal((nuint)PayloadSize, map.Size);
            Assert.Equal(-1, map.Span.IndexOfAnyExcept(Pattern));
        }

        // The copy is a mini object of its own with a single reference, so its
        // fields are writable even though its bytes are not.
        Assert.True(copy.IsWritable);
        copy.SetPts(new ClockTime(5678));

        Assert.Equal(new ClockTime(5678), copy.Pts);
        Assert.Equal(new ClockTime(1234), original.Pts);
    }

    /// <summary>
    /// A disposed wrapper owns nothing, so it writes nothing.
    /// </summary>
    [Fact]
    public void FieldsAreRefusedOnADisposedWrapper()
    {
        Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => buffer.SetPts(new ClockTime(1)));
    }
}
