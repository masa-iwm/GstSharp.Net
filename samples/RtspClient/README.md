# RtspClient

The client half of [`samples/RtspServer`](../RtspServer): `rtspsrc` plays one
mount point, the L16 audio it receives is depayloaded into an `appsink` that
counts what arrives, and the run ends when enough buffers are in. There is no
upstream C example that just plays a URL through `rtspsrc`, so this is the
element's own documented pipeline written out: `select-stream` decides which of
the announced streams is set up — a signal with a *return value*, connected by
name through `ConnectSignal` — and the pad of what it took appears while the
pipeline is already running, so the receiving branch is built and linked in
`pad-added`.

```sh
dotnet run --project samples/RtspClient
dotnet run --project samples/RtspClient -- rtsp://127.0.0.1:8554/test --buffers 30
dotnet run --project samples/RtspClient -- --tcp --buffers 20
```

Without a URL the sample is self-contained: it hosts the same mount
`samples/RtspServer` serves by default — one shared factory with
`( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )` at `/test`, on a
port the operating system picks — and plays that. It is that server sample in
miniature, down to the four shutdown steps, which are documented there.

## Pairing it with RtspServer

In one terminal, `dotnet run --project samples/RtspServer -- --port 8554`; in
another, `dotnet run --project samples/RtspClient -- rtsp://127.0.0.1:8554/test`.
That is the same conversation the loopback mode has with itself, with the two
halves in two processes, and it is the shape a real deployment has.

With the default `protocols` the media is tried over UDP first. If none arrives
within the element's own timeout, `rtspsrc` posts a warning and reconnects over
TCP and sets the streams up again — so `select-stream` runs a second time, and
because the reconnect happens before any source pad was added, `pad-added` is
called for the first time only after it. No pad is removed and added back; the
sample links a later pad to the branch that is already there as a guard, not
because it expects one. The sample prints those warnings and goes on;
`--tcp` asks for the interleaved transport from the start and skips the wait.

There is no main loop. `select-stream` is called on the RTSP task of
`rtspsrc`, `pad-added` and `new-sample` arrive on streaming threads, and in
loopback mode the connection of the client is served on a thread of the
server's pool — the thread that started the run only polls the bus, reads the
counter and iterates the context the server accepts on. Everything those
threads share is an `Interlocked` counter or a `volatile` field, and a handler
that throws leaves its message in one of them. That thread split is also what
makes the teardown safe: the pipeline goes to `NULL` on the main thread, which
sends `PAUSE` and then `TEARDOWN` and joins the task of the element, and the
pool thread of the server is what answers it. Only afterwards is the loopback
server taken down.

The run needs `rtspsrc` and `rtpL16depay` from gst-plugins-good and
`audioconvert` and `appsink` from gst-plugins-base, plus `audiotestsrc` and
`rtpL16pay` in loopback mode; all of them are checked before anything is built,
and a missing one is a named message and exit 1. The three CI legs that run the
samples install both plugin sets and run the loopback mode.

| Argument | Default | What it is |
| --- | --- | --- |
| `<rtsp url>` | none | The mount point to play. Without one the sample hosts the mount itself and plays that. |
| `--buffers` | `100` | How many received buffers have to arrive at the sink before the run is a success. |
| `--timeout` | `30` | How many seconds the run may take. At least one; the deadline is what ends a run that never receives. |
| `--latency` | `200` | The `latency` of `rtspsrc` in milliseconds. The element's own default is 2000; zero is allowed and means no jitter buffer delay. |
| `--tcp` | off | Sets `protocols` to TCP only, so the media is interleaved on the RTSP connection instead of being tried over UDP first. |
| `--native-path`, `--flavor` | | Where to look for the native GStreamer, as in the other samples. |
