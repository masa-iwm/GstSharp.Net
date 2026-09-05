# GesCustomSource

A timeline made of managed types: `CustomSourceClip`, a `GES.SourceClip` whose
`OnCreateTrackElement` answers a video child, and `CustomVideoSource`, a
`GES.VideoSource` whose `OnCreateSource` answers a `videotestsrc`. Both are
built by the editing services rather than by C#, which is the child contract
`docs/subclassing.md` §11 describes: a child has to be extracted from an asset
for its own `GType` (`GES.Asset.Request(...)!.Extract<T>()`), because a child
built with `new` has no asset, never gets an `nleobject` and is removed from the
clip again. The sample builds a video-only timeline with one layer and one half
second clip, plays it through a `GES.Pipeline` whose preview video sink is a
`fakesink`, and waits on the bus for the end of stream — so it is headless and
bounded. What it needs installed is the `nle` and `ges` plugins the editing
services are themselves built on, and the elements a video source bin is made
of: `videotestsrc`, `compositor`, `videoconvertscale` and `videorate` from the
base plugins, `capsfilter` from the core elements, and `videoflip`,
`videocrop` and `deinterlace` from the good ones. It prints how many children the
clip was given, whether that child is the very wrapper the override answered,
and the element the source built; it exits 0 on the end of stream and 1 on an
error or on the timeout.

```sh
dotnet run --project samples/GesCustomSource
dotnet run --project samples/GesCustomSource -- --timeout 20
```

| Argument | Default | What it is |
| --- | --- | --- |
| `--timeout` | `10` | How many seconds to wait for the end of stream before giving up with exit code 1. |
| `--native-path`, `--flavor` | | Where to look for the native GStreamer, as in the other samples. |

Everything runs on the main thread. The editing services assert the thread a
timeline and its tracks were created on, so there is no `async` and no
`Task.Run` here: moving any of this off the thread that built the timeline
aborts the process rather than failing. `OnCreateSource` also never answers
`null` — a null answer, which the binding has to substitute an `identity` (a
core element as well) for,
would otherwise leave the track element holding an nleobject the composition
frees under it (`ges-track-element.c:1022`, `1066-1070`).
