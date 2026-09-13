# The GStreamer tutorials, ported

The [official GStreamer tutorials](https://gstreamer.freedesktop.org/documentation/tutorials/)
are how most people meet GStreamer. These are the same programs written against
this binding, one runnable project per tutorial, with the upstream numbering
kept so that a page and a project are easy to line up.

**The prose stays upstream.** Each project links its page and does not reproduce
the walkthrough; what the files add is a header comment saying where the port
differs from the C original and why, and comments at the places where the
binding's rules — ownership, disposal, the type registry, no main loop — replace
a C idiom. Read the page for what the program does and the file for how it is
said here.

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
| `BasicTutorial12` | [Streaming](https://gstreamer.freedesktop.org/documentation/tutorials/basic/streaming.html) | buffering, a live source, a lost clock |
| `BasicTutorial13` | [Playback speed](https://gstreamer.freedesktop.org/documentation/tutorials/basic/playback-speed.html) | seek events with a rate, reverse playback, step events |

The playback tutorials are a second series, and the five of them that can run
without a window are here as well:

| Project | Upstream page | What it teaches |
| --- | --- | --- |
| `PlaybackTutorial01` | [Playbin usage](https://gstreamer.freedesktop.org/documentation/tutorials/playback/playbin-usage.html) | `playbin`, its flags, its stream counts, its tag signals |
| `PlaybackTutorial02` | [Subtitle management](https://gstreamer.freedesktop.org/documentation/tutorials/playback/subtitle-management.html) | `suburi`, the text flag, choosing a subtitle stream |
| `PlaybackTutorial04` | [Progressive streaming](https://gstreamer.freedesktop.org/documentation/tutorials/playback/progressive-streaming.html) | the download flag, buffering ranges, `deep-notify` |
| `PlaybackTutorial06` | [Audio visualization](https://gstreamer.freedesktop.org/documentation/tutorials/playback/audio-visualization.html) | a registry feature filter, factory metadata, `vis-plugin` |
| `PlaybackTutorial08` | [Hardware-accelerated video decoding](https://gstreamer.freedesktop.org/documentation/tutorials/playback/hardware-accelerated-video-decoding.html) | plugin feature ranks, the decoder list, walking a bin |

## What is not ported, and why

Of the basic tutorials, 5 needs a window and a widget toolkit, 10, 11 and 14
have no code upstream, and 15 is Clutter, which is gone. Every basic tutorial
that has code and does not need a window is here.

Four of the nine playback tutorials are still missing, each for a reason of its
own:

| Tutorial | What it is about | Why it is not here |
| --- | --- | --- |
| [Playback 3](https://gstreamer.freedesktop.org/documentation/tutorials/playback/short-cutting-the-pipeline.html) | Short-cutting the pipeline | not ported yet; it is an `appsrc` inside a playbin |
| [Playback 5](https://gstreamer.freedesktop.org/documentation/tutorials/playback/color-balance.html) | Color Balance | needs a real video sink, so it cannot run headless in this tree |
| [Playback 7](https://gstreamer.freedesktop.org/documentation/tutorials/playback/custom-playbin-sinks.html) | Custom playbin sinks | needs a real video sink, so it cannot run headless in this tree |
| [Playback 9](https://gstreamer.freedesktop.org/documentation/tutorials/playback/digital-audio-pass-through.html) | Digital audio pass-through | no example code upstream, and it needs pass-through hardware |

## Running one

```sh
dotnet run --project samples/tutorials/BasicTutorial02
dotnet run --project samples/tutorials/BasicTutorial03 -- <file-or-uri>
```

Every project takes `--native-path <directory>` and `--flavor msvc|mingw`, which
point the loader at a particular GStreamer installation, and `--timeout
<seconds>`, which bounds the run. The ones that are given media — basic 1, 3, 4,
9, 12 and 13, and playback 1, 2, 4 and 6 — take a URI or the path of a local
file as a positional argument and default to whatever the upstream page uses, so
a manual run with no arguments reproduces the tutorial exactly. **Those defaults
need a network.** Basic 9 only asks what is inside its file; the rest play it.
`PlaybackTutorial02` takes a second positional argument, the subtitle file, and
`PlaybackTutorial08` takes its media last and optionally: with no media it only
prints the decoder ranking.

`PlaybackTutorial06` keeps the upstream default of
`http://radio.hbr1.com:19800/ambient.ogg`, a radio station that stopped
answering years ago, so that one is worth pointing at a local audio-only file.

`PlaybackTutorial01 --keys` and `PlaybackTutorial02 --keys` script the keyboard
the same way `BasicTutorial13 --keys` does, and `PlaybackTutorial08 --enable
<factory>` / `--disable <factory>` change the rank of a factory before the list
is printed.

`BasicTutorial13` is worth pointing at a local file even when there is one. A
flushing rate seek travels back to the source, and against `souphttpsrc` the
first one observed here ended the stream instead of changing its speed; the
same file downloaded and played from disk runs the whole sequence. That is a
property of seeking an HTTP stream rather than of the port — the C original
sends the identical event to the identical element — but it makes the default
run of that one tutorial look broken.

## Options the tutorials do not have

Two options exist so that a tutorial can be run unattended, in the same spirit
as `GstLaunch --interrupt-after`. They are sample scaffolding, not part of what
the tutorial teaches, and each file says so where it uses them.

* `--headless` replaces the automatic sinks with `fakesink`. An audio sink is
  the worst thing to leave in an unattended run: in an environment with no sound
  daemon it does not fail, it waits. Where the tutorial's source has no end of
  its own, `--headless` also bounds it so that the run finishes.
  `BasicTutorial09` is the one project without it, because it builds no sink at
  all — a discoverer is the whole program.
* `--keys <string>` feeds those characters to the handler the keyboard would
  feed, one every half second from the moment the pipeline reports PLAYING, so
  the tutorial can be driven without a terminal. `BasicTutorial13` uses it for
  the rate and step events; `PlaybackTutorial01` and `PlaybackTutorial02` use it
  to choose an audio and a subtitle stream, where a digit is the index the C
  original reads with `strtoull` and `q` quits. Those two read one character
  rather than a line, so an index of more than one digit cannot be typed.
* `PlaybackTutorial08 --enable <factory>` and `--disable <factory>` apply the
  page's `enable_factory` snippet before anything else runs, as many times as
  they are given and in the order they are written. A name the registry does not
  have exits 1 rather than returning silently as the snippet does.
* `BasicTutorial04 --seek-at` / `--seek-to` move
  the two thresholds of the C original, so that a short local file can be used
  instead of the 52 second trailer. `BasicTutorial07 --buffers` and
  `BasicTutorial08 --chunks` say how many buffers to produce, or to push,
  before ending the stream; giving either one bounds the run on its own, so a
  run that does open its windows can be bounded too.

## The two `#ifdef`s of the C originals

The upstream sources carry exactly two pieces of per-operating-system code, and
neither survives the port:

* Every one of them wraps its `tutorial_main` in `gst_macos_main` under
  `__APPLE__`. That call runs the program on a thread of its own while the main
  thread runs a Cocoa run loop, which is what a video window on macOS needs.
  `gst_macos_main` is in none of the `.gir` files, but it is bound by hand as
  `Gst.Global.MacosMain`. Nothing here uses it — `--headless` opens no window,
  and CI runs these on Linux — so a **manual run with a video window on macOS**
  is the one case where the C original still does something these ports do not:
  expect `autovideosink` not to come up there unless you wrap the run in
  `Gst.Global.MacosMain` yourself.
* `basic-tutorial-13.c`, `playback-tutorial-1.c` and `playback-tutorial-2.c`
  read the keyboard through `g_io_channel_win32_new_fd` on Windows and
  `g_io_channel_unix_new` everywhere else. All three ports use `System.Console`
  and are one program on every operating system, which is how the last
  `#ifdef` of the series disappears.

## Exit codes

Every project follows one rule, which is what lets CI run them as gates:

* **0** — the stream ended, or the run was quit as asked, or the bound elapsed
  on a pipeline that was deliberately endless.
* **1** — an error message was posted, an element was missing, or the bound
  elapsed on a pipeline that was supposed to end. `BasicTutorial09`, which has
  no bus at all, reads it as a discovery that came back with anything other than
  OK: the C original prints "This URI cannot be played" and returns 0, which
  would leave the CI line nothing to gate on. `PlaybackTutorial08` reads it the
  same way for a factory that `--enable` or `--disable` named and the registry
  does not have: the C snippet returns silently, which a typo would be
  indistinguishable from.

`PlaybackTutorial06` is the one place where the bound is a success. Its upstream
default is a radio stream, and a stream with no end is meant to be stopped from
outside: the bound counts as 0 when the pipeline came up live or the URI is
http, and as 1 when a local file was supposed to reach its own end and did
not.

## Which of them CI runs

All fifteen are built on every CI leg, which is the point of putting them in
the solution: a rename anywhere in the generated surface breaks a tutorial
visibly.

Thirteen are also *run*, on both Linux legs — x64 and arm64 — because those
have the richest plugin set and no GUI. The `GstLaunch` sample encodes the
fixtures in the same step, so the media is made on the spot rather than
fetched:

* `tutorial-media.ogg`, ten seconds of Theora and Vorbis in an Ogg container,
  which is what basic 3 needs to have a pad to ignore, basic 4 and 13 need to
  have something to seek in, and basic 9 needs to have a topology worth walking.
  Playback 8 plays it too, and names the decoder that got plugged.
* `tutorial-multitrack.ogg`, the same but with **two** Vorbis streams, so that
  playback 1 and 2 have something to choose between. Each audio branch carries
  its own `num-buffers`, because a branch that never ends would keep the muxer
  from finalising the file.
* `tutorial-audio.ogg`, Vorbis and nothing else, because playsink only builds a
  visualization chain when the media has no video stream — that file is what
  makes playback 6 exercise the chain rather than only look one up.
* `tutorial-subs.srt`, two cues written with `printf`, for playback 2.

Basic 12 and playback 4 are not given a file at all. A `file://` URI never
buffers and never downloads, so both would run straight past the paths they
exist to show; the step starts `eng/ci/range-http-server.py` on the loopback
and points those two at it, which makes the BUFFERING messages and the
`temp-location` deep notification real. That script exists because Python's
stock `http.server` ignores `Range` and always answers with the whole body,
which makes souphttpsrc — seekable by its own account, and asked to seek to the
end by oggdemux — fail the run at exactly 100% buffering. The server is killed when the step
ends, whether or not a gate failed.

Basic 1 is the one that is only built: its default media is an https URI and
nothing local would be the tutorial. Playback 3, 5, 7 and 9 are not ported.

`wavescope`, which both tee tutorials draw with, is in `gst-plugins-bad`. The
Linux leg installs `gstreamer1.0-plugins-bad` — it needs it for `webrtcbin` —
so tutorials 7 and 8 do build their visualization branch there and run every
branch of the tee. Where the plugin is absent both of them look it up, leave
that branch out, say so on a log line and exit 0 — but **no CI leg exercises
that fallback**, so a manual run on an installation without the bad plugins is
what checks it.

## Licence and provenance

The upstream tutorial *code* is tri-licensed BSD-2-Clause / MIT / LGPL-2.1+ at
the user's choice, which is compatible with this repository. Each file names the
C source it was ported from. The upstream *prose* is CC-BY-SA and is therefore
linked rather than copied: every comment in these files is written for this
port.
