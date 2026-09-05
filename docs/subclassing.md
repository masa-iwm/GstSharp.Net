# GObject subclassing in GstSharp.Net — design

Status: **approved design**; stages 0, 1, 2a and 2b of §10 have shipped;
stage 3 has not. §11 is the guide to what shipped; everything before it is the design the
implementation follows.
Scope: class-struct ABI, vfunc overrides, managed type registration.
Audience: contributors to the runtime (`src/GstSharp.Net/Core`) and the
generator (`generator/GstSharp.Generator`); §11 is for applications.

---

## 1. Goal and non-goals

### Goal

Let application code written in C# derive from wrapped GObject classes and
have GStreamer call the managed overrides through the native vtable — under
the constraints this binding is built on: NativeAOT-first, zero reflection on
runtime paths, everything dispatched through static function pointers.

The classes that matter, in order:

1. **`Gst.Element`** (`<class name="Element" glib:type-struct="ElementClass">`
   in `Gst-1.0.gir`) — the root of everything an application can plug into a
   pipeline. Key vfuncs: `change_state` (invoker `change_state`),
   `request_new_pad` (invoker `request_pad`), `release_pad`, `set_state`,
   `provide_clock`, `send_event`, `query`, `post_message`, `set_context`
   (16 `<virtual-method>` elements on the class).
2. **`GstBase.BaseSrc` / `GstBase.PushSrc`** (`BaseSrcClass`,
   `PushSrcClass` in `GstBase-1.0.gir`) — custom sources. Key vfuncs:
   `create`, `fill`, `alloc`, `start`, `stop`, `is_seekable`, `get_size`,
   `unlock`, `unlock_stop`, `set_caps`, `get_caps`, `fixate`, `query`,
   `event`. `do_seek` and `prepare_seek_segment` lend a `GstSegment` by
   pointer for the slot to write into; so do `BaseTransform::filter_meta`,
   `AudioFilter::setup` and the `set_info` of the video classes. A boxed
   wrapper that copied what it is handed would lose every write the override
   made, so those slots are bound through a *borrowing* boxed wrapper instead,
   which is detached again when the trampoline returns (§11). A record with no
   boxed type behind it - a video frame, a ring buffer specification - has no
   copy to make and is lent as it is.
3. **`GstBase.BaseSink`** (`BaseSinkClass`) — custom sinks: `render`,
   `preroll`, `render_list`, `set_caps`, `get_caps`, `start`, `stop`,
   `unlock`, `unlock_stop`, `propose_allocation`, `query`, `event`.
4. **`GstBase.BaseTransform`** (`BaseTransformClass`) — in-pipeline
   filters: `transform`, `transform_ip`, `transform_caps`, `fixate_caps`,
   `set_caps`, `accept_caps`, plus the two **data** fields
   `passthrough_on_same_caps` and `transform_ip_on_passthrough` that sit in
   the class struct before the vfunc slots.
5. **`Gst.Bin`** (`BinClass`) — custom containers: `add_element`,
   `remove_element`, `handle_message`.

A forward driver is **GES**: authoring a custom GES source ultimately needs a
registered `GstElement` subclass. The GES module itself is already bound and
works with the elements the libraries provide; what it cannot do until this
design's stage 3 lands is instantiate a *managed* element by type name
(§5.4), which is what a custom GES source amounts to.

### Non-goals (this design, all stages)

* **Overriding `GObjectClass.dispose` / `finalize`.** Those vfuncs run when
  the managed wrapper may already be collected or mid-release; the
  interaction with the toggle-ref lifecycle is not resolvable in general.
  Subclasses that need teardown use the `change_state` NULL transition or a
  future explicit hook.
* **Defining new GObject interfaces** from managed code. (Implementing an
  existing one landed in stage 3b, see §5.7.)
* **Dynamic types** (`g_type_register_dynamic`, `GTypeModule`) and full
  GStreamer plugin authoring (`gst_plugin_register_static`,
  `gst_element_register` making the type constructible by factory name).
  Plugin-style registration becomes possible once native-initiated
  construction lands (stage 3), but shipping a loadable plugin is out of
  scope for this design.
* **Installing properties and signals** on managed types
  (`g_object_class_install_property`, `g_signal_newv`) — stage 3+, not
  needed for the first usable sources/sinks.
* **Class finalization** — managed types are static types; their classes are
  never finalized (`class_finalize = NULL`, `base_init = NULL`,
  `value_table = NULL`).
* **32-bit validation.** The ABI probes (`tests/GstSharp.IntegrationTests/
  AbiProbeTests.cs`) assume the 64-bit layouts; that assumption carries over.

---

## 2. Where the repo stands today (survey result)

What the design builds on, with the exact names:

* **Wrapper interning + toggle refs** — `Gst.GObject.Object`
  (`src/GstSharp.Net/Core/GObject/Object.cs`): a
  `ConcurrentDictionary<nint, ToggleRef> Wrappers` interning table, a
  `ToggleRefs` identifier table (toggle `data` is a counter id, never a
  `GCHandle`), `FromNative(nint, Transfer)` as the only supported wrap path,
  finalizers that enqueue releases into `PendingReleases`, drained by
  `DrainPendingReleases()`. The wrapper is held strongly while native code
  holds any reference besides the toggle ref.
* **Type → factory dispatch** — `Gst.GObject.TypeRegistry`
  (`Core/GObject/TypeRegistry.cs`): a frozen dictionary of
  `nuint (GType) → TypeEntry { delegate*<nint, Transfer, object> Factory }`,
  fed by `NativeModule` tables that every generated assembly registers from a
  module initializer (`GstModule.Initialize`, `Generated/_Module.cs`,
  `ModuleTypeEntry`). `TypeRegistry.GetInstanceType(nint)` already reads the
  `GType` through the instance's class pointer
  (`*(nuint*)(*(nint*)handle)`) — the exact trick vfunc dispatch and
  `class_init` discrimination will reuse. Walking `g_type_parent` gives the
  ancestor fallback, reported once per type via the `Fallback` event.
* **Signals** — `SignalRegistry.Connect` passes `[UnmanagedCallersOnly]`
  statics plus a `CallbackHandle` (`GCHandle` in `user_data`, freed by
  `CallbackHandle.ClosureNotify`); the generated per-signal trampolines (e.g.
  `Element.PadAddedTrampoline` in `Generated/Element.cs`) already prove the
  **reverse-marshalling direction**: native → managed with
  `Object.FromNative(..., Transfer.None)`, `try/catch` →
  `ExceptionTrap.Report`. `DynamicSignalClosure` shows the closure/meta-
  marshal route for signature-generic dispatch.
* **Generator** — `GirReader` already parses everything subclassing needs:
  `glib:type-struct` (on `GirClass`/`GirInterface`), `glib:is-gtype-struct-for`
  (on `GirRecord`), and `<virtual-method>` into `GirVirtualMethod`
  (79 occurrences in `Gst-1.0.gir` alone). Today the `Classifier` maps class
  structs to `TypeKind.GTypeStruct`, which `Classifier.IsSkipped` drops
  entirely, and `InterfaceEmitter` states "The virtual methods of an
  interface are not bound". So the data is parsed and deliberately unused —
  the postponement was in emission, not in parsing.
* **Class surface** — generated classes are `partial`, non-sealed, with a
  `protected (nint, Transfer)` constructor; abstract gir classes stay
  `abstract` and carry a private `Concrete` subclass for the registry
  (`ClassEmitter.ConcreteName`). A user assembly can chain to that constructor
  and wrap a native subtype (see [`docs/modules.md`](modules.md)); what it
  cannot do is define a *new* `GType`, which is what §5.3 is about.
* **ABI validation** — `AbiProbeTests` asserts constant sizes/offsets of
  `MiniObjectRaw` (64 bytes), `BufferRaw` (112), `MapInfo` (104) and probes
  dynamically against values the library wrote. This is the template for
  class-struct validation (§6).
* **Quality gates** — warning-free build, byte-identical double generation,
  `CensusTests` (fixed emission counts), ABI probes, AotSmoke publish with
  zero AOT warnings. Every stage below has to keep all five green.

---

## 3. Type registration

### 3.1 `g_type_register_static`, not `_simple`

`GObject-2.0.gir` declares both. `g_type_register_static_simple` takes
`(parent_type, type_name, class_size, class_init, instance_size,
instance_init, flags)` — **no `class_data`** — and is marked
`introspectable="0"`. `g_type_register_static` takes a full `GTypeInfo`,
whose `class_data` member is delivered as the second argument of
`GClassInitFunc`. Since NativeAOT forces a **single shared**
`class_init` for all managed subclasses (no per-type codegen), a
discriminator pointer is required, and `GTypeInfo.class_data` is the channel
GObject provides for exactly that. Decision: **`g_type_register_static`**
with a blittable `GTypeInfo` mirror.

`GTypeInfo` (gir record `TypeInfo`, fields in order): `class_size`
(**guint16**), `base_init`, `base_finalize`, `class_init`, `class_finalize`,
`class_data`, `instance_size` (**guint16**), `n_preallocs` (guint16),
`instance_init`, `value_table`. The 16-bit size fields are a real
constraint the mirror must spell (`ushort`), not `int`.

### 3.2 Sizes come from the running library

`class_size` and `instance_size` are **not** taken from the gir or from
compile-time constants. They are read at registration time with
`g_type_query(parent_type)` (gir record `TypeQuery`: `type`, `type_name`,
`class_size`, `instance_size`, both guint):

* `info.class_size  = (ushort)query.class_size;`
* `info.instance_size = (ushort)query.instance_size;`

A managed subclass adds **zero native bytes** — all per-instance state lives
in C# fields of the wrapper (§5.1). With sizes taken from the running
library, registration is layout-agnostic: nothing breaks if a newer
GStreamer grows a class within its padding, because the only offsets the
runtime touches are the vfunc slots it patches, and those are frozen ABI
(§6).

### 3.3 The registration record and the shared init callbacks

New runtime surface (names provisional), hand-written in
`src/GstSharp.Net/Core/GObject/`:

```
Gst.GObject.SubclassRegistry            // static; owns all registrations
Gst.GObject.SubclassDescriptor          // one managed subclass:
    GType ParentType                    //   resolved parent (native call)
    string TypeName                     //   GType name, validated
    ClassInitializer                    //   managed delegate: runs inside class_init
    delegate*<nint, Transfer, object>?  //   optional wrap factory (stage 3)
    nint ParentClass                    //   captured in class_init (chain-up)
    // per-slot patch list: (slot offset in class struct, UCO function pointer)
```

Registration flow (`SubclassRegistry.Register(descriptor)` → `GType`):

1. Take a lock; if `g_type_from_name(descriptor.TypeName)` already resolves,
   fail loudly (double registration is a caller bug).
2. `g_type_query(parent)` → fill a stack `GTypeInfo` with the shared
   `&ClassInit` / `&InstanceInit` `[UnmanagedCallersOnly(CallConvs =
   [typeof(CallConvCdecl)])]` statics and `class_data` = a **counter
   identifier** (the same doctrine as `Object.ToggleRefs`: never a raw
   `GCHandle` as a native-held pointer; here the class is immortal so a
   `GCHandle` would actually be safe, but the id-table keeps one uniform
   rule).
3. `g_type_register_static(parent, name, &info, 0)`.
4. Enter the new `GType` into two tables: `SubclassRegistry`'s own
   `GType → SubclassDescriptor` map, and — when a wrap factory is present —
   `TypeRegistry` (so `Object.FromNative` can construct the right wrapper for
   a native-created instance; without one the type is C#-initiated-only,
   §5.4). `TypeRegistry` has no entry point for an already-resolved `GType`
   today — `ModuleTypeEntry` wants a `get_type` function pointer — so this
   needs a small new API, e.g.
   `TypeRegistry.RegisterSubclass(GType, delegate*<nint, Transfer, object>)`.

The shared callbacks:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static void ClassInit(nint gClass, nint classData)
{
    try
    {
        // *(nuint*)gClass is the GType of the class being initialised —
        // the same read TypeRegistry.GetInstanceType does one hop later.
        SubclassDescriptor d = Lookup(*(nuint*)gClass, classData);
        d.ParentClass = GObjectNative.TypeClassPeekParent(gClass);
        PatchDeclaredSlots(gClass, d);   // writes UCO pointers into slots
        d.ClassInitializer?.Invoke(new ClassConfig(gClass, d)); // metadata, pad templates
    }
    catch (Exception e) { ExceptionTrap.Report(e); }
}

[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static void InstanceInit(nint instance, nint gClass)
{
    // Deliberately (almost) empty: NO wrapper is created here. See §5.2.
}
```

`ClassInit` runs while GObject holds its type lock; the managed code inside
it must stay narrow (patch slots, install pad templates/metadata) and must
not wander into paths that wrap arbitrary objects.

New `GObjectNative` imports needed: `g_type_register_static`,
`g_type_query`, `g_type_class_peek_parent`, `g_type_class_ref`,
`g_type_class_unref`, `g_object_new` (or `g_object_new_with_properties`),
and `g_type_add_interface_static`. All on the existing `"GObject"`
logical library name (`NativeNames`).

### 3.4 NativeAOT constraints, restated as rules

* Every function pointer handed to GObject is an `[UnmanagedCallersOnly]`
  static (`class_init`, `instance_init`, every vfunc trampoline). No
  `Marshal.GetFunctionPointerForDelegate`, no delegates crossing the
  boundary — same doctrine as `SignalRegistry` and the generated signal
  trampolines already follow.
* No reflection anywhere: which vfuncs a subclass overrides is **declared
  explicitly** at registration (§4.2); the runtime never inspects
  `GetType().GetMethod(...)`.
* Per-subclass behavior is reached through ordinary C# virtual dispatch on
  the interned wrapper — the one polymorphism NativeAOT gives for free.
* Registration must happen after the native libraries are loadable; the
  natural shape is a lazy, locked, idempotent `GetGType()` per managed
  subclass type, mirroring how `TypeRegistry.Freeze()` defers `get_type`
  calls.

### 3.5 GType naming

GType names must be unique per process, start with a letter, length ≥ 3,
charset `[A-Za-z0-9_+-]`. Proposal: the registration API takes an explicit
`TypeName` (recommended pattern `"MyAppMySrc"`); no automatic derivation
from the CLR name in stage 0/1 (open question §9 whether a default derived
name is worth the collision risk).

---

## 4. Vfunc override dispatch

### 4.1 Trampoline shape: per-slot, shared across all managed subclasses

One `[UnmanagedCallersOnly]` static per vfunc slot per base class — e.g. a
single `ChangeStateTrampoline` serves every managed `Element` subclass. The
trampoline mirrors the generated signal trampolines
(`Element.PadAddedTrampoline`) exactly in error discipline:

```csharp
[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
static int ChangeStateTrampoline(nint element, int transition)
{
    try
    {
        if (Gst.GObject.Object.TryGetInterned(element) is Gst.Element managed)
        {
            return (int)managed.OnChangeState((Gst.StateChange)transition);
        }

        // No live wrapper (construction window, or post-Dispose):
        // behave as if the slot had not been overridden.
        return (int)ChainUpChangeState(element, (Gst.StateChange)transition);
    }
    catch (Exception e)
    {
        Gst.Interop.ExceptionTrap.Report(e);
        return (int)Gst.StateChangeReturn.Failure;   // per-slot error default
    }
}
```

Three load-bearing decisions in that shape:

* **Lookup, never fabricate.** `Object.FromNative` creates a wrapper when
  none is interned (via `TypeRegistry.TryCreateWrapper`), which is exactly
  wrong inside a vfunc: during `g_object_new` the wrapper does not exist yet,
  and fabricating one would install a toggle ref that collides with the one
  the constructor is about to install (the `Object(nint, Transfer)`
  constructor throws on a live double-wrap by design). A new internal
  `Object.TryGetInterned(nint)` consults only the `Wrappers` table.
  **Doctrine: vfunc dispatch only dispatches to an interned live wrapper;
  otherwise it chains up.** This single rule covers the construction window,
  the post-`Dispose` window, and vfuncs fired from `GObjectClass.constructed`
  or property-setting during `g_object_new`.
  An instance-`qdata` side channel (`g_object_set_qdata`) was considered as
  the lookup and rejected: it would duplicate what the interning table
  already is, cost a per-instance native write, and need a lifecycle of its
  own next to the toggle ref — the `Wrappers` table stays the single source
  of truth.
* **Managed override = C# `protected virtual`.** The wrapper base (e.g.
  `Gst.Element`) grows `protected virtual StateChangeReturn
  OnChangeState(StateChange transition)` whose base implementation chains up.
  Per-subclass routing is ordinary vtable dispatch on the managed side; the
  native side sees one static pointer.
* **Exceptions never unwind into native frames.** `ExceptionTrap.Report` +
  a per-slot error default: `StateChangeReturn.Failure`, `FlowReturn.Error`,
  `FALSE`, `NULL` — following the table the hand-written
  `AppSink.SetSimpleCallbacks` documentation already established for its
  callbacks. On exception the trampoline returns the error default and does
  **not** chain up (chaining after side effects already happened is less
  predictable than failing the operation).

### 4.2 Only declared slots are patched — and why "patch everything" is wrong

Patching every slot with a managed default that chains up looks attractive
(zero override metadata) but is **semantically incorrect**, not merely slow:
GStreamer inspects slot **presence**. `GstBaseTransform` decides
passthrough/in-place behavior from whether `transform` / `transform_ip` are
non-NULL; `GstBaseSrc` picks its data-production strategy from which of
`create` / `alloc` / `fill` are set; `GstElement` treats a non-NULL
`request_new_pad` as "has request pads". Blanket patching would flip those
decisions for every managed subclass. Also, per-buffer slots
(`BaseSrc.fill`, `BaseTransform.transform`) must not pay a managed
transition when not overridden.

Therefore: at registration the subclass **declares** the set of slots it
overrides (a flags value or builder calls on the descriptor), and only those
slots are written in `ClassInit`. The C# `OnX` virtual and the declaration
are two statements of the same fact; keeping them in sync is a documented
contract in stage 0/1, and a `GstSharp.Net.Analyzers` diagnostic
("override of `OnChangeState` without declaring `ChangeState` in the
registration", and the converse) is the planned guard once the surface
settles (the repo already ships analyzers, GST0001/GST0002).

### 4.3 Parameter marshalling

The trampolines reuse the exact conversions the generated signal trampolines
and generated methods already use, driven by the same gir data
(`<virtual-method>` carries full `<parameters>`/`<return-value>` with
transfer annotations, e.g. `change_state` takes
`StateChange` and returns `StateChangeReturn`, both plain enums;
`request_new_pad` takes `PadTemplate*` (wrap `Transfer.None`), `utf8` name,
`Caps*`, returns a `Pad*` the **caller assumes** — see below):

* GObject-derived parameters: `Object.FromNative<T>(ptr, Transfer.None)` —
  borrowed for the call, same as signal args.
* MiniObjects (`GstBuffer` in `BaseSink.render`, `GstQuery` in `query`):
  wrap with the existing MiniObject borrow-with-ref doctrine; transfer-full
  parameters (a vfunc that consumes) adopt.
  **Amended in stage 1**: borrow-with-ref does not work for a vfunc that has
  to *write* to what it is given. `gst_buffer_map_range` refuses a write
  mapping on a buffer that anybody else holds, so the reference the wrapper
  would take makes `BaseTransform.transform_ip` — whose buffer GStreamer has
  already made writable — unwritable. Transfer-none mini object parameters are
  therefore wrapped by a true borrow (`Gst.Interop.Borrowed`): no reference is
  taken, and disposing the wrapper only detaches it, so a wrapper that outlives
  the call throws instead of releasing what it never owned. Transfer-full
  parameters adopt, unchanged.
* Out parameters (`get_state`'s `GstState *state, *pending`): write-back
  through pointers, the same plan shapes `MarshalPlanner` already produces
  for generated methods, mirrored.
* **Transfer-full returns**: the two families answer differently. A returned
  **GObject** keeps its wrapper's own reference (the toggle ref owns it) and
  the trampoline takes an extra `g_object_ref` on the handle before returning
  it. A returned **mini object** is *handed over*: the trampoline detaches the
  wrapper and passes the reference it held on, minting nothing, so the buffer
  an override produced is writable downstream and a pooled one is back in its
  pool when the slot returns instead of waiting for a finalizer. The wrapper
  throws from then on, exactly like the wrapper of an argument the slot
  consumed, and an override that needs the object afterwards copies or refs it
  first. A wrapper that only *borrows* the mini object has no reference to give
  away and gets one minted for the caller, which is what an override answering
  the very object it was lent relies on. For floating-capable returns
  (`request_new_pad` returning a fresh `Pad`), the same
  `IsFloating`/ref-sink reasoning as `Object`'s constructor applies and must
  be spelled per slot.

### 4.4 Chaining up

`ClassInit` captures `g_type_class_peek_parent(gClass)` into the descriptor
(§3.3). One **static** chain-up core per slot reads the parent's slot
through the class-struct mirror and calls it as a raw function pointer; the
trampoline fallback (§4.1) and a `protected` instance wrapper both call it.
The generator writes both out of the same plan the trampoline is written
from, so the sketch below is what the emitted code looks like and not
something anybody types:

```csharp
// Static core: shared by the trampoline's no-wrapper fallback and the
// protected instance helper below.
static StateChangeReturn ChainUpChangeState(nint element, StateChange transition)
{
    var parent = (ElementClassRaw*)DescriptorFor(element).ParentClass;
    var slot = (delegate* unmanaged[Cdecl]<nint, int, int>)parent->ChangeState;
    // GstElement's own change_state is never NULL; slots that may be NULL
    // return the documented default instead.
    return (StateChangeReturn)slot(element, (int)transition);
}

protected StateChangeReturn ChainUpChangeState(StateChange transition) =>
    ChainUpChangeState(Handle, transition);
```

A NULL parent slot maps to the per-slot documented default (`TRUE`,
`GST_FLOW_OK`, "not handled" ...), stated in the helper's doc comment.
Chain-up helpers are `protected` on the same base class that carries the
`OnX` virtual.

**Managed-derives-from-managed is a trap the descriptor lookup must not
fall into.** GObject builds a derived class by copying the parent's class
struct, so for `B : A : Element` (both managed, both declaring
`change_state`) B's slot holds the shared trampoline before B's own
`class_init` runs, and B's captured `ParentClass` is **A's class — whose
slot is the same trampoline**. A chain-up that resolves the descriptor from
the instance's exact `GType` would call back into itself: infinite
recursion. C# virtual dispatch already collapses the managed override
levels into one `OnChangeState` call, so the only correct chain-up target
is the native implementation *below the entire managed stack* — the
`ParentClass` captured by the **base-most managed registration that
declared the slot** (found by walking descriptors up the `GType` chain).
Decision: **stage 0/1 requires the registered parent to be a
native, binding-wrapped type** — deriving a managed subclass from another
managed subclass is unsupported until the descriptor walk lands — and with
that restriction the single-level `DescriptorFor` sketch above is correct
as written.

---

## 5. Managed instance state and construction

### 5.1 State lives on the wrapper, instance adds zero native bytes

All per-instance state of a managed subclass is ordinary C# fields on the
subclass instance. `instance_size` equals the parent's (§3.2), so there is
no native field layout to design, no offsets to keep, nothing for the ABI
probes to learn per subclass — and the state's lifetime rides on the
interning + toggle-ref machinery that already exists (§8).

### 5.2 C#-initiated construction (stage 0/1: the only flow)

```
new MySrc()                                  (user code)
  └─ MySrc ctor : base(construction args)
       └─ [Base subclass ctor] resolves GType (lazy Register, §3.3)
            calls g_object_new(gtype, NULL)
              ├─ GObject allocates instance, runs InstanceInit  → no-op
              ├─ constructed / property notify may fire vfuncs  → trampolines
              │    find no interned wrapper → chain up (§4.1)
              └─ returns handle (floating: GstElement is GInitiallyUnowned)
       └─ chains to the existing Object(nint, Transfer.Full) ctor:
            IsFloating(handle) → g_object_ref_sink   (wrapper owns 1 ref)
            interns wrapper, installs toggle ref
  └─ from here every vfunc dispatches to the managed instance
```

The existing constructor logic in `Object.cs` needs **no change** for this
flow: `g_object_new` on an initially-unowned type returns a floating
reference; the `IsFloating` branch ref-sinks it, the wrapper ends up owning
exactly one reference, and interning happens before user code can hand the
element to anything native. The only true blind spot is vfuncs fired
*during* `g_object_new`, and the lookup-don't-fabricate rule (§4.1) makes
those behave as un-overridden — the same behavior a C subclass would get if
it guarded against a partially constructed self.

One ordering caveat to state in docs: C# instance field initializers run
before the base constructor completes, but virtual `OnX` overrides can be
invoked (post-construction) before a *derived* constructor body finishes if
the user hands the element out of its own constructor. That is the ordinary
C# virtual-call-in-constructor caveat, not a binding defect.

### 5.3 The constructor accessibility problem (repo-visible decision)

Generated wrappers now expose `protected Element(nint handle, Transfer
transfer)`, which a user assembly can chain to — that is what
[`docs/modules.md`](modules.md) is built on. It is not enough here: a managed
subclass defines a *new* `GType` and its instances are created rather than
wrapped, so what the base has to offer is a construction path, not a wrap
path. Options:

* **(a) The `(nint, Transfer)` constructor, which is now `protected`.** It is
  the wrong door: it takes a handle that already exists, and it invites
  wrapping arbitrary handles from user code, which the interning doctrine
  discourages (`FromNative` is documented as the supported wrap path, and the
  constructor throws on double-wrap only at runtime).
* **(b) A dedicated construction type**: the base gains
  `protected Element(SubclassCtorArgs args)` where `SubclassCtorArgs` is a
  small struct only obtainable from the runtime's registration path
  (`SubclassRegistry.NewInstance<T>()` internally: resolve GType →
  `g_object_new` → wrap args). Misuse-resistant; the handle never appears in
  user code.
* **(c) Keep generated classes untouched; subclassable bases are
  hand-written `Custom/` partials** adding (b)'s constructor per class.

**Recommendation: (b) via (c) in stage 1, promoted into the generator in
stage 2** — i.e. the mechanism is (b), initially hand-written as `partial`
extensions in `src/GstSharp.Net/Custom/` and `src/GstSharp.Net.Base/Custom/`
for the closed set (Element, Bin, BaseSrc, PushSrc, BaseSink,
BaseTransform), then emitted by `ClassEmitter` for an allowlist once the
shape is proven. This keeps stage 1 free of generator changes and keeps the
public surface deliberate.

### 5.4 Native-initiated construction (stage 3a, landed)

`gst_element_register` + `gst_element_factory_make`, a base class building its
pad from a template that names a managed pad type, an `Aggregator` answering a
requested pad: all of these create the instance natively, and the managed
wrapper has to be **fabricated** on first contact. A subclass says how by
implementing `IManagedSubclass<TSelf>` and registering through the generic
`DefineSubclass<TSelf>` overload, which hands `TSelf.CreateWrapper` to
`TypeRegistry.RegisterSubclass`. The call is a static abstract one, compiled
per instantiation, so nothing reflects and NativeAOT keeps it.

The rules the fabrication follows:

* **It never sinks.** The instance belongs to whoever created it —
  `gst_element_factory_create_with_properties` complains about a non floating
  element, and `gst_object_set_parent`, `gst_element_add_pad` and
  `ges_layer_add_clip_full` all sink for themselves. The wrapper therefore takes
  a reference of its own and adopts the instance instead of sinking it. The
  reference the *call* was handed is settled separately by the entry point that
  was handed it: `Object.FromNative` drops a `Transfer.Full` one, and sinks a
  floating instance whatever the annotation says, because "transfer floating" is
  spelled `transfer-ownership="none"` in the gir and that is how
  `gst_element_factory_make` arrives. A trampoline is handed nothing and settles
  nothing.
* **The toggle reference is installed before `g_object_new` returns.** The
  instance already counts one reference from GObject's own instance init, and
  the GType lock is not held across instance init, `set_property` or
  `constructed`, so a fabrication that happens inside `g_object_new` — a pad a
  base class builds from a managed template — is safe.
* **One winner per instance.** Fabrication runs behind a gate keyed by the
  handle, and that gate is always taken *outside* the interning lock of
  `Object`: the factory ends in the `Object` constructor, which takes that lock
  itself. Without the gate, a streaming thread and an application thread could
  both build a wrapper for the same instance, and the loser's constructor would
  throw about a double wrap inside a reverse P/Invoke, which is a process abort
  under NativeAOT.
* **A C#-initiated construction suppresses it.** `new MySrc()` reaches the
  `Object` constructor only after `g_object_new` returned, so
  `SubclassType.NewInstance` pushes the type onto a thread-static stack of types
  being constructed and a fabrication for an instance of a type on that stack is
  refused. What happens instead is what always happened: no wrapper, chain up.
* **A disposed wrapper means chain up for ever.** Disposing the wrapper of a
  subclass instance sets a marker on the instance itself, and a marked instance
  is never fabricated again. It arrives as its nearest wrapped ancestor and its
  slots chain up, which is the same answer the surface gave before stage 3a.
  The marker is written by `Dispose` only. A wrapper the collector took —
  which can only happen while the toggle reference was the last reference,
  so the state it carried was already unobservable — is rebuilt with default
  state on the next contact; keep a reference to an instance whose managed
  state matters.
* **`CreateWrapper` runs on streaming threads, under GStreamer's locks.** It
  must forward its arguments to the constructor of the subclass and do nothing
  else: no property access, no pad operation, no waiting. It is also checked —
  the runtime compares the handle of the wrapper it got back with the one it
  asked for, and a wrapper of another instance is an `InvalidOperationException`
  with the wrong wrapper disposed.
* **The gate covers the constructor too.** It is held for the whole factory
  call, and that call is `CreateWrapper` *plus* the field initialisers of the
  subclass — they run before the arguments of the `: base(...)` call are
  evaluated — *plus* the body of the `(SubclassCtorArgs)` constructor. All
  three have to be empty or trivially cheap, by the same rule: no native call,
  no waiting, and **no wrapping of another instance of a managed type**. A
  constructor that reaches for a sibling — `GetStaticPad` of another element of
  the same type — takes gate(A) then gate(B) while another streaming thread
  takes gate(B) then gate(A). State that costs anything goes into the
  parameterless constructor, after its `this(...)` call, or behind a lazy
  field. No analyzer checks it — `GST0005` is about a `CreateWrapper` that
  throws its argument away, not about what a constructor body does — so it
  stays a rule you keep.
* **A constructor body that throws is cleaned up.** The base constructor interns
  the wrapper before the derived body runs, so a body that throws would leave a
  live, toggle-holding wrapper nobody is ever handed. The fabrication disposes
  it and rethrows — which also writes the disposed marker, so the instance
  chains up from then on, exactly as any other disposed wrapper leaves it.

A type registered through the plain non generic `DefineSubclass` registers no
factory and stays **C#-initiated-only**: a natively created instance of it falls
back to an ancestor wrapper, `TypeRegistry.Fallback` reports it once, and its
vfuncs chain up for ever.

### 5.5 Class configuration is part of registration

`class_init` must do more than patch vfuncs for the result to be usable:

* `gst_element_class_set_metadata` (longname/klass/description/author).
* `gst_element_class_add_pad_template` — **mandatory** for the GstBase
  classes: `GstBaseSrc`'s instance init fetches the class's `"src"` pad
  template to create its pad; a `BaseSrc` subclass whose class has no such
  template fails at instance init. Same for `BaseSink`'s `"sink"`.

The `ClassInitializer` delegate on the descriptor (§3.3) receives a
`ClassConfig` facade exposing exactly these operations, implemented over
the raw `gClass` pointer. `ClassConfig` derives from `ObjectClassConfig`, the
GObject level facade, which is what the class initialiser of a class that is
*not* an element is given: `Gst.Pad` and `GstBase.AggregatorPad` have no
metadata and no pad templates, so the generated `DefineSubclass` of such a
class asks for an `Action<ObjectClassConfig>?` instead. Deriving rather than
replacing is what keeps every `ClassConfig` call that was written before
compiling unchanged. Because the runtime (`Core/`) and the `Gst`
bindings share one assembly (`GstSharp.Net`), while `GstBase` bases live in
`GstSharp.Net.Base`, the facade is extensible per module rather than
hardcoded in Core.

### 5.6 Properties and signals a subclass installs (stage 3b, landed)

`ObjectClassConfig` grew two more class-init operations, so every subclassable
type has them and not only the elements:

```csharp
config.InstallProperty(ValueId, ValueSpec);              // ParamSpecX.New(...)
uint id = config.AddSignal("gstsharp-ping", SignalFlags.RunLast,
                           GType.None, [GType.Int], OnPing);
```

**Dispatch is by owner, so there is no chain up.** `object_set_property` picks
the class to call by the `owner_type` of the specification, not by the type of
the instance (gobject.c:2214-2217 / 2188-2191): a property an ancestor installed
is answered by the ancestor's own class struct and never travels through the
managed slot. So `Object.OnSetProperty`/`OnGetProperty` are the one pair of
`On<X>` members with no `ChainUp<X>` beside them — there is nothing below them
that could answer. The default arm of the `switch` calls `base.OnSetProperty`,
which warns about an identifier no property claims, the way GObject's own
`G_OBJECT_WARN_INVALID_PROPERTY_ID` does. Chaining a managed identifier up
would land in the parent's `switch (prop_id)`, which ignores the specification
entirely, and a numeric collision would silently write a field of the parent.

Redefining a property of an ancestor is legal — GObject documents it — and it
follows from the same rule that the redefinition *takes the name over*: install
a property called `name` on an `Element` subclass and `name=` in a pipeline
description reaches the managed setter and nothing else, so the element has no
name as far as `GstObject` is concerned. It is a sharp tool.

**Construct properties are refused** (`ArgumentException`). `g_object_new`
delivers every construct property — the value the caller named, or the default
of the specification when nobody named one — from inside the constructor
(gobject.c:2688-2709), before a wrapper can exist for an instance a C#
constructor is building; the fabrication is suppressed for the type being
constructed on this thread (§5.4). The value would therefore arrive for an
instance GStreamer created and vanish for one C# created. Construct-time state
belongs in the constructor of the subclass.

**Notifications.** Without `ParamFlags.ExplicitNotify` GObject emits `notify`
itself once the setter returns (gobject.c:2264-2266), and the managed setter
must stay silent or the change is announced twice. With the flag GObject stays
silent and the setter calls `Notify(pspec)` — which is `g_object_notify_by_pspec`,
safe from any thread and from inside a setter, queued when notifications are
frozen. `Notify` checks that the specification belongs to the object, because
GObject checks nothing there.

**The window inside `g_object_new`.** A property write can be the *first*
managed contact an instance ever has: `gst_parse_launch("… value=5 ! fakesink")`
creates the element with `gst_element_factory_create_with_properties`, so the
set_property slot runs before the caller of `ParseLaunch` has seen anything.
The trampoline fabricates the wrapper there, on the calling thread.

What that window *is* is worth stating exactly, because it is not the
fabrication-time window of §5.4. A plain property is not a construct property,
so GObject sets it after `constructed` has run and just before `g_object_new`
returns (gobject.c:2713-2721); `Object.Fabricate` releases the per-handle gate
before the trampoline calls the setter, so **no gate and no lock of the runtime
is held**, and a setter may call whatever it likes — `Notify(pspec)` above all,
which is what an `ExplicitNotify` property requires of it. GObject may have
frozen the notification queue for the duration of the call
(gobject.c:2674-2681), in which case the notification is delivered when it
thaws, a few lines later. What is *not* ready is everything around the
instance: it is in no bin, it has no peers, and nobody has been handed it.
Store the value; leave anything that needs a pipeline to the state change that
brings one. A write from another thread stays on that thread; nothing hops.

**No wrapper means warn and drop.** If `TryGetOrFabricate` answers nothing — an
instance of the type being constructed on this thread, or a wrapper that was
disposed — the slot logs a warning through GLib and returns. It cannot chain up
(above), and it must not throw across the native frame. The one case worth
naming is the rare one of §8: a fabricated wrapper that was collected is
rebuilt in its default state, so the managed side of an installed property is
lost with it. Keep a reference to a managed element you care about.

**Signals.** `AddSignal` wraps `g_signal_newv` — the array form, because
`g_signal_new` is variadic — with a `NULL` C marshaller, which asks GObject for
its generic marshaller and covers the `va_list` path as well. The class handler
is an ordinary `DynamicSignalHandler` wrapped in the same closure the
`ConnectSignal` path uses, with one difference: the class closure resolves the
instance without settling its reference, because it can be reached while the
instance is still floating. Two accumulators are offered, the two GLib exports:
`TrueHandled` (the signal has to return a boolean; the first handler that
returns `true` ends the emission) and `FirstWins`. Emission and subscription go
through the existing `EmitSignal` and `ConnectSignal`: a signal a managed
subclass defined is not special once it exists. `SignalFlags.MustCollect` is
dropped — it describes a variadic collection this binding never performs.

Specifications are owned as GObject owns them: `g_object_class_install_property`
sinks the specification and the pool takes a reference of its own, and the
runtime keeps one long-lived wrapper per installed specification so the property
slots have something to hand out without leaking a reference per call. The
wrapper the caller built is theirs to dispose.

### 5.7 Interfaces a subclass implements (stage 3b, landed)

An interface is declared once per type — declaring the same one twice in one
registration is refused before the type exists — and it is declared when the
type is defined and nowhere else:

```csharp
private static readonly SubclassType Definition = DefineSubclass<FeedSrc>(
    "FeedSrc",
    ConfigureClass,
    new SubclassOptions { Interfaces = [URIHandlerImplementation.For<FeedSrc>()] },
    CreateOverride);
```

**Define time is the only window.** `g_type_add_interface_static` refuses a
type whose class initialisation has begun — the refusal starts the moment
`type_class_init_Wm` assigns `class.class`, which is before the first
`base_init` and long before the class initialiser of the type runs — so a call
from inside `configureClass` is a `g_critical` and nothing else. The
registration therefore attaches every declared interface between
`g_type_register_static` and `g_type_class_ref`, which is the one moment the
type exists and its class does not. GLib copies the `GInterfaceInfo`, so a
stack value is enough, and it fills the vtable in `interface_init`, which runs
*after* the class initialiser, on the thread that registered the subclass, over
memory that is never freed. The class-init rules apply there too: filling a
vtable writes function pointers and creates no wrapper.

`InterfaceImplementation` cannot be derived from outside the binding. The
binding hands out one ready made implementation per interface it supports, and
`GstURIHandler` is the first:

```csharp
internal sealed class FeedSrc : PushSrc, IManagedSubclass<FeedSrc>, IURIHandlerImplementation
{
    public static URIType UriType => URIType.Src;

    public static IReadOnlyList<string> Protocols => ["feed"];

    public string? GetUri() => _uri;

    public bool SetUri(string uri, out GException? error)
    {
        error = null;
        _uri = uri;
        return true;
    }
}
```

Three facts of the C interface show through:

* **The protocol list is pinned for the life of the process.** The element
  factory deep-copies it during registration, but
  `gst_uri_handler_get_protocols` hands the array of the *type* straight to its
  callers without copying, so it has to stay valid for as long as anyone can
  ask. `URIHandlerImplementation.For<TSelf>()` reads `Protocols` once, copies it
  into unmanaged memory and never releases it — the type is equally permanent.
  It is **one pin per type**: the vector is cached per `TSelf` and a second
  call answers the pointer the first one pinned, so a declaration that is asked
  for twice costs nothing. The validation does run on every call, so a wrong
  declaration is refused however often it is made. Nothing else reads the
  property.
* **A refusal always carries an error.** `gst_uri_handler_set_uri` synthesises
  none of its own, and `gst_element_make_from_uri` reads the message of the
  error of every candidate that refused — a null one is a crash there whenever
  GStreamer debugging is on. So a `SetUri` that answers `false` and leaves
  `error` null gets a `GST_URI_ERROR_BAD_URI` naming the type, and so does one
  that throws: the exception is reported to the trap, and the caller is told
  the URI was refused.
* **`GetUri` and `SetUri` run on the caller's thread**, whichever that is.
  `uridecodebin` asks on its autoplug thread, a pipeline description asks on
  the thread that parsed it, and `gst_element_make_from_uri` asks before the
  element is in a pipeline. Store the URI; do not open anything.

The two remaining slots, `get_type` and `get_protocols`, are asked about a
`GType` rather than about an instance: they answer out of the registration
table and never fabricate a wrapper, which is what lets `gst_element_register`
interrogate the type while no instance exists. That registration also refuses a
type without metadata, so a URI handler still calls `ClassConfig.SetMetadata`.

An interface an ancestor already implements is refused. GLib would allow it and
hand the subclass a copy of the ancestor's slots, but a managed implementation
has no way to chain up through those, so what would look like an override would
silently be a replacement.

---

## 6. Class-struct ABI: layout source, validation, versioning

### 6.1 Layout source: the gir type-struct records

The gir carries complete class-struct layouts. `<record name="ElementClass"
c:type="GstElementClass" glib:is-gtype-struct-for="Element">` lists, in
order: `parent_class` (`GstObjectClass`), the **data fields** `metadata`,
`elementfactory`, `padtemplates`, `numpadtemplates`, `pad_templ_cookie`,
then the vfunc fields `pad_added` … `set_context` (each with an inline
`<callback>` carrying the full signature), then
`_gst_reserved` with `fixed-size="18"` — i.e. `GST_PADDING_LARGE - 2`,
because `post_message` and `set_context` were carved out of the padding in
past minor releases. `BaseSrcClass` ends with `fixed-size="20"`
(`GST_PADDING_LARGE`, untouched); `BaseTransformClass` interleaves the two
`gboolean` data fields before its vfuncs. So class structs are **not**
vfunc-only tables — mirrors must lay out data fields and padding faithfully.

Emitted mirrors (stage 2; hand-written for the stage-0/1 set) are blittable
`LayoutKind.Sequential` structs in the `*Raw` style the repo already uses
(`MiniObjectRaw`, `BufferRaw`): pointer-sized `nint` slots for callbacks
(typed `delegate* unmanaged[Cdecl]<…>` fields where the chain-up helpers
want them), and the reserved tail as an inline array. None of the class
structs the design touches contain bitfields or `long`/`ulong` fields, so
there is no MSVC/MinGW layout divergence to manage (`CLong` not needed).

### 6.2 Validation: extend the ABI probe tests

`AbiProbeTests` gains class-struct probes in its existing two-tier style:

1. **Constant asserts**: `Unsafe.SizeOf<ElementClassRaw>()` and per-field
   offsets against constants derived from the headers, exactly like
   `MiniObjectRawMatchesTheHeaderLayout`.
2. **Dynamic probes against the running library**:
   * `Unsafe.SizeOf<ElementClassRaw>() == g_type_query(gst_element_get_type()).class_size`
     — the library is the ground truth for total size.
   * Slot-content probes: `g_type_class_ref(GST_TYPE_BIN)` and assert that
     `((ElementClassRaw*)klass)->ChangeState != 0` (GstBin overrides
     `change_state`; a wrong offset reads a null or unrelated field).
     Similar known-non-null slots exist for `BaseSrcClass.create` on
     `pushsrc`-derived types, etc.
   * A slot-content probe reads the class of a real element, so it names one.
     Those elements are the promise the Linux CI leg makes through
     `GSTSHARP_REQUIRED_ELEMENTS` in `.github/workflows/ci.yml`; the probes of
     the subclassable base classes use `audioconvert` (`BaseTransform`),
     `audiomixer` (`Aggregator`), `alsasink` (`AudioSink`), `videoconvert`
     (`VideoFilter`), `rawaudioparse` (`BaseParse`), `vorbisdec` (`AudioDecoder`),
     `vorbisenc` (`AudioEncoder`), `theoradec` (`VideoDecoder`) and
     `theoraenc` (`VideoEncoder`). Everywhere else `[RequiresElementFact]`
     skips the probe when the plugin is absent, so a leg that does not list an
     element loses the probe instead of failing.
3. **The end-to-end probe is a test subclass** (stage 0): register a managed
   `Element` subclass overriding `change_state`, run it through
   NULL→READY→NULL inside a `Gst.Pipeline`, assert the override fired with
   the right transitions and that chain-up returned `Success`. That test
   validates offsets, calling convention, and dispatch at once, on both CI
   legs (1.28 and the 1.24 floor — subclassing introduces no 1.28-only
   entry points, so `RequiresGStreamerFactAttribute` should not be needed
   for the core tests).

### 6.3 Across GStreamer versions

* GStreamer's ABI promise: class structs only grow by consuming their
  `_gst_reserved` padding; existing slot offsets never move within 1.x.
  The offsets the runtime patches have been stable since well before 1.24
  (the repo's declared floor, asserted by
  `AbiProbeTests.NativeVersionIsSupported`).
* Registration is robust against *newer* libraries automatically because
  `class_size` comes from `g_type_query` at run time (§3.2).
* A vfunc slot **introduced after 1.24** would need availability gating
  (`girs/overlays/platform-symbols.json` is the existing per-platform
  mechanism; a version floor per slot is the analogous overlay). None of the
  slots in the stage-1 set needs this; the mechanism is only designed, not
  built, until a gated slot exists.
* The generator regenerates mirrors from the 1.28 girs; a gir refresh that
  moves an offset (should never happen in 1.x) is caught by the constant
  asserts against the dynamic `class_size` probe disagreeing.

---

## 7. Generator involvement: generated vs hand-written

| Artifact | Stage | Produced by |
|---|---|---|
| `GObjectNative` imports (`g_type_register_static`, `g_type_query`, `g_type_class_peek_parent`, `g_type_class_ref/unref`, `g_object_new`) | 0 | hand-written (Core/Interop) |
| `GTypeInfo` / `GTypeQuery` blittable mirrors | 0 | hand-written (Core/GObject) |
| `SubclassRegistry`, `SubclassDescriptor`, shared `ClassInit`/`InstanceInit`, `Object.TryGetInterned` | 0 | hand-written (Core/GObject) |
| `ElementClassRaw`, `BaseSrcClassRaw`, … for the closed set | 0–1 | was hand-written; **deleted** in stage 2a, generated since |
| Subclass bases: `protected` ctor, `OnX` virtuals, `ChainUpX` helpers, vfunc trampolines, `ClassConfig` glue | 1 | was hand-written `Custom/` partials; **generated** since stage 2a |
| Class-struct mirrors for an **allowlisted** set of classes | 2a | generated (`ClassStructEmitter`) |
| Typed vfunc surface (`OnX` + trampoline + chain-up) from `<virtual-method>` | 2a | generated (`VfuncEmitter`), reusing `MarshalPlanner` plans in reverse (the `SignalEmitter` trampolines are the proof the reverse direction fits the planner) |
| Per module `ClassStructRegistry` for the ABI probes | 2a | generated (`ClassStructEmitter`) |
| Analyzer: override/declaration consistency | 2b | `GstSharp.Net.Analyzers` |
| Wrap factories via static abstract interface members, interface `interface_init` | 3 | generated + runtime |

Generator specifics for stage 2:

* **Allowlist, not everything**: `Gst-1.0.gir` alone declares ~40 type-structs;
  emitting vfunc surfaces for all of them is scope creep with no consumer.
  A new overlay key in `girs/overlays/fixups.json` (e.g. `"subclassable":
  ["Gst.Element", "Gst.Bin", "GstBase.BaseSrc", "GstBase.PushSrc",
  "GstBase.BaseSink", "GstBase.BaseTransform"]`) selects the set, the same
  way `forceOpaque` and `skip` already steer classification.
* The `Classifier` keeps `TypeKind.GTypeStruct` skipped as a *wrapper* type;
  the new emitter consumes the same records as raw mirrors only for the
  allowlisted classes' type-struct chain (a `BaseSrcClassRaw` embeds
  `ElementClassRaw` embeds `ObjectClassRaw` embeds `GObject.ObjectClass`
  layout — the parent chain must be emitted transitively, including the
  `GObject-2.0.gir` structs `TypeClass`/`ObjectClass`).
* Per-vfunc skip must exist from day one (some vfuncs have un-marshallable
  shapes — the same reality that produced the `skip` list for methods);
  the overlay grows a `skipVirtuals` list keyed
  `"Gst.Element::set_bus"`-style if needed. What landed is that list plus
  six more keys, all documented in
  [`CONTRIBUTING.md`](../CONTRIBUTING.md): `vfuncDefaults` (what a chain-up
  answers for a NULL parent slot), `vfuncIdentityBuffers` (a buffer that may
  be handed back unchanged), `vfuncNonNullReturns` (a slot whose caller
  dereferences the answer), `vfuncDocNotes` (the part of a contract only the
  C implementation states), `vfuncSpans` (a counted block the slot only
  reads) and `vfuncFailureValues` (what a trapped exception answers when the
  zero of the return type means something else).
* **Census**: new emission categories (class-struct mirrors, vfunc
  trampolines, subclass bases) get fixed counts in `EmissionCensus` /
  `CensusTests` — the existing gate against silent scope drift.
* Determinism/LF and double-generation byte-identity apply unchanged.

---

## 8. Lifecycle: interaction with toggle refs and the finalizer queue

Mostly, subclassing *inherits* correctness from the existing design; the
document spells it out because the failure modes are subtle:

* **A managed subclass instance is not collected while native holds refs.**
  The `Object` constructor installs the toggle ref at interning; while any
  reference besides the toggle ref exists (a `GstBin` parent, a scheduled
  task, a posted message holding the src), `ToggleNotify` keeps the
  `ToggleRef` strong, so the wrapper — and every C# field on it, i.e. all
  subclass state — survives with no action from the user. This is the exact
  scenario toggle refs exist for, and it is already implemented.
* **Vfunc reentrancy is safe against collection**: a vfunc call implies the
  caller holds a native reference to the instance, which implies the toggle
  ref is in strong mode, which implies `TryGetInterned` finds a live
  wrapper. The only windows without a wrapper are construction (§5.2),
  after `Dispose`, and after the collector took a wrapper whose toggle
  reference was the last reference — the first two are covered by the
  chain-up rule (§4.1), the third by re-fabrication with default state for a
  `DefineSubclass<TSelf>` type (§5.4). Since stage 3b that third window is
  user-visible: the managed state behind an installed property lives on the
  wrapper, so a wrapper that was collected and re-fabricated answers the
  default of every property the type installed, and whatever was written into
  it is gone. Keep a reference to a managed element whose properties anyone
  reads.
* **Dispose doctrine extends unchanged**: GObject wrappers are never
  disposed except one's own pipeline after `SetState(Null)`. Disposing a
  managed subclass instance that native code still drives does not crash —
  `IsDisposed` flips, the interning entry is released, and subsequent vfuncs
  chain up — but the element silently loses its managed behavior. Document
  it as a misuse; consider a debug-time `ExceptionTrap`-reported diagnostic
  when a vfunc chains up because the wrapper was disposed (distinguishable
  from the construction window because the release path ran).
* **Finalization of an unreferenced subclass instance** (created, never
  added anywhere, dropped): wrapper goes weak once the toggle ref is the
  only reference; the GC collects it; the finalizer enqueues the release
  (`PendingReleases`); `DrainPendingReleases` drops the last ref; GObject
  destroys the instance. During destruction, `dispose`/`finalize` vfuncs
  run **with no wrapper** — which is precisely why overriding them is a
  non-goal (§1); patched *other* slots called during teardown chain up
  harmlessly.
* **The registration itself is immortal**: descriptors, patched class
  structs, and the parent-class pointer live for the process. No unload
  path exists (static GTypes cannot be unregistered), so no lifecycle
  design is needed there beyond "never free".

---

## 9. Risks and open questions

1. **Slot presence vs. C# overrides drifting apart** (declare `ChangeState`
   but forget to override `OnChangeState`, or vice versa). Mitigation:
   analyzer (stage 2); until then, the base `OnX` chains up, so the
   worst case of a spurious declaration is a redundant managed transition —
   except for presence-sensitive slots (§4.2), where a spurious declaration
   changes element behavior. Documentation must name the presence-sensitive
   slots explicitly.
2. **`ClassInit` runs under the GObject type lock.** Creating
   `PadTemplate` wrappers or calling back into wrapping paths from inside
   `ClassInitializer` touches `Object.Sync` and `TypeRegistry` — a
   lock-order interaction that does not exist today. Mitigation: the
   `ClassConfig` facade works on raw handles where possible (pad templates
   can be built before registration and only *added* inside class init);
   this needs a deliberate review during stage 0, plus a stress test.
3. **Vfuncs with hard shapes**: out-parameters that are arrays,
   caller-allocated structs, or transfer-full mini objects flowing *into*
   managed code. `MarshalPlanner` covers the forward direction and the
   signal trampolines cover simple reverse cases; a per-vfunc skip valve
   (§7) bounds the risk. The stage-1 hand-written set intentionally picks
   vfuncs with tame signatures.
4. **`request_new_pad` ownership**: returns a `Pad` the caller assumes;
   managed code returning a pad it also keeps a wrapper for needs the
   extra-ref rule (§4.3) verified against `gst_element_request_pad`'s
   actual unref behavior in an integration test.
5. **Native-initiated construction gap** — **closed in stage 3a.** A type
   registered through `DefineSubclass<TSelf>` states how its wrapper is built,
   and a factory-made instance of it arrives as the managed type with its
   overrides running (§5.4). What is left of the gap is the deliberate half:
   a type registered through the non generic `DefineSubclass` is still
   C#-initiated-only, and `TypeRegistry.Fallback` is still the diagnostic that
   names it.
6. **GES dependency direction**: the GES module is bound and consumes only
   elements the native libraries instantiate, which needs nothing from this
   design. Authoring a custom GES source means GES instantiating a managed
   type by GType name — which lands squarely on §5.4 and is therefore a
   stage-3 deliverable, not an earlier one.
7. **AotSmoke coverage**: the smoke sample currently exercises raw imports
   and core; a registered subclass must be added
   (`samples/AotSmoke`) so ILC reachability covers `UnmanagedCallersOnly`
   trampolines, the registration path, and the class mirrors — zero
   IL/AOT warnings stays a gate.
8. **GType name policy** (§3.5): explicit-only vs. derived default —
   decide when the registration API is reviewed.
9. **Interfaces**: settled in stage 3b and landed. `GstURIHandler` is the
   first concrete consumer of `g_type_add_interface_static` +
   `GInterfaceInfo.interface_init`, and it follows the same
   patch-declared-slots pattern on the interface vtable — but only at Define
   time, for the reason given in §5.7. `InterfaceEmitter` still binds no vfuncs
   of its own: what a managed type implements is a hand-written
   `InterfaceImplementation` per interface, not generated code.
10. **Properties on managed types**: settled in stage 3b and landed, on
    `ObjectClassConfig` rather than only on `ClassConfig` —
    `InstallProperty` (`g_object_class_install_property` inside `ClassInit`)
    and the `set_property`/`get_property` overrides of §5.6. What stays out is
    construct properties, for the reason given there.

---

## 10. Staged implementation plan

**Stage 0 — runtime primitives + proof (no generator change, no public API
promise).**
`GObjectNative` imports; `GTypeInfo`/`GTypeQuery` mirrors;
`SubclassRegistry` + shared `ClassInit`/`InstanceInit`;
`Object.TryGetInterned`; hand-written `ElementClassRaw` (+ its parent chain
`ObjectClassRaw`/`GstObjectClassRaw`/`GTypeClass`); construction path (§5.3
option b, internal). One **test-only** managed `Element` subclass in
`tests/GstSharp.IntegrationTests` overriding `change_state`: asserts
dispatch, chain-up, exception policy, toggle-ref survival inside a `Bin`,
and the class-struct ABI probes (§6.2). Gates: all five, AotSmoke gains the
registration path.

**Stage 1 — closed set of hand-written subclass bases (first public
surface).**
`Custom/` partials for `Gst.Element`, `Gst.Bin`, and in `GstSharp.Net.Base`
for `BaseSrc`, `PushSrc`, `BaseSink`, `BaseTransform`: protected
construction, `OnX` virtuals + trampolines + `ChainUpX` for the curated
vfunc set, `ClassConfig` with `SetMetadata` and `AddPadTemplate`
(mandatory for the Base classes, §5.5). Samples: a managed `PushSrc`
producing buffers and a managed `BaseSink` consuming them, run in AotSmoke.
Docs page derived from this design document.

**Stage 2a — generator-emitted vfunc surface (landed).**
`ClassStructEmitter` and `VfuncEmitter` behind the `"subclassable"` overlay
allowlist: generated `*ClassRaw` mirrors (transitive parent chain), a
generated per module `ClassStructRegistry` the ABI probes walk, generated
`OnX` / trampoline / chain-up members reusing `MarshalPlanner` in reverse,
the per-vfunc overlay keys of §7, and the census categories `class struct`
and `vfunc` with a `Virtuals` section in `girs/skip-report.md`. The
stage-1 hand-written mirrors and subclass partials were deleted in the same
change, and the sixteen curated `OnX`/`ChainUpX`/`XOverride` members of
stage 1 came out of the generator byte-identically — the package validation
baseline is what held that.

Fourteen classes are subclassable: `Gst.Element`, `Gst.Bin`,
`GstBase.BaseSrc`, `PushSrc`, `BaseSink`, `BaseTransform`, `Aggregator`,
`GstAudio.AudioBaseSink`, `AudioBaseSrc`, `AudioSink`, `AudioSrc`,
`AudioFilter`, and `GstVideo.VideoSink`, `VideoFilter`. §11 lists their
slots.

**Stage 2b — the rest of the allowlist and the instance-keyed callbacks
(landed).** `GstBase.BaseParse` and the four codec bases (`AudioDecoder`,
`AudioEncoder`, `VideoDecoder`, `VideoEncoder`) landed first, together with
the boxed borrow that un-skipped the six `set_info`-shaped slots. The
**instance-keyed callback** mechanism the pad functions need landed with
them: `gst_pad_set_chain_function_full` and its ten siblings take a callback
whose own C signature carries no closure argument — gstpad.c:4605 calls
`chainfunc (pad, parent, buffer)`, and the `user_data` the setter took stays
on the pad — so `Gst.Interop.InstanceKeyedCallbacks` files the delegate under
the pad and the storage slot of the pad the setter writes, and the
`GDestroyNotify` the same setter takes removes the entry again. Replacement
and `gst_pad_finalize` therefore release the state exactly when C says they
do, and the entry is only removed while the key still names the very handle
being released, because C installs the new function after notifying the old
one and holds no lock in between (gstpad.c:1820-1835, :772-791). The slot
names live in the `instanceKeyedCallbacks` overlay; `event` and `event_full`
share one slot, so a pad carries one of the two and the later call wins.

Three shapes of the callback path landed with the mechanism and are what
also un-skipped `gst_collect_pads_set_buffer_function` and
`gst_collect_pads_set_clip_function`, whose callbacks do carry a
`user_data`: a handle a callback is handed with `transfer full`, which the
trampoline scopes and releases when the handler returns; a handle the
handler fills in through a `GstBuffer**`, which the caller is given with one
added reference; and the in place buffer of `GstPadGetRangeFunction`, which
arrives borrowed and must be answered unchanged — a success with no buffer,
and a success with a buffer other than the one that was lent, are both
corrected to `GST_FLOW_ERROR` there, because gst_pad_get_range asserts the
identity and answers its own error without releasing what it handed over
(gstpad.c:5127). What a handler produces through a plain `out` is handed on
whatever it answered, because its caller reads the storage before the answer
(gstcollectpads.c:2170-2181), and a `GstCollectPadsBufferFunction` is called
with no data and no buffer once every pad has reached EOS
(gstcollectpads.c:1540), so both of those are nullable.

The analyzer that checks that an `OnX` override and the `XOverride`
declaration of the same type come in pairs is the `GST0003`/`GST0004` pair.

**Stage 3a — native-initiated construction and the two pad classes
(landed).** `IManagedSubclass<TSelf>` and the generic `DefineSubclass<TSelf>`
overload on every subclassable class, the `TypeRegistry.RegisterSubclass` entry
point behind it, and the fabrication rules of §5.4. The non generic
`DefineSubclass` was not retrofitted, so subclasses written before this keep
compiling and keep their old behaviour. `SubclassType.NewInstance` gained the
construction-property overload a `GstPad` needs, because `direction` is
construct only; `ObjectClassConfig` arrived as the base of `ClassConfig` (§5.5);
`Gst.Pad` and `GstBase.AggregatorPad` joined the allowlist, which is what
un-skipped `Aggregator::create_new_pad`. Twenty one classes are subclassable,
with twenty two class struct mirrors and 218 slots.

**Stage 3b — properties, signals and interfaces (landed).** `g_param_spec_*`
construction (twenty one `New` factories, plus the `ParamSpecFraction` and
`ParamSpecArray` of GStreamer), `ObjectClassConfig.InstallProperty` with the
`OnSetProperty`/`OnGetProperty` overrides, `AddSignal` over the dynamic signal
closure, and `g_type_add_interface_static` with `GstURIHandler` first. A
property a managed type installs cannot be written while the instance is being
built: GObject dispatches the write to the class that owns the property, and
the wrapper that would serve it does not exist yet, which is why
`NewInstance(properties)` refuses one.

Improvements during the implementation, each of them a refusal or a rule the
plan did not name:

* **Interfaces are a Define-time declaration only.** `g_type_add_interface_static`
  after `g_type_class_ref` is refused by GLib itself, so there is no
  `ClassConfig.AddInterface` and there never will be one (§5.7). An interface
  the *parent* implements is refused as well, which is stricter than GLib: it
  would hand the subclass a copy of the parent's slots and no way to chain up
  through them.
* **Property dispatch has no chain up, and construct properties are refused.**
  GObject dispatches a write to the class that owns the specification, so a
  managed slot is reached only for a property the managed type installed and
  there is nothing above it to call. A specification that asks for `CONSTRUCT`
  or `CONSTRUCT_ONLY` — or one that is neither readable nor writable, which
  GObject would only assert about — is refused before anything native runs.
* **`TrueHandled` requires a boolean return.** `g_signal_accumulator_true_handled`
  reads every answer with `g_value_get_boolean`, so a signal that answers
  anything else criticals on every handler and never stops; `AddSignal` refuses
  the combination instead.
* **An installed specification is held four times and the caller's wrapper is
  still theirs.** The install sinks it and GObject's pool takes a reference,
  and the runtime interns one long-lived wrapper so the property slots have
  something to hand out without leaking one per call. Disposing the wrapper the
  property was installed from leaves three references and changes nothing about
  the class.
* **Protocol lists are pinned once per type.** `gst_uri_handler_get_protocols`
  hands its array straight to the caller, so the vector can never be freed;
  `URIHandlerImplementation.For<TSelf>()` caches it per type, and a second call
  answers the same pointer rather than pinning another copy.
* **`set_uri` synthesises the error a refusal owes.** `gst_uri_handler_set_uri`
  makes none of its own and `gst_element_make_from_uri` reads the error of every
  candidate that refused, so a handler that answers `false` without a reason
  gets a `GST_URI_ERROR` written for it — and a wrapper that does not implement
  the interface at all is told apart from an instance that has no wrapper.
* **`GST0005`** reports an `IManagedSubclass<TSelf>.CreateWrapper` that throws
  its `SubclassCtorArgs` away, which is the one mistake in the fabrication path
  that compiles and then wraps the wrong instance.

**Stage 3c — GES custom sources.** The seven GES classes on the allowlist, the
named callback typedef slots (`create_track_element(s)`), and `OnCreateSource`
with a sample and tests. It rests on stage 3a: GES constructs a managed type
natively whenever a clip is copied, split or pasted.

---

## 11. Using it

What ships is a **generated surface for an allowlist of twenty one base
classes**: `Gst.Element`, `Gst.Bin`, `Gst.Pad`, `Gst.Base.BaseSrc`, `PushSrc`,
`BaseSink`, `BaseTransform`, `BaseParse`, `Aggregator`, `AggregatorPad`,
`Gst.Audio.AudioBaseSink`, `AudioBaseSrc`, `AudioSink`, `AudioSrc`,
`AudioFilter`, `AudioDecoder`, `AudioEncoder`, and `Gst.Video.VideoSink`,
`VideoFilter`, `VideoDecoder`, `VideoEncoder`. Each one carries four things — a
`DefineSubclass` that registers a managed type, a `DefineSubclass<TSelf>` that
also states how the wrapper of an instance native code created is built, a
`protected` constructor, and, per bound vfunc, an `OnX` virtual with a matching
`ChainUpX` and an `XOverride` declaration.

### A source in thirty lines

```csharp
using Gst;
using Gst.Base;
using Gst.GObject;

internal sealed class CounterSrc : PushSrc
{
    // Templates are built BEFORE the registration and only added inside it,
    // so this field has to be declared before the one below.
    private static readonly PadTemplate SrcTemplate = PadTemplate.New(
        "src", PadDirection.Src, PadPresence.Always, Caps.NewAny())!;

    private static readonly SubclassType Definition = DefineSubclass(
        "MyAppCounterSrc",          // the GType name, unique in the process
        ConfigureClass,             // runs inside class_init
        CreateOverride,             // one declaration per OnX override
        StartOverride);

    private int _produced;

    public CounterSrc() : base(Definition.NewInstance()) { }

    protected override bool OnStart() { _produced = 0; return ChainUpStart(); }

    protected override FlowReturn OnCreate(out Gst.Buffer? buffer)
    {
        if (_produced == 10) { buffer = null; return FlowReturn.Eos; }
        buffer = Gst.Buffer.NewMemdup([(byte)_produced++]);
        return FlowReturn.Ok;
    }

    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata("Counter source", "Source/Testing", "Counts up", "me");
        config.AddPadTemplate(SrcTemplate);
    }
}
```

`new CounterSrc()` is then an ordinary element: add it to a `Pipeline`, link it,
set the state. `GstSharp.Initialize()` (or `GstBase.Initialize()`) has to have
run before the registration, which happens the first time `Definition` is
touched.

### Letting GStreamer create the instances

A subclass that states how its wrapper is built implements
`IManagedSubclass<TSelf>` and registers through `DefineSubclass<TSelf>`. From
then on an instance GStreamer creates — through an element factory, or as the
pad of a base class — arrives as the managed type with its overrides running:

```csharp
internal sealed class CounterSrc : PushSrc, IManagedSubclass<CounterSrc>
{
    private static readonly SubclassType Definition = DefineSubclass<CounterSrc>(
        "MyAppCounterSrc", ConfigureClass, CreateOverride);

    public CounterSrc() : this(Definition.NewInstance()) { }

    private CounterSrc(SubclassCtorArgs args) : base(args) { }

    internal static GType RegisteredType => Definition.GType;

    // The implicit implementation of a static abstract member is public.
    public static CounterSrc CreateWrapper(SubclassCtorArgs args) => new(args);
}
```

`CreateWrapper` runs wherever GStreamer happens to create the instance —
a streaming thread, inside `g_object_new`, under GStreamer's own locks. **Hand
`args` to the constructor and do nothing else**: no property access, no pad
operation, no waiting. A `CreateWrapper` that builds a fresh instance instead
of wrapping the one it was given is caught at run time with an
`InvalidOperationException`; §5.4 has the whole rule set.

The same holds for the constructor it hands them to. The per-instance gate is
held for the whole call, so the field initialisers of the subclass — they run
before the `: base(args)` arguments are evaluated — and the body of the
`(SubclassCtorArgs)` constructor run inside it as well. Keep both empty:
**wrapping another instance of a managed type there is how two streaming
threads take the two gates in opposite orders.** Everything else belongs in the
parameterless constructor, after its `this(...)` call, or behind a lazy field.
`CounterSrc` above is the shape to copy — an empty body and no fields.

### An element other code can ask for by name

`gst_element_register` puts the type in the registry under a factory name, and
from there `ElementFactory.Make` — or a `gst_parse_launch` description, or
`playbin` — creates it:

```csharp
Element.Register(null, "mycountersrc", (uint)Rank.None, CounterSrc.RegisteredType);

using Element? made = ElementFactory.Make("mycountersrc", "counter");
// made is a CounterSrc.
```

**Register with `Rank.None` unless the element really is a decoder or a sink
that autoplugging should pick.** A non zero rank makes the type eligible for
`decodebin` and `playbin`, which construct it on their own streaming threads
whenever a stream matches — the managed type is then built without the
application asking for it, and everything the class does has to be ready for
that.

### A managed pad type

`Gst.Pad` and `GstBase.AggregatorPad` are subclassable, and both are classes
whose instances GStreamer builds: a `GstBaseSrc` creates its pad from the class
pad template during construction, and an aggregator creates a sink pad when one
is requested. A pad template built with `PadTemplate.NewWithGtype` is what says
which type to build, and `Aggregator.OnCreateNewPad` is what answers a
requested one:

```csharp
internal sealed class CounterPad : AggregatorPad, IManagedSubclass<CounterPad>
{
    private static readonly SubclassType Definition = DefineSubclass<CounterPad>(
        "MyAppCounterPad", null, FlushOverride);

    private CounterPad(SubclassCtorArgs args) : base(args) { }

    public static CounterPad CreateWrapper(SubclassCtorArgs args) => new(args);

    internal static CounterPad New(string name, PadTemplate templ) =>
        new(Definition.NewInstance(new Dictionary<string, object?>
        {
            ["name"] = name,
            ["direction"] = PadDirection.Sink,
            ["template"] = templ,
        }));
}
```

The dictionary overload of `NewInstance` is not a convenience: `GstPad:direction`
is `CONSTRUCT_ONLY`, so it can only be given while the instance is being built.
The names are resolved against the class, and a name the class does not have, a
property that cannot be written and a property a managed type installed itself
are all refused with an `ArgumentException` — the last one because GObject
dispatches the write to the class that owns the property, whose wrapper does not
exist yet.

The class initialiser of a pad type is an `Action<ObjectClassConfig>?` rather
than an `Action<ClassConfig>?` (§5.5), and `null` is a legal value for it, as it
is for `Gst.Element` and `Gst.Bin`.

### The rules the surface enforces

* **Declaring and overriding are two statements of the same fact.** Only
  declared slots are patched, because GStreamer reads slot *presence* (§4.2);
  `PushSrc.CreateOverride` is what says "this element produces its own
  buffers", `BaseTransform.TransformIpOverride` what says "this element
  rewrites buffers in place". A declaration without an override costs a managed
  transition that chains up — harmless except on those presence-sensitive
  slots. An override without a declaration is never called. `GST0003` reports an
  override without a declaration and `GST0004` a declaration without an
  override.
* **A slot belongs to the class that hands it out.** Passing
  `BaseSink.RenderOverride` to `PushSrc.DefineSubclass` is refused: the offset
  only means anything inside `GstBaseSinkClass`.
* **Pad templates are mandatory for every base that creates pads in its
  instance init** (`Gst.Bin` needs none) — `src`
  for `BaseSrc`, `PushSrc`, `Aggregator`, `AudioBaseSrc` and `AudioSrc`,
  `sink` for `BaseSink`, `AudioBaseSink`, `AudioSink` and `VideoSink`, both
  for `BaseTransform`, `AudioFilter`, `VideoFilter`, `BaseParse`,
  `AudioDecoder`, `AudioEncoder`, `VideoDecoder` and `VideoEncoder` — because
  their
  instance init creates the pads from them. `DefineSubclass` checks for
  them once the class is initialised and fails with a message rather than
  letting `g_object_new` produce a half built element.
* **Build pad templates outside `class_init`.** It runs under the GObject type
  lock, and creating a wrapper there would take the interning lock of the
  binding under it — the reverse of the order every other path uses (§9,
  risk 2). `ClassConfig` therefore only adds templates that already exist.
* **Mini objects follow the C annotation.** A `transfer none` parameter — the
  buffer of `OnRender`, the caps of `OnSetCaps`, the buffer of `OnTransformIp`
  — is *borrowed*: the wrapper takes no reference and is released when the
  override returns, so keeping one means copying it. That is also what keeps
  the buffer of `OnTransformIp` writable, which a reference of our own would
  not. A `transfer full` parameter — the message of `Bin.OnHandleMessage` — is
  owned by the override, and chaining up passes it on.
* **An in/out mini object that is owned both ways is the third form.** The
  buffer of `AudioEncoder.OnPrePush` and `AudioDecoder.OnPrePush` reaches the
  override as a `ref Gst.Buffer?` that is
  `transfer full` in *and* out: the reference the caller held is handed to the
  override, and whatever the override leaves in the handle is handed back.
  Leaving it alone hands the very buffer on, assigning another one releases
  the first, and setting it to `null` drops the buffer. An override that
  throws is the fourth case: the trap answers `FlowReturn.Error`, the handle
  is cleared and the buffer is released, so nothing is leaked and the caller
  is never left holding a pointer the override did not hand over.
* **A boxed value lent to a slot is borrowed for the call and no longer.** The
  `GstAudioInfo` of `AudioFilter.OnSetup`, the `GstVideoInfo` of
  `VideoFilter.OnSetInfo` and `VideoSink.OnSetInfo`, the `GstSegment` of
  `BaseSrc.OnDoSeek` and `OnPrepareSeekSegment`, and the frames of
  `BaseParse.OnHandleFrame` all wrap the caller's value directly rather than a
  copy, which is what makes an override's writes land where the caller reads
  them. The codec classes lend the same way: the `VideoCodecState` of
  `VideoDecoder.OnSetFormat` and `VideoEncoder.OnSetFormat`, the
  `VideoCodecFrame` of `VideoEncoder.OnPrePush`, the `OnTransformMeta` of both
  video codecs and `VideoDecoder.OnParse`, the `AudioInfo` of
  `AudioEncoder.OnSetFormat`, and the `BaseParseFrame` of
  `BaseParse.OnPrePushFrame`. The wrapper is detached when the trampoline
  returns: keeping it and
  reading through it afterwards throws `ObjectDisposedException`. Whatever has
  to outlive the call is read out during it, or kept through `Copy()`, which
  for the reference counted `VideoCodecFrame` and `VideoCodecState` hands back
  a wrapper holding its own reference to the same value.
* **Exceptions never reach a native frame.** Each slot answers its documented
  error value and reports the exception through
  `GstSharp.UnhandledCallbackException`: `StateChangeReturn.Failure` for
  `OnChangeState`, `FlowReturn.Error` for `OnCreate`, `OnRender`, `OnPreroll`
  and `OnTransformIp`, `false` for the lifecycle and caps slots, and a dropped
  message for `OnHandleMessage`. The chain-up does not run afterwards. Two
  slots answer something other than the zero of their type, because their
  caller reads more into it than a failure: `AudioSink.OnWrite` answers `-1`
  and `AudioSrc.OnRead` the whole range of its unsigned answer, both of which
  the thread of the ring buffer reads as the error it is, while a zero would
  make it ask for the same block again.

### The vfuncs that are bound

One row per class, in the order the class struct lays the slots out. A slot
a class inherits is overridden through the base class that declares it, so a
managed `VideoSink` overrides `render` through `BaseSink.RenderOverride` and
`show_frame` through `VideoSink.ShowFrameOverride`.

| Base | Slots |
| --- | --- |
| `Gst.Element` | `request_new_pad`, `release_pad`, `get_state`, `set_state`, `change_state`, `state_changed`, `set_bus`, `provide_clock`, `set_clock`, `send_event`, `query`, `post_message`, `set_context` |
| `Gst.Bin` | `add_element`, `remove_element`, `handle_message`, `do_latency` |
| `Gst.Pad` | `linked`, `unlinked` |
| `Gst.Base.BaseSrc` | `get_caps`, `negotiate`, `fixate`, `set_caps`, `decide_allocation`, `start`, `stop`, `get_times`, `get_size`, `is_seekable`, `prepare_seek_segment`, `do_seek`, `unlock`, `unlock_stop`, `query`, `event`, `create`, `alloc`, `fill` |
| `Gst.Base.PushSrc` | `create`, `alloc`, `fill` |
| `Gst.Base.BaseSink` | `get_caps`, `set_caps`, `fixate`, `activate_pull`, `get_times`, `propose_allocation`, `start`, `stop`, `unlock`, `unlock_stop`, `query`, `event`, `wait_event`, `prepare`, `prepare_list`, `preroll`, `render`, `render_list` |
| `Gst.Base.BaseTransform` | `transform_caps`, `fixate_caps`, `accept_caps`, `set_caps`, `query`, `decide_allocation`, `filter_meta`, `propose_allocation`, `transform_size`, `get_unit_size`, `start`, `stop`, `sink_event`, `src_event`, `prepare_output_buffer`, `copy_metadata`, `transform_meta`, `before_transform`, `transform`, `transform_ip`, `submit_input_buffer`, `generate_output` |
| `Gst.Base.BaseParse` | `start`, `stop`, `set_sink_caps`, `handle_frame`, `pre_push_frame`, `convert`, `sink_event`, `src_event`, `get_sink_caps`, `detect`, `sink_query`, `src_query` |
| `Gst.Base.Aggregator` | `flush`, `clip`, `finish_buffer`, `sink_event`, `sink_query`, `src_event`, `src_query`, `src_activate`, `aggregate`, `stop`, `start`, `get_next_time`, `update_src_caps`, `fixate_src_caps`, `negotiated_src_caps`, `decide_allocation`, `propose_allocation`, `negotiate`, `sink_event_pre_queue`, `sink_query_pre_queue`, `finish_buffer_list`, `peek_next_sample` |
| `Gst.Base.AggregatorPad` | `flush`, `skip_buffer` |
| `Gst.Audio.AudioBaseSink` | `create_ringbuffer`, `payload` |
| `Gst.Audio.AudioBaseSrc` | `create_ringbuffer` |
| `Gst.Audio.AudioSink` | `open`, `prepare`, `unprepare`, `close`, `write`, `delay`, `reset`, `pause`, `resume` |
| `Gst.Audio.AudioSrc` | `open`, `prepare`, `unprepare`, `close`, `read`, `delay`, `reset` |
| `Gst.Audio.AudioFilter` | `setup` |
| `Gst.Audio.AudioDecoder` | `start`, `stop`, `set_format`, `parse`, `handle_frame`, `flush`, `pre_push`, `sink_event`, `src_event`, `open`, `close`, `negotiate`, `decide_allocation`, `propose_allocation`, `sink_query`, `src_query`, `getcaps`, `transform_meta` |
| `Gst.Audio.AudioEncoder` | `start`, `stop`, `set_format`, `handle_frame`, `flush`, `pre_push`, `sink_event`, `src_event`, `getcaps`, `open`, `close`, `negotiate`, `decide_allocation`, `propose_allocation`, `transform_meta`, `sink_query`, `src_query` |
| `Gst.Video.VideoSink` | `show_frame`, `set_info` |
| `Gst.Video.VideoFilter` | `set_info`, `transform_frame`, `transform_frame_ip` |
| `Gst.Video.VideoDecoder` | `open`, `close`, `start`, `stop`, `parse`, `set_format`, `reset`, `finish`, `handle_frame`, `sink_event`, `src_event`, `negotiate`, `decide_allocation`, `propose_allocation`, `flush`, `sink_query`, `src_query`, `getcaps`, `drain`, `transform_meta`, `handle_missing_data` |
| `Gst.Video.VideoEncoder` | `open`, `close`, `start`, `stop`, `set_format`, `handle_frame`, `reset`, `finish`, `pre_push`, `getcaps`, `sink_event`, `src_event`, `negotiate`, `decide_allocation`, `propose_allocation`, `flush`, `sink_query`, `src_query`, `transform_meta` |

`Aggregator::create_new_pad` is bound as well, and is what a managed sink pad
type is answered from. Eight slots of those classes carry no `OnX` member:
seven are the signal class closures of `Element` and `Bin`, which the base
library never calls through the class pointer — subscribing to the signal is
the same hook — and `AudioSink::stop` shares its name with the `stop` of
`BaseSink` and answers nothing where that one answers a `bool`, so no managed
name can carry both. `girs/skip-report.md` lists all eight with their reason.

`Gst.Pad`'s two slots are signal class closures too, and mechanically they are
no different: `linked` and `unlinked` are declared with `g_signal_new (...,
G_STRUCT_OFFSET (GstPadClass, linked), ...)` and reached by `g_signal_emit`,
which is the same `G_STRUCT_OFFSET` construction `Element::pad_added` uses and
the same emission that reads it. Subscribing to `Pad::linked` is therefore an
equivalent hook, as it is for `Element`. They are bound anyway because they are
the only two slots `GstPadClass` has, and a pad type has to be on the
subclassing allowlist for a base class to be able to build one from a managed
template at all.

The six slots that lend a boxed record by pointer — `BaseSrc::do_seek` and
`prepare_seek_segment`, `BaseTransform::filter_meta`, `AudioFilter::setup`,
and the `set_info` of `VideoSink` and `VideoFilter` — are bound: the wrapper
they are handed borrows the value rather than copying it, so what the override
writes lands in the record the caller owns. The borrow lasts for the call and
no longer, which is the boxed-borrow rule above.

### Slots a subclass has to declare

Most slots have an answer for a NULL parent that the element survives, and
`DefineSubclass` accepts a registration without them. Fourteen slots on ten
classes do not, and the registration says so before it takes the type name:

| Class | Slot | Why |
| --- | --- | --- |
| `Aggregator` | `aggregate` | the base class calls it unguarded |
| `AudioBaseSink`, `AudioBaseSrc` | `create_ringbuffer` | without a ring buffer the element cannot leave the NULL state |
| `AudioSink`, `AudioSrc` | `prepare`, `unprepare` | acquiring and releasing the ring buffer start out with a failure that only the slot turns into a success |
| `AudioSink` | `write` | the thread of the ring buffer stops before it starts when the slot is NULL |
| `AudioSrc` | `read` | the same |
| `BaseParse`, `AudioDecoder`, `AudioEncoder`, `VideoDecoder`, `VideoEncoder` | `handle_frame` | the base class calls it for every frame, and for the drain at the end of the stream, unguarded |

### The limits

* **`AudioBaseSink` and `AudioBaseSrc` cannot be subclassed directly from
  managed code yet.** Their required `create_ringbuffer` slot has to answer a
  `GstAudioRingBuffer` subclass, and `AudioRingBuffer` is not subclassable;
  derive from `AudioSink` / `AudioSrc`, which bring their own ring buffer.
* **A managed subclass cannot be derived from by another managed subclass.**
  One level only: the chain-up resolves the parent class of the registration,
  and a managed parent's slot would be the same trampoline (§4.4). The surface
  cannot express it — `DefineSubclass` always derives from the wrapped native
  class it is called on.
* **A type defined with the non generic `DefineSubclass` is
  C#-initiated-only.** `new CounterSrc()` works, and so does anything that
  creates the instance natively — an element factory, a `gst_parse_launch`
  description, GES instantiating a type by name — but only for a type that was
  defined with `DefineSubclass<TSelf>`. Without a wrapper factory the instance
  is wrapped as the closest registered ancestor, `TypeRegistry.Fallback` reports
  it once, and its vfuncs chain up for ever: **functional never**. The overload
  is the whole difference, and the fallback is what diagnoses the mistake.
* **No construct properties** on managed types: a property a managed subclass
  installs is refused if it asks for `CONSTRUCT` or `CONSTRUCT_ONLY`, because
  GObject delivers those before any wrapper exists (§5.6). Plain readable and
  writable properties are installed, are settable from a pipeline description,
  and notify like any other; signals are defined with `AddSignal`.
* **`GstAudioSink::stop` has no managed member.** Its name collides with
  `BaseSink::stop`, which answers a `bool` where the audio one answers nothing,
  and C# cannot give one name two return types; a disambiguated managed name is
  a naming decision that has not been taken. The device is unblocked through
  `OnReset` instead, which is what `gst_audio_sink_ring_buffer_stop` falls back
  to when the slot is NULL (gstaudiosink.c:594-602).
* **A managed pad installs properties and signals through the same facade** as
  everything else: `InstallProperty` and `AddSignal` live on
  `ObjectClassConfig`, which is what the class initialiser of `Gst.Pad` and
  `GstBase.AggregatorPad` is given, so a pad type is not a special case here.
* **An interface can only be declared when the type is defined**, and only
  one the binding provides an implementation of — `GstURIHandler` today. There
  is no way to add one from the class initialiser or afterwards: GObject
  refuses it (§5.7). Defining a new interface from managed code stays out
  entirely.
* **No `dispose` or `finalize` override**, by design (§1): teardown belongs in
  the `READY` to `NULL` transition of `OnChangeState`, or in `OnStop`.
* **Disposing a managed element that GStreamer still drives** does not crash,
  but the element silently loses its managed behaviour: the wrapper is gone, so
  every vfunc chains up. The ordinary rule of
  [`docs/ownership.md`](ownership.md) applies — do not dispose GObject
  wrappers.

---

*Appendix — gir facts referenced above (verified against
`girs/reference/` at 1.28):*
`Gst-1.0.gir`: 79 `<virtual-method>` elements; `Element` declares 16
(`change_state`, `get_state`, `no_more_pads`, `pad_added`, `pad_removed`,
`post_message`, `provide_clock`, `query`, `release_pad`, `request_new_pad`,
`send_event`, `set_bus`, `set_clock`, `set_context`, `set_state`,
`state_changed`); `ElementClass._gst_reserved` is `fixed-size="18"`.
`GstBase-1.0.gir`: `BaseSrcClass` fields `get_caps … create, alloc, fill`,
`_gst_reserved` `fixed-size="20"`; `BaseTransformClass` leads with
`passthrough_on_same_caps`, `transform_ip_on_passthrough`.
`GObject-2.0.gir`: `TypeInfo` (`class_size` guint16 …), `TypeQuery`,
`type_register_static` (takes `info`), `type_register_static_simple`
(no `class_data`, `introspectable="0"`), `ClassInitFunc`,
`InstanceInitFunc`, `InterfaceInfo`.