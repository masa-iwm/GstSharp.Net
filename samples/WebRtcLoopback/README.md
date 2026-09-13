# WebRtcLoopback

The port of gst-plugins-bad's `tests/examples/webrtc/webrtc.c`, one way and
audio only: two `webrtcbin` elements in a single pipeline, called `send` and
`recv`, negotiate a call with each other and the receiving one plays the stream
back into an `appsink` that counts what arrives. `webrtcbin` has no `.gir`, so
everything the negotiation needs — `create-offer`, `create-answer`,
`set-local-description`, `set-remote-description`, `add-ice-candidate`,
`on-negotiation-needed`, `on-ice-candidate` — goes through `EmitSignal` and
`ConnectSignal` by name, and the offer and the answer travel as the boxed
`WebRTCSessionDescription` they are.

```sh
dotnet run --project samples/WebRtcLoopback
dotnet run --project samples/WebRtcLoopback -- --buffers 50 --timeout 60 --print-sdp
dotnet run --project samples/WebRtcLoopback -- --stun-server stun://stun.l.google.com:19302
```

Both peers live in this process, so the sample has no signalling channel at
all: the offer, the answer and every ICE candidate are handed straight to the
other element. A real application serialises `GetSdp().AsText()` and the
candidate string over a channel of its own and calls the same signals on the
other side.

There is no main loop. Each `webrtcbin` runs a private thread the negotiation
and ICE callbacks are dispatched on, `pad-added` and `new-sample` arrive on
streaming threads, and the thread that started the run only polls the bus, so
the polled-bus pattern of the other samples is enough here. Everything those
threads share is an `Interlocked` counter or a `volatile` field, and a handler
that throws leaves its message in one of them, because an exception on one of
those threads is the one failure the bus cannot report.

The run needs `webrtcbin`, `nicesrc` and `dtlssrtpenc` from gst-plugins-bad
plus the Opus elements of the base and good plugin sets; all of them are
checked before anything is built, and a missing one is a named message and exit
1. In CI that is only true of the Linux job, which is the one that installs the
webrtc and nice plugins.

| Argument | Default | What it is |
| --- | --- | --- |
| `--buffers` | `100` | How many decoded buffers have to arrive at the sink before the run is a success. |
| `--timeout` | `30` | How many seconds the run may take. At least one; the deadline is what ends a call that never pairs. |
| `--stun-server` | none | The `stun-server` property of both peers. Host candidates are enough for a loopback, so the default asks no server anything. |
| `--print-sdp` | off | Prints the offer and the answer as their SDP text, which is what a real application would put on its signalling channel. |
| `--native-path`, `--flavor` | | Where to look for the native GStreamer, as in the other samples. |
