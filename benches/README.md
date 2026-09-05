# Benchmarks

`benches/GstSharp.Benchmarks` is a [BenchmarkDotNet](https://benchmarkdotnet.org/)
harness for the four paths the binding makes claims about: managed vfunc
dispatch, the untyped property path, the mapped `Span<byte>`, and the interned
wrapper table.

It is a normal project on `GstSharp.Net.slnx`, so **every CI job builds it and
no CI job runs it**. A benchmark is a measurement of the machine it ran on;
a hosted runner cannot produce a number worth gating on, and a number nobody
gates on has no business costing minutes on every push. What CI is asked for
here is only that the harness keeps compiling against the binding.

## Running

A native GStreamer installation is needed, the same one the integration tests
and the samples use. The loader finds it the usual way; nothing has to be set.

```sh
# everything
dotnet run --project benches/GstSharp.Benchmarks -c Release -- --filter '*'

# one class
dotnet run --project benches/GstSharp.Benchmarks -c Release -- --filter '*Trampoline*'

# what is there
dotnet run --project benches/GstSharp.Benchmarks -c Release -- --list flat
```

Two things about the configuration are deliberate and are set in code rather
than left to the command line:

* **The default job is `Job.ShortRun`** — three warmup and three measured
  iterations. These benchmarks drive a native library, and a full run buys
  precision that the variance of the native side swamps anyway.
* **The default toolchain is `InProcessEmitToolchain`.** The out-of-process
  default writes a generated child project underneath this repository, where
  it inherits `Directory.Build.props` and its `TreatWarningsAsErrors`. In
  process, there is no generated project to inherit anything.

Passing `--job short` on the command line **replaces** the default job and
takes the in-process toolchain with it, which is why the commands above do not
use it. `--inProcess` and `--filter` are the flags worth reaching for.

Every class runs in the same process, so GStreamer is initialised once and the
managed identity filter registers its `GType` once. Both live in
`GstRuntime`, and every `[GlobalSetup]` asks that class for them instead of
doing the work itself: a second registration of the same type name is a hard
failure, not a slow benchmark.

## What each class measures

| Class | Baseline | Variants |
| --- | --- | --- |
| `TrampolineBenchmarks` | `videotestsrc num-buffers=300 ! identity ! fakesink sync=false`, run to the end of the stream | the same pipeline with a managed `BaseTransform` subclass in place of `identity` |
| `ValueBenchmarks` | the generated accessors `GstBaseSink.SetSync` / `GetSync`, and a bare `GValue` int round trip | `Object.SetProperty(string, object?)`, `Object.GetProperty<T>(string)`, a `GValue` string round trip |
| `MapBenchmarks` | summing a 64 KiB buffer through the span of `Buffer.Map` | summing it after `Buffer.Extract` copies it into an array rented from `ArrayPool<byte>` |
| `InternedLookupBenchmarks` | `Element.GetStaticPad("src")` | `Bin.GetByName`, `Pad.GetParentElement` |

Notes on reading them:

* The two trampoline pipelines are built once, in `[GlobalSetup]`, and differ
  only in the element in the middle; one operation is one run from `NULL` to
  the end of the stream and back. `sync=false` on the sink is what keeps the
  number about dispatch instead of about the clock, and 320x240 is what
  `videotestsrc` produces without being asked.
* The `ValueBenchmarks` baselines are the **generated accessors**, which are
  direct calls into the native setter with no `GValue` and no boxing. The
  ratio is therefore the price of looking a property up by name and going
  through a `GValue` — not the price of GObject properties as such.
* `InternedLookupBenchmarks` calls each accessor once in `[GlobalSetup]`, so
  what is measured is the hit in the interning table rather than the first
  fabrication of a wrapper. All three accessors are `transfer full` in C and
  all three answer an interned wrapper the caller does not dispose.

## One local run

x64, Windows, GStreamer 1.28.6 (MinGW), short job, in process. These are the
numbers of one machine on one day and are here to show the shape of the
output, not as a target anything is held to: three measured iterations is what
makes the error columns as wide as they are. Read the `Ratio` and `Allocated`
columns, not the absolute times.

```
| Method          | Mean     | Error      | StdDev    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------- |---------:|-----------:|----------:|------:|--------:|----------:|------------:|
| NativeIdentity  | 351.0 ms | 1,141.5 ms |  62.57 ms |  1.02 |    0.22 |   1.55 KB |        1.00 |
| ManagedIdentity | 512.8 ms | 3,382.4 ms | 185.40 ms |  1.49 |    0.52 |  13.95 KB |        9.02 |

| Method               | Categories | Mean        | Error        | StdDev       | Median      | Ratio | RatioSD | Gen0   | Allocated |
|--------------------- |----------- |------------:|-------------:|-------------:|------------:|------:|--------:|-------:|----------:|
| GetSyncTyped         | get        |    47.15 ns |    398.54 ns |    21.845 ns |    35.19 ns |  1.13 |    0.59 |      - |         - |
| GetSyncByName        | get        | 1,774.33 ns |  1,343.77 ns |    73.656 ns | 1,785.09 ns | 42.43 |   13.55 |      - |         - |
| GetNameByName        | get        | 1,378.20 ns |  4,500.67 ns |   246.697 ns | 1,275.08 ns | 32.96 |   11.74 | 0.0019 |      56 B |
|                      |            |             |              |              |             |       |         |        |           |
| ValueRoundtripInt    | gvalue     |    99.73 ns |     50.04 ns |     2.743 ns |   100.42 ns |  1.00 |    0.03 |      - |         - |
| ValueRoundtripString | gvalue     |   478.25 ns |  1,949.52 ns |   106.860 ns |   538.43 ns |  4.80 |    0.94 | 0.0024 |      40 B |
|                      |            |             |              |              |             |       |         |        |           |
| SetSyncTyped         | set        |    96.57 ns |    462.29 ns |    25.340 ns |   110.31 ns |  1.06 |    0.38 |      - |         - |
| SetSyncByName        | set        | 5,297.14 ns | 23,878.64 ns | 1,308.869 ns | 4,971.26 ns | 57.99 |   20.12 |      - |      24 B |
| SetNameByName        | set        | 3,315.63 ns | 35,736.51 ns | 1,958.839 ns | 2,272.69 ns | 36.30 |   21.48 |      - |         - |

| Method                  | Categories | Mean      | Error     | StdDev    | Ratio | RatioSD | Allocated |
|------------------------ |----------- |----------:|----------:|----------:|------:|--------:|----------:|
| ReadMappedSpan          | read       | 90.090 us | 48.183 us | 2.6411 us |  1.00 |    0.04 |       1 B |
| ReadCopiedToPooledArray | read       | 84.408 us | 93.966 us | 5.1506 us |  0.94 |    0.05 |       1 B |
|                         |            |           |           |           |       |         |           |
| WriteMappedSpan         | write      |  2.760 us |  2.890 us | 0.1584 us |  1.00 |    0.07 |         - |

| Method           | Mean       | Error        | StdDev      | Ratio | RatioSD | Allocated |
|----------------- |-----------:|-------------:|------------:|------:|--------:|----------:|
| GetStaticPad     |   509.0 ns |  1,798.25 ns |    98.57 ns |  1.03 |    0.25 |         - |
| GetByName        | 2,856.6 ns | 19,297.92 ns | 1,057.78 ns |  5.76 |    2.13 |         - |
| GetParentElement |   185.4 ns |     65.20 ns |     3.57 ns |  0.37 |    0.07 |         - |
```

What that run says, and what it does not:

* **Trampoline.** A managed `transform_ip` over 300 buffers cost about half as
  much again as the native `identity` — roughly half a microsecond per buffer
  on this host — and allocated about 46 bytes per buffer, consistent with one
  managed `Buffer` wrapper per call.
* **Properties.** Going through a name and a `GValue` is one to two orders of
  magnitude more expensive than the generated accessor. That is the whole
  argument for generating typed accessors, and the reason `SetProperty(string,
  object?)` is documented as the escape hatch rather than the normal way.
* **Interned lookup.** Every one of the three answers an existing wrapper with
  **zero managed allocation**, which is the claim that matters here; the times
  differ because the native lookups differ (`gst_bin_get_by_name` walks and
  takes the bin lock, `gst_pad_get_parent_element` is a field read).
* **Map.** On this run the copy row landed *below* the mapped row, which only
  means the memcpy of 64 KiB is smaller than the noise of a three-iteration
  run: both rows are dominated by the pass over the bytes, which both do. The
  honest reading of this pair on this hardware is the `Allocated` column —
  neither path allocates anything, the `1 B` being what the diagnoser rounds
  to on the in-process toolchain, because the pooled array is rented once —
  and not the ratio. A quiet machine is needed for the ratio to say
  anything.

## Artifacts

BenchmarkDotNet writes its reports under `BenchmarkDotNet.Artifacts/` in the
working directory, which `.gitignore` covers.
