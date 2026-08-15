# Reference gir files

`reference/` holds the GObject-introspection XML that the generator consumes.
Nothing in this directory is edited by hand: to change generated output, either
refresh these files from their upstream source, add an entry to
`overlays/fixups.json` / `overlays/platform-symbols.json`, or change the
generator.

## Provenance

| File | Source |
| --- | --- |
| `Gst-1.0.gir`, `GstBase-1.0.gir`, `GstApp-1.0.gir`, `GstVideo-1.0.gir`, `GstAudio-1.0.gir`, `GstPbutils-1.0.gir` | GStreamer monorepo, `girs/` directory, commit `2d3e05cbdad68e47d645f548899b432dc9fb4473` ("Release 1.28.6", 2026-08-05). Linux flavor. |
| `GLib-2.0.gir`, `GObject-2.0.gir`, `GModule-2.0.gir` | GStreamer 1.28.6 MSVC installer (`share/gir-1.0`). Used only for cross-namespace type resolution; the GLib/GObject runtime layer is hand-written in `GstSharp.Net.Core` and is never generated from these files. |

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
for m in Gst-1.0 GstBase-1.0 GstApp-1.0 GstVideo-1.0 GstAudio-1.0 GstPbutils-1.0; do
    git -C "$GST" show "$REV:girs/$m.gir" > girs/reference/"$m".gir
done
```

The GLib stack is copied from an installed GStreamer 1.28.6 (MSVC flavor):

```sh
cp "$GST"/girs/{GLib-2.0.gir,GObject-2.0.gir,GModule-2.0.gir} girs/reference/
```

After refreshing, update the commit hash in the table above, run
`dotnet run --project generator/GstSharp.Generator -- generate --gir-dir girs --out-dir src`
and commit the regenerated sources together with the gir change.

## Sanity checks

* The GStreamer girs start with the `<!-- This file was automatically generated
  from C sources -->` comment; unlike the GLib stack they carry no `<?xml ... ?>`
  declaration.
* `Gst-1.0.gir` declares `VERSION_MAJOR=1`, `VERSION_MINOR=28`, `VERSION_MICRO=6`.
* Each GStreamer gir carries the Linux `shared-library` attribute, for example
  `shared-library="libgstreamer-1.0.so.0"`.
* All files use LF line endings (`.gitattributes` enforces `*.gir text eol=lf`).

## Platform differences

The reference girs describe a single platform, which is sufficient because the
measured differences between flavors are tiny:

* **Linux vs. Windows/MinGW**: only the `shared-library` attribute differs. The
  runtime library name is resolved by `NativeLoader` in `GstSharp.Net.Core`, so
  the attribute is ignored by the generator.
* **Windows MSVC vs. MinGW**: only the library file naming differs
  (`gstreamer-1.0-0.dll` vs. `libgstreamer-1.0-0.dll`); the exported signatures
  are identical. This is likewise handled by `NativeLoader`.
* **macOS**: adds the `gst_macos_main` / `gst_macos_main_simple` family plus two
  callback types. These are hand-bound in `GstSharp.Net.Core` behind
  `[SupportedOSPlatform("macos")]` instead of being generated.

Symbols that genuinely exist on only some platforms are listed in
`overlays/platform-symbols.json`.

## License

These files are generated from, and embed documentation text of, the GStreamer
and GLib libraries, which are licensed under the GNU Lesser General Public
License version 2.1 or later. The generated bindings therefore inherit
LGPL-2.1-or-later; see the repository `LICENSE`.
