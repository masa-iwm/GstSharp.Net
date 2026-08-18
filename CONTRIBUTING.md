# Contributing to GstSharp.Net

Thanks for looking. This page is the short version of how the repository works;
`eng/ci-notes.md` explains the workflows themselves.

Everything inside this repository is written in **English**: code, comments, XML
documentation, Markdown, and commit messages.

## Build and test

```sh
dotnet build
dotnet test
```

`dotnet test` runs all four suites: `GstSharp.Generator.Tests`,
`GstSharp.Analyzers.Tests`, `GstSharp.Core.Tests` and
`GstSharp.IntegrationTests`. The first three are pure. The integration suite
needs a native GStreamer installation that `NativeLoader` can find (see the
installation section of the README), and so do the samples.

## Regenerating the bindings

The C# surface under `src/*/Generated/` is produced from the `.gir` files in
`girs/reference/` and **is committed to the repository**. Never hand-edit it:
the next generator run overwrites it and the CI diff gate fails.

```sh
# Regenerate.
dotnet run --project generator/GstSharp.Generator -- generate --gir-dir girs --out-dir src

# Regenerate into a scratch tree and fail when the committed output differs.
dotnet run --project generator/GstSharp.Generator -- verify --gir-dir girs --out-dir src
```

To change generated output, change one of the inputs instead:

* `girs/reference/*.gir` — refresh from upstream, see `girs/README.md`;
* `girs/overlays/fixups.json` — skip, rename, annotation corrections;
* `girs/overlays/platform-symbols.json` — per-platform availability;
* the generator itself.

## Where hand-written code goes

| Kind | Location |
| --- | --- |
| Per-module glue, as `partial` extensions of the generated types | `src/<Project>/Custom/` |
| The runtime: loader, marshalling, GObject/GLib/Gio layer | `src/GstSharp.Net/Core/` |
| Roslyn analyzers | `src/GstSharp.Net.Analyzers/` |

The runtime is part of the `GstSharp.Net` assembly; there is no separate core
package.

## Quality gates

All of these must pass before a change is merged:

1. **`dotnet build` is warning-free.** Warnings are errors here, so a warning is
   a build failure by construction. Do not silence one with
   `#pragma warning disable` or `NoWarn` without a comment explaining why.
2. **Running the generator twice produces byte-identical output** — deterministic
   ordering, LF line endings.
3. **Census tests pass.** The generator asserts fixed counts of emitted classes,
   records, enums and bitfields, so scope creep or accidental skipping shows up
   immediately.
4. **ABI probe tests pass** (`tests/GstSharp.IntegrationTests`). They validate
   struct sizes and raw field offsets against the running library, so they need
   a native GStreamer.
5. **NativeAOT smoke:**
   `dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true`
   completes with zero IL trimming or AOT warnings. `eng/aot-gate.ps1` runs this
   the way CI does, for both AOT samples.
6. **The packages still contain the 1.28.1 surface.** `dotnet pack` restores
   each package at 1.28.1 from nuget.org and compares: a public type or member
   that vanished, or that kept its name and changed its shape, fails the pack
   with a `CP####` error naming it. Adding members passes, which is the promise
   the README makes for `1.28.x`. The `verify` job packs on every push, so the
   answer does not wait for a tag.

   A failure here is rarely a mistake in the check. It means the change removed
   or reshaped something already published, and that waits for `1.30`: keep the
   old member, add the new one beside it, and mark the old one `[Obsolete]` if
   it should stop being used. Do not reach for
   `/p:ApiCompatGenerateSuppressionFile=true`, which the error message offers —
   a suppression file would make the promise unenforced rather than kept.

## When the census tests fail

Quality gate 3 is the one a first pull request usually trips. The census is a
set of frozen numbers, so any deliberate change of the surface — a gir refresh,
a new fixup, a new marshalling rule — fails it by design.

The symptom is an `Assert.Equal()` failure in `GstSharp.Generator.Tests` whose
whole message is two integers, an expected `1205` against an actual `1206`.

When the move is intended, update the expectations:

1. Regenerate, as above. The `generate` verb prints the new census, one line per
   module and category, and rewrites `girs/skip-report.md`.
2. Fix the `[InlineData]` rows of the failing theory. The expectations live in
   two files: `tests/GstSharp.Generator.Tests/CensusTests.cs` for what the gir
   files declare — classes, records, interfaces, enumerations, bitfields,
   callbacks, aliases, constants, functions and signals per namespace — and
   `tests/GstSharp.Generator.Tests/ClassEmitterTests.cs` for what a run emitted
   and what it skipped, per module and per skip reason.
3. Commit the regenerated sources, `girs/skip-report.md` and the new counts as
   one change, and say in the pull request which numbers moved and why.

Never bump a number you cannot account for. Census drift that nobody asked for
is a bug in the change — typically an accidental skip: an overlay entry that
matches more than it was meant to, or a rule that rejects a signature it should
handle. The diff of `girs/skip-report.md` names the symbol that disappeared and
the reason it was dropped.

## Line endings and determinism

Everything is LF, enforced by `.gitattributes` and `.editorconfig`. The
generator emits LF explicitly and orders its output deterministically, so two
runs over the same inputs produce identical bytes; a change that makes the
second run differ is a bug in the change.

## Ownership doctrine

New API has to fit the rules in
[`docs/ownership.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/ownership.md):
mini objects and boxed values are owned and disposed, GObject wrappers are
interned and are not. A call that consumes its argument is written by hand in
`Custom/`, never generated, and documents the consumption on the parameter.

## Commits and pull requests

Imperative mood, concise, English, one logical change per commit. A pull request
runs the whole CI matrix — Linux, macOS, Windows MSVC and Windows MinGW, plus
both NativeAOT gates — so keep the change small enough that a red leg points at
one thing.
