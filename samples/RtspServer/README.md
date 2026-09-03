# RtspServer

The port of `gst-rtsp-server`'s `examples/test-launch.c`. It serves one mount
point, `/test`, whose media is built from a `gst-launch` description, and it
demonstrates the shutdown order that `docs/ownership.md` describes under
"RTSP server".

```sh
dotnet run --project samples/RtspServer
dotnet run --project samples/RtspServer -- "( videotestsrc ! x264enc ! rtph264pay name=pay0 pt=96 )"
dotnet run --project samples/RtspServer -- --port 5540 --timeout 30
```

Without a launch line the sample serves `( audiotestsrc ! audioconvert !
rtpL16pay name=pay0 pt=96 )`, a test tone in raw L16, which needs nothing but
the base plugins. Any description works as long as it is parenthesised and
contains elements named `pay0`, `pay1` and so on: each of those becomes one
stream of the media.

| Argument | Default | What it is |
| --- | --- | --- |
| `[launch line]` | the test tone above | The description every media is built from. |
| `--port` | `8554` | The service to listen on. It is a string, so a name from the services database works too, and `0` lets the operating system pick a free port — the URL that is printed always names the port that was bound. |
| `--address` | `0.0.0.0` | The address to listen on, as `test-launch.c` does. The default accepts connections from every interface; pass `127.0.0.1` to serve the loopback only. |
| `--timeout` | `0` | How many seconds to serve. Zero, the default, serves until Ctrl-C. |
| `--native-path`, `--flavor` | | Where to look for the native GStreamer, as in the other samples. |

## Playing it back

The sample prints the URL it serves and it is a normal RTSP URL, so any client
plays it:

```sh
gst-launch-1.0 playbin uri=rtsp://127.0.0.1:8554/test
```

`rtspsrc location=rtsp://127.0.0.1:8554/test latency=0 ! fakesink` is the same
thing without an output device, which is what the tests use.

## Shutting down

Ctrl-C, or the timeout running out, does not end the process on the spot. The
sample stops the server the way a server has to be stopped, and the code says
what each step is for:

1. `Detach(sourceId, context)`, the counterpart of `Attach`, taking the same
   context — `g_source_remove` searches the default context only. The server
   stops accepting.
2. `ClientFilter` answering `Remove` for every client, which closes the
   connections. The close completes later, on the thread of the client.
3. `SessionPool.Filter` answering `Remove`: a closing client does not remove
   its session, and it is the session going away that unprepares the media and
   stops its pipeline.
4. Iterate the context until `ClientFilter(null)` is empty, which is the
   asynchronous half of step 2, with a deadline so that a stuck client is a
   non-zero exit code rather than a hang.

`RTSPThreadPool.Cleanup()` is deliberately not called: it joins every thread of
the process-wide pool and blocks forever if a client is still closing.

The server is attached to a `MainContext` of the sample's own, which the main
thread iterates without blocking. That is the arrangement the other samples
here use — the application owns its thread and no main loop runs behind its
back — and it is what makes the shutdown expressible: `Detach` needs the exact
context `Attach` was given, and step 4 needs a context that is still being
iterated after the server has stopped accepting.

`MediaConfigure` is connected to the factory **before** the factory is mounted,
the way `test-launch.c` connects it. That only holds because
`RTSPMountPoints.AddFactory` is written by hand to leave the factory wrapper
alive; the generated consuming shape would dispose it and disconnect the
handler. The handler runs with the lock of the media held, so it asks the media
nothing — see `docs/ownership.md`.
