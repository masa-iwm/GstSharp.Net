# GstSharp.Net

Modern .NET bindings for [GStreamer](https://gstreamer.freedesktop.org/) 1.28,
designed for NativeAOT from the start.

* **.NET 10**, `IsAotCompatible=true` on every shipping assembly.
* **`[LibraryImport]`** everywhere: interop stubs are produced at build time by
  the runtime source generator, not by reflection or IL emit.
* **Zero reflection on the runtime path**. Native types are mapped through a
  generated registry of function pointers, so trimming and AOT compilation keep
  the whole surface intact.
* **Generated from `.gir`**, by a generator that lives in this repository
  (`generator/GstSharp.Generator`). The generated C# is committed, so consumers
  never need Python, `gapi`, or `xsltproc`.
* **Cross platform**: Windows (both the MSVC and the MinGW flavor of the
  official GStreamer builds, plus MSYS2), macOS and Linux. The native library is
  located at startup by `NativeLoader`; no `dllmap`, no environment variables
  required.

Initial module set: `Gst`, `GstBase`, `GstApp`, `GstVideo`, `GstAudio`,
`GstPbutils`.

## Status

Early development. Nothing is usable yet: the repository currently contains the
project scaffold and the vendored `.gir` inputs. APIs will change without
notice until the first preview package.

## Usage sketch

The intended shape of the API, once the generator lands:

```csharp
GstSharp.Initialize();

using var pipeline = Gst.Parse.Launch("playbin uri=file:///path/to/movie.mkv");
pipeline.SetState(Gst.State.Playing);

using var bus = pipeline.Bus;
using var message = bus.TimedPopFiltered(Gst.ClockTime.None, Gst.MessageType.Eos | Gst.MessageType.Error);

pipeline.SetState(Gst.State.Null);
```

## Repository layout

| Path | Contents |
| --- | --- |
| `girs/` | Vendored `.gir` inputs and overlay files. See `girs/README.md`. |
| `generator/` | The `.gir` to C# generator (console application, no NuGet dependencies). |
| `src/` | Shipping libraries: hand-written runtime (`GstSharp.Net.Core`), generated bindings, and the Roslyn analyzers. |
| `samples/` | Runnable samples, including the NativeAOT smoke test. |
| `tests/` | Generator unit tests, analyzer tests, and integration tests that need a native GStreamer. |

## Building

```sh
dotnet build
dotnet test
```

A native GStreamer installation is only needed for the integration tests and the
samples.

## License

LGPL-2.1-or-later. See [`LICENSE`](LICENSE).

The bindings are generated from GStreamer's `.gir` files and embed their
documentation text, which is LGPL licensed; the same license therefore applies
to the generated sources.
