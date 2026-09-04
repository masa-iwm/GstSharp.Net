# E5 Stage 2a, wave 1 — handoff

Branch `e5-stage2`, worktree `GstSharp.Net-e5s2`, based on `main` aa7e484.
This file is scratch for the next agent and is deleted before the wave is
merged.

## Done

Fourteen commits on top of `main`, six of them this session. All gates green:
`dotnet build` 0 warnings, `dotnet pack` of both packages 0 CP diagnostics
against the 1.28.6 baseline,
generator twice byte-identical, `verify` clean (506 files), 1731 tests pass
in all four projects, AotSmoke publishes with 0 IL/AOT warnings.

The first five commits (c7eb44b … 4a64caf) are the class struct model, the
mirror emitter and the ABI registry; see `git log` for their messages.

* **c0aa5de "Plan the marshalling of a virtual method"** —
  `Planning/VirtualMethodPlan.cs` (`VfuncBucket`, `VfuncReturnBucket`,
  `VfuncArgument`, `VirtualMethodPlan`) and `MarshalPlanner.PlanVirtualMethod`
  / `PlanVirtualMethodArgument` / `PlanProducedArgument` /
  `PlanVirtualMethodReturn`, modelled on `TryPlanSignal`. A shape it refuses
  answers null, which is the auto-skip.
* **0af2ad7 "Emit the subclassing surface of an allowlisted class"** —
  `Emit/VfuncEmitter.cs` → `src/<Project>/Generated/Subclassing/<Class>.Subclass.cs`,
  census category `"vfunc"`, the `SubclassBaseRules` table, `EmissionCensus.SkippedVirtual`
  and the `## Virtuals` section of `girs/skip-report.md`. `skipVirtuals`
  changed shape from `string[]` to a map key → reason so the ledger can print
  it.
* **933311b "Generate the subclassing surface of the Gst and GstBase leg"** —
  the atomic swap: allowlist + the overlay data, the six hand written
  `*.Subclass.cs` and both hand written mirror files deleted, the ABI probes
  renamed onto `Gst.ObjectClassRaw` and the drifted field names, and the two
  fixed counts in `RecordEmitterTests`/`ClassEmitterTests` updated.
* **3db13dc "Plan the inout handle of a virtual method"** — an inout handle is
  planned as the out one it shares its native shape with, which lands
  `GstBase.BaseSrc::create` with the identity rule.
* **"Hand a produced handle over only on success"** — the write-back of a
  handle a `GstFlowReturn` slot produces is guarded by
  `if (result == Gst.FlowReturn.Ok)`, which is what stage-1 `PushSrc` spelled
  by hand: the caller does not read the pointer on any other answer, so a
  reference minted for it would be one nobody releases. The instance chain-up
  also reads its own handle before it mints anything.

100 slots are emitted (Gst 17, GstBase 83) and 9 are listed in the ledger:
8 named by `skipVirtuals`, 1 `UnsupportedSignature`
(`BaseTransform::transform_meta`, the one that lends a `GstMeta`).

## Remaining (brief steps h–j)

Tests and docs only. Nothing of the generator is left to write for wave 1.

* **New tests** per spec §3: one dispatch + chain-up + exception test per new
  class; `Aggregator::aggregate` as a required override, `BaseSrc::create`
  with the same and with a different handle, `BaseTransform::prepare_output_buffer`
  identity, `sink_event` adopt → chain-up pass-through, `request_new_pad`
  borrowed return. The generic ABI theory over `ClassStructRegistry.CreateEntries()`
  (spec 0.8) is also unwritten; `AbiProbeTests` still asserts the mirrors one
  by one.
* **docs**: `docs/subclassing.md` §7/§10 still describe stage 2 as future work,
  and §4.4 still shows the hand written chain-up sketch. The release note line
  of spec §5 is unwritten.

## Findings that the next agent needs

1. **Done in wave 1**: every generated mini object wrapper carries
   `internal static T Borrow(nint)` and its private `Borrowed` constructor, so a
   transfer-none mini object parameter is bindable whatever its type. That
   unlocked 20 of the 21 `UnsupportedSignature` slots. Two class structs
   redeclare a slot of their parent (`GstBaseSrcClass.query` and
   `GstBaseSinkClass.query` over `GstElementClass.query`), which the emitter
   answers with the `new` modifier, keyed on the managed shape of the member.
2. `HandleFlavor.Opaque` is refused as well (`GstMeta` in
   `BaseTransform::transform_meta`): its wrapper has neither a transfer taking
   `FromNative` nor a `Dispose`, so the borrow has no shape.
3. The stage-1 `configureClass` is **non-nullable on BaseSrc, BaseSink and
   BaseTransform too**, not only on PushSrc — `repo-survey.md` §2,
   `notes-wave1.md` and spec 0.2 all say "nullable" for those three and are
   wrong against the code. `SubclassBaseRules` therefore keys the
   `ArgumentNullException.ThrowIfNull` on "requires at least one pad template",
   which also gives Aggregator the non-nullable shape.
4. A handle a slot answers is nullable in the managed surface
   (`protected virtual Gst.Caps? OnFixate(...)`): the parent slot may leave the
   pointer NULL and a chain-up hands that on. None of the frozen sixteen
   answers a handle, so nothing shipped changed.
5. `PlanScalar` refuses `ArgumentDirection.Ref` for a handle unconditionally
   (`MarshalPlanner.cs`, the `if (direction == ArgumentDirection.Ref)` guard in
   the handle branch). `PlanVirtualMethodArgument` works around it rather than
   relaxing it, because the guard is right for a forward call.
6. `Gst.MiniObject.Dispose` is idempotent (`Interlocked.Exchange` of the
   handle), so the identity path disposing the very wrapper an outer `using`
   also disposes - `prepare_output_buffer` answering its `input` - is safe. The
   identity test of step (h) is what pins that.
7. The emission census counts are asserted in
   `RecordEmitterTests.EveryModuleEmitsItsOwnFiles` /
   `TheRecordCensusIsStable` and `ClassEmitterTests` — `CensusTests` counts the
   **gir** and needed no change.
