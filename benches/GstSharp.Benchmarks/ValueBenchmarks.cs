using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.Benchmarks;

/// <summary>
/// What the untyped property path costs against the generated accessor, and
/// what a <see cref="Value"/> round trip costs on its own.
/// </summary>
/// <remarks>
/// <para>
/// The baseline of each group is the generated accessor
/// (<c>GstBaseSink.SetSync</c> / <c>GetSync</c>), which is a direct call into
/// the native setter with no <c>GValue</c> and no boxing anywhere. The variant
/// is <c>Object.SetProperty(string, object?)</c> and
/// <c>Object.GetProperty&lt;T&gt;(string)</c>, which look the property up by
/// name and go through a <c>GValue</c>: the ratio is the price of the untyped
/// path, not of GObject properties as such.
/// </para>
/// <para>
/// The third group is the <c>GValue</c> alone, without a property: create,
/// write, read back, free.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[CategoriesColumn]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ValueBenchmarks
{
    private const string SetCategory = "set";

    private const string GetCategory = "get";

    private const string ValueCategory = "gvalue";

    private BaseSink sink = null!;

    private bool flag = true;

    private int number = 42;

    private string text = "gstsharp";

    /// <summary>Creates the element the property benchmarks talk to.</summary>
    [GlobalSetup]
    public void Setup()
    {
        GstRuntime.EnsureInitialised();

        Element element = GstRuntime.NewElement("fakesink", "value-bench-sink");

        this.sink = element as BaseSink
            ?? throw new InvalidOperationException(
                $"fakesink was wrapped as {element.GetType()} rather than a BaseSink, "
                + "so the typed baseline of this benchmark does not exist.");

        // A silent wrong answer would be a fast benchmark of nothing, so both
        // round trips are checked once before anything is measured.
        if (this.RoundtripInt() != this.number || this.RoundtripString() != this.text)
        {
            throw new InvalidOperationException("A GValue round trip did not answer what it was given.");
        }
    }

    /// <summary>Releases the element.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.sink.Dispose();

    /// <summary>Sets <c>sync</c> through the generated setter.</summary>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(SetCategory)]
    public void SetSyncTyped() => this.sink.SetSync(this.flag);

    /// <summary>Sets <c>sync</c> through the untyped property setter.</summary>
    [Benchmark]
    [BenchmarkCategory(SetCategory)]
    public void SetSyncByName() => this.sink.SetProperty("sync", this.flag);

    /// <summary>Sets <c>name</c> through the untyped property setter.</summary>
    [Benchmark]
    [BenchmarkCategory(SetCategory)]
    public void SetNameByName() => this.sink.SetProperty("name", this.text);

    /// <summary>Reads <c>sync</c> through the generated getter.</summary>
    /// <returns>What the element answered.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(GetCategory)]
    public bool GetSyncTyped() => this.sink.GetSync();

    /// <summary>Reads <c>sync</c> through the typed property getter.</summary>
    /// <returns>What the element answered.</returns>
    [Benchmark]
    [BenchmarkCategory(GetCategory)]
    public bool GetSyncByName() => this.sink.GetProperty<bool>("sync");

    /// <summary>Reads <c>name</c> through the typed property getter.</summary>
    /// <returns>What the element answered.</returns>
    [Benchmark]
    [BenchmarkCategory(GetCategory)]
    public string GetNameByName() => this.sink.GetProperty<string>("name");

    /// <summary>Writes an int into a fresh <c>GValue</c> and reads it back.</summary>
    /// <returns>What the value answered.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory(ValueCategory)]
    public int ValueRoundtripInt() => this.RoundtripInt();

    /// <summary>Writes a string into a fresh <c>GValue</c> and reads it back.</summary>
    /// <returns>What the value answered.</returns>
    [Benchmark]
    [BenchmarkCategory(ValueCategory)]
    public string? ValueRoundtripString() => this.RoundtripString();

    private int RoundtripInt()
    {
        // A plain local rather than a using declaration: Value carries the
        // GValue inline, and a read-only local would let the compiler mutate a
        // defensive copy instead of this one.
        Value value = Value.New(GType.Int);

        try
        {
            value.SetInt(this.number);
            return value.GetInt();
        }
        finally
        {
            value.Dispose();
        }
    }

    private string? RoundtripString()
    {
        Value value = Value.New(GType.String);

        try
        {
            value.SetString(this.text);
            return value.GetString();
        }
        finally
        {
            value.Dispose();
        }
    }
}
