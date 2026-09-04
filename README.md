# GstSharp.Net

[![NuGet](https://img.shields.io/nuget/v/GstSharp.Net?logo=nuget)](https://www.nuget.org/packages/GstSharp.Net)
[![CI](https://github.com/masa-iwm/GstSharp.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/masa-iwm/GstSharp.Net/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-LGPL--2.1--or--later-blue)](https://github.com/masa-iwm/GstSharp.Net/blob/main/LICENSE)

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

Generated module set: `Gst`, `GstBase`, `GstApp`, `GstVideo`, `GstAudio`,
`GstPbutils`, `GstSdp`, `GstWebRTC`, `GstNet`, `GstRtsp`, `GstAllocators`,
`GstTag`, `GstTranscoder`, `GstPlay`, `GstRtp`, `GstRtspServer`, `GES`.
An eighteenth module, `GstController`, is written by hand against the public
module SPI and ships alongside them.

## Packages

One version for the whole set, and every identifier starts with `GstSharp.Net`,
so a single `packageSourceMapping` pattern covers all of them.

| Package | Contents |
| --- | --- |
| `GstSharp.Net` | `Gst` core, the hand-written runtime (native loader, marshalling, GObject/GLib layer) and the Roslyn analyzers. Every other package depends on it. |
| `GstSharp.Net.Base` | `GstBase`. |
| `GstSharp.Net.Controller` | `GstController`: the interpolation, LFO and trigger control sources, and the direct, ARGB and proxy control bindings that drive a property from one. Hand written against the public module SPI — see [`docs/modules.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md). |
| `GstSharp.Net.App` | `GstApp`: `appsrc` and `appsink`. |
| `GstSharp.Net.Video` | `GstVideo`. |
| `GstSharp.Net.Audio` | `GstAudio`. |
| `GstSharp.Net.Pbutils` | `GstPbutils`. |
| `GstSharp.Net.Sdp` | `GstSdp`: SDP session descriptions and MIKEY key management. |
| `GstSharp.Net.WebRTC` | `GstWebRTC`: session descriptions, ICE, transports and data channels for `webrtcbin`. |
| `GstSharp.Net.Net` | `GstNet`: network clocks and time providers. |
| `GstSharp.Net.Rtsp` | `GstRtsp`: RTSP connections, messages, URLs and transports. |
| `GstSharp.Net.Rtp` | `GstRtp`: the RTP and RTCP buffer helpers of gst-plugins-base, the payloader and depayloader base classes and the header extension API, which is what drives the payloader and depayloader elements the RTP plugins ship and the header extensions built into the library. Authoring a payloader of your own means subclassing `GstRTPBasePayload`, which the binding does not ship: the module binds the classes as they are used from the outside, not as they are derived from. |
| `GstSharp.Net.RtspServer` | `GstRtspServer`: the RTSP server of gst-rtsp-server — the server itself, its clients and sessions, the mount points a media factory is attached to, the media and streams a factory builds, and their ONVIF variants. Needs `libgstrtspserver-1.0`, which ships separately from the core GStreamer libraries. |
| `GstSharp.Net.Allocators` | `GstAllocators`: the file descriptor, DMA-BUF, shared memory and DRM dumb allocators. |
| `GstSharp.Net.Tag` | `GstTag`: tag parsing and writing for ID3, Vorbis comments, XMP and EXIF. |
| `GstSharp.Net.Transcoder` | `GstTranscoder`: transcoding a media URI into another one against an encoding profile. Needs the `transcode` plugin of gst-plugins-bad — `uritranscodebin` and `transcodebin` — at run time, which ships separately from the `libgsttranscoder-1.0` library this module imports from. |
| `GstSharp.Net.Play` | `GstPlay`: a high level playback API — a URI, the play controls around it, its media information and its message bus — from gst-plugins-bad. Upstream marks the library API *unstable* (`docs/libs/play/index.md` in the GStreamer monorepo). The 1.24 floor carries only the index-based track selection API, which the generator marks `[Obsolete]` because upstream deprecated it in 1.26 in favour of the track-id calls. `Play.Start()` is `gst_play_play`, renamed because `Play.Play` is not a legal C# member name. |
| `GstSharp.Net.GES` | `GES`: the editing services — timelines, layers, clips and the assets behind them. Initialise through `GES.GstGES.Initialize()`, which runs `ges_init` on top of the usual startup. |

The analyzers ship inside `GstSharp.Net` rather than as a package of their own:
they cannot get out of step with the binding that way, and no second package
reports the same diagnostic twice. They are `GST0001` (a wrapper that owns a
reference and never releases it), `GST0002` (a buffer mapping that is never
released), `GST0003` (a subclass that overrides an `OnX` vfunc without
declaring the matching slot in its `DefineSubclass` call) and `GST0004` (the
converse, a declared slot with no override behind it); see
[`docs/analyzers.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md).
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

The set is published to **nuget.org**. The `.nupkg` files are also attached to every
[GitHub release](https://github.com/masa-iwm/GstSharp.Net/releases) for
offline use. The copies on GitHub Packages exist for the project's own
release plumbing; that feed requires authentication and is not the intended
way to consume the bindings.

The `GstSharp.Net*` prefix is why every package identifier shares it: in a
solution that pins feeds with `packageSourceMapping`, one pattern covers the
whole set.

### The native GStreamer

The packages contain managed code only. Install GStreamer itself:

| Platform | How |
| --- | --- |
| Windows | The [official runtime installer](https://gstreamer.freedesktop.org/download/) of one flavor; `msvc` and `mingw` both work. The development installer is not needed — the binding carries its own interop and never compiles against the headers. |
| Windows (MSYS2) | `pacman -S mingw-w64-x86_64-gstreamer mingw-w64-x86_64-gst-plugins-base mingw-w64-x86_64-gst-plugins-good` |
| Linux (Debian/Ubuntu) | `apt install libgstreamer1.0-0 libgstreamer-plugins-base1.0-0 gstreamer1.0-plugins-base gstreamer1.0-plugins-good`, plus `libgstreamer-plugins-bad1.0-0` for `GstWebRTC`, `GstTranscoder` and `GstPlay`, `gstreamer1.0-plugins-bad` for the `transcode` plugin the transcoder drives, and `libges-1.0-0` for `GES` |
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
| `samples/AppSrcPush` | The source half: the application generates the audio and pushes it into an `appsrc` in push mode, only while `need-data` says the pipeline wants it and never after `enough-data`. Bounded by a buffer count, and `--output` turns the run into a byte count gate on top of the exit code. | `dotnet run --project samples/AppSrcPush -- --buffers 200` |
| `samples/RtpPacketDump` | The RTP module end to end: every packet an `rtpL16pay` produced, read through a mapped `RTPBuffer` — sequence number, timestamp, SSRC, payload type, marker and payload length — and then a compound RTCP packet built through `RTCPBuffer`/`RTCPPacket`, unmapped, mapped again and walked back. Its header comment states the lifetime rules of `docs/ownership.md` that the mapped structures impose. | `dotnet run --project samples/RtpPacketDump` |
| `samples/GstLaunch` | A port of `gst-launch-1.0`: the whole bus loop, the preroll/buffering/progress state machine, `-t -c -v -q -m -e -X -f`, `--gst-*` passthrough and the exit codes of the C tool. One binary with per-OS behavior — Ctrl+C through a `GstLaunchInterrupt` application message everywhere, SIGHUP and SIGQUIT on POSIX, the multimedia timer on Windows. Its header comment lists what it cannot match. | `dotnet run --project samples/GstLaunch -- videotestsrc num-buffers=100 ! fakesink` |
| `samples/GstTypefind` | A port of `gst-typefind-1.0`: `filesrc ! typefind ! fakesink` per file, PAUSED and a blocking `GetState`, directory recursion, and the `<file> - <caps>` line of the C tool. It is the sample that connects a signal **by name** — `have-type` on a plugin element no `.gir` describes — and its header comment records what that emission can and cannot hand over. | `dotnet run --project samples/GstTypefind -- <file-or-directory>` |
| `samples/GstDeviceMonitor` | A port of `gst-device-monitor-1.0`: `DeviceMonitor` with the `DEVICE_CLASSES[:FILTER_CAPS]` filters, the device listing with caps and properties, and `--follow` for hotplug — all of it as messages on the monitor's bus, polled rather than watched from a main loop. Its header comment lists the shell-quoting and property-enumeration parts of the C tool that the binding cannot reach yet. | `dotnet run --project samples/GstDeviceMonitor` |
| `samples/GstDiscoverer` | A port of `gst-discoverer-1.0`, synchronous path: `DiscoverUri` per URI, the result and duration, the topology walk with its container recursion, the per-stream blocks for audio, video and subtitles, `--verbose` tags and `--toc`. Its output is byte for byte the C tool's on generated media; its header comment says why `-a` is absent and what a failed discovery cannot report. | `dotnet run --project samples/GstDiscoverer -- <file-or-uri>` |
| `samples/GstInspect` | A partial port of `gst-inspect-1.0`: the registry census, and the element page as far as the bound surface reaches — factory and plugin details, the type hierarchy, pad templates with their caps, URI handling and the property listing. Every page ends with a note naming the sections it does not print, and its header says what each of them would need. | `dotnet run --project samples/GstInspect -- fakesink` |
| `samples/GstTranscode` | Transcoding one URI into another against a serialized `GstEncodingProfile`, on the route the transcoder documents as the recommended one: `RunAsync` plus a polled API bus, with no main loop and no signal adapter. It is also where the hand-written `ParseError` earns its keep — the imported one aborts the process on an error that carries no details. | `dotnet run --project samples/GstTranscode -- file:///in.ogg file:///out.ogg` |
| `samples/GstPlay` | A port of `gst-play-1.0`'s user experience onto the `Gst.Play.Play` object: a playlist, the keyboard controls, `--volume`, `--audiosink`/`--videosink`, `--visualization` and `--list-visualizations`, with the API bus read by a timed pop rather than watched from a main loop. It writes the two sink properties on the playbin that `GetPipeline()` answers, the way the C tool does; `PlayVideoOverlayVideoRenderer`, the other way to place the video, is for a GUI application that has a window handle to embed it in. Headless is the default — nothing reads the keyboard without `--interactive`. | `dotnet run --project samples/GstPlay -- --duration 10 <file-or-uri>` |
| `samples/AotSmoke` | The NativeAOT gate: initialise, make an element, release it, and run four managed subclasses - an element, a source and sink pair, a managed audio sink and a managed video sink - with zero trimming warnings. | `dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true` |

`PlaybinPlayer`, `AppSinkSpans` and `AppSrcPush` also take
`--native-path <directory>`,
`--flavor msvc\|mingw` and `--timeout <seconds>`; `GstLaunch`, `GstTypefind`,
`GstDeviceMonitor`, `GstDiscoverer` and `GstInspect` take the first two. Each
of the five ports adds one option of its own that the C tool does not have, so
that a path which normally needs a console signal or a person can be run
unattended, or so that what the port cannot do stays visible:
`GstLaunch --interrupt-after <seconds>` drives its Ctrl+C path,
`GstDeviceMonitor --follow-for <seconds>` bounds a hotplug run,
`GstTypefind --fail-on-unknown` turns a file whose type was not found into a
non-zero exit code, `GstDiscoverer --fail-on-error` does the same for a URI
that could not be discovered, and `GstInspect --no-coverage-note` takes the
closing note off a page that is being diffed against the C tool's.

`GstTranscode` takes no option of its own: it is `<src-uri> <dst-uri>
[<profile>]`, where the profile defaults to `application/ogg:audio/x-vorbis`.
It needs the `uritranscodebin` and `transcodebin` elements of the `transcode`
plugin of gst-plugins-bad at run time, which ship separately from the
`libgsttranscoder-1.0` library the module imports from; without them the
sample says so and stops rather than reporting a transcoding failure.

`GstPlay` takes a playlist of URIs or paths and these options of its own:
`--volume <0..1>`, `--audiosink <factory>` and `--videosink <factory>`,
`--visualization <name>` beside `--list-visualizations`, `--duration <seconds>`
to bound an unattended run, and `--interactive` to read the keyboard (press `k`
for the list of keys). Its header comment says which keys the C tool puts
elsewhere and what it leaves out. Cycling tracks goes through the index-based
setters, which upstream deprecated in 1.26 and the generator therefore marks
`[Obsolete]`; they are the only ones that exist on the 1.24 floor, so the sample
calls them under a `#pragma warning disable CS0618`.

### The official tutorials

`samples/tutorials/` holds the [GStreamer basic
tutorials](https://gstreamer.freedesktop.org/documentation/tutorials/) ported
onto this binding, one runnable project per tutorial, with the upstream
numbering kept:

| Project | Upstream page | What it teaches |
| --- | --- | --- |
| `BasicTutorial01` | [Hello world](https://gstreamer.freedesktop.org/documentation/tutorials/basic/hello-world.html) | `ParseLaunch`, the states, the bus |
| `BasicTutorial02` | [GStreamer concepts](https://gstreamer.freedesktop.org/documentation/tutorials/basic/concepts.html) | factories, a pipeline, a link, a property, a parsed error |
| `BasicTutorial03` | [Dynamic pipelines](https://gstreamer.freedesktop.org/documentation/tutorials/basic/dynamic-pipelines.html) | `pad-added`, linking pads, reading caps |
| `BasicTutorial04` | [Time management](https://gstreamer.freedesktop.org/documentation/tutorials/basic/time-management.html) | position, duration, the seeking query, `SeekSimple` |
| `BasicTutorial06` | [Media formats and pad capabilities](https://gstreamer.freedesktop.org/documentation/tutorials/basic/media-formats-and-pad-capabilities.html) | caps, structures, fields, pad templates |
| `BasicTutorial07` | [Multithreading and pad availability](https://gstreamer.freedesktop.org/documentation/tutorials/basic/multithreading-and-pad-availability.html) | a `tee`, its request pads, a `queue` per branch |
| `BasicTutorial08` | [Short-cutting the pipeline](https://gstreamer.freedesktop.org/documentation/tutorials/basic/short-cutting-the-pipeline.html) | `appsrc`, `appsink`, a `tee` and its request pads |
| `BasicTutorial09` | [Media information gathering](https://gstreamer.freedesktop.org/documentation/tutorials/basic/media-information-gathering.html) | `GstDiscoverer`, an answer that arrives as a signal, the topology |
| `BasicTutorial13` | [Playback speed](https://gstreamer.freedesktop.org/documentation/tutorials/basic/playback-speed.html) | seek events with a rate, reverse playback, step events |

The walkthrough text stays upstream; each file carries a header comment saying
where the port differs from the C original and why — a polled bus instead of a
`GMainLoop`, `using` instead of `gst_*_unref`, a typed event instead of
`g_signal_connect`. `samples/tutorials/README.md` is the index and explains the
options the tutorials do not have (`--headless`, `BasicTutorial13 --keys` and
the per-tutorial bounds), which exist so that a tutorial can be run unattended.

```sh
dotnet run --project samples/tutorials/BasicTutorial02
dotnet run --project samples/tutorials/BasicTutorial03 -- <file-or-uri>
```

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

* **Subclassing is limited to an allowlist of base classes.** A C# type can
  derive from `Gst.Element`, `Gst.Bin`, `Gst.Base.BaseSrc`, `PushSrc`,
  `BaseSink`, `BaseTransform`, `Aggregator`, `Gst.Audio.AudioBaseSink`,
  `AudioBaseSrc`, `AudioSink`, `AudioSrc`, `AudioFilter`, or
  `Gst.Video.VideoSink`, `VideoFilter`, override the vfuncs of the class and
  be called back through the native vtable. What is not there yet: the parser
  and codec base classes, the pad functions, properties and signals on managed
  types, and construction from native code — an element registered this way
  cannot be built by `gst_element_factory_make` or named in a pipeline
  description. See
  [`docs/subclassing.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/subclassing.md#11-using-it).
* **Writing GValue-typed structures is incomplete.** Reading is covered —
  `Value.GetBoxed<T>()` for a boxed value and `Value.GetMiniObject<T>()` for a
  caps, a tag list or a sample — and building a `GValue` for every fundamental
  type is in progress. A boxed value that is not a mini object still comes back
  from `EmitSignal` as an opaque handle rather than as a typed wrapper.
* **`scope="async"` callbacks are not generated.** The Gio asynchronous pattern
  is exposed as `Task`-returning methods instead, hand written per operation;
  see
  [`docs/gio-async.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/gio-async.md).
* **The byte and bit cursors are out of scope.** `GstByteReader`,
  `GstByteWriter`, `GstBitReader` and `GstBitWriter` walk a block of memory the
  caller already owns, and .NET has that surface built in: `Span<byte>`,
  `BinaryPrimitives` (every width and endianness as a single inlined
  instruction), `ArrayBufferWriter<byte>` for the growing writer. The memory is
  already reached as a span — `Gst.Buffer.Map` and `Gst.Base.Adapter.Map` hand
  one out, `Gst.Buffer.NewMemdup` takes one back — and the masked 32 bit start
  code scan is bound on `Gst.Base.Adapter.MaskedScanUint32`. The C cursors add a
  bit level position with fixed width reads only — no Exp-Golomb, nothing codec
  aware. The 148 dropped methods are listed under `GstBase` in
  [`girs/skip-report.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/girs/skip-report.md).
  This is a decision, not a gap, and it does not change within `1.28.x`.

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

**Stable.** From `1.28.1` on the public surface only grows: new members
appear as the binding closes gaps, existing signatures stay. A behavioral
bug — ownership, lifetime, a wrong contract — is fixed in a patch release and
called out in the release notes. A change that would break compilation waits
for the next GStreamer series.

`1.28.1` itself is the one deliberate exception, taken in the first week of
the series while nothing depended on the surface: it re-projected the types a
value copy could not represent — the buffer metas, the static caps and pad
templates — and repaired the shipped members that discarded or overran what
the native call wrote. That window is closed. The compatibility promise above
counts from `1.28.1`, and `1.28.0` is unlisted for it.

The version is `<gstreamer-major>.<gstreamer-minor>.<binding-patch>`:

* **major and minor track the GStreamer series** the bindings are generated
  from. `1.28.x` of this package is generated from GStreamer 1.28, and a move
  to GStreamer 1.30 makes the package `1.30.0`.
* **patch is the binding's own counter.** It has nothing to do with the
  GStreamer patch release; `1.28.3` does not imply GStreamer 1.28.3, and the
  runtime floor stays 1.24 regardless.

## Extending the binding

**A hand-written binding module can live outside this repository.** An assembly
that references the `GstSharp.Net` package can register its own native library
with the loader, hand its `GType`-to-wrapper table to `TypeRegistry`, and derive
its wrappers through the `protected` constructors of `Gst.GObject.Object`,
`Gst.GObject.InitiallyUnowned`, `Gst.MiniObject` and `Gst.GObject.Boxed` — with
no `InternalsVisibleTo` from here. `GstSharp.Net.Controller` is that module
written out: it binds all of `libgstcontroller-1.0`, it ships, and nothing
grants it the internals of anything.

**It attaches to the generated hierarchy too.** Every generated wrapper class —
`Gst.Element`, `Gst.Object`, `Gst.ControlSource` and the rest — carries the same
`protected` constructor, so a module's classes derive from the wrapper of the
nearest native ancestor and are shaped like the C types they stand for. That
means the members of the generated ancestors are inherited and that generated
API taking one of them takes your wrapper:
`Gst.Controller.InterpolationControlSource` really is a `Gst.ControlSource`, and
`GES.TrackElement.SetControlSource` accepts it, across three assemblies and
still with no grant of internals anywhere.

**Generator-backed modules are not supported.** The generator is a tool of this
repository, not a product, and the code it emits uses internals; a module is
written by hand.

**[`docs/modules.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md)**
is the guide: the three registration calls, the obligations that come with each
wrapper base, how to attach to the generated hierarchy and what is still closed
— everything about a generated class except its constructor — and the worked
example, file by file.

## Documentation

The API reference and these guides are published as a site at
<https://masa-iwm.github.io/GstSharp.Net/>, built from `main`.

| Page | Contents |
| --- | --- |
| [`docs/ownership.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md) | Who owns a wrapper, who disposes it, and the GType registry. **Start here.** |
| [`docs/analyzers.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md) | `GST0001` to `GST0004`, what they catch and how to satisfy them. |
| [`docs/gio-async.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/gio-async.md) | How Gio's `*_async` / `*_finish` pairs become `Task`-returning methods. |
| [`docs/subclassing.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/subclassing.md) | Deriving from `Element`, `Bin` and the `GstBase` classes in C#: the guide is §11, the design is the rest. |
| [`docs/modules.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md) | Writing a binding module for a library this repository does not cover, from your own assembly. |
| [`docs/platform-notes.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/platform-notes.md) | Behaviour that is a property of one platform, such as which Windows device providers a `DeviceMonitor` can watch. |
| [`girs/skip-report.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/girs/skip-report.md) | Every gir symbol the generator did not bind, grouped by reason. |
| [`eng/ci-notes.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/eng/ci-notes.md) | Why the workflows look the way they do, and how to run each gate by hand. |
| [`CONTRIBUTING.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/CONTRIBUTING.md) | Build, test, regenerate, and the quality gates a change has to pass. |

## Repository layout

| Path | Contents |
| --- | --- |
| `girs/` | Vendored `.gir` inputs and overlay files. See `girs/README.md`. |
| `generator/` | The `.gir` to C# generator (console application, no NuGet dependencies). |
| `src/` | Shipping libraries: the bindings, the hand-written runtime under `src/GstSharp.Net/Core/`, and the Roslyn analyzers. |
| `samples/` | Runnable samples, including the NativeAOT smoke test and, under `samples/tutorials/`, the official GStreamer tutorials ported onto the binding. |
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
