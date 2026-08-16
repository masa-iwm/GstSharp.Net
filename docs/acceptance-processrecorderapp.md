# Acceptance requirements: ProcessRecorderApp

GstSharp.Net is published to NuGet only after ProcessRecorderApp
(github.com/masa-iwm/ProcessRecorderApp) works against it, consuming packages
from GitHub Packages. This document distills the app analysis (2026-08-15,
branches `main` = GirCore variant, `gstsharpbundle` = gstreamer-sharp fork
variant) into requirements and tests. preview1 gates on sections 1-3;
section 4 is the preview2 intake.

**Status: preview1 acceptance PASSED (2026-08-16).** ProcessRecorderApp PR #1
(branch `gstsharpnet`) ports the app to GstSharp.Net 1.28.0-preview.1: CI green
with the full L2+L3 suite against both the CoreCLR and NativeAOT
(`TrimMode=full`, no `TrimmerRootAssembly`) publishes, and real-GPU
verification passes 9/9 encoder cases — including runs where the app loads the
bundled runtime tree. Sections 1-3 are settled; section 4 below is the
preview2 work order, refreshed with what the port actually found.

**Status: preview2 re-verification PASSED (2026-08-16).** ProcessRecorderApp
PR #2 moves to 1.28.0-preview.2 and absorbs every self-implementation:
resolution is fully delegated to the loader (the app's locator is deleted),
the debug category, camera enumeration and monitor resolutions all go through
the public API, and no raw GStreamer P/Invoke remains in the app. Real-GPU
verification passes 9/9 encoder cases again, this time with the loader's
bundled-runtime stage selecting the shipped `runtimes/<rid>` tree in
production, and the multi-adapter device ordering, preview and camera checks
all confirmed on real hardware. Section 4 items 1-4 are shipped; what remains
before NuGet is release mechanics, not binding work.

The app is a pure consumer: no subclassing, no custom elements, no GMainLoop
(bus is polled). Required modules: Gst core + GstApp + GstBase (only
`BaseSrc` as a runtime-recognizable type). GstVideo/GstAudio/GstPbutils are
not used by the app. preview1's module set is a superset — no gap.

## 1. API checklist (must exist and behave as noted)

Gst core:
- `Parse.Launch` (must accept caps like `video/x-raw(memory:D3D12Memory)`),
  result castable to `Pipeline`; `Bin.GetByName` castable to
  `AppSink`/`AppSrc` via `as`/`is` (see §2 GType map)
- `Object.Name` get/set; `Element.SetState`; `Element.GetState(out, out,
  timeout)` with full `StateChangeReturn` (incl. `NoPreroll`)
- `Pipeline.Bus`; `Bus.PopFiltered`; `Bus.TimedPopFiltered` (timeout 0 =
  non-blocking)
- `Message.Type` / `Message.Src` (borrow that stays valid while the message
  wrapper lives) / `ParseError` / `ParseWarning` — the `debug` out-string is
  transfer-full and MUST be freed with `g_free`
- `Buffer`: Pts/Dts get (properties) + set (`SetPts`/`SetDts`, and
  `SetDuration`/`SetOffset`/`SetOffsetEnd`, which throw when the buffer is
  not writable), `HasFlags`, `Size`, metadata-only copy
  (`CopyRegion` with an `All`-style composite of `BufferCopyFlags`),
  first-class `MakeWritable`
- `Sample.Buffer` / `Sample.Caps`; `Caps.GetStructure`; `Structure.Name` /
  `GetInt` / `GetFraction`
- `ElementFactory.Find` (nullable); property set by name with string value
- Debug: `SetActive`, `SetThresholdFromString`, `BinToDotData`;
  `ClockTime.None`

GstApp:
- `AppSink.TryPullSample(ClockTime)` -> nullable owned `Sample`
- `AppSrc.PushBuffer` (consumes, gir transfer=full) / `AppSrc.PushSample`
  (does NOT consume, transfer=none) / `Caps` setter / `EndOfStream()` /
  `CurrentLevelBuffers`

GstBase:
- `BaseSrc` recognizable via `Message.Src is BaseSrc` (GType map, not a cast
  of convenience).

## 2. Cross-cutting requirements (silent-failure class)

1. **GType -> managed type registry** must cover every emitted class.
   Failure mode is `GetByName(...) as AppSink == null` and
   `msg.Src is BaseSrc == false` with no error. Under NativeAOT +
   `TrimMode=full` this must work with no `TrimmerRootAssembly` entry
   (the GirCore/gstreamer-sharp variants both failed here; the app carries
   a trimmer root workaround today).
   Each binding assembly fills the registry from a `[ModuleInitializer]`,
   which CoreCLR runs before the first *call* into that assembly and not
   before one of its types is named in a cast. `GstSharp.Initialize()`
   therefore runs the module initializer of every loaded `GstSharp.Net*`
   assembly and subscribes to `AppDomain.AssemblyLoad` to do the same for
   assemblies loaded later; under NativeAOT they have all run at startup and
   the sweep finds nothing to do. `Gst.Base.GstBase.Initialize()` and the
   per-module forwarders next to it are the deterministic way to say the same
   thing. A wrapper keeps the type it was created with, so an object wrapped
   before its module registered stays the base type it was built as —
   initialize first; `GstSharp.TypeFallback` reports (once per GType) when an
   object is wrapped as an ancestor, which is how the otherwise silent case
   becomes visible.
2. **Ownership policy for transfer-none getters.** GirCore returns borrows
   (app double-freed); the gstreamer-sharp fork returns owned copies (app
   leaked ~150 MB/min until it added `using`). GstSharp.Net has one rule per
   object model, and which one applies follows from the base type:
   - **`MiniObject` and `Boxed`** (Buffer, Caps, Sample, Message, Event,
     Structure, ...): every wrapper handed to user code owns a reference of
     its own — a mini object is reffed, a boxed value is copied — and **must
     be disposed**. Wrappers are not interned: two lookups of the same object
     give two wrappers holding two references. `GST0001` flags a local that is
     never disposed.
   - **`GObject`** (Element, Pad, Bus, Pipeline, ...): the wrapper is
     interned. Every lookup of the same object hands out the *same* instance,
     and that instance owns one reference for the whole process, held through
     a toggle reference. `Dispose` therefore does not mean "release my
     reference", it means "this process is done with the object": it
     disconnects the handlers the wrapper connected and gives up its part in
     the lifetime, for every holder at once. Normally do not call it — let the
     collector take the wrapper and the runtime release the object. Dispose a
     GObject wrapper only for something this code created and is done with,
     for example a pipeline that has been set to `NULL`.
3. **Deterministic release.**
   - Mini objects and boxed values release synchronously: `Dispose` unrefs on
     the calling thread and the finalizer unrefs directly. Nothing is ever
     deferred through a GLib timeout or idle — this app has no main loop, and
     gtk-sharp's deferred path leaks permanently.
   - A GObject finalizer must not unref: removing the toggle reference races
     with the toggle notification and would call into GStreamer from the
     finalizer thread. It enqueues the release instead, and the queue is
     drained on a thread that may call native code — on every GObject wrapper
     lookup, on every mini object that is adopted, from the idle callback of a
     running main loop, and from `GstSharp.DrainPendingReleases()`. An
     application that pulls samples in a loop drains it constantly and needs
     nothing; **an application with no main loop that also goes long stretches
     without touching a wrapper should call `GstSharp.DrainPendingReleases()`
     periodically**, for example once per poll of the bus. The queue holds one
     small record per pending object, never a copy of the media.
4. **DebugCategory must be wrapped by pointer**, not as a by-value struct:
   a value copy snapshots the threshold and runtime `GST_DEBUG` changes are
   lost. (Done: `forceOpaque` in `girs/overlays/fixups.json` emits it as an
   opaque pointer wrapper. Creating a category is still impossible from app
   code — that half remains §4.4.)
5. **No flavor mixing.** One (flavor, directory) pinned for every module;
   expose which root won so apps can log it. (Done: `NativeLoader` exposes
   `ResolvedDirectory`, `ResolvedFlavor`, `ResolvedOrigin`,
   `ResolvedSourceDescription` and `GetLoadedModulePath`. The Windows search
   also scans the PATH directories first among the implicit stages and probes
   an application-bundled `runtimes/<rid>` tree — both flavors, MSVC
   preferred — after every installed source, so an application no longer
   needs its own locator.)

## 3. Packaging constraints

- All package IDs share the `GstSharp.Net*` prefix (the app's
  `packageSourceMapping` is per-prefix; non-matching IDs silently fall back
  to nuget.org and fail restore). Single version for the whole set.
- App uses Central Package Management: every package needs a
  `PackageVersion` entry. App TFM is `net10.0-windows10.0.19041.0`; our
  packages stay plain `net10.0` (never force a TFM lift). RID `win-x64`,
  `PublishAot=true` + `TrimMode=full`.
- GitHub Packages feed: `https://nuget.pkg.github.com/masa-iwm/index.json`;
  auth required even for public packages (`read:packages` scope; the app
  repo's nuget.config on `gstsharpbundle` documents the env-var credential
  pattern).
- If a native runtime package is ever shipped: use `runtimes/<rid>/native/`
  assets, never `contentFiles` (WinAppSDK PRI generation breaks on hyphened
  paths: PRI249/PRI252).

## 4. preview2 intake (from the app's hand-rolled code)

Priority order:
1. **Device enumeration**: `DeviceProviderFactory.GetByName`,
   `DeviceProvider.Start/Stop/GetDevices`, `Device.DisplayName/Caps/
   Properties`, with safe GList-of-transfer-full-GstObject marshalling
   (GirCore's GList wrapper corrupted the managed heap; the app rewrote
   this path in raw C). Note `gst_device_get_properties` returns a
   `GstStructure*` freed with `gst_structure_free`, not mini-object unref.
2. **appsink callback options** (today the app polls `TryPullSample` on its
   own threads with a 100 ms timeout because of binding gaps):
   - leak-free typed `new-sample`/`new-preroll` signals + `emit-signals`
     (the signal itself carries no arguments; the historic leak was the
     no-op `Unref` in gstreamer-sharp's Opaque plus per-emission
     reflection);
   - `gst_app_sink_set_simple_callbacks` (GStreamer >= 1.28, introspectable,
     the designed-for-bindings API; immutable once installed; boxed
     `GstAppSinkSimpleCallbacks` builder with `set_new_sample` etc.). The
     standard `scope="notified"` + closure + destroy annotations are on the
     setters of the builder, not on the install, which carries none: its `cb`
     parameter is `transfer-ownership="full"`, which is why the generator
     skips it. preview1 already emits the `AppSinkSimpleCallbacks` /
     `AppSrcSimpleCallbacks` builder types; the missing piece is binding the
     install method itself, which currently leaves the builders orphaned.
     (Done: `Custom/AppSink.cs` and `Custom/AppSrc.cs` bind both installs by
     hand along the lines of `PushBuffer` — the call takes the builder over,
     which seals it the way the C API documents. A convenience overload takes
     the delegates directly. `AppSinkSimpleCallbacksTests` pins the install,
     the destroy notification of every slot, replacement and removal against
     the installed library.)
   - `gst_app_sink_set_callbacks` is `introspectable="0"` (struct of
     function-pointer fields) — out of scope; simple callbacks supersede it.
3. **Already shipped in preview1 — no work needed** (this entry predates the
   port): gpointer property read is `Object.GetProperty(name)` returning an
   owned `Value` with `GetPointer()`, and action-signal emit is
   `Object.EmitSignal("resize", w, h)` with pre-emission signature validation.
   Both are verified in production by the app's preview path (swapchain
   handle, resize).
4. **DebugCategory.New + Log** (`GST_DEBUG_CATEGORY_INIT` is a macro,
   absent from gir; needs hand binding, pointer-based per §2.4). The public
   `Gst.Global.DebugLogLiteral` exists but is unusable from app code because
   `DebugCategory` cannot be created (ctors internal, `_gst_debug_category_new`
   not in gir). The app keeps a raw P/Invoke pair
   (`_gst_debug_category_new` — leading underscore is the real export — plus
   `gst_debug_log_literal` with an `IntPtr` category) until this lands.
5. **Small gaps found during the preview1 port (2026-08-16):**
   - `gst_message_parse_info` has no binding (only `ParseInfoDetails`); the
     app's only use was in dead code, but the parse-API family is incomplete.
   - `Buffer.Copy()` convenience is absent — consumers write
     `CopyRegion(BufferCopy.All, 0, nuint.MaxValue)` to get `gst_buffer_copy`.
   - ~~No public API reports the actually-loaded module file paths~~ —
     done: `NativeLoader.GetLoadedModulePath(logicalName)` answers from the
     module handle (truthful even for bare-name loads), and
     `ResolvedOrigin`/`ResolvedSourceDescription` name the winning search
     stage.
   - `Custom/Caps.cs` doc/code mismatch: the `MakeWritable` remarks describe
     `GetStructure` results as borrows that write through on writable caps;
     the generated method returns an independent boxed copy (mutations are
     lost). Fix the docs, or provide a write-through mutation path.
   - `Custom/AppSrc.cs` stale remark: claims the push method is named `Push`
     because of an action-signal collision; the method is `PushBuffer` and no
     collision exists.

## 5. Acceptance tests (smallest set that catches every bug the app hit)

1. Parse tee pipeline, `GetByName(...) as AppSink` non-null (GType map).
2. `TryPullSample(100ms)` loop, 60 s at ~20 Mbit: flat RSS and flat
   mini-object refcounts.
3. Ownership policy tests per getter (`Sample.Buffer`, `Sample.Caps`,
   `Caps.GetStructure`, `Message.Src`).
4. `emit-signals=true` + `new-sample` handler pulling samples, 60 s, flat
   memory.
5. `Message.ParseError` x 100k: `debug` string freed.
6. `PushBuffer` consumes / `PushSample` does not (refcount asserts).
7. `TimedPopFiltered(0)` returns immediately; `PopFiltered` drains to null
   without any main loop.
8. `msg.Src is BaseSrc` true for a real source error.
9. AOT publish (`TrimMode=full`) of a sample doing 1+8 runs correctly —
   the failure mode is a null cast, not a build warning.
10. Clean-machine restore from the GitHub Packages feed with
    `packageSourceMapping` pattern `GstSharp.Net*` under CPM.
