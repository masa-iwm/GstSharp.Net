# E5 Stage 2b wave 1 — handoff

Worktree `C:\src\_worktrees\e5s2b-codec`, branch `e5s2b-codec`, based on main `ac48d7e`.
Everything below is committed; the tree is clean and green.

## Done

Commits on the branch (oldest first):

1. `99490d9` **Lend and hand over a boxed value across a class struct slot** — spec items B, C, D.
   * `src/GstSharp.Net/Core/GObject/Boxed.cs`: `internal Boxed(Borrowed, GType)` (no copy, no
     free, disposing only detaches), `internal nint HandOver()` (owned → detach, borrowed →
     `BoxedCopy`), `Dispose(bool)` skips the free for a borrowed wrapper.
   * `generator/.../Emit/RecordEmitter.cs` emits `Borrow(nint)` + the private `Borrowed` ctor on
     every generated boxed wrapper (`WriteBorrow(..., boxed: true)`); `TypeSurface.BoxedNames`
     reserves `Borrow` and `HandOver`.
   * `generator/.../Planning/MarshalPlanner.cs`: `BorrowBoxed` for a lent boxed argument (the
     `BoxedParameterReason` constant is gone), `Adopt` accepts `MarshalKind.Boxed`,
     `InOutHandOver` replaces the `InOutHandOverReason` refusal. `VfuncArgument` carries
     `IsBoxed`.
   * `generator/.../Emit/VfuncEmitter.cs`: trampoline and chain-up for both new buckets, the
     parent-slot guard emitted before anything is handed over, boxed release through
     `GObjectNative.BoxedFree`.
   * Un-skips six Stage 2a slots (`BaseSrc::do_seek`, `BaseSrc::prepare_seek_segment`,
     `BaseTransform::filter_meta`, `AudioFilter::setup`, `VideoFilter::set_info`,
     `VideoSink::set_info`); ledger 15 → 9. Census and the two record snapshots updated;
     `VirtualOverlayDiagnosticTests.AnInOutHandleThatHandsOwnershipOverIsRefused` rewritten as
     `...IsEmitted`.
2. `8b8cc40` **Subclass the parser and the four codec base classes** — spec item A + census.
   * `girs/overlays/fixups.json`: the 5 allowlist entries, the corrected `$comment-subclassable`,
     16 `annotationOverrides` (the 15 from the dry run, each with its C file:line, plus
     `GstBase.BaseParse::convert#dest_value` direction out), 8 `vfuncDefaults`, 2
     `vfuncNonNullReturns`, 7 `vfuncDocNotes`.
   * `generator/.../Emit/VfuncEmitter.cs` `BaseRules`: `("sink", "src")` and a required
     `handle_frame` for all five classes.
   * New hand written defaults: `src/GstSharp.Net.Base/Custom/BaseParseDefaults.cs`,
     `src/GstSharp.Net.Audio/Custom/CodecDefaults.cs`,
     `src/GstSharp.Net.Video/Custom/CodecDefaults.cs`.
3. `9c0228d` **Let a caps query without a filter reach the codec slots** — five extra
   `#filter` nullable overrides (see deviations).
4. `121c3d2` **Write the output buffer and the flags of a parse frame** —
   `src/GstSharp.Net.Base/Custom/BaseParseFrame.cs` with `AddFlags` and `SetOutBuffer`.

### Numbers (from the generator's own census, verified by the tests)

* Per module: `Gst 3/17`, `GstBase 6/96`, `GstAudio 7/55`, `GstVideo 4/45`.
* Run total: **20 mirrors, 213 slots**.
* Virtuals ledger: **9**, all pre-existing —
  `Gst.Bin::{deep_element_added,deep_element_removed,element_added,element_removed}`,
  `Gst.Element::{no_more_pads,pad_added,pad_removed}` (class closures),
  `GstAudio.AudioSink::stop` (name collision), `GstBase.Aggregator::create_new_pad` (Stage 3).
  **No new skip for any of the five classes.**
* `ClassEmitterTests`: 209 classes (comment rewritten), 131 records unchanged.
  `RecordEmitterTests.EveryModuleEmitsItsOwnFiles`: Base 32, Audio 52, Video 81.

### Gates run

* `dotnet build -v q --nologo` — warning free.
* `dotnet run --project generator/GstSharp.Generator -- generate` twice + `verify` — byte identical,
  "522 generated file(s) are up to date".
* `dotnet test tests/GstSharp.Generator.Tests` — 847 passed.
* `dotnet test tests/GstSharp.Core.Tests` — 120 passed.
* `dotnet test tests/GstSharp.IntegrationTests` — 819 passed, 1 skipped (this includes the generic
  ABI `Theory`, which picks the five new mirrors up automatically: their sizes match the running
  library).
* NativeAOT publish — **not run yet** (spec E.5).

## Remaining (spec items E.2 – E.6, verbatim scope)

* **E.2 ABI slot probes + CI**: five `[RequiresElementFact]` slot-content probes in
  `tests/GstSharp.IntegrationTests/AbiProbeTests.cs` (BaseParse→`wavparse`,
  AudioDecoder→`vorbisdec`, AudioEncoder→`vorbisenc`, VideoDecoder→`theoradec`,
  VideoEncoder→`theoraenc`) asserting the `handle_frame` slot is non-NULL; append
  `wavparse,vorbisdec` to `GSTSHARP_REQUIRED_ELEMENTS` in `.github/workflows/ci.yml` (~line 253,
  Linux job) and mention them in `docs/subclassing.md` where the required elements are listed
  (commit `ac48d7e` is the pattern).
* **E.3 pipeline tests** (follow `SubclassAudioVideoTests.cs` + `Probe*.cs` +
  `SubclassBufferOwnershipTests.cs`): managed BaseParse, AudioEncoder, AudioDecoder,
  VideoEncoder, VideoDecoder through bounded pipelines; the `pre_push` ownership test with all
  three outcomes plus a trap; BorrowBoxed invalidation (using the wrapper after the call throws
  `ObjectDisposedException`); one `sink_event` adopt → chain-up pass-through per module;
  `DefineSubclass` without `HandleFrameOverride` throws `ArgumentException` for each of the five;
  one test that `AudioFilter::setup` is now bound. One managed codec added to
  `samples/AotSmoke` following `ManagedAudioSink.cs`.
  `BaseParseFrame.AddFlags` / `SetOutBuffer` (commit 4) are what a managed parser writes through.
* **E.4 docs**: `docs/subclassing.md` (class table = 19 allowlist classes, the inout+full "third
  form", the BorrowBoxed rule, remove the six now-bound items from the limits list, AggregatorPad
  stays Stage 3) and `docs/ownership.md` (one paragraph each for the third form and the
  trampoline-scoped boxed borrow). `CONTRIBUTING.md` needs nothing — no overlay key was added.
* **E.5**: `dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true` with zero
  IL/AOT warnings (prepend `C:\Program Files (x86)\Microsoft Visual Studio\Installer` to `PATH`
  in that shell, per `CLAUDE.local.md`).
* **E.6**: keep committing in logical steps with the `Co-Authored-By: Claude Fable 5.1
  <noreply@anthropic.com>` trailer. Do not push.

## Deviations from the frozen spec (all deliberate, with reasons)

1. **`Invalidate()` is spelled as `Dispose()`** on a borrowed boxed wrapper: disposing detaches
   without freeing, exactly as `MiniObject` does for a borrowed mini object, and the trampoline
   scopes it with `using`, which gives the `finally` semantics the spec asked for. A separate
   `Invalidate` member would have been a second name for the same operation.
2. **The four `getcaps` defaults call `<Class>Defaults.ProxyGetcaps(nint, nint)` helpers** rather
   than the public `ProxyGetcaps` method: the null-slot branch is emitted inside the *static*
   chain-up, which only has raw pointers and has to answer a raw handle. Same reason
   `BaseParse::get_sink_caps` and `::pre_push_frame` call `BaseParseDefaults`.
3. **The BorrowBoxed XML doc does not say "call `Copy()`"** — `VideoCodecFrame` and
   `BaseParseFrame` have no `Copy` member. It says the wrapper stops meaning anything when the
   call returns and that what has to outlive it must be copied out.
4. **`configureClass` is non-nullable for the five classes.** The spec's A.4 asks for nullable,
   but the emitter derives that from the pad-template rule (`bool mandatory = rule is
   { PadTemplates.Count: > 0 }`), and `BaseTransform`, which A.4 names as the model, is
   non-nullable today for the same reason. The survey's claim was wrong; the emission is
   consistent with the codebase.
5. **Five `#filter` nullable overrides beyond the spec's 16** (commit 3). A CAPS query carries a
   filter only when the peer asked for one; without the annotation an override that chains up
   threw. Addition, not a change of the enumerated 16.
6. **`BaseParseFrame` writers are hand written in `Custom/`** (commit 4). Generated boxed record
   accessors are read-only across the board, and adding field setters to the generator is a wave
   of its own.
7. **Census bumps are folded into the commits that move the numbers**, so that every commit is
   green on build *and* test, rather than being one commit of their own.

## Open questions

* `eng/ci-notes.md` still describes the pre-`ac48d7e` required-element set. Sync it when E.2
  touches `ci.yml`, or leave `ci.yml` as the single source of truth?
* The spec's E.3 asks for a VideoDecoder test that asserts "no leak via the frame's refcount where
  observable" — `VideoCodecFrame` exposes no refcount, so the observable proxy is
  `SetUserData(notify)` running when the frame is released.

## Exact next command

```sh
cd C:/src/_worktrees/e5s2b-codec
dotnet test tests/GstSharp.IntegrationTests --logger "console;verbosity=minimal"
```

then start E.2 by reading `tests/GstSharp.IntegrationTests/AbiProbeTests.cs:1900-1990` (the four
existing slot-content probes) and `.github/workflows/ci.yml:253`.
