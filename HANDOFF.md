# E5 Stage 2a, wave 1 — handoff

Branch `e5-stage2`, worktree `GstSharp.Net-e5s2`, based on `main` aa7e484.
This file is scratch for the next agent and is deleted before the wave is
merged.

## Done

Five commits, all green (build 0 warnings, `verify` clean, 1731 tests pass,
AotSmoke publishes with 0 IL/AOT warnings). Generated output is still
**unchanged**, because the committed `subclassable` allowlist is still empty.

* **c7eb44b "Pair class struct slots with their virtual methods"** — brief step
  (a), landed by the previous agent. `Semantic/ClassStructModel.cs`
  (`ClassStructMember`, `ClassStructModel`, `SubclassModel.Build`), the four
  new overlay keys in `Semantic/Overlays.cs`, `GirVirtualMethod.OverlayKey` and
  the `AnnotationKeyOf` fallback in `Planning/MarshalPlanner.cs`.
  Diagnostics GEN0027 (stale `subclassable`), GEN0028 (allowlisted class with
  no class struct).
* **899689c "Build the class struct model before anything is emitted"** —
  `SubclassModel.Build` now runs in `GenerationPipeline.Execute`, right after
  the classification pass and before `TypeMap`, and the model is carried to
  every module in `ModuleEmitters.Subclasses`. New:
  `ReportStaleVirtualKeys` → **GEN0029** (`skipVirtuals`), **GEN0030**
  (`vfuncDefaults`), **GEN0031** (`vfuncIdentityBuffers`), each checked against
  `SubclassModel.VirtualMethodKeys` / `.VirtualMethodParameterKeys` (slots of
  *subclassable* classes only, so a key naming a chain-only class such as
  `Gst.Object` is stale by construction).
  `NameMapper.VirtualMethodParameterName(overlayKey, girName)` reads the
  existing `rename` map at `Ns.Class::vfunc#param` (orchestrator decision).
  `rename` has no stale reporting, so those 4 entries can land whenever.
* **ed0e8c6 "Emit the mirror of a class struct"** — `Emit/ClassStructEmitter.cs`,
  wired into `EmitModule` before `RecordEmitter`. One file per mirrored class at
  `src/<Project>/Generated/ClassStructs/<Class>ClassRaw.cs`, census category
  **`"class struct"`**. Also moved `ClassSlot` from
  `src/GstSharp.Net.Base/Custom/ClassStructs.cs` to
  `src/GstSharp.Net/Core/GObject/ClassStructs.cs` (namespace `Gst.GObject`), because
  a generated mirror in the `Gst` module cannot reach a helper of the Base module.
  The Base mirrors still compile: that file already has `using Gst.GObject;`.
* **4a64caf "Let the mirrors describe themselves to the ABI probes"** — per
  module `Generated/ClassStructs/ClassStructRegistry.cs` with
  `internal static Gst.GObject.ClassStructProbe[] CreateEntries()`, plus the two
  hand written runtime rows `ClassStructProbe` / `ClassSlotProbe` appended to
  `src/GstSharp.Net/Core/GObject/ClassStructs.cs`. A row carries the C name, the
  managed `&<Wrapper>.GetGType`, `Unsafe.SizeOf<...ClassRaw>()` and every
  own slot as `(gir name, Offset)`.

### How the emitters were verified without the swap

A scratch gir tree with a filled allowlist, generated into a scratch copy of
`src/`; the committed `girs/` stays allowlist-empty so `verify` is clean:

```sh
cp -r girs C:/src/_scratch/e5s2/girs-scratch      # then add "subclassable" to overlays/fixups.json
cp -r src  C:/src/_scratch/e5s2/out-src
dotnet run --project generator/GstSharp.Generator -- generate \
    --gir-dir C:/src/_scratch/e5s2/girs-scratch --out-dir C:/src/_scratch/e5s2/out-src
```

Both scratch trees exist already with the 7 wave-1 classes in the allowlist.
The 9 mirrors it produces (`Gst`: Object, Element, Bin; `Gst.Base`: BaseSrc,
PushSrc, BaseSink, BaseTransform, Aggregator, + 2 registries) were read and match
the stage-1 shape. They have **not been compiled** — that only happens in the
swap commit.

## Remaining (brief steps b–j)

Nothing of the planner, the vfunc emitter, the overlay *data*, the census
values, the tests or the docs is written.

**Sequencing that keeps every commit green** (unchanged): generated mirrors and
`*.Subclass.cs` cannot coexist with the stage-1 hand-written ones. So there is
exactly one atomic swap commit: allowlist + `vfuncDefaults` + `skipVirtuals` +
the 4 `rename` entries + the 4 `annotationOverrides` + stage-1 file deletions +
regenerate + `AbiProbeTests` renames, all at once. Do not start it with less
than ~40 tool calls of budget left.

Files the swap deletes: `src/GstSharp.Net/Custom/{Element,Bin}.Subclass.cs`,
`src/GstSharp.Net.Base/Custom/{BaseSrc,PushSrc,BaseSink,BaseTransform}.Subclass.cs`,
`src/GstSharp.Net.Base/Custom/ClassStructs.cs` (whole file — `ClassSlot` already
moved out), and the `GstObjectClassRaw`/`ElementClassRaw`/`BinClassRaw` part of
`src/GstSharp.Net/Core/GObject/ClassStructs.cs` (keep `GTypeClassRaw`,
`GObjectClassRaw`, `ClassSlot`, `ClassStructProbe`, `ClassSlotProbe`).
The only other consumer is `tests/GstSharp.IntegrationTests/AbiProbeTests.cs`
(`Gst.GObject.GstObjectClassRaw` → `Gst.ObjectClassRaw`, `Gst.Base.*ClassRaw` →
unchanged names in the same namespace). `SubclassRegistry.cs` names
`ElementClassRaw` in a doc comment only.

### Next command

Read `Planning/MarshalPlan.cs` (601 lines, the `ArgumentPlan`/`ScalarPlan`
vocabulary) and `Emit/SignalEmitter.cs` (697 lines, the closest existing
native→managed renderer), then write `PlanVirtualMethod` per brief step (b).

## Decisions already taken (do not re-litigate)

1. Return-value annotation address is `Ns.Class::vfunc#return`; the
   `<virtual-method>` is the only carrier the generator reads.
2. Parameter renames go through `rename` at `Ns.Class::vfunc#param`. The four
   entries the frozen surface needs (checked against the gir):
   `GstBase.BaseTransform::set_caps#incaps` → `inCaps`,
   `#outcaps` → `outCaps`, `GstBase.BaseTransform::transform_ip#buf` →
   `buffer`, `GstBase.PushSrc::create#buf` → `buffer`.
3. `SubclassBaseRules` gains `RequiredOverrides`; `DefineSubclass` throws
   `ArgumentException` **before** registration when one is missing
   (`Aggregator` → `AggregateOverride`). The "NULL parent slot throws
   `InvalidOperationException`" test uses `BaseTransform::transform`, not
   `Aggregator::aggregate`.
4. The null-slot check is emitted for **every** slot, uniformly.
   Non-void: the `vfuncDefaults` expression if present, else `throw new
   InvalidOperationException("<Class>.<vfunc> has no parent implementation;
   override On<Vfunc>")`. Void: consume every adopted (transfer-full) parameter
   and return — that is what reproduces stage-1 `Bin::handle_message`. No
   `vfuncDefaults` entry for `change_state` or `handle_message`.
5. Wave-1 allowlist is the 7 Gst/GstBase classes. `Gst.Object` is mirrored on
   the chain with no vfunc surface. GObject-2.0 mirrors stay hand written.

## Findings this session that change the plan

1. **Only 4 of the 13 `annotationOverrides` can land in wave 1.**
   `tests/GstSharp.Generator.Tests/ClassEmitterTests.cs:815-819` asserts the
   real-gir run emits no GEN0024, and an annotation correction addressing a
   vfunc of a class that is not allowlisted is never read, hence stale. The
   four that are in wave-1 scope: `Gst.Element::request_new_pad#return` →
   `none`, `GstBase.BaseSink::fixate#caps` → `full`,
   `GstBase.BaseSink::event#event` → `full`,
   `GstBase.BaseTransform::submit_input_buffer#input` → `full`. The other nine
   (BaseParse, AudioDecoder, AudioEncoder, VideoDecoder, VideoEncoder) and both
   nullable/direction fixes belong to the wave that allowlists those classes.
   Do **not** exempt vfunc keys from the stale check to work around this.
2. **"Byte-identical" means the signatures, not the file bytes.** The stage-1
   files carry hand-written prose no generator reproduces; the mechanism the
   spec names is the package-validation baseline plus the diff gate, which see
   public/protected signatures, parameter names, `new` and nullability.
3. **`ClassSlot` was in the Base module, not in Core** (repo-survey.md §1 says
   `Core/GObject/ClassStructs.cs:387`; it was
   `src/GstSharp.Net.Base/Custom/ClassStructs.cs:387`). Moved, see ed0e8c6.
4. **Mirror data-field names drift from stage-1** where the gir runs words
   together: `elementfactory` → `Elementfactory` (stage-1 `ElementFactory`),
   `padtemplates` → `Padtemplates`, `numpadtemplates` → `Numpadtemplates`,
   `pad_templ_cookie` → `PadTemplCookie` (stage-1 `PadTemplateCookie`). These
   members are `internal` and nothing outside the mirror reads them, so the
   divergence costs nothing; add `rename`-style handling only if a reviewer
   asks.
5. **`Gst.Object`'s mirror is `Gst.ObjectClassRaw`**, in namespace `Gst`
   (the module's CLR namespace), where stage-1 had `Gst.GObject.GstObjectClassRaw`.
   All generated mirrors live in the module namespace, so the Base ones stay
   `Gst.Base.BaseSrcClassRaw` etc. — same spelling as stage-1.
6. `DefineSubclass` needs `new` iff a transitive parent is allowlisted: Element
   no, everything else yes (Bin, BaseSrc, PushSrc, BaseSink, BaseTransform,
   Aggregator).
7. The wrappers of all 7 classes are already `abstract`/non-sealed `partial`,
   so the generated `*.Subclass.cs` partial compiles without a ClassEmitter
   change.

## Open questions

* Nothing blocking. The one judgement call left is whether `VfuncEmitter`
  renders its `ChainUp` bodies from the forward `MarshalPlan` or from a small
  purpose-built set of per-bucket snippets; `CallableRenderer` was not
  investigated far enough to say whether its native call target is pluggable.
