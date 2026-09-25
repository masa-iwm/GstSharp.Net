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

`generate` also writes `skip-report.md`, which defaults to the `--gir-dir`
directory: a `generate` run with a non-default `--out-dir` should pass
`--report-dir` with the same path so that the committed `girs/skip-report.md`
is left alone.

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

The benchmarks under `benches/` are not one of these gates: every CI job builds
them with the solution, no CI job runs them, and the numbers in
`benches/README.md` come from a local run on a machine whose owner can vouch
for it.

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
committed overlays reports none of the sixteen findings an entry that no longer
describes the gir produces: `GEN0020`, `GEN0023`, `GEN0024`, `GEN0025`,
`GEN0026`, `GEN0042`, `GEN0048`, `GEN0049`, `GEN0050`, `GEN0051`, `GEN0052`,
`GEN0053`, `GEN0054`, `GEN0055`, `GEN0056` and `GEN0057`. Thirteen of them say
an entry matched nothing, `GEN0054` says a `borrow` names no signal argument at
all, and `GEN0050` and `GEN0052` say an entry is still read but silently does
nothing.

The `skip` array beside it takes a fourth kind of key, next to the
`c:identifier` of a callable, the qualified gir name of a type and the GObject
spelling of a property (`Gst.Bus:enable-async`): the GObject spelling of a
signal, `GstRtspServer.RTSPClient::send-message`, for a signal whose C
registration no annotation describes — one whose gir names an argument type the
C never registered, say. The generated event for such a signal cannot be
corrected, only kept out, so the entry belongs with a `handBound` twin and a
hand written member under `Custom/` that carries the same name. The key is read
in the signal loop alone; one that matched no signal of an emitted type is
reported as `GEN0055`, because the event the entry exists to keep out would
otherwise be generated again beside the member that replaced it. A key of any
of the other three shapes that matched nothing is reported as `GEN0056`, for
the same reason and in the same words; a `rename` the run never looked up is
reported as `GEN0057`, because the name it decides is then a decision about
nothing. A key that
matches wins over every rule based reason, so a signal the run would have filed
under `ActionSignal` is filed under `OverlaySkip` — or, with the twin, under
`HandBound` — instead.

Hand binding a signal is therefore the pair of entries plus the member: the
`Ns.Type::signal-name` key in `skip` and the same key in `handBound`, and a
`partial` class under `Custom/` that declares the event, its arguments class and
its trampoline under the same public names the generator would have taken. The
names are what lets the hand binding be dropped again: if the gir is corrected
upstream, deleting both entries brings the generated event back, and the
members the hand binding added on its own are what the deletion costs. Where
the public name is not the one the gir derives, dropping the hand binding is
three edits rather than two — the two entries go and a `rename` at the same key
comes back, which is then held to the gir by `GEN0057`; a `rename` is not kept
beside a skip entry for a name only the hand written member carries, because
while the signal is skipped it names nothing and `GEN0057` reports it. For
`RTSPClient.SendingMessage` a corrected gir would generate
the context as `Ctx` and a `Message` copied out of the emission; the `[Obsolete]`
`Session` and the lent `Message` exist in the hand binding alone, and a lent
argument needs a `borrow` overlay to be generated at all. It would also
generate the event as `SendMessage`, which `gst_rtsp_client_send_message` has
taken and which `GEN0011` reports as a collision: the `rename` that gives the
event the `SendingMessage` the hand written member carries is exactly the third
edit above, and it belongs in the same change as the deletion of the two
entries rather than years before it.

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

An instance field of a GObject class is a third ledger, `## Class fields`, and a
third key. A wrapper holds a native instance and mirrors no part of it, so every
field of a class is listed there by construction; the one statement that takes
one off is the `instanceFields` object of the same file, keyed the same way
(`GstBaseSink.segment`). An entry emits a mirror of the fields the class declares
itself and an accessor that reads the field at the instance size of the parent
type plus the offset that mirror measured, so no offset is ever written down. It
states three things and all three are required: `lock`, the lock the library
rewrites the field under, which the generated remark prints; `overrides`, the gir
virtual methods the base class calls on the streaming thread, which is the window
a read is consistent in, because managed code can take none of the locks a writer
of such a field holds; and a `$comment` with the header file and line. An
optional `name` renames the accessor. The class has to be on `subclassable`:
without a subclassing surface there is no override to read the field inside, so
there is no window to state.

The refusals are errors rather than warnings, because an entry is the only reason
a piece of public surface exists. `GEN0060` reports an entry that matched no
field of an emitted class — a key naming a nested `<union>` is one of those,
because a gir keeps a union out of the field list — that states too little, that
spells a `name` which is no identifier, that names the base instance, that a
`fieldSkips` entry already claims, that names a class which is not subclassable,
or whose `overrides` name a member the subclassing surface does not emit.
`GEN0061` reports a shape this wave refuses — a pointer, a callback, a scalar, an
embedded structure other than `GstSegment`, a field above the support floor, a
field the gir marks private. `GEN0062` reports a mirror the closed table cannot
lay out: a field it has no storage for, a field that occupies a part of a word
rather than the whole of it, a class that grew a nested `<union>`, or a field
whose name is one the mirror gives a static of its own. All four are errors
rather than truncations: a short mirror would still measure the right offset and
would break the size probe instead. `GEN0063` reports an accessor whose name the
class or a descendant of it already carries, which the field answers with a
`name`. Every entry also joins the three probe layers of
`InstanceFieldProbeTests`, and the meta test there fails for a field with no
behavioural witness.

One rule of the allowlist is held by review and not by the generator: a field a
header puts behind an `#if` is a field whose offset differs between builds of the
library, and a gir records neither the macro nor the condition. The reviewer of
an entry therefore reads the header around and in front of the field, and says in
the `$comment` that it did — the generator cannot.

## The overlay keys of the subclassing surface

`girs/overlays/fixups.json` steers what a subclassable class emits through
ten keys, each documented by a `$comment-` entry beside it. `subclassable`
is the allowlist itself: a class named there gets a class struct mirror, one
`OnX` member per bindable slot and a registration, and its whole parent chain
gets mirrors as well. The other nine address a single slot, keyed
`Ns.Class::vfunc`, or a single parameter of one, keyed
`Ns.Class::vfunc#param`:

* `skipVirtuals` — the slots that carry no managed member, with the reason the
  `Virtuals` section of `girs/skip-report.md` prints. A slot the planner
  cannot project is *not* listed here: the run reports it as
  `UnsupportedSignature` on its own, so a shape that becomes bindable stops
  being skipped without anybody editing the file. A key may also name one slot
  of a chain-only mirror — a class that is not subclassable but whose mirror
  lays the slots out — which is how a slot that is bound by hand for the classes
  below it states its reason instead of the fixed "not subclassable" one.
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
* `vfuncFloatingReturns` — a slot whose caller owns the *floating* reference
  of the answer, which no gir transfer kind spells: such a slot is annotated
  `transfer none`, and reading that as a borrow leaves the caller with a
  pointer it owns no reference of. The trampoline of a listed slot references
  the answer once more and forces the floating flag back on. It is only legal
  on a slot that answers a class instance; anything else is `GEN0059`, an
  error, and an entry that names no slot of a subclassable class is `GEN0058`.
* `vfuncFailureValues` — what a trampoline answers when the exception trap
  caught an override, for a slot whose caller reads something other than a
  failure into the zero of the return type.
* `vfuncSiblingArguments` — a parameter that hands the slot a second instance
  of the type the slot itself runs for, which the base class has just created
  and may still hold floating. Its wrapper is resolved the way the wrapper of
  the instance is and settles no reference, so the one the caller means to
  drop stays the caller's. An entry that names no parameter of a slot, or one
  of another shape, is reported as `GEN0044`.

One more key addresses a record rather than a slot:

* `lentOpaqueRecords` — the opaque records a slot is lent one of. Their wrapper
  holds nothing but the pointer, which is regularly an address on the stack of
  the caller, so the trampoline detaches it when the call returns and every
  member throws `ObjectDisposedException` afterwards. The list is stated rather
  than derived because a run emits one module after the other, and the slots
  that lend a record of the core module belong to modules reached long after
  that record is written out. A record a slot lends and the list does not name
  is `GEN0045`, an error; one that no slot lends is `GEN0046`.

The `rename` table of the same file reaches a slot as well, under that same
`Ns.Class::vfunc` key: a slot whose derived name is one an inherited member of
another return type already carries has no managed spelling of its own, and
the entry gives it one. `GstAudio.AudioSink::stop` is the one such slot, and
it is emitted as `OnStopDevice`.

The remaining keys address a callback type and a member rather than a slot:

* `instanceKeyedCallbacks` — the qualified gir name of a callback whose own C
  signature carries no `user_data`, mapped onto the storage slot of the
  instance the setter writes. The trampoline recovers the managed delegate
  from that slot and from the instance it is handed as its first argument;
  two callbacks that share one slot are mutually exclusive per instance. An
  entry that names no emitted callback is reported as `GEN0041`.
* `docNotes` — the part of the contract of a member that neither the gir nor
  the marshalling states, keyed by `c:identifier` and appended to the
  generated documentation as one paragraph. It is the counterpart of
  `vfuncDocNotes` for a member; an entry that names no planned callable is
  reported as `GEN0042`.
* `signalDocNotes` — the same thing for a signal, keyed by the GObject
  spelling of it, `GES.Timeline::select-tracks-for-object`. An entry that names
  no rendered signal is reported as `GEN0048`.
* `annotationOverrides` on a signal argument — keyed by the GObject spelling of
  the signal and the gir name of the argument,
  `GstApp.AppSink::propose-allocation#query`, and read for two flags only.
  `nullable` corrects what the emission may pass; `borrow` hands the handler a
  wrapper that borrows the mini object or the boxed value of the emitter
  instead of one holding a reference or a copy of its own, which is the only
  way the argument is writable in place. A `borrow` is legal on nothing else
  than an argument projected onto one of those two wrappers, and on no key but
  a signal argument: on a parameter of a method or of a callback, on an
  argument of a virtual method — whose key is the signal key of the same
  concept with the underscore of the slot in place of the hyphen — or on a
  return, it is `GEN0054`, an error, rather than a key consumed in silence.
  Where the slot name is a single word the two spellings are not merely close
  but the **same string**, so one entry stands for both paths. That is a
  documented limitation rather than a hazard: the signal path honours the
  `borrow`, and the virtual method path refuses the very same key with
  `GEN0054` as soon as the class is `subclassable`, so a colliding entry fails
  loudly and never applies in silence to the slot. `nullable` has shared this
  key space for as long as both paths have read corrections. The one collision
  in a bound namespace today is `Gst.Bus::message#message`, whose class is not
  subclassable and whose argument must not be borrowed in the first place.
  `borrow: false` states the
  default and changes nothing, the way `nullable: false` does. The flag states
  two facts about the C that no gir carries: the signal registered the argument
  `G_SIGNAL_TYPE_STATIC_SCOPE`, so GObject copies it on no emission path, and
  whoever sent the value reads back what the handler wrote. An entry that names
  no argument the planner reached is reported as `GEN0024` like any other stale
  annotation override — a key is consumed where its argument is planned, so an
  entry on an argument of a signal that is skipped afterwards is read and stays
  silent, which is how `nullable` behaves there as well.
* `preconditions` — C# statements the generated body of a callable runs before
  it marshals anything, keyed by `c:identifier`. They keep a member off a call
  the C answers with a crash or with a critical. The helper each one calls is
  hand written in the `Custom/` partial of the type, so the compiler reports a
  statement that names nothing; an entry that names no rendered callable is
  reported as `GEN0049`.
* `handOverRefusals` — the callables that refuse what they are handed instead
  of taking it, keyed by `c:identifier`, with the C lines the reading rests on
  as the value. The generated body releases the reference minted for every
  handed over argument again when the raw result is a zero — `FALSE` for a
  `gboolean` return, `NULL` for a pointer one — so a refusal leaks nothing. A
  callable whose return carries no such refusal, or that hands nothing over, is
  reported as `GEN0050`; an entry that names no rendered callable is `GEN0051`.
* `docStrip` — exact substrings taken out of the raw gir documentation of a
  callable before it is split into paragraphs, keyed by `c:identifier`. It is
  the one overlay that removes upstream text, and it exists for the sentence
  that states a rule of the C which is not the rule of the binding. Each
  substring has to stand in the documentation exactly once; one that stands
  nowhere or twice — and an entry whose callable carries no documentation at
  all — is reported as `GEN0052`, and an entry that names no rendered callable
  is `GEN0053`.
* `typeDocReplace` — exact substrings of the raw gir documentation of a *class*
  and the text written in their place, keyed by qualified gir name
  (`GES.EffectClip`) with an `old`/`new` pair per replacement. It is the
  counterpart of `docStrip` one level up, for the upstream sentence that
  documents a string grammar the library itself does not write — a note beside
  it would leave the reader two grammars and no way to choose. Replacements are
  applied in order, each to the text the one before left behind. Both members
  have to be written: an entry that names no `new` — which is also how a
  misspelled key reads, since an unknown member is tolerated so that `$comment`
  can sit inside the entry — is refused while the overlays load, and a removal
  is written out as `"new": ""`. Every failure of a loaded entry is `GEN0064`
  and an error: an `old` that stands nowhere or more than once, a class with no
  documentation at all, and an entry that names no rendered class. Only
  classes are covered — records and interfaces obtain their type documentation
  on emitter paths of their own.
* `handWrittenMembers` — the C# names of the members a `Custom/` partial of a
  GObject class declares by hand, keyed by the `c:type` of the class
  (`GstBaseSink`). The generator never reads `Custom/`, so a generated member
  of the same name on a derived class would hide the hand-written one without
  `new`, which is `CS0108` and so a build failure. A listed name counts as a
  member of that class for the hiding rules of every descendant, and a value
  backed property over it is emitted with `new`. List only names a generated
  descendant would actually hide: a listed method name puts `new` on every
  overload of that name, and on one of a different signature that is
  `CS0109`. An empty list or a blank name is refused while the overlays load,
  and a key that names no rendered class is `GEN0065`.

Every entry cites the C file and line its claim rests on in a `$comment` or in
the `$comment-` block of the key. An entry that names no slot or no parameter
of the emitted surface is reported as `GEN0029` through `GEN0031`, `GEN0036`
through `GEN0039`, `GEN0044`, `GEN0046` and `GEN0058`. A slot whose managed
member would hide an inherited one of the same name and the same parameters
while answering another type is `GEN0040`, an error: C# accepts such a pair and
the override that runs then depends on the static type the caller holds, so the
slot needs a `skipVirtuals` entry or a managed name of its own. A slot that
answers a handle nobody references on the way out and carries no
`vfuncDocNotes` entry is `GEN0047`, an error as well: the note the generator
writes for it says the base class takes a reference of its own, and only that
entry says which call site does that and what the override owes it. A
`vfuncFloatingReturns` entry on a slot that answers anything but a class
instance is `GEN0059`, an error too: the mode mints a reference and sets a flag
on the handle, and neither means anything for another shape of answer.

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
interned and are not. A call whose C half takes a `transfer-ownership="full"`
argument over is generated: it is handed a value minted for it, and it documents
on the parameter either the consumption — a mini object or a boxed value is
disposed when the member returns — or the handover, a GObject wrapper being
interned and staying the caller's.

## Traps in hand-written glue

Three traps that the build and the tests do not explain on their own:

* **A `GValue` crosses an assembly boundary as a pointer.** The `LibraryImport`
  generator accepts a struct by `ref` only when it can prove the struct
  strictly blittable, and a struct declared in a referenced assembly never
  qualifies (`SYSLIB1051`, whose message suggests disabling runtime
  marshalling for the assembly, which this repository does not do).
  An import declared outside the `GstSharp.Net` assembly therefore takes a
  `Gst.GObject.GValueNative*`, and the caller hands it the address of the
  value's `NativeValue`, pinned with `fixed` when the value is an `in` or `ref`
  parameter; the generated members do the same. The internal imports of
  `GstSharp.Net` itself keep their `ref GValueNative` parameters and can be
  called from any assembly the runtime grants its internals to.
* **A string the library keeps by pointer cannot go through marshalling.** The
  `*_static_str` family stores the caller's pointer rather than a copy of the
  string, while the marshalled string lives in a buffer that is released when
  the call returns. Whether the stored pointer dangles depends on the length of
  the string: `GstIdStr` copies a name of at most 15 bytes into its inline
  buffer and keeps only the pointer of a longer one, so a test that uses short
  names passes over the bug. These symbols are skipped in
  `girs/overlays/fixups.json`; glue that binds one has to hand over an interned
  copy, and its tests have to use names of 16 bytes or more.
* **A write to a frozen caps or structure is refused differently on the two
  sides.** This is an exception to the `preconditions` rule above: the C call
  logs a critical and writes nothing, and a generated member keeps that
  behaviour instead of growing a guard, as `gst_caps_append_structure` always
  has. The members listed in `WritableTargets`
  (`generator/GstSharp.Generator/Emit/CallableRenderer.cs`) say so in their
  documentation. A
  hand written member checks first and throws `InvalidOperationException`, as
  `Structure.SetValue` does. The split is deliberate: do not add a
  `preconditions` guard to a generated writer to make the two match, and do not
  let new glue fail silently.

## Commits and pull requests

Imperative mood, concise, English, one logical change per commit. A pull request
runs the whole CI matrix — Linux, macOS, Windows MSVC and Windows MinGW, plus
both NativeAOT gates — so keep the change small enough that a red leg points at
one thing.
