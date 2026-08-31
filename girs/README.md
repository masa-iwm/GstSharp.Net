# Reference gir files

`reference/` holds the GObject-introspection XML that the generator consumes.
Nothing in this directory is edited by hand: to change generated output, either
refresh these files from their upstream source, add an entry to
`overlays/fixups.json` / `overlays/platform-symbols.json`, or change the
generator.

## Provenance

| File | Source |
| --- | --- |
| `Gst-1.0.gir`, `GstBase-1.0.gir`, `GstApp-1.0.gir`, `GstVideo-1.0.gir`, `GstAudio-1.0.gir`, `GstPbutils-1.0.gir`, `GstNet-1.0.gir`, `GstSdp-1.0.gir`, `GstRtsp-1.0.gir`, `GstWebRTC-1.0.gir`, `GstAllocators-1.0.gir`, `GstTag-1.0.gir`, `GstTranscoder-1.0.gir`, `GES-1.0.gir` | GStreamer monorepo, `girs/` directory, commit `2d3e05cbdad68e47d645f548899b432dc9fb4473` ("Release 1.28.6", 2026-08-05). Linux flavor. |
| `GLib-2.0.gir`, `GObject-2.0.gir`, `GModule-2.0.gir` | GStreamer 1.28.6 MSVC installer (`share/gir-1.0`). Used only for cross-namespace type resolution; the GLib/GObject runtime layer is hand-written in `src/GstSharp.Net/Core/` and is never generated from these files. |
| `Gio-2.0.gir` | GStreamer 1.28.6 MSVC installer (`gstreamer-1.0-msvc-x86_64-1.28.6.exe`, `share/gir-1.0`), unpacked with `innoextract` without running the installer, then normalized from CRLF to LF. Joins the GLib stack: cross-namespace type resolution only, never generated. |

The GStreamer girs are the canonical API source. The Linux flavor is used
because it is the one tracked in the monorepo, so it can be refreshed
reproducibly from git.

## Refreshing

The GStreamer girs come straight out of the monorepo, so pick the release
commit and extract each blob (run from a Bash shell; `>` redirection in
PowerShell would re-encode the files):

```sh
GST=/path/to/gstreamer            # monorepo checkout
REV=2d3e05cbdad68e47d645f548899b432dc9fb4473
for m in Gst-1.0 GstBase-1.0 GstApp-1.0 GstVideo-1.0 GstAudio-1.0 GstPbutils-1.0 \
         GstNet-1.0 GstSdp-1.0 GstRtsp-1.0 GstWebRTC-1.0 GstAllocators-1.0 \
         GstTag-1.0 GstTranscoder-1.0 GES-1.0; do
    git -C "$GST" show "$REV:girs/$m.gir" > girs/reference/"$m".gir
done
```

The GLib stack — `GLib-2.0.gir`, `GObject-2.0.gir`, `GModule-2.0.gir` and
`Gio-2.0.gir` — is *not* in the monorepo `girs/` directory. It ships in the
`share/gir-1.0` payload of the Windows MSVC installer, which `innoextract`
unpacks without running the installer:

```sh
INSTALLER=/path/to/gstreamer-1.0-msvc-x86_64-<version>.exe
OUT=/path/to/scratch                # anywhere outside the repository
innoextract --include GLib-2.0.gir --include GObject-2.0.gir \
    --include GModule-2.0.gir --include Gio-2.0.gir \
    --output-dir "$OUT" "$INSTALLER"
```

A single invocation matters: every run decompresses the installer's whole
solid stream, so one pass with four `--include` filters is four times faster
than four passes. Note that `--include` matches bare file names; full
`app/share/...` paths match nothing. A name filter also cannot end the run
early — after the last match the tool keeps scanning the rest of the stream
for further files of the same names, which takes far longer than the
extraction itself. The gir files appear within the first minutes, so it is
safe to abort the run once all four are on disk.

Those files use CRLF, so normalize them while copying them in:

```sh
for m in GLib-2.0 GObject-2.0 GModule-2.0 Gio-2.0; do
    tr -d '\r' < "$OUT"/app/share/gir-1.0/"$m".gir > girs/reference/"$m".gir
done
```

After refreshing, update the commit hash in the table above, run
`dotnet run --project generator/GstSharp.Generator -- generate --gir-dir girs --out-dir src`
and commit the regenerated sources together with the gir change.

## Sanity checks

* Every one of the fourteen GStreamer girs starts with the `<!-- This file was
  automatically generated from C sources -->` comment and carries no
  `<?xml ... ?>` declaration.
* The four GLib stack girs, `Gio-2.0.gir` included, do carry the `<?xml ... ?>`
  declaration. That is the one structural difference between the two families.
* `Gst-1.0.gir` declares `VERSION_MAJOR=1`, `VERSION_MINOR=28`, `VERSION_MICRO=6`.
* Each GStreamer gir carries the Linux `shared-library` attribute, for example
  `shared-library="libgstreamer-1.0.so.0"`. `Gio-2.0.gir` carries the Windows
  MSVC name, `shared-library="gio-2.0-0.dll"`, because it comes from the MSVC
  installer; the attribute is ignored by the generator either way, exactly as it
  is for the other girs.
* No file has a byte order mark, and all of them use LF line endings
  (`.gitattributes` enforces `*.gir text eol=lf`).

## Platform differences

The reference girs describe a single platform, which is sufficient because the
measured differences between flavors are tiny:

* **Linux vs. Windows/MinGW**: only the `shared-library` attribute differs. The
  runtime library name is resolved by `NativeLoader` in `src/GstSharp.Net/Core/`,
  so the attribute is ignored by the generator.
* **Windows MSVC vs. MinGW**: only the library file naming differs
  (`gstreamer-1.0-0.dll` vs. `libgstreamer-1.0-0.dll`); the exported signatures
  are identical. This is likewise handled by `NativeLoader`.
* **macOS**: adds the `gst_macos_main` / `gst_macos_main_simple` family, which
  runs the program on a thread of its own while the main thread runs a Cocoa
  run loop, plus the two callback types those functions take. None of them is
  in the `.gir` files, and upstream compiles `gstmacos.m` only where the host
  system is Darwin and the subsystem is macOS, so no other build carries the
  symbols at all — not even as a pass-through. The family is therefore bound by
  hand in `src/GstSharp.Net/Custom/Global.Macos.cs`, as `Gst.Global.MacosMain`
  and `Gst.Global.MacosMainSimple` with the `Gst.MainFunc` and
  `Gst.MainFuncSimple` delegates. Both are marked
  `[SupportedOSPlatform("macos")]`, so a caller guards them with
  `OperatingSystem.IsMacOS()` the way the C tools guard their own `main` with an
  `#ifdef`; calling them anywhere else throws `EntryPointNotFoundException`.

Symbols that genuinely exist on only some platforms are listed in
`overlays/platform-symbols.json`.

## License

These files are generated from, and embed documentation text of, the GStreamer
and GLib libraries, which are licensed under the GNU Lesser General Public
License version 2.1 or later. The generated bindings therefore inherit
LGPL-2.1-or-later; see the repository `LICENSE`.
