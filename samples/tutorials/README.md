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
| `BasicTutorial08` | [Short-cutting the pipeline](https://gstreamer.freedesktop.org/documentation/tutorials/basic/short-cutting-the-pipeline.html) | `appsrc`, `appsink`, a `tee` and its request pads |
| `BasicTutorial13` | [Playback speed](https://gstreamer.freedesktop.org/documentation/tutorials/basic/playback-speed.html) | seek events with a rate, reverse playback, step events |

The rest of the basic and playback tutorials follow later. Basic 5 needs a
window and a widget toolkit, basic 10, 11 and 14 have no code upstream, and
basic 15 is Clutter, which is gone.

## Running one

```sh
dotnet run --project samples/tutorials/BasicTutorial02
dotnet run --project samples/tutorials/BasicTutorial03 -- <file-or-uri>
```

Every project takes `--native-path <directory>` and `--flavor msvc|mingw`, which
point the loader at a particular GStreamer installation, and `--timeout
<seconds>`, which bounds the run. The four that play media — 1, 3, 4 and 13 —
take a URI or the path of a local file as their one positional argument and
default to the same Sintel trailer the upstream pages use, so a manual run with
no arguments reproduces the tutorial exactly. **That default needs a network.**

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
* `BasicTutorial13 --keys <string>` feeds those characters to the handler the
  keyboard would feed, one every half second from the moment the pipeline
  reports PLAYING, so the rate and step events can be exercised without a
  terminal. `BasicTutorial04 --seek-at` / `--seek-to` move
  the two thresholds of the C original, so that a short local file can be used
  instead of the 52 second trailer. `BasicTutorial08 --chunks` says how many
  buffers to push before ending the stream.

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
* `basic-tutorial-13.c` reads the keyboard through `g_io_channel_win32_new_fd`
  on Windows and `g_io_channel_unix_new` everywhere else. `BasicTutorial13` uses
  `System.Console` and is one program on every operating system, which is how
  the last `#ifdef` of the series disappears.

## Exit codes

Every project follows one rule, which is what lets CI run them as gates:

* **0** — the stream ended, or the run was quit as asked, or the bound elapsed
  on a pipeline that was deliberately endless.
* **1** — an error message was posted, an element was missing, or the bound
  elapsed on a pipeline that was supposed to end.

## Which of them CI runs

All seven are built on every CI leg, which is the point of putting them in the
solution: a rename anywhere in the generated surface breaks a tutorial visibly.

Five are also *run*, on the Linux leg only, because it has the richest plugin
set and no GUI. Tutorials 3, 4 and 13 run against ten seconds of Theora and
Vorbis in an Ogg container that the `GstLaunch` sample encodes in the step
before them — real media with two streams, made on the spot rather than
fetched. Tutorial 2 needs no media and tutorial 8 generates its own. The other
two are build-only: 1 would need the network, and 6 is only interesting when a
real audio sink is on the other end of the pipeline.

One element of tutorial 8 is missing there: `wavescope` is in
`gst-plugins-bad`, of which the Linux leg installs only the library package, so
that run exercises two branches of the tee rather than three — and proves the
fallback while it is at it.

## Licence and provenance

The upstream tutorial *code* is tri-licensed BSD-2-Clause / MIT / LGPL-2.1+ at
the user's choice, which is compatible with this repository. Each file names the
C source it was ported from. The upstream *prose* is CC-BY-SA and is therefore
linked rather than copied: every comment in these files is written for this
port.
