using BenchmarkDotNet.Attributes;
using Gst;

namespace GstSharp.Benchmarks;

/// <summary>
/// What it costs to reach a native object that already has a wrapper.
/// </summary>
/// <remarks>
/// Every one of these accessors is <c>transfer full</c> in the C API and comes
/// back through <c>Object.FromNative</c>, which finds the existing wrapper in
/// the interning table and drops the reference the call was handed. That is the
/// hit path, so <c>[GlobalSetup]</c> calls each accessor once: what is measured
/// is a lookup in the table, never the first fabrication of a wrapper. The
/// results are interned wrappers, which the caller never disposes
/// (<c>docs/ownership.md</c>).
/// </remarks>
[MemoryDiagnoser]
public class InternedLookupBenchmarks
{
    private Pipeline pipeline = null!;

    private Element source = null!;

    private Element sink = null!;

    private Pad sourcePad = null!;

    /// <summary>Builds a pipeline and warms every lookup the benchmarks make.</summary>
    [GlobalSetup]
    public void Setup()
    {
        GstRuntime.EnsureInitialised();

        this.pipeline = Pipeline.New("interned-lookup");
        this.source = GstRuntime.NewElement("videotestsrc", "source");
        this.sink = GstRuntime.NewElement("fakesink", "sink");

        if (!this.pipeline.AddMany(this.source, this.sink) || !this.source.Link(this.sink))
        {
            throw new InvalidOperationException("The lookup pipeline could not be built.");
        }

        this.sourcePad = this.source.GetStaticPad("src")
            ?? throw new InvalidOperationException("videotestsrc has no static src pad.");

        _ = this.pipeline.GetByName("source");
        _ = this.sourcePad.GetParentElement();
    }

    /// <summary>Releases the pipeline and the two elements it holds.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.pipeline.Dispose();

        // The pipeline took a reference of its own on each element next to the
        // one these wrappers hold, so both are disposed here. The pad and the
        // results of the benchmarks below are interned and are not.
        this.source.Dispose();
        this.sink.Dispose();
    }

    /// <summary>Looks a static pad of an element up by name.</summary>
    /// <returns>The pad, which is interned and not disposed.</returns>
    [Benchmark(Baseline = true)]
    public Pad? GetStaticPad() => this.source.GetStaticPad("src");

    /// <summary>Looks an element of a bin up by name.</summary>
    /// <returns>The element, which is interned and not disposed.</returns>
    [Benchmark]
    public Element? GetByName() => this.pipeline.GetByName("source");

    /// <summary>Walks from a pad to the element it belongs to.</summary>
    /// <returns>The element, which is interned and not disposed.</returns>
    [Benchmark]
    public Element? GetParentElement() => this.sourcePad.GetParentElement();
}
