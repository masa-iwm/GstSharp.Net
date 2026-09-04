# E5 Stage 2a, wave 1 — handoff

Branch `e5-stage2`, worktree of this checkout, based on `main` aa7e484. This
file is scratch for the next agent and is deleted before the wave is merged
(the last commit of the wave is "Remove the wave handoff notes").

## Done

Twenty-two commits on top of `main`. The first fourteen are the class struct
model, the mirror emitter, the ABI registry, the planner, the emitter and the
atomic swap; see `git log` for their messages. The eight of this session answer
the findings of the wave-1 review:

* **MAJOR-1 / M2** "Let a chain-up honour an identity preserving handle" — an
  identity `out` handle hands the wrapper of the input back instead of adopting
  the same pointer twice, and an identity `inout` handle mints nothing on the
  way in and replaces the wrapper only when the pointer changed.
* **M1** "Chain up on raw handles below the managed surface" — the static
  chain-up of a handle answering slot returns `nint`; only the protected
  instance member builds a wrapper.
* **MAJOR-4** "Refuse a slot that lends a boxed instance" — a transfer-none
  boxed parameter is auto-skipped, which removes `BaseSrc::do_seek`,
  `BaseSrc::prepare_seek_segment` and `BaseTransform::filter_meta`.
* **MAJOR-2 / M7** "Answer what the base class answers for a NULL slot" — 23
  new `vfuncDefaults` entries, a statement block form for the overlay value,
  and a per type "no value" for out scalars.
* **M6** "Refuse a C long in a class struct mirror" — GEN0035, an error.
* **M4** "Let a slot say that its answer may not be null" — the overlay key
  `vfuncNonNullReturns`, GEN0036.
* **MAJOR-3 / M3** "Document what a slot says and what it owes" — the gir doc
  pipeline, per bucket ownership sentences, the overlay key `vfuncDocNotes`,
  GEN0037.
* **MAJOR-1 test** "Drive the identity chain-up through a running pipeline".

All gates green at this commit: `dotnet build` 0 warnings, generator twice
byte-identical, `verify` clean (506 files), 1748 tests pass in all four
projects, `dotnet pack` of both packages 0 CP diagnostics, AotSmoke publishes
with 0 IL/AOT warnings.

Counts: 97 slots emitted (Gst 17, GstBase 80), 8 class struct mirrors (Gst 3,
GstBase 5), 12 slots in the ledger (Gst 7, GstBase 5).

## Remaining

* **M5 — gate holes.** Three parts, none written:
  1. tests that fire GEN0027–GEN0031 and the new GEN0035 / GEN0036 / GEN0037 on
     a bad overlay key (`GstSharp.Generator.Tests`);
  2. census assertions for the categories `"vfunc"` (97) and `"class struct"`
     (8), next to the existing fixed counts in `RecordEmitterTests` /
     `ClassEmitterTests`;
  3. the generic `ClassStructRegistry.CreateEntries()` Theory of spec 0.8 in
     `AbiProbeTests`: every registry entry's `Unsafe.SizeOf` equals
     `g_type_query(...).class_size`. `AbiProbeTests` asserts the mirrors one by
     one today, so the new Aggregator mirror is size-probed only by name.
* **docs.** `docs/subclassing.md` §7/§10 still describe stage 2 as future work,
  §4.4 still shows the hand written chain-up sketch, and §2 lists `do_seek`
  among the key vfuncs of `BaseSrc` without saying it is not bindable. The
  release note line of spec §5 is unwritten.
* **the final commit** that deletes this file.

## Findings the next agent needs

1. `PushSrc::alloc` answers `Gst.FlowReturn.NotSupported` for a NULL slot,
   which is **not** what C does: `gst_push_src_alloc` (gstpushsrc.c:130-135)
   falls back to `GST_BASE_SRC_CLASS (parent_class)->alloc`, and BaseSrc
   installs `gst_base_src_default_alloc`. A managed PushSrc that wants the
   pooled allocation chains up through `BaseSrc.ChainUpAlloc` instead, which
   the `new` modifier keeps reachable. `PushSrc::fill` is the same shape and is
   *safer* than C there, which would dereference a NULL BaseSrc slot.
2. `BaseSink::render_list` deliberately keeps the throw: a NULL slot changes
   the dispatch strategy - `gst_base_sink_chain_list` splits the list and pushes
   every buffer through `render` - which is behaviour and not a value.
3. `Aggregator::clip` answers the adopted handle raw, so the reference the
   caller gave up passes straight through and the instance chain-up wraps it
   again. It is refcount equivalent to "hand the wrapper back", not the same
   object.
4. `BaseTransform::filter_meta` is skipped under the boxed rule although its
   `params` structure is only read by the C caller. The rule is a blanket one:
   `Boxed` has no borrow mode at all.
5. The gir text of `Element::request_new_pad` says "Release after usage", which
   is the contract of the *invoker* and not of the slot; the generated return
   note says the answer is borrowed. A `#return` doc override would settle it.
6. Every generated mini object wrapper carries `internal static T Borrow(nint)`,
   so a transfer-none mini object parameter is bindable whatever its type. Two
   class structs redeclare a slot of their parent (`GstBaseSrcClass.query` and
   `GstBaseSinkClass.query`), which the emitter answers with `new`.
7. `HandleFlavor.Opaque` is still refused (`GstMeta` in
   `BaseTransform::transform_meta`): its wrapper has neither a transfer taking
   `FromNative` nor a `Dispose`.
8. `Gst.MiniObject.Dispose` is idempotent, which is what makes the identity path
   safe when the same wrapper is disposed twice.
