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

Module set: `Gst`, `GstBase`, `GstApp`, `GstVideo`, `GstAudio`, `GstPbutils`,
`GstSdp`, `GstWebRTC`.

## Packages

One version for the whole set, and every identifier starts with `GstSharp.Net`,
so a single `packageSourceMapping` pattern covers all of them.

| Package | Contents |
| --- | --- |
| `GstSharp.Net` | `Gst` core, the hand-written runtime (native loader, marshalling, GObject/GLib layer) and the Roslyn analyzers. Every other package depends on it. |
| `GstSharp.Net.Base` | `GstBase`. |
| `GstSharp.Net.App` | `GstApp`: `appsrc` and `appsink`. |
| `GstSharp.Net.Video` | `GstVideo`. |
| `GstSharp.Net.Audio` | `GstAudio`. |
| `GstSharp.Net.Pbutils` | `GstPbutils`. |
| `GstSharp.Net.Sdp` | `GstSdp`: SDP session descriptions and MIKEY key management. |
| `GstSharp.Net.WebRTC` | `GstWebRTC`: session descriptions, ICE, transports and data channels for `webrtcbin`. |

The analyzers ship inside `GstSharp.Net` rather than as a package of their own:
they cannot get out of step with the binding that way, and no second package
reports the same diagnostic twice. They are `GST0001` (a wrapper that owns a
reference and never releases it) and `GST0002` (a buffer mapping that is never
released); see [`docs/analyzers.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md).
NuGet never passes analyzers along a package dependency, so they reach the
projects that reference `GstSharp.Net` themselves.

The packages target plain `net10.0` and carry managed code only. GStreamer
itself is not bundled: install it separately and let `NativeLoader` find it.

## Status

Preview. The generated surface covers the module set above, and the API may
still change until 1.28.0 final.

## Usage sketch

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
| `src/` | Shipping libraries: the bindings, the hand-written runtime under `src/GstSharp.Net/Core/`, and the Roslyn analyzers. |
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

LGPL-2.1-or-later. See [`LICENSE`](https://github.com/masa-iwm/GstSharp.Net/blob/main/LICENSE).

The bindings are generated from GStreamer's `.gir` files and embed their
documentation text, which is LGPL licensed; the same license therefore applies
to the generated sources.
