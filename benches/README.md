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
use it. When a job has to be named on the command line, `--inProcess` has to
come with it: `--job short --inProcess` is what reproduces the default. On its
own `--inProcess` only asks for what is already in force, so `--filter` is the
flag actually worth reaching for.

Every class runs in the same process, so GStreamer is initialised once and the
managed identity filter registers its `GType` once. Both live in
`GstRuntime`, and every `[GlobalSetup]` asks that class for them instead of
doing the work itself: a second registration of the same type name is a hard
failure, not a slow benchmark.

## What each class measures

| Class | Baseline | Variants |
| --- | --- | --- |
| `TrampolineBenchmarks` | `fakesrc num-buffers=20000 sizetype=fixed sizemax=64 ! identity signal-handoffs=false ! fakesink sync=false`, run to the end of the stream | the same pipeline with a managed `BaseTransform` subclass in place of `identity` |
| `ValueBenchmarks` | the generated accessors `GstBaseSink.SetSync` / `GetSync`, and a bare `GValue` int round trip | `Object.SetProperty(string, object?)`, `Object.GetProperty<T>(string)`, a `GValue` string round trip |
| `MapBenchmarks` | summing a 64 KiB buffer as 64 bit lanes through the span of `Buffer.Map` | summing it after `Buffer.Extract` copies it into an array rented from `ArrayPool<byte>`; mapping and unmapping it without reading it |
| `InternedLookupBenchmarks` | `Element.GetStaticPad("src")` | `Bin.GetByName`, `Pad.GetParentElement` |

Notes on reading them:

* The two trampoline pipelines are built once, in `[GlobalSetup]`, and differ
  only in the element in the middle; one operation is one run from `NULL` to
  the end of the stream and back. Everything around the middle is chosen so
  that the middle is what is left over. `fakesrc` hands out 64 byte buffers
  without producing any content — under `videotestsrc` the per buffer
  dispatch is a small part of the roughly one millisecond each 320x240 frame
  takes to paint, and cannot be seen at all. `sync=false` on the sink keeps the number about dispatch
  instead of about the clock. `signal-handoffs=false` stops the native
  `identity`, where that property is on by default, from emitting a signal
  per buffer that the managed filter has no counterpart for. 20,000 buffers
  is enough that the state cycle both rows share is a small part of one
  operation.
* Both `MapBenchmarks` read rows sum eight bytes at a time. A scalar `byte`
  loop over 64 KiB costs several times the map and the copy put together and
  hides the one thing the pair exists to compare; `MapOnly` maps and unmaps
  without reading anything, so the map has a number of its own.
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
| Method          | Mean      | Error     | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|---------:|------:|--------:|----------:|------------:|
| NativeIdentity  |  90.62 ms | 727.38 ms | 39.87 ms |  1.20 |    0.77 |     697 B |        1.00 |
| ManagedIdentity | 140.57 ms | 303.80 ms | 16.65 ms |  1.85 |    0.93 |  640963 B |      919.60 |

| Method               | Categories | Mean        | Error        | StdDev       | Median      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |----------- |------------:|-------------:|-------------:|------------:|------:|--------:|-------:|----------:|------------:|
| GetSyncTyped         | get        |    60.11 ns |    419.67 ns |    23.004 ns |    64.55 ns |  1.13 |    0.59 |      - |         - |          NA |
| GetSyncByName        | get        | 1,081.64 ns |  9,402.50 ns |   515.382 ns |   812.46 ns | 20.30 |   11.95 |      - |         - |          NA |
| GetNameByName        | get        | 2,804.09 ns |  5,812.05 ns |   318.578 ns | 2,987.88 ns | 52.63 |   21.33 |      - |      56 B |          NA |
|                      |            |             |              |              |             |       |         |        |           |             |
| ValueRoundtripInt    | gvalue     |    38.15 ns |     19.06 ns |     1.045 ns |    37.63 ns |  1.00 |    0.03 |      - |         - |          NA |
| ValueRoundtripString | gvalue     |   344.25 ns |  1,907.94 ns |   104.581 ns |   316.34 ns |  9.03 |    2.39 | 0.0024 |      40 B |          NA |
|                      |            |             |              |              |             |       |         |        |           |             |
| SetSyncTyped         | set        |    62.16 ns |    211.15 ns |    11.574 ns |    55.63 ns |  1.02 |    0.22 |      - |         - |          NA |
| SetSyncByName        | set        | 3,130.44 ns | 14,638.42 ns |   802.381 ns | 2,878.89 ns | 51.43 |   13.74 |      - |      24 B |          NA |
| SetNameByName        | set        | 3,034.55 ns | 19,999.44 ns | 1,096.237 ns | 2,594.70 ns | 49.86 |   17.34 |      - |         - |          NA |

| Method                  | Categories | Mean       | Error        | StdDev      | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |----------- |-----------:|-------------:|------------:|-----------:|------:|--------:|----------:|------------:|
| ReadMappedSpan          | read       | 7,220.6 ns | 53,570.54 ns | 2,936.38 ns | 5,563.3 ns |  1.10 |    0.51 |         - |          NA |
| ReadCopiedToPooledArray | read       | 7,152.6 ns |  1,997.74 ns |   109.50 ns | 7,157.3 ns |  1.09 |    0.31 |         - |          NA |
| MapOnly                 | read       |   125.2 ns |     84.63 ns |     4.64 ns |   126.8 ns |  0.02 |    0.01 |         - |          NA |
|                         |            |            |              |             |            |       |         |           |             |
| WriteMappedSpan         | write      | 2,506.8 ns |  1,309.02 ns |    71.75 ns | 2,478.7 ns |  1.00 |    0.03 |         - |          NA |

| Method           | Mean       | Error       | StdDev   | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |-----------:|------------:|---------:|------:|--------:|----------:|------------:|
| GetStaticPad     |   519.7 ns |  4,011.1 ns | 219.9 ns |  1.17 |    0.70 |         - |          NA |
| GetByName        | 2,190.0 ns | 11,892.8 ns | 651.9 ns |  4.92 |    2.60 |         - |          NA |
| GetParentElement |   331.8 ns |  2,243.5 ns | 123.0 ns |  0.75 |    0.42 |         - |          NA |
```

What that run says, and what it does not:

* **Trampoline.** The managed filter allocated **32.0 bytes per buffer**:
  (640,963 − 697) B over 20,000 buffers. That is consistent with the one
  managed `Buffer` wrapper the generated trampoline creates per call through
  `Gst.Buffer.Borrow` — `Buffer` declares no instance fields of its own, and a
  `MiniObject` is an object header, a handle and a `bool`. The time column
  says the managed run cost some 2.5 µs per buffer more than the native one
  ((140.57 − 90.62) ms over 20,000), which is dispatch, the wrapper and
  collecting it together; at three iterations that is an order of magnitude
  and not a measurement, the two rows overlapping well inside their error and
  the pair moving by a factor of two when it is re-run. The 697 B on the
  native row is an incidental allocation of the run amortised over one
  operation, not a per buffer cost.
* **Properties.** Going through a name and a `GValue` is one to two orders of
  magnitude more expensive than the generated accessor. That is the whole
  argument for generating typed accessors, and the reason `SetProperty(string,
  object?)` is documented as the escape hatch rather than the normal way.
* **Interned lookup.** Every one of the three answers an existing wrapper with
  **zero managed allocation**, which is the claim that matters here; the times
  differ because the native lookups differ (`gst_bin_get_by_name` walks and
  takes the bin lock, `gst_pad_get_parent_element` is a field read).
* **Map.** `MapOnly` is the number worth keeping here: `gst_buffer_map` and
  `gst_buffer_unmap` of a 64 KiB buffer and nothing else, at about **125 ns**,
  two percent of either read row. Mapping is that cheap, which is why reading
  through the map rather than through a copy is decided by the copy and not by
  the map. **The two read rows do not discriminate on this run**: their means
  tie (7.22 µs against 7.15 µs) and only their medians separate (5.56 µs
  mapped against 7.16 µs copied), one slow iteration having pulled the mapped
  mean up over a `StdDev` of 2.9 µs. Read that pair as a direction and not as
  a factor. Neither read row allocates, the pooled array being rented once in
  `[GlobalSetup]`.

## Artifacts

BenchmarkDotNet writes its reports under `BenchmarkDotNet.Artifacts/` in the
working directory, which `.gitignore` covers.
