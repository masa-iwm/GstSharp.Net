# The samples

`samples/` holds sixteen runnable programs, plus the ported GStreamer
tutorials under `samples/tutorials/`. They are written to be run unattended:
headless by default, or made so by a sink option, on media they make
themselves wherever the program needs none from outside, and bounded — by a
`--timeout`, a buffer count or a duration — wherever a run would otherwise
wait for a person. That is also what lets CI
use them as gates: every leg builds all of them, since they are in the
solution, and the Linux, macOS and Windows MinGW legs run most of them as well,
each with the plugin set that leg has.

Run one with the project path:

```sh
dotnet run --project samples/PlaybinPlayer
dotnet run --project samples/AppSinkSpans -- --mode pull
```

Three options recur. `--native-path <directory>` and `--flavor msvc|mingw`
point the loader at a particular GStreamer installation instead of letting it
probe, and `--timeout <seconds>` bounds the run. Not every sample takes all
three, and most of the ports of the C tools add an option or two of their own
so that a path which normally needs a console signal or a person can be run
unattended; the `## Samples` section of the
[README](https://github.com/masa-iwm/GstSharp.Net/blob/main/README.md) is the
short index that says which sample takes which.

Each sample's header comment is the source of truth for what it demonstrates,
where it differs from the C original and what it deliberately leaves out. The
entries below are pointers into those comments, not a replacement for them.

## Playback

**[`samples/PlaybinPlayer`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/PlaybinPlayer/Program.cs)**
— what an application on this binding looks like with nothing else in it: a
pipeline built from a description, run and driven from a polled bus, without a
main loop and without a single signal handler. Given no URI it plays a
generated test pattern into a `fakesink`, so it runs on a headless machine.

**[`samples/GstPlay`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstPlay/Program.cs)**
— the user experience of `gst-play-1.0` on top of `Gst.Play.Play`: the
playlist, the volume, the rate and direction keys and the sink selection, with
the state machine left to the module rather than rebuilt on playbin. The API
bus of `GetMessageBus` is read with a timed pop, the sinks are written to the
playbin `GetPipeline` answers, and headless is the default — nothing reads the
keyboard without `--interactive`. `PlaySignalAdapter` is not used: an
asynchronous adapter only fires while a GLib main loop runs, and a synchronous
one takes the whole bus over.

**[`samples/GstTranscode`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstTranscode/Program.cs)**
— one URI into another against a serialized `GstEncodingProfile`, on the route
the transcoder module documents as the recommended one: `RunAsync` plus a
polled API bus, with no main loop and no signal adapter. Where the `transcode`
plugin of gst-plugins-bad is missing, `GetPipeline` answers null and the sample
reports a missing plugin rather than a transcoding failure.

## The command line tools, ported

Each of these is a port of a GStreamer tool, faithful to what the C tool prints
and to how it behaves; every one names the C source it is the ground truth of
and lists what it cannot match.

**[`samples/GstLaunch`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstLaunch/Program.cs)**
— `gst-launch-1.0`: the whole run state machine — target state, preroll,
buffering, progress, live pipelines, end of stream on shutdown — with the bus
polled by a timed pop and `GstSharp.DrainPendingReleases()` called once per
poll. One cross-platform binary lights up Ctrl+C everywhere, SIGHUP and SIGQUIT
on POSIX and the multimedia timer on Windows. The fault handler is half ported:
a managed SIGSEGV handler is not memory safe and .NET owns that path already.

**[`samples/GstInspect`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstInspect/Program.cs)**
— `gst-inspect-1.0`: the registry census and every section of an element page,
down to the quirks of the C tool, because CI diffs the two outputs against the
same installation and fails on any difference. A caps field that is itself a
caps or a structure is printed as its serialization rather than recursed into,
since `gst_value_get_caps` and `gst_value_get_structure` are not bound.

**[`samples/GstTypefind`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstTypefind/Program.cs)**
— `gst-typefind-1.0`, and the sample that connects a signal **by name**:
`typefind` is a plugin element no `.gir` describes, so `have-type` is reached
through `Object.ConnectSignal`, which looks the signal up in the introspection
GObject itself keeps. The caps arrive as a `Gst.Caps` wrapper built through the
type registry — `Value.GetMiniObject<T>()` is the same route for a value held
directly — and are lent to the handler, so keeping them means `Caps.Copy()`.

**[`samples/GstDiscoverer`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstDiscoverer/Program.cs)**
— `gst-discoverer-1.0`, the synchronous half: `TryDiscoverUri` per URI, the
result and the duration, the topology walk with its container recursion, the
per-stream blocks, and the tags and table of contents under their options.
`-a` is absent because the asynchronous path is what `BasicTutorial09` already
drives end to end.

**[`samples/GstDeviceMonitor`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GstDeviceMonitor/Program.cs)**
— `gst-device-monitor-1.0`: one `DeviceMonitor.AddFilter` per
`DEVICE_CLASSES[:FILTER_CAPS]` argument, the device listing and the `--follow`
hotplug report, all of it read off the monitor's bus with a timed pop. The
launch line under each device is `get_launch_line()` statement by statement,
`Object.ListProperties()` and `Global.ValueCompare` included. The one case out
of reach is the shell quoting of a property value that is not valid UTF-8: a
managed `string` has already decoded it.

## Application integration

**[`samples/AppSinkSpans`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/AppSinkSpans/Program.cs)**
— raw video out of an `appsink`, read through a `Span<byte>` that points
straight at the mapped GStreamer memory with no copy in between. Both ways of
getting a sample out — pull mode and the signal — run the same pipeline and
compute the same checksum over the same frames, so the two totals have to
match.

**[`samples/AppSrcPush`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/AppSrcPush/Program.cs)**
— the source half: the application generates the audio and pushes it into an
`appsrc` in push mode, that is only between `need-data` and `enough-data`, and
counts both signals. It uses the signals rather than the simple callbacks
GStreamer 1.28 added, because the floor of this binding is 1.24. `--output`
turns the run into a byte count gate on top of the exit code.

**[`samples/CustomMeta`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/CustomMeta/Program.cs)**
— a `GstMeta` of the application's own: `Gst.Meta.ApiTypeRegister` for the API
type, `Gst.Meta.Register<T>` for an implementation over an unmanaged payload
plus a transformation delegate, and `Gst.Buffer.AddMeta`, `Gst.Buffer.GetMeta`
and `Gst.Meta.Payload<T>()` on both sides of a real conversion. The header
explains which of the two mechanisms does what: the empty tag list decides that
the item is offered to the new buffer, and the delegate is what puts it there.
`Gst.Interop.ExceptionTrap` is subscribed to, because an exception out of any
of the six delegates is caught at the interop boundary rather than unwound into
native code.

**[`samples/RtpPacketDump`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/RtpPacketDump/Program.cs)**
— `Gst.Rtp.RTPBuffer` over every packet an `rtpL16pay` produced, and then a
compound RTCP packet written through `Gst.Rtp.RTCPBuffer` and
`Gst.Rtp.RTCPPacket` and walked back with `GetFirstPacket` and `MoveToNext`.
Its header states the lifetime rules those mapped structures impose: they are
plain C structures rather than scopes, so each one is mapped once, unmapped
exactly once on every path, and never copied into another stack frame.

## Servers and editing

**[`samples/RtspServer`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/RtspServer/Program.cs)**
— the port of gst-rtsp-server's `examples/test-launch.c`: one mount point built
from a `gst-launch` description, served until it is asked to stop and then shut
down in the order the library documents. `--disable-rtcp` is `SetEnableRtcp`
negated, set before the factory is mounted, because the setting is read when a
media is built. Without a launch line it serves a test tone.

**[`samples/GesCustomSource`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GesCustomSource/Program.cs)**
— a timeline whose clip and whose source are managed types, both through
overrides of the editing services' class struct slots. It is the smallest
application that exercises the child contract of `docs/subclassing.md` §11.
Everything runs on one thread: the editing services assert the thread a
timeline and its tracks were created on.

**[`samples/GesLaunch`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/GesLaunch/Program.cs)**
— `ges-launch-1.0`: the `ges:` description with its escaping, loading and
saving a project, the render branch with its encoding profile, the preview
sinks and the keyboard. It is where the asynchronous half of the editing
services shows: `loaded` is never emitted inside `Asset.Extract<Timeline>()`
but deferred through an idle source, so the sample pumps
`MainContext.Default.Iteration(mayBlock: false)` on the very thread that called
`Extract` and pushes no context of its own. GstValidate — `--set-scenario` and
its neighbours — is not ported, and neither is the per-keyword help synopsis,
which comes from a function the generator cannot bind.

**[`samples/WebRtcLoopback`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/WebRtcLoopback/Program.cs)**
— the port of gst-plugins-bad's `webrtc.c`, audio only and one way: two
`webrtcbin` elements in one pipeline negotiate a call with each other and the
receiving one decodes the stream into an `appsink`. `webrtcbin` has no `.gir`,
so the whole negotiation — `create-offer`, `create-answer`, the two
`set-*-description`, `add-ice-candidate`, `on-negotiation-needed`,
`on-ice-candidate` — is emitted and connected by name, and the offer and the
answer travel as the boxed session description rather than as SDP text, since
both peers are in this process. It is also the sample with the most threads in
it: each `webrtcbin` dispatches its callbacks on a private thread of its own,
`pad-added` and `new-sample` arrive on streaming threads, and the thread that
started the run does nothing but poll the bus — which is why it needs no main
loop and why a handler that throws leaves its message in a field instead.

## NativeAOT

**[`samples/AotSmoke`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/AotSmoke/Program.cs)**
— the NativeAOT gate: initialise, ask GStreamer for an element, release it
again, and run the managed subclasses of the sample: an element, a source and
sink pair, an audio sink, a video sink, an audio encoder and an element an
element factory made. Everything it touches has to survive trimming and ahead
of time compilation, which is what the gate publishes:

```sh
dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true
```

CI runs it through `eng/aot-gate.ps1`, which publishes with
`-p:PublishAot=true -p:TrimMode=full`, asserts that not one trimming or AOT
warning was printed, and then runs the published executable — a binary with no
host and no `deps.json` beside it — and fails on a non-zero exit code.

## Tutorials

`samples/tutorials/` holds the official GStreamer basic tutorials ported onto
this binding, one runnable project per tutorial, with the upstream numbering
kept. The prose stays upstream: each project links its page instead of
reproducing the walkthrough, and what the files add is a header comment saying
where the port differs from the C original and why.
[`samples/tutorials/README.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/samples/tutorials/README.md)
lists the ported ones, says which of the remaining basic and playback tutorials
are only missing and which cannot be ported at all, and documents the options
the ports add so that a tutorial can be run unattended.
