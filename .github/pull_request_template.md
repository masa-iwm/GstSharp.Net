<!-- One logical change per pull request. Link the issue it closes, if there is one. -->

## What this changes

## Checklist

The long form of each gate is in
[`CONTRIBUTING.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/CONTRIBUTING.md).

- [ ] `dotnet build` is warning-free. Warnings are errors here; a `#pragma warning disable`
      or `NoWarn` carries a comment explaining why.
- [ ] The generator agrees with what is committed:
      `dotnet run --project generator/GstSharp.Generator -- verify --gir-dir girs --out-dir src`
      reports no diff.
- [ ] Nothing under `src/*/Generated/` was hand-edited. Generated output changed only by
      changing `girs/reference/`, `girs/overlays/` or the generator itself.
- [ ] Census counts were changed deliberately if the emitted surface moved, and the new
      numbers are accounted for above.
- [ ] Nothing the published baseline shipped was removed or reshaped. `dotnet pack`
      compares every package that has a published baseline against it and names what
      went missing; a deliberate break waits for `1.30`.
- [ ] `dotnet test` passes. The integration suite needs a native GStreamer — say so if you
      could not run it.
- [ ] NativeAOT smoke checked when the runtime or marshalling was touched:
      `dotnet publish samples/AotSmoke -r <rid> -c Release /p:PublishAot=true` with zero
      trimming or AOT warnings (`eng/aot-gate.ps1` runs it the way CI does).
- [ ] English throughout: code, comments, XML documentation, Markdown, commit messages.
- [ ] Commits are in the imperative mood, one logical change.
