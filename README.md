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
`GstSdp`, `GstWebRTC`, `GstNet`, `GstRtsp`, `GES`.

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
| `GstSharp.Net.Net` | `GstNet`: network clocks and time providers. |
| `GstSharp.Net.Rtsp` | `GstRtsp`: RTSP connections, messages, URLs and transports. |
| `GstSharp.Net.GES` | `GES`: the editing services — timelines, layers, clips and the assets behind them. Initialise through `GES.GstGES.Initialize()`, which runs `ges_init` on top of the usual startup. |

The analyzers ship inside `GstSharp.Net` rather than as a package of their own:
they cannot get out of step with the binding that way, and no second package
reports the same diagnostic twice. They are `GST0001` (a wrapper that owns a
reference and never releases it) and `GST0002` (a buffer mapping that is never
released); see [`docs/analyzers.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md).
They travel along the package dependency, so a project that references only a
module package — `GstSharp.Net.Sdp`, say — gets them too. Every module clears
`PrivateAssets` on its reference to `GstSharp.Net` to say so, because the
default would pack a dependency that asks for the analyzer assets to be left
behind.

The packages target plain `net10.0` and carry managed code only. GStreamer
itself is not bundled: install it separately and let `NativeLoader` find it.

## Installation

```sh
dotnet add package GstSharp.Net
dotnet add package GstSharp.Net.App     # and one per module you use
```

### Where the packages come from

Until the set is published to nuget.org, the only feed is **GitHub Packages**,
`https://nuget.pkg.github.com/masa-iwm/index.json`. That feed **requires
authentication even for public packages**: a personal access token with the
`read:packages` scope. A `nuget.config` next to the solution is the usual shape:

```xml
<configuration>
  <packageSources>
    <add key="gstsharp" value="https://nuget.pkg.github.com/masa-iwm/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <gstsharp>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </gstsharp>
  </packageSourceCredentials>
  <packageSourceMapping>
    <packageSource key="gstsharp">
      <package pattern="GstSharp.Net*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

The `GstSharp.Net*` prefix is why every package identifier shares it: one
mapping entry covers the whole set, and an identifier outside the pattern would
silently fall back to nuget.org and fail to restore.

### The native GStreamer

The packages contain managed code only. Install GStreamer itself:

| Platform | How |
| --- | --- |
| Windows | The [official runtime installer](https://gstreamer.freedesktop.org/download/) of one flavor; `msvc` and `mingw` both work. The development installer is not needed — the binding carries its own interop and never compiles against the headers. |
| Windows (MSYS2) | `pacman -S mingw-w64-x86_64-gstreamer mingw-w64-x86_64-gst-plugins-base mingw-w64-x86_64-gst-plugins-good` |
| Linux (Debian/Ubuntu) | `apt install libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good`, plus `libgstreamer-plugins-bad1.0-0` for `GstWebRTC` and `libges-1.0-0` for `GES` |
| macOS | `brew install gstreamer` |

**Supported versions.** The surface is generated from the 1.28.6 `.gir` files.
The floor at run time is **GStreamer 1.24**: the struct layouts the ABI probes
mirror have been stable since then. An entry point that GStreamer added after
1.24 is present in the managed surface and throws `EntryPointNotFoundException`
against an older library — the missing export is the documented behavior, not a
bug. CI runs the whole suite against four installations: Ubuntu 24.04 (1.24, the
floor), the official Windows MSVC build (1.28.6), MSYS2's MinGW build, and
Homebrew on macOS.

## Getting started

```csharp
using Gst;
using Gst.GLib;

GstSharp.Initialize();

// gst_parse_launch returns a GstElement; the cast goes through the type
// registry, which is what turns it into a Pipeline wrapper.
if (Global.ParseLaunch("playbin uri=file:///path/to/movie.mkv") is not Pipeline pipeline)
{
    Console.Error.WriteLine("that description is not a pipeline.");
    return 1;
}

// A pipeline this code built and stops is the one GObject wrapper a consumer
// disposes; see docs/ownership.md.
using (pipeline)
{
    // The bus wrapper is interned and shared with every other lookup of the
    // same bus, so it is not disposed here.
    Bus bus = pipeline.GetBus();

    pipeline.SetState(State.Playing);

    // A Message is a mini object: the wrapper owns a reference and has to be
    // released. GST0001 reports it when it is not.
    using Message? message = bus.TimedPopFiltered(
        ClockTime.None,
        MessageType.Eos | MessageType.Error);

    if (message?.Type == MessageType.Error)
    {
        (GException error, string? debug) = message.ParseError();
        Console.Error.WriteLine($"{message.SourceName}: {error.Message}");
        Console.Error.WriteLine($"debug: {debug}");
    }

    // Back to NULL before the pipeline is released: one that is still PLAYING
    // when its last reference goes away leaves its streaming threads running.
    pipeline.SetState(State.Null);
}

return 0;
```

`GstSharp.Initialize()` loads the native libraries, runs `gst_init` and fills
the type registry. A module that is only ever *named* — in a cast, in a type
test — never runs its own initialiser, so reach for the module entry point when
that is all an application does with it: `Gst.App.GstApp.Initialize()`,
`Gst.Base.GstBase.Initialize()`, and one next to every other module. The
[GType registry](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md#the-gtype-registry)
section explains the failure this avoids.

## Ownership and lifetime

Read
**[`docs/ownership.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md)**
before writing anything that runs for longer than a second. The short version:

* `MiniObject` and `Boxed` wrappers (`Buffer`, `Caps`, `Sample`, `Message`,
  `Structure`, ...) own a reference and **must be disposed**. `GST0001`
  enforces it.
* `GObject` wrappers (`Element`, `Pipeline`, `Bus`, `Pad`, ...) are interned and
  shared: **normally do not dispose them**. Disposal acts for every holder at
  once. The sanctioned exception is a pipeline this code created, after
  `SetState(State.Null)`.
* A few calls consume their argument (`AppSrc.PushBuffer`, `Element.SendEvent`,
  `BufferPool.SetConfig`, `WebRTCSessionDescription.New`, ...). `Dispose` is
  idempotent, so a `using` around the argument stays correct.
* An application with no main loop should call
  `GstSharp.DrainPendingReleases()` periodically — once per poll of the bus is
  the natural place.

## When the loader cannot find GStreamer

`NativeLoader` probes the registry, the documented environment variables, the
known installation directories, `PATH`, and an application-bundled
`runtimes/<rid>` tree. When none of that wins, say where to look:

```csharp
GstSharp.Initialize(new GstSharpOptions
{
    NativeSearchPath = @"C:\gstreamer\1.0\msvc_x86_64\bin",
    WindowsFlavor = GstFlavor.Msvc,   // or GstFlavor.MinGW
});
```

And to find out what it did pick, which is the first thing to log in an
application that ships to machines you do not own:

```csharp
Console.WriteLine(NativeLoader.ResolvedDirectory);          // the directory that won
Console.WriteLine(NativeLoader.ResolvedFlavor);             // Msvc / MinGW, Windows only
Console.WriteLine(NativeLoader.ResolvedOrigin);             // which probe stage found it
Console.WriteLine(NativeLoader.ResolvedSourceDescription);  // that stage, in words
Console.WriteLine(NativeLoader.GetLoadedModulePath("Gst")); // the file actually mapped
```

One flavor and one directory are pinned for every module, so a process can
never end up with half an MSVC and half a MinGW GStreamer.

## Samples

| Sample | What it shows | Run it |
| --- | --- | --- |
| `samples/PlaybinPlayer` | A pipeline from a description, driven by a polled bus. No main loop, no signal handler. | `dotnet run --project samples/PlaybinPlayer` |
| `samples/AppSinkSpans` | Raw video out of an `appsink`, read through a `Span<byte>` over the mapped GStreamer memory. Pull mode and signal mode produce the same checksum. | `dotnet run --project samples/AppSinkSpans -- --mode pull` |
| `samples/GstLaunch` | A port of `gst-launch-1.0`: the whole bus loop, the preroll/buffering/progress state machine, `-t -c -v -q -m -e -X -f`, `--gst-*` passthrough and the exit codes of the C tool. One binary with per-OS behavior — Ctrl+C through a `GstLaunchInterrupt` application message everywhere, SIGHUP and SIGQUIT on POSIX, the multimedia timer on Windows. Its header comment lists what it cannot match. | `dotnet run --project samples/GstLaunch -- videotestsrc num-buffers=100 ! fakesink` |
| `samples/AotSmoke` | The NativeAOT gate: initialise, make an element, release it, with zero trimming warnings. | `dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true` |

`PlaybinPlayer` and `AppSinkSpans` also take `--native-path <directory>`,
`--flavor msvc\|mingw` and `--timeout <seconds>`; `GstLaunch` takes the first
two and `--interrupt-after <seconds>`, which drives its Ctrl+C path without a
console signal.

## Properties and signals without a generated binding

Not everything is in the `.gir` files. Plugins add properties and signals that
introspection never sees, and GStreamer publishes a good deal of its
functionality as *action signals*, which are calls dressed up as signals. The
by-name surface on `Gst.GObject.Object` reaches all of it, without reflection:

```csharp
using Gst.GObject;

// A property no binding exposes, read and written by name.
using Value swapchain = sink.GetProperty("swapchain-handle");
nint handle = swapchain.GetPointer();

using Value sync = Value.CreateFor(false, GType.Boolean);
sink.SetProperty("sync", sync);

// An action signal is a call dressed up as a signal.
sink.EmitSignal("resize", 1920, 1080);

// A signal the .gir never mentioned, connected and disconnected by name.
ulong id = playbin.ConnectSignal("about-to-finish", (sender, args) => null);
playbin.RemoveHandler(id);
```

Arguments are validated against the signature the object declares before
anything is emitted, and a mismatch names the expectation.

What the generator did **not** bind, and why, is listed per module and per
reason in
[`girs/skip-report.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/girs/skip-report.md).
The gaps worth naming here:

* **Subclassing is not available yet.** C# types cannot derive from
  `Gst.Element` or `GstBase.BaseSrc` and be called back through the native
  vtable. The design is written down in
  [`docs/subclassing.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/subclassing.md);
  no stage of it has shipped.
* **Writing GValue-typed structures is incomplete.** Reading is covered;
  building a `GValue` for every fundamental type is in progress, and a boxed
  value returned by `EmitSignal` comes back as an opaque handle rather than as
  a typed wrapper.
* **`scope="async"` callbacks are not generated.** The Gio asynchronous pattern
  is exposed as `Task`-returning methods instead, hand written per operation;
  see
  [`docs/gio-async.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/gio-async.md).

## Namespaces

The C# namespace of a module is its gir namespace under `Gst`: `GstBase`
becomes `Gst.Base`, `GstApp` becomes `Gst.App`, `GstWebRTC` becomes
`Gst.WebRTC`, and so on. **`GES` is the exception**: its gir namespace is
already top level, so its C# namespace is `GES`, mirroring the gir rather than
inventing a prefix the C library does not have.

`Gst.GLib`, `Gst.GObject` and `Gst.Interop` are the hand-written runtime, not
generated from the GLib girs. `Gst.Gio` is a deliberately small hand-written
subset of Gio — cancellables, sockets, TLS — covering what the GStreamer
surface hands out and nothing else.

## Status and versioning

**Preview.** The packages are on GitHub Packages; nuget.org publication is
pending. The public surface is settled enough to build on, and breaking changes
before the first nuget.org release are possible where a shape turns out to be
wrong.

The version is `<gstreamer-major>.<gstreamer-minor>.<binding-patch>`:

* **major and minor track the GStreamer series** the bindings are generated
  from. `1.28.x` of this package is generated from GStreamer 1.28, and a move
  to GStreamer 1.30 makes the package `1.30.0`.
* **patch is the binding's own counter.** It has nothing to do with the
  GStreamer patch release; `1.28.3` does not imply GStreamer 1.28.3, and the
  runtime floor stays 1.24 regardless.

## Extending the binding

**Third-party binding modules are not a supported extension point today.** The
registration surface exists — `TypeRegistry.RegisterModule` takes a
`NativeModule` of `GType`-to-factory entries — but the other half does not:
wrapper construction is `internal`, so an assembly outside this repository
cannot build a `Gst.Element`-derived wrapper for a handle, and therefore cannot
supply a working factory. The modules in this repository work because they are
named in `InternalsVisibleTo`.

Opening that seam — a public construction protocol for wrappers, so that a
binding for a GStreamer library this repository does not cover can live
elsewhere — is on the roadmap, alongside the subclassing work that needs the
same protocol. Until then, a library that is not in the module set is reached
through the by-name property and signal surface above.

## Documentation

| Page | Contents |
| --- | --- |
| [`docs/ownership.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md) | Who owns a wrapper, who disposes it, and the GType registry. **Start here.** |
| [`docs/analyzers.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md) | `GST0001` and `GST0002`, what they catch and how to satisfy them. |
| [`docs/gio-async.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/gio-async.md) | How Gio's `*_async` / `*_finish` pairs become `Task`-returning methods. |
| [`docs/subclassing.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/subclassing.md) | The approved design for deriving from GObject classes in C#. Not implemented yet. |
| [`girs/skip-report.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/girs/skip-report.md) | Every gir symbol the generator did not bind, grouped by reason. |
| [`eng/ci-notes.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/eng/ci-notes.md) | Why the workflows look the way they do, and how to run each gate by hand. |
| [`CONTRIBUTING.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/CONTRIBUTING.md) | Build, test, regenerate, and the quality gates a change has to pass. |

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
samples. See
[`CONTRIBUTING.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/CONTRIBUTING.md)
for the generator commands and the quality gates.

## License

LGPL-2.1-or-later. See [`LICENSE`](https://github.com/masa-iwm/GstSharp.Net/blob/main/LICENSE).

The bindings are generated from GStreamer's `.gir` files and embed their
documentation text, which is LGPL licensed; the same license therefore applies
to the generated sources.

**What that means in practice** — not legal advice, and not a substitute for
reading the license or asking a lawyer:

* **GStreamer is loaded dynamically**, through `[LibraryImport]` against the
  shared libraries an installation provides. Nothing in these packages links
  GStreamer statically, **including under NativeAOT**: an AOT-published
  application is one native executable of managed origin that still resolves
  `libgstreamer-1.0` at run time.
* **A closed-source application may use these packages.** The LGPL condition
  that matters is that the user can replace the LGPL parts, which dynamic
  loading satisfies: point the application at a different GStreamer
  installation and it uses that one.
* **Changes to GstSharp.Net itself are LGPL** and belong back here, because the
  generated sources carry GStreamer's own documentation text.
* **The GStreamer installation is a separate question.** Its plugins carry
  their own licenses — several of the "ugly" and "bad" sets are GPL or carry
  patent conditions — and shipping a runtime alongside an application means
  auditing what is in it.
