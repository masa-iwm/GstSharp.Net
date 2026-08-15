# Agent guidelines for GstSharp.Net

GstSharp.Net is a NativeAOT-first .NET 10 binding for GStreamer 1.28. The C#
surface is generated from the `.gir` files in `girs/reference/` by
`generator/GstSharp.Generator`, and the generated sources are committed to the
repository.

## Language

**Everything inside this repository is written in English**: code, comments,
XML documentation, Markdown, and commit messages. This holds regardless of the
language used when talking to the user.

## Generated code

* Never hand-edit anything under `src/*/Generated/`. Those files are
  overwritten by the next generator run and the CI diff gate will fail.
* To change generated output, change one of the inputs instead:
  * `girs/reference/*.gir` (refresh from upstream, see `girs/README.md`),
  * `girs/overlays/fixups.json` (skip / rename / annotation corrections),
  * `girs/overlays/platform-symbols.json` (per-platform availability),
  * the generator itself.
* Hand-written code belongs in `src/<Project>/Custom/` (per-module glue, as
  `partial` extensions of the generated types) or in `GstSharp.Net.Core` (the
  runtime: loader, marshalling, GObject/GLib layer).

## Commands

```sh
# Regenerate the bindings.
dotnet run --project generator/GstSharp.Generator -- generate --gir-dir girs --out-dir src

# Regenerate into a scratch tree and fail when the committed output differs.
dotnet run --project generator/GstSharp.Generator -- verify --gir-dir girs --out-dir src

dotnet build
dotnet test
```

## Quality gates

All of these must pass before a change is committed:

1. `dotnet build` is warning-free. Warnings are errors in this repository, so a
   warning is a build failure by construction; do not silence one with
   `#pragma warning disable` or `NoWarn` without a comment explaining why.
2. Running the generator twice produces byte-identical output (deterministic
   ordering, LF line endings).
3. Census tests still pass: the generator's fixed counts of emitted classes,
   records, enums and bitfields are asserted, so scope creep or accidental
   skipping shows up immediately.
4. ABI probe tests pass (`tests/GstSharp.IntegrationTests`; they need a native
   GStreamer installation). They validate struct sizes and raw field offsets
   against the running library.
5. NativeAOT smoke:
   `dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true`
   completes with zero IL trimming/AOT warnings.

## Commits

Imperative mood, concise, English, one logical change per commit. Commit once
the change has passed review.
