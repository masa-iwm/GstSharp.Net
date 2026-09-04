# E5 Stage 2a, wave 1 — handoff

Branch `e5-stage2`, worktree `GstSharp.Net-e5s2`, based on `main` aa7e484.
This file is scratch for the next agent and is deleted before the wave is
merged.

## Done

* **c7eb44b "Pair class struct slots with their virtual methods"** — the
  parser/semantic half of brief step (a). Build green, 0 warnings; generated
  output unchanged (`verify` clean), because the allowlist is empty.
  * `generator/GstSharp.Generator/Semantic/ClassStructModel.cs` (new, and
    **not called from anywhere yet**):
    `ClassStructMember(GirField, GirVirtualMethod?)`, `ClassStructModel`
    (namespace, owner class, type-struct record, members in ABI order,
    `IsSubclassable`, `Parent`, `Slots`, `KeyOf(vfuncName)`) and
    `SubclassModel.Build(Repository, Overlays, DiagnosticBag)`, which walks the
    parent chain of every allowlisted class (stopping at the `GObject`
    namespace, whose mirrors stay hand written in Core), resolves each
    `glib:type-struct` record and pairs its fields with the virtual methods of
    the class by name. Emission order is parents first, gir document order.
    New diagnostics: **GEN0027** (a `subclassable` entry matched no class),
    **GEN0028** (an allowlisted class declares no class struct record).
    `SubclassModel.Build` has to be called from `GenerationPipeline` right
    after `Overlays.Load` and **before any emitter plans a callable**: the
    `OverlayKey` stamp is what lets `annotationOverrides` address a vfunc, so a
    build that happens inside the new emitter instead would leave the 13
    GIR-WRONG corrections silently unapplied and reported stale.
  * `Semantic/Overlays.cs`: the four new keys — `subclassable` (string[]),
    `skipVirtuals` (string[]), `vfuncDefaults` (map key → C# expression),
    `vfuncIdentityBuffers` (string[] `Ns.Class::vfunc#param`) — with
    `IsSubclassable`, `IsVirtualSkipped`, `TryGetVfuncDefault`,
    `IsIdentityBuffer` and the four `*Keys` collections for stale reporting.
    **The stale reporting itself is not wired**: `GenerationPipeline` has to
    grow GEN0029-31 next to the existing GEN0024, or a misspelled
    `vfuncDefaults` key silently turns a slot into a throwing required one.
  * `GirParsing/Model/GirCallable.cs`: `GirVirtualMethod.OverlayKey`, stamped by
    the pairing pass; `Planning/MarshalPlanner.cs` `AnnotationKeyOf` falls back
    to it, so `annotationOverrides` addresses a vfunc as
    `Gst.Element::request_new_pad#return` with no other change.
* Stage-1 sources copied to `C:\src\_scratch\e5s2\stage1-before\` (6
  `*.Subclass.cs` + `Core.ClassStructs.cs` + `Base.ClassStructs.cs`) and the
  16 chain-up defaults + pad template names transcribed into
  `C:\src\_scratch\e5s2\notes-wave1.md`.

## Remaining (brief steps b–j)

Nothing of the emitters, planner, overlay *data*, tests or docs is written.

**Sequencing that keeps every commit green** (advisor-checked): the generated
mirrors and `*.Subclass.cs` cannot coexist with the stage-1 hand-written ones
(duplicate type and member names), and deleting the stage-1 mirrors breaks the
stage-1 subclass partials. So there is exactly one atomic swap commit:
allowlist filled + `annotationOverrides`/`vfuncDefaults` data added + stage-1
files deleted + regenerate + AbiProbeTests renames, all at once. Everything
before it is generator code with `subclassable` still absent, which emits
nothing and leaves `verify` clean. Do not start the swap with less than ~40
tool calls of budget left.

Also note: adding the 13 GIR-WRONG `annotationOverrides` before the vfunc
planner consumes them produces 13 stale-key **GEN0024** warnings, so that data
belongs in the swap commit too, not earlier.

Next command: read `generator/GstSharp.Generator/Planning/MarshalPlanner.cs`
`PlanSignalArgument` / `PlanSignalReturn` / `PlanCallbackCore` (anchors
:3765-3830 and :4088-4177) and `Emit/SignalEmitter.cs`, then write
`PlanVirtualMethod` per brief step (b).

## Open questions the next agent must resolve

1. **Three stage-1 parameter names do not match the gir** (checked per class
   against `girs/reference/GstBase-1.0.gir`; `Gst-1.0.gir` was checked too and
   `Element::change_state` = `transition` and `Bin::handle_message` =
   `message` both match, so the three below are the whole list):
   `BaseTransform::set_caps` gir `incaps`/`outcaps` vs shipped
   `inCaps`/`outCaps`; `BaseTransform::transform_ip` gir `buf` vs shipped
   `buffer`; `PushSrc::create` gir `buf` vs shipped `buffer`.
   (`BaseSink::preroll`/`render` = `buffer` and `BaseSrc::set_caps` = `caps`
   do match.) Public parameter names are part of the frozen stage-1 surface
   and package validation flags a rename, so byte-identical emission needs a
   fifth overlay key — extending the existing `rename` map to the address
   `Ns.Class::vfunc#param` is the consistent shape. The frozen spec does not
   mention this; it is a decision for the orchestrator, not the impl agent.
   Not verified: whether `NameMapper` spells the emitted members `TransformIp`
   and `IsSeekable` rather than `TransformIP`/`Isseekable`. The same gate
   catches it, but check before emitting.
2. **`Bin::handle_message` has no expressible default.** It is void and
   *consumes* its message; the stage-1 null-slot branch unrefs the message
   (Bin.Subclass.cs:145-157). A `vfuncDefaults` C# expression cannot carry a
   side effect, so the emitter must release consumed parameters on the
   null-slot path independently of the overlay.
3. **`Element::change_state` has no null branch at all** in stage-1 (the slot
   is never null in any class). Either the emitter omits the null check for
   slots the C verification marks "Y, never null", or a fabricated
   `vfuncDefaults` entry is added. The first keeps the emitted body
   byte-identical to stage-1.
4. **`Aggregator::aggregate` is checked at instance-init time**, not only at
   call time: `gst_aggregator_init` opens with
   `g_return_if_fail (klass->aggregate != NULL)` (gstaggregator.c:3186),
   before `self->priv` is assigned at :3188. A `g_return_if_fail` does not
   fail `g_object_new`: it logs a GLib CRITICAL and returns early, so a
   managed aggregator that declares no `AggregateOverride` is constructed with
   a NULL private and crashes on first use, and aborts outright under
   `G_DEBUG=fatal-criticals`. No ChainUp of `aggregate` is ever reached, so
   the "required slot throws `InvalidOperationException`" test of brief step
   (d) cannot be written on this slot. Write it on another required slot, or
   assert the CRITICAL with whatever the suite already uses for GLib
   criticals, and declare `AggregateOverride` in every aggregator fixture.
5. **Aggregator pad templates: "src" only is confirmed.**
   `gst_aggregator_init` looks up exactly one template,
   `gst_element_class_get_pad_template (klass, "src")` with a
   `g_return_if_fail` (gstaggregator.c:3192-3194); no sink template is
   required at init, sink pads arrive through `request_new_pad`.
6. **Annotation carriers.** The pairing attaches the correction to the virtual
   method only. The `<record><field><callback>` carrier is inert for the
   mirror — an `nint` slot has no annotation — so the "both carriers" wording
   of the spec is satisfied by correcting the one carrier the generator reads.
7. **`Gst.Object`'s mirror is named `ObjectClassRaw` under the `<Class>ClassRaw`
   rule**, where stage-1 calls it `GstObjectClassRaw`; it is internal, but
   `tests/GstSharp.IntegrationTests/AbiProbeTests.cs` names it and must be
   updated in the swap commit. `Gst.Object`'s gir parent is
   `GObject.InitiallyUnowned`, so the chain walker must map both
   `GObject.ObjectClass` and `GObject.InitiallyUnownedClass` onto the
   hand-written `Gst.GObject.GObjectClassRaw`.
8. **`new` on `DefineSubclass`**: emit `new` iff a transitive parent is in the
   allowlist (Element none, everything else `new`, including Aggregator).
