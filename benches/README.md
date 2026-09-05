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

* **The default job is `Job.Default`** — BenchmarkDotNet's own warmup and
  iteration heuristics, which run until the measurement settles. A short job
  puts the whole suite under three minutes instead of twenty odd, and cannot
  see through the variance of a native library: the trampoline pair swung
  between a ratio of 0.63 and 2.10 across five short runs of the same code. A harness that exists to catch a
  regression has to settle before it is worth reading, so the time is spent.
  It buys less than it should: on the default job the map pair settles and
  the trampoline pair still does not, for the reason under the table below.
* **The default toolchain is `InProcessEmitToolchain`.** The out-of-process
  default writes a generated child project underneath this repository, where
  it inherits `Directory.Build.props` and its `TreatWarningsAsErrors`. In
  process, there is no generated project to inherit anything.

Passing `--job` on the command line **replaces** the default job and takes the
in-process toolchain with it, which is why the commands above do not use it —
`--job short` is the one to reach for when a rough answer is wanted quickly,
and it has to be `--job short --inProcess` for the toolchain to survive. On its
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

x64, Windows, GStreamer 1.28.6 (MinGW), the default job, in process; the whole
suite took about twenty-three minutes. These are the numbers of one machine on
one day and are here to show the shape of the output, not as a target anything
is held to. Read the `Ratio` and `Allocated` columns, not the absolute times,
and read the `StdDev` before either of them.

```
| Method          | Mean     | Error    | StdDev   | Median   | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------- |---------:|---------:|---------:|---------:|------:|--------:|----------:|------------:|
| NativeIdentity  | 106.4 ms | 12.81 ms | 37.78 ms | 116.7 ms |  1.19 |    0.73 |     745 B |        1.00 |
| ManagedIdentity | 126.2 ms | 14.06 ms | 41.46 ms | 129.4 ms |  1.41 |    0.84 |  640864 B |      860.22 |

| Method               | Categories | Mean        | Error      | StdDev       | Median      | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------- |----------- |------------:|-----------:|-------------:|------------:|-------:|--------:|-------:|----------:|------------:|
| GetSyncTyped         | get        |    49.35 ns |   3.071 ns |     8.812 ns |    52.55 ns |   1.03 |    0.27 |      - |         - |          NA |
| GetSyncByName        | get        | 1,146.99 ns | 107.479 ns |   313.522 ns | 1,059.69 ns |  24.03 |    8.03 |      - |         - |          NA |
| GetNameByName        | get        | 2,061.01 ns | 270.205 ns |   792.464 ns | 1,889.19 ns |  43.19 |   18.66 | 0.0019 |      56 B |          NA |
|                      |            |             |            |              |             |        |         |        |           |             |
| ValueRoundtripInt    | gvalue     |    66.47 ns |   9.572 ns |    28.222 ns |    52.77 ns |   1.17 |    0.67 |      - |         - |          NA |
| ValueRoundtripString | gvalue     |   400.40 ns |  60.916 ns |   179.612 ns |   312.66 ns |   7.06 |    4.20 | 0.0019 |      40 B |          NA |
|                      |            |             |            |              |             |        |         |        |           |             |
| SetSyncTyped         | set        |    49.12 ns |   8.313 ns |    24.511 ns |    33.35 ns |   1.22 |    0.78 |      - |         - |          NA |
| SetSyncByName        | set        | 3,146.65 ns | 495.690 ns | 1,461.554 ns | 2,345.11 ns |  77.86 |   47.83 |      - |      24 B |          NA |
| SetNameByName        | set        | 4,219.31 ns | 589.804 ns | 1,739.050 ns | 4,399.40 ns | 104.40 |   59.59 |      - |         - |          NA |

| Method                  | Categories | Mean       | Error     | StdDev      | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|------------------------ |----------- |-----------:|----------:|------------:|-----------:|------:|--------:|----------:|------------:|
| ReadMappedSpan          | read       | 5,026.3 ns | 189.28 ns |   530.77 ns | 4,959.8 ns |  1.01 |    0.15 |         - |          NA |
| ReadCopiedToPooledArray | read       | 8,870.8 ns | 700.29 ns | 1,952.13 ns | 8,379.7 ns |  1.78 |    0.43 |         - |          NA |
| MapOnly                 | read       |   175.0 ns |  18.98 ns |    54.47 ns |   150.8 ns |  0.04 |    0.01 |         - |          NA |
|                         |            |            |           |             |            |       |         |           |             |
| WriteMappedSpan         | write      | 3,652.6 ns | 449.39 ns | 1,325.04 ns | 3,130.5 ns |  1.11 |    0.53 |         - |          NA |

| Method           | Mean       | Error     | StdDev    | Median     | Ratio | RatioSD | Allocated | Alloc Ratio |
|----------------- |-----------:|----------:|----------:|-----------:|------:|--------:|----------:|------------:|
| GetStaticPad     |   433.2 ns |  13.47 ns |  38.63 ns |   447.6 ns |  1.01 |    0.14 |         - |          NA |
| GetByName        | 2,556.5 ns | 270.52 ns | 797.62 ns | 2,456.8 ns |  5.96 |    1.98 |         - |          NA |
| GetParentElement |   283.8 ns |  19.20 ns |  56.31 ns |   291.4 ns |  0.66 |    0.15 |         - |          NA |
```

What that run says, and what it does not:

* **Trampoline.** The managed filter allocated **32.0 bytes per buffer**:
  (640,864 − 745) B over 20,000 buffers. That is consistent with the one
  managed `Buffer` wrapper the generated trampoline creates per call through
  `Gst.Buffer.Borrow` — `Buffer` declares no instance fields of its own, and a
  `MiniObject` is an object header, a handle and a `bool`. That figure is the
  firm half of this pair and comes back within 0.03 B of 32.0 on every run.
  **The time column is not.** It puts the managed run about 1 µs per buffer
  above the native one ((126.2 − 106.4) ms over 20,000) — dispatch, the
  wrapper, and collecting it — but the `StdDev` of both rows is a third of
  their mean, and two further runs of the class on this same build put the
  ratio at 1.03 and 1.40 against the 1.41 above. One operation here is a
  whole pipeline going to `PLAYING` and back, and what that costs depends on
  how the machine schedules the streaming thread that day; the default job
  stopped the pair inverting, which a short job did not, but it did not make
  it settle. Take the direction, not the number. The 745 B on the native row
  is an incidental allocation of the run amortised over one operation, not a
  per buffer cost.
* **Properties.** Going through a name and a `GValue` is one to two orders of
  magnitude more expensive than the generated accessor. That is the whole
  argument for generating typed accessors, and the reason `SetProperty(string,
  object?)` is documented as the escape hatch rather than the normal way.
* **Interned lookup.** Every one of the three answers an existing wrapper with
  **zero managed allocation**, which is the claim that matters here; the times
  differ because the native lookups differ (`gst_bin_get_by_name` walks and
  takes the bin lock, `gst_pad_get_parent_element` is a field read).
* **Map.** `MapOnly` is `gst_buffer_map` and `gst_buffer_unmap` of a 64 KiB
  buffer and nothing else, at about **175 ns** — four percent of the mapped
  read. Mapping is that cheap, which is why reading through the map rather
  than through a copy is decided by the copy and not by the map, and the read
  pair now shows it: **5.03 µs mapped against 8.87 µs copied, a ratio of
  1.78**, each row clear of the other's error. The 3.8 µs between them is the
  `memcpy` of 64 KiB that `Buffer.Extract` does and the mapped span does not.
  Neither read row allocates, the pooled array being rented once in
  `[GlobalSetup]`.

## Artifacts

BenchmarkDotNet writes its reports under `BenchmarkDotNet.Artifacts/` in the
working directory, which `.gitignore` covers.
