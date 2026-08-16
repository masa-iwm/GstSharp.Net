using System.Runtime.CompilerServices;
using Gst;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A smoke test of the reference count discipline of the wrapper: neither the
/// disposal path nor the finalizer may release a buffer twice, and a double
/// release corrupts the heap of GStreamer rather than raising an exception.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class BufferLifetimeTests
{
    private const int Iterations = 1000;

    private const int PayloadSize = 16;

    private const byte Pattern = 0x5A;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public BufferLifetimeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Creates and disposes a thousand buffers, abandons another thousand to
    /// the finalizer and waits for them. Surviving this is the assertion.
    /// </summary>
    [Fact]
    public void DisposedAndFinalizedBuffersReleaseExactlyOnce()
    {
        for (int i = 0; i < Iterations; i++)
        {
            using Buffer buffer = AbiProbeTests.NewBuffer();
            Assert.False(buffer.IsDisposed);
        }

        for (int i = 0; i < Iterations; i++)
        {
            Abandon();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        _output.WriteLine(FormattableString.Invariant(
            $"{Iterations} disposed and {Iterations} finalized buffers survived."));

        // The buffers are gone; the library still works.
        using Buffer live = AbiProbeTests.NewBuffer();
        Assert.True(live.IsWritable);
    }

    /// <summary>
    /// Disposing a wrapper twice releases the reference once, which the second
    /// wrapper of the same buffer proves by keeping it alive.
    /// </summary>
    [Fact]
    public unsafe void DisposingAWrapperTwiceReleasesOneReference()
    {
        nint handle = TestNatives.BufferNew();
        Assert.NotEqual(nint.Zero, handle);

        try
        {
            Buffer buffer = Buffer.FromNative(handle, Transfer.None)!;
            Assert.Equal(2, ((MiniObjectRaw*)handle)->Refcount);

            buffer.Dispose();
            buffer.Dispose();

            Assert.Equal(1, ((MiniObjectRaw*)handle)->Refcount);
            Assert.True(buffer.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => _ = buffer.Handle);
        }
        finally
        {
            GstNative.MiniObjectUnref(handle);
        }
    }

    /// <summary>
    /// A copy and its original are two mini objects that happen to share their
    /// memory. Releasing either of them leaves the other one readable, in both
    /// orders: the memory belongs to whoever still holds a reference to it.
    /// </summary>
    [Fact]
    public void ACopyAndItsOriginalOutliveEachOther()
    {
        // The copy goes first.
        using (Buffer original = NewFilledBuffer())
        {
            Buffer copy = Assert.IsType<Buffer>(original.Copy());
            copy.Dispose();

            using Buffer.MapScope map = original.Map(MapFlags.Read);
            Assert.Equal(-1, map.Span.IndexOfAnyExcept(Pattern));
        }

        // And the original goes first.
        Buffer survivor;
        {
            Buffer original = NewFilledBuffer();
            survivor = Assert.IsType<Buffer>(original.Copy());
            original.Dispose();
        }

        using (survivor)
        {
            using Buffer.MapScope map = survivor.Map(MapFlags.Read);
            Assert.Equal(-1, map.Span.IndexOfAnyExcept(Pattern));
        }

        _output.WriteLine("A copy and its original released their shared memory exactly once.");
    }

    /// <summary>
    /// Allocates a buffer of <see cref="PayloadSize"/> bytes filled with
    /// <see cref="Pattern"/>.
    /// </summary>
    /// <returns>The buffer, which the caller has to dispose.</returns>
    private static Buffer NewFilledBuffer()
    {
        Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, PayloadSize, null));

        using (Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            map.Span.Fill(Pattern);
        }

        return buffer;
    }

    /// <summary>
    /// Wraps a buffer and drops the wrapper on the floor, out of line so that
    /// no reference to it stays alive in a register of the caller.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Abandon() => _ = AbiProbeTests.NewBuffer();
}
