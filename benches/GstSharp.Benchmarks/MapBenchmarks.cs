using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Gst;

namespace GstSharp.Benchmarks;

/// <summary>
/// What reading a buffer through a mapped <see cref="Span{T}"/> costs against
/// copying it out first.
/// </summary>
/// <remarks>
/// The buffer is 64 KiB and lives for the whole class, so what is measured is
/// the mapping and the pass over the bytes, not an allocation. The copy variant
/// reads the same bytes out of the same buffer into an array rented once from
/// <see cref="ArrayPool{T}"/>, which is the cheapest a copy can be: the
/// difference between the two rows is the copy itself.
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class MapBenchmarks
{
    /// <summary>How many bytes the mapped buffer holds.</summary>
    public const int Size = 64 * 1024;

    private const string ReadCategory = "read";

    private const string WriteCategory = "write";

    private Gst.Buffer buffer = null!;

    private byte[] scratch = null!;

    private byte fill = 0x5A;

    /// <summary>Allocates the buffer and rents the destination array.</summary>
    [GlobalSetup]
    public void Setup()
    {
        GstRuntime.EnsureInitialised();

        this.buffer = Gst.Buffer.NewAllocate(null, Size, null)
            ?? throw new InvalidOperationException("A 64 KiB buffer could not be allocated.");

        this.scratch = ArrayPool<byte>.Shared.Rent(Size);

        // The wrapper holds the only reference to the buffer, which is what
        // makes a writable mapping possible at all.
        using (Gst.Buffer.MapScope map = this.buffer.Map(MapFlags.Write))
        {
            map.Span.Fill(1);
        }
    }

    /// <summary>Gives the array back and releases the buffer.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        ArrayPool<byte>.Shared.Return(this.scratch);
        this.buffer.Dispose();
    }

    /// <summary>Sums the buffer through the mapped span, without copying.</summary>
    /// <returns>The sum of the bytes.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(ReadCategory)]
    public long ReadMappedSpan()
    {
        using Gst.Buffer.MapScope map = this.buffer.Map(MapFlags.Read);

        long total = 0;

        foreach (byte value in map.Span)
        {
            total += value;
        }

        return total;
    }

    /// <summary>Sums the buffer after copying it into a pooled array.</summary>
    /// <returns>The sum of the bytes.</returns>
    [Benchmark]
    [BenchmarkCategory(ReadCategory)]
    public long ReadCopiedToPooledArray()
    {
        int copied = (int)this.buffer.Extract(0, this.scratch.AsSpan(0, Size));

        long total = 0;

        foreach (byte value in this.scratch.AsSpan(0, copied))
        {
            total += value;
        }

        return total;
    }

    /// <summary>Fills the buffer through a writable mapping.</summary>
    /// <returns>How many bytes were written.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(WriteCategory)]
    public int WriteMappedSpan()
    {
        using Gst.Buffer.MapScope map = this.buffer.Map(MapFlags.Write);

        map.Span.Fill(this.fill);

        return map.Span.Length;
    }
}
