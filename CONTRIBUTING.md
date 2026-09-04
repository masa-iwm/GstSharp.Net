# Contributing to GstSharp.Net

Thanks for looking. This page is the short version of how the repository works;
`eng/ci-notes.md` explains the workflows themselves.

Everything inside this repository is written in **English**: code, comments, XML
documentation, Markdown, and commit messages.

Taking part in this project means following the
[`CODE_OF_CONDUCT.md`](https://github.com/masa-iwm/GstSharp.Net/blob/main/CODE_OF_CONDUCT.md),
which is the Contributor Covenant 2.1.

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

## Documentation site

The site under <https://masa-iwm.github.io/GstSharp.Net/> is the API reference
extracted from the XML documentation of the eighteen packable projects, plus the
README and the guides in `docs/`. docfx is pinned in
`.config/dotnet-tools.json`, so a local preview is two commands:

```sh
dotnet tool restore
dotnet docfx docfx/docfx.json --serve
```

`.github/workflows/docs.yml` runs the same command on every push to `main` and
deploys the result to GitHub Pages, so the published site describes the tip of
the branch rather than the latest release.

The run ends with a handful of warnings and still exits 0. Most of them come
from the generated XML documentation: gtk-doc comments that link to native C
symbols such as `GST_PAD_SRC`, which have no page in a managed reference. Those
are accepted as they stand — the fix belongs in the generator, not in
`Generated/`, and is tracked as backlog work. The remaining few are
pre-existing and not about the site: two duplicate-source warnings for the
analyzer project's `AnalyzerReleases.*.md` and two duplicated-member warnings
for `Gst.Interop.ModuleTypeEntry`. `eng/ci-notes.md` has the detail.

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

Both verbs also look at what is in a `Generated` directory beside what the run
wrote: `generate` deletes a committed source the generator no longer emits and
prints the deletion, and `verify` reports it as an orphan generated file.

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
6. **The packages still contain the published surface.** `dotnet pack` restores
   each package at `PackageValidationBaselineVersion`
   (`src/Directory.Build.props`) from nuget.org and compares: a public type or member
   that vanished, or that kept its name and changed its shape, fails the pack
   with a `CP####` error naming it. Adding members passes, which is the promise
   the README makes for `1.28.x`. The `verify` job packs on every push, so the
   answer does not wait for a tag.

   A failure here is rarely a mistake in the check. It means the change removed
   or reshaped something already published, and that waits for `1.30`: keep the
   old member, add the new one beside it, and mark the old one `[Obsolete]` if
   it should stop being used. On a generated member that attribute comes from
   the `obsolete` message of an `annotationOverrides` entry keyed on the bare
   `c:identifier`, and the new shape is hand written in `Custom/` beside it;
   neither half is a hand edit of a generated file. Do not reach for
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
   three files: `tests/GstSharp.Generator.Tests/CensusTests.cs` for what the gir
   files declare — classes, records, interfaces, enumerations, bitfields,
   callbacks, aliases, constants, functions and signals per namespace —
   `tests/GstSharp.Generator.Tests/ClassEmitterTests.cs` for what a run emitted
   and what it skipped, per module and per skip reason, and
   `tests/GstSharp.Generator.Tests/SubclassCensusTests.cs` for the subclassing
   surface: the `class struct` mirrors and the `vfunc` slots per module, and
   the `Virtuals` ledger with the reason of every slot that carries no managed
   member.
3. Commit the regenerated sources, `girs/skip-report.md` and the new counts as
   one change, and say in the pull request which numbers moved and why.

`girs/skip-report.md` groups what a run left out by reason, and one of the
reasons is `HandBound`: the symbol is not generated, but its managed surface
exists, hand written, under a `Custom/` folder or in `src/GstSharp.Net/Core/`.
The ledger for that is the `handBound` array of `girs/overlays/fixups.json`; it
changes nothing about what is emitted and only moves the symbol out of the
sections that measure the real binding gap. A new hand binding belongs there on
the day it is written, and an entry the generator never sees skipped — because
the symbol is generated after all, or no longer exists, or is misspelt — is
reported as `GEN0023`. That is a warning, so `generate` and `verify` still exit
zero on it; what it fails is the test suite, which asserts that a run over the
committed overlays reports no `GEN0020`, `GEN0023`, `GEN0024`, `GEN0025` or
`GEN0026`.

A hand bound consumer keeps its callback type generated: a `<callback>` whose
only consumers are on the `handBound` ledger is emitted all the same, so the
hand written member binds the generated delegate and trampoline instead of a
copy of them.

The last section of the report, `## Fields`, is not about callables at all: a
record field has no `c:identifier` and no skip reason, so a record whose methods
are all bound would read as fully bound however many of its fields carry API in
C and none in C#. The section lists those fields under the shape that kept them
out, and the census tests freeze its totals like every other count. A field
counts as bound when a wrapper declares an accessor for it, or when a value
projected structure declares it as a typed public field; one that is projected
onto a raw address, and one that only a hand written member reads through, stay
listed, because what the section measures is the generated surface - unless the
field is registered under `fieldSkips`, which is the one statement that takes it
off this ledger.

A field is not a callable, so the `handBound` array above cannot name one. The
ledger for a field that another member of the binding does answer is the
`fieldSkips` object of `girs/overlays/fixups.json`, keyed by the `c:type` of the
record and the gir name of the field — `GstPadProbeInfo.flow_ret` — and stating
either the generated member that hands the same value out (`exposedBy`) or that
a hand written one does (`handBound`). An entry moves the field into the
`## Fields exposed elsewhere` section of the report and keeps the generator from
emitting an accessor for it, which is what makes it the answer to a name a
`Custom/` member already carries. A key that matches no field of an emitted
record, or an entry that states neither half, is reported as `GEN0025`.

What no gir annotation carries about a field goes in the `fieldAnnotations`
object of the same file, keyed the same way. Exactly one of two corrections has
to be stated. `nullable: false` says the field never holds the null pointer,
which a gir cannot spell on a `<field>` at all — the attribute exists on
parameters and on return values only — so a field projected onto a reference is
nullable unless an entry says otherwise, and one that says so emits the accessor
non nullable and reports the null pointer as an `InvalidOperationException`
rather than handing it out. `accessor: false` holds the field back from the
accessors altogether: the pointer stays on the mirror and the field stays on the
`## Fields` ledger under its own shape. The reason belongs in the `$comment` of
the entry rather than in a key of its own, because it differs per field — a
pointer the library replaces or clears while a consumer holds the structure,
whose reference a `transfer none` projection takes after the read and therefore
possibly too late; an accessor whose name a member that shipped already carries;
or a field a wave deliberately left for the next one. Every entry cites the C
file and line its claim rests on in the same `$comment`. A key that matches no
field an accessor would be emitted for, an entry that states neither correction
or only the default, and one that states both are reported as `GEN0026`.

## The overlay keys of the subclassing surface

`girs/overlays/fixups.json` steers what a subclassable class emits through
eight keys, each documented by a `$comment-` entry beside it. `subclassable`
is the allowlist itself: a class named there gets a class struct mirror, one
`OnX` member per bindable slot and a registration, and its whole parent chain
gets mirrors as well. The other seven address a single slot, keyed
`Ns.Class::vfunc`, or a single parameter of one, keyed
`Ns.Class::vfunc#param`:

* `skipVirtuals` — the slots that carry no managed member, with the reason the
  `Virtuals` section of `girs/skip-report.md` prints. A slot the planner
  cannot project is *not* listed here: the run reports it as
  `UnsupportedSignature` on its own, so a shape that becomes bindable stops
  being skipped without anybody editing the file.
* `vfuncDefaults` — what a chain-up answers when the parent class leaves the
  slot NULL, which is the behaviour the base class documents for that case. A
  slot with no entry has no value a chain-up could invent and throws instead.
* `vfuncIdentityBuffers` — a buffer parameter the slot may hand back
  unchanged, whose caller compares the two pointers.
* `vfuncNonNullReturns` — a slot whose caller dereferences the answer without
  checking it, with the value the trampoline substitutes for a null one.
* `vfuncDocNotes` — the part of the contract of a slot that neither the gir
  nor the marshalling states, as one sentence appended to the generated
  documentation.
* `vfuncSpans` — a counted block of elements the slot only reads, which makes
  the parameter a `ReadOnlySpan` instead of a `Span`; the gir counts the block
  by the parameter beside it either way.
* `vfuncFailureValues` — what a trampoline answers when the exception trap
  caught an override, for a slot whose caller reads something other than a
  failure into the zero of the return type.

Every entry cites the C file and line its claim rests on in a `$comment` or in
the `$comment-` block of the key. An entry that names no slot or no parameter
of the emitted surface is reported as `GEN0029` through `GEN0031` and
`GEN0036` through `GEN0039`.

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
