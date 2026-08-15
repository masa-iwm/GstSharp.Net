using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Exercises the mapping glue of <see cref="Buffer"/> against real memory.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class BufferMapTests
{
    private const int PayloadSize = 64;
    private const byte Pattern = 0xAB;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public BufferMapTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// What is written through a writable mapping is what a later readable
    /// mapping reads back, which is only true when the data pointer and the
    /// size of the <c>GstMapInfo</c> are read from the right offsets.
    /// </summary>
    [Fact]
    public void MapRoundTripsThePayload()
    {
        using Buffer buffer = NewAllocatedBuffer(PayloadSize);

        using (Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            _output.WriteLine(FormattableString.Invariant(
                $"write mapping: size={map.Size}, span={map.Span.Length}"));

            Assert.Equal((nuint)PayloadSize, map.Size);
            Assert.Equal(PayloadSize, map.Span.Length);
            map.Span.Fill(Pattern);
        }

        using (Buffer.MapScope map = buffer.Map(MapFlags.Read))
        {
            Assert.Equal((nuint)PayloadSize, map.Size);
            Assert.Equal(-1, map.Span.IndexOfAnyExcept(Pattern));
        }
    }

    /// <summary>
    /// The size of the mapping is the size of the memory of the buffer, as the
    /// library itself reports it.
    /// </summary>
    [Fact]
    public void MapSizeMatchesTheBufferSize()
    {
        using Buffer buffer = NewAllocatedBuffer(PayloadSize);

        nuint reported = TestNatives.BufferGetSize(buffer.Handle);
        using Buffer.MapScope map = buffer.Map(MapFlags.Read);

        _output.WriteLine(FormattableString.Invariant(
            $"gst_buffer_get_size={reported}, mapped={map.Size}"));

        Assert.Equal(reported, map.Size);
        Assert.Equal((nuint)PayloadSize, map.Size);
    }

    /// <summary>
    /// Releasing a mapping twice unmaps once. The second release is a no-op,
    /// and the accessors refuse to hand out memory that was given back.
    /// </summary>
    [Fact]
    public void DisposingTheMappingTwiceUnmapsOnce()
    {
        using Buffer buffer = NewAllocatedBuffer(PayloadSize);

        Buffer.MapScope map = buffer.Map(MapFlags.Read);
        map.Dispose();
        map.Dispose();

        // A ref struct cannot be captured by a lambda, so Assert.Throws is out.
        ObjectDisposedException? refused = null;
        try
        {
            _ = map.Size;
        }
        catch (ObjectDisposedException error)
        {
            refused = error;
        }

        Assert.NotNull(refused);

        // The buffer is still usable: the memory was released exactly once.
        using Buffer.MapScope second = buffer.Map(MapFlags.Read);
        Assert.Equal((nuint)PayloadSize, second.Size);
    }

    /// <summary>
    /// A writable mapping needs the only reference to the buffer, so it fails
    /// while somebody else holds one. GStreamer logs a critical warning of its
    /// own when it refuses, so the run prints
    /// <c>write map requested on non-writable buffer</c>; that line is part of
    /// this test passing, not a failure.
    /// </summary>
    [Fact]
    public void WritableMapOfASharedBufferFails()
    {
        using Buffer buffer = NewAllocatedBuffer(PayloadSize);

        // The second reference is what makes the buffer unwritable.
        GstNative.MiniObjectRef(buffer.Handle);

        try
        {
            Assert.False(buffer.IsWritable);

            InvalidOperationException? failure = null;
            try
            {
                using Buffer.MapScope map = buffer.Map(MapFlags.Write);
                Assert.Fail("A shared buffer must not be mappable for writing.");
            }
            catch (InvalidOperationException error)
            {
                failure = error;
            }

            Assert.NotNull(failure);
            _output.WriteLine(failure.Message);
        }
        finally
        {
            GstNative.MiniObjectUnref(buffer.Handle);
        }
    }

    private static Buffer NewAllocatedBuffer(int size)
    {
        nint handle = TestNatives.BufferNewAllocate(nint.Zero, (nuint)size, nint.Zero);
        Assert.NotEqual(nint.Zero, handle);

        Buffer? buffer = Buffer.FromNative(handle, Transfer.Full);
        Assert.NotNull(buffer);
        return buffer;
    }
}
