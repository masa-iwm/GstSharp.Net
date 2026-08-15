# Acceptance requirements: ProcessRecorderApp

GstSharp.Net is published to NuGet only after ProcessRecorderApp
(github.com/masa-iwm/ProcessRecorderApp) works against it, consuming packages
from GitHub Packages. This document distills the app analysis (2026-08-15,
branches `main` = GirCore variant, `gstsharpbundle` = gstreamer-sharp fork
variant) into requirements and tests. preview1 gates on sections 1-3;
section 4 is the preview2 intake.

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
- `Buffer`: Pts/Dts get+set, `HasFlags`, `Size`, metadata-only copy
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
2. **Uniform ownership policy for transfer-none getters.** GirCore returns
   borrows (app double-freed); the gstreamer-sharp fork returns owned copies
   (app leaked ~150 MB/min until it added `using`). GstSharp.Net policy:
   every wrapper handed to user code owns a reference (adoption with
   `Transfer.None` takes a ref — cheap ref, not a deep copy) and is released
   by `Dispose`. One discipline everywhere; document on each getter.
3. **Deterministic release.** `Dispose` unrefs synchronously; finalizers
   unref directly. Never defer an unref through a GLib timeout/idle — this
   app has no main loop, and gtk-sharp's deferred path leaks permanently.
4. **DebugCategory must be wrapped by pointer**, not as a by-value struct:
   a value copy snapshots the threshold and runtime `GST_DEBUG` changes are
   lost. (Currently classified PlainStruct — needs a fixup before the debug
   API ships.)
5. **No flavor mixing.** One (flavor, directory) pinned for every module;
   expose which root won so apps can log it.

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
     standard `scope="notified"` + closure + destroy annotations — the
     designed-for-bindings API; immutable once installed; boxed
     `GstAppSinkSimpleCallbacks` builder with `set_new_sample` etc.);
   - `gst_app_sink_set_callbacks` is `introspectable="0"` (struct of
     function-pointer fields) — out of scope; simple callbacks supersede it.
3. **gpointer property read** (e.g. `d3d12swapchainsink` `swapchain`) and
   **action-signal emit by name with arguments** (e.g. `resize`).
4. **DebugCategory.New + Log** (`GST_DEBUG_CATEGORY_INIT` is a macro,
   absent from gir; needs hand binding, pointer-based per §2.4).

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
