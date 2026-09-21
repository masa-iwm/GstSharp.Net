# GesCustomSource

A timeline made of managed types: `CustomSourceClip`, a `GES.SourceClip` whose
`OnCreateTrackElement` answers a child per track type, `CustomVideoSource`, a
`GES.VideoSource` whose `OnCreateSource` answers a `videotestsrc`, and
`CustomAudioSource`, a `GES.AudioSource` whose `OnCreateSource` answers an
`audiotestsrc`. All of them are built by the editing services rather than by
C#, which is the child contract `docs/subclassing.md` §11 describes: a child
has to be extracted from an asset for its own `GType`
(`GES.Asset.Request(...)!.Extract<T>()`), because a child built with `new` has
no asset, never gets an `nleobject` and is removed from the clip again. The
sample builds a timeline with one audio and one video track, one layer and one
half second clip, plays it through a `GES.Pipeline` whose two preview sinks are
a `fakesink` each, and waits on the bus for the end of stream — so it is
headless and bounded. What it needs installed is the `nle` and `ges` plugins
the editing services are themselves built on, and the elements a video and an
audio source bin are made of: `videotestsrc`, `compositor`,
`videoconvertscale` and `videorate` from the base plugins, `capsfilter` from
the core elements, and `videoflip`, `videocrop` and `deinterlace` from the good
ones for the video side, and `audiotestsrc`, `audioconvert`, `audioresample`,
`volume` and `audiomixer` from the base plugins for the audio one. It exits 0
when every step below held and the run reached the end of stream, and 1 on an
error or on the timeout.

It prints four numbered steps.

1. **The clip builds its own children.** How many children the clip was given,
   the element each source built, and whether the two children are the very
   wrappers the overrides answered — the interning is what makes them the same
   objects rather than second wrappers of the same instances.
2. **The audio source watches its child properties.** The properties of the
   element a source is made of are not child properties by themselves
   (`ges_audio_source_create_element` registers the volume and the converter of
   the bin it builds and nothing of the sub element), so `OnCreateSource`
   registers `freq` with `AddChildrenProps`, the way `GESAudioTestSource`
   registers its own. The source then declares
   `TimelineElement.SetChildPropertyFullOverride`, which takes over every child
   property write of the element: `OnSetChildPropertyFull` lets a tone below its
   ceiling through by chaining up — which is what keeps the `set_child_property`
   slot reachable at all — and refuses one above it with a `GException` that
   carries a domain, a code and a reason. `SetChildPropertyFull` raises that
   reason; the plain `SetChildProperty`, which gives the slot no room for one,
   raises an `InvalidOperationException` that says the property exists and the
   write was refused, and the tone the source kept is the accepted one.
3. **The timeline renders audio and video to fakesinks.**
4. **The clip answers its own split.** `Container.UngroupOverride` is declared
   against the clip, because `GES.Container` itself cannot be subclassed from
   managed code; `OnUngroup` records what it was asked and what the
   implementation below it — `GESClip::ungroup`, which a chain-up always reaches
   — answered, and hands that list on. The list belongs to the caller: the clip
   is in it and is disposed by the `using` that made it, and the one new clip
   beside it is disposed by the sample — which releases the reference of that
   caller, the layer keeping its own, but also retires the managed side of that
   clip: a disposed wrapper chains up for ever, which costs nothing here
   because the run is over, while an editing application keeps the wrapper for
   as long as it wants its overrides to answer. GStreamer 1.28 reads the
   `recursive` flag nowhere, which is why the override only reports it.

```sh
dotnet run --project samples/GesCustomSource
dotnet run --project samples/GesCustomSource -- --timeout 20
```

| Argument | Default | What it is |
| --- | --- | --- |
| `--timeout` | `10` | How many seconds to wait for the end of stream before giving up with exit code 1. |
| `--native-path`, `--flavor` | | Where to look for the native GStreamer, as in the other samples. |

The child properties are written once the clip is in a layer, which is when its
track elements — and with them the elements that carry the properties — exist,
and the split comes after the run, when the pipeline is back at `NULL`.

Everything runs on the main thread. The editing services assert the thread a
timeline and its tracks were created on, so there is no `async` and no
`Task.Run` here: moving any of this off the thread that built the timeline
aborts the process rather than failing. `OnCreateSource` also never answers
`null` — a null answer, which the binding has to substitute an `identity` (a
core element as well) for,
would otherwise leave the track element holding an nleobject the composition
frees under it (`ges-track-element.c:1022`, `1066-1070`).
