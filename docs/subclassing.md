# GObject subclassing in GstSharp.Net — design

Status: **approved design**; stages 0 and 1 of §10 have shipped, stages 2 and
3 have not. §11 is the guide to what shipped; everything before it is the
design the implementation follows.
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
   `event`, `do_seek`.
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
* **Defining new GObject interfaces** from managed code. (Implementing
  existing ones is a late stage, see §9.)
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
and later `g_type_add_interface_static`. All on the existing `"GObject"`
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
* **Transfer-full returns**: a managed override that returns a wrapper does
  not give up the wrapper's own reference (the toggle ref owns it). The
  trampoline takes an extra `g_object_ref` (or `gst_mini_object_ref`) on the
  handle before returning it to native code. For floating-capable returns
  (`request_new_pad` returning a fresh `Pad`), the same
  `IsFloating`/ref-sink reasoning as `Object`'s constructor applies and must
  be spelled per slot.

### 4.4 Chaining up

`ClassInit` captures `g_type_class_peek_parent(gClass)` into the descriptor
(§3.3). One **static** chain-up core per slot reads the parent's slot
through the class-struct mirror and calls it as a raw function pointer; the
trampoline fallback (§4.1) and a `protected` instance wrapper both call it:

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

### 5.4 Native-initiated construction (deferred, stage 3)

`gst_element_register` + `gst_element_factory_make`, or GES instantiating a
registered type by name, create the instance natively; the managed wrapper
must be **fabricated** on first contact. That requires a wrap factory
(`delegate*<nint, Transfer, object>` — the exact `ModuleTypeEntry.Factory`
shape) for the user's type, which in turn requires user subclasses to expose
a handle-based construction path — the natural NativeAOT-safe protocol is a
static abstract interface member
(`static abstract MySrc CreateWrapper(nint, Transfer)` on an
`IManagedSubclass<TSelf>` constraint of the registration API). Until that
lands, types without a factory are **C#-initiated-only**; a natively created
instance of such a type would fall back to an ancestor wrapper
(`TypeRegistry.Fallback` fires) and its vfuncs would permanently chain up —
functional never, so stage 1 documents this loudly and stage 3 closes it.

### 5.5 Class configuration is part of registration

`class_init` must do more than patch vfuncs for the result to be usable:

* `gst_element_class_set_metadata` (longname/klass/description/author).
* `gst_element_class_add_pad_template` — **mandatory** for the GstBase
  classes: `GstBaseSrc`'s instance init fetches the class's `"src"` pad
  template to create its pad; a `BaseSrc` subclass whose class has no such
  template fails at instance init. Same for `BaseSink`'s `"sink"`.

The `ClassInitializer` delegate on the descriptor (§3.3) receives a
`ClassConfig` facade exposing exactly these operations, implemented over
the raw `gClass` pointer. Because the runtime (`Core/`) and the `Gst`
bindings share one assembly (`GstSharp.Net`), while `GstBase` bases live in
`GstSharp.Net.Base`, the facade is extensible per module rather than
hardcoded in Core.

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
| `ElementClassRaw`, `BaseSrcClassRaw`, … for the closed set | 0–1 | hand-written; **replaced by generated** in stage 2 |
| Subclass bases: `protected` ctor, `OnX` virtuals, `ChainUpX` helpers, vfunc trampolines, `ClassConfig` glue | 1 | hand-written `Custom/` partials |
| Class-struct mirrors for an **allowlisted** set of classes | 2 | generated (`ClassStructEmitter`, new) |
| Typed vfunc surface (`OnX` + trampoline + chain-up) from `<virtual-method>` | 2 | generated, reusing `MarshalPlanner` plans in reverse (the `SignalEmitter` trampolines are the proof the reverse direction fits the planner) |
| Analyzer: override/declaration consistency | 2 | `GstSharp.Net.Analyzers` |
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
  `"Gst.Element::set_bus"`-style if needed.
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
  wrapper. The only windows without a wrapper are construction (§5.2) and
  after `Dispose` — both covered by the chain-up rule (§4.1).
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
5. **Native-initiated construction gap** (stage ≤ 2): a factory-made
   instance of a managed type would be an ancestor-wrapped zombie (§5.4).
   Ship stage 1 with the limitation documented and `TypeRegistry.Fallback`
   as the diagnostic; close it in stage 3.
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
9. **Interfaces**: custom URI-addressable sources need `GstURIHandler`
   (`<interface name="URIHandler" glib:type-struct="URIHandlerInterface">`),
   the first concrete consumer of `g_type_add_interface_static` +
   `GInterfaceInfo.interface_init`. `InterfaceEmitter` binds no vfuncs
   today; interface implementation is stage 3 and follows the same
   patch-declared-slots pattern on the interface vtable.
10. **Properties on managed types**: GES effect/source configuration may
    eventually require installable properties (`g_object_class_install_property`
    inside `ClassInit` + `get_property`/`set_property` vfunc overrides).
    Deliberately out of stage 0–2; the `ClassConfig` facade is the natural
    future home.

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

**Stage 2 — generator-emitted vfunc surface.**
New `ClassStructEmitter` behind a `"subclassable"` overlay allowlist:
generated `*ClassRaw` mirrors (transitive parent chain), generated `OnX` /
trampoline / chain-up members reusing `MarshalPlanner`, per-vfunc skip
overlay, census categories, analyzer for declaration/override consistency.
Stage-1 hand-written mirrors are deleted in the same change (the diff gate
keeps the swap honest).

**Stage 3 — breadth.**
Native-initiated construction via static abstract `CreateWrapper` factories
registered into `TypeRegistry` (needs the new
`TypeRegistry.RegisterSubclass` entry point, §3.3);
`g_type_add_interface_static` with `GstURIHandler` first; property/signal
installation; `gst_element_register` for by-name construction (prerequisite
for GES custom sources and for plugin-style use); then the GES wave can
rely on all of it.

---

## 11. Using it (stage 1)

What shipped is a **closed set of subclassable base classes**: `Gst.Element`,
`Gst.Bin`, `Gst.Base.BaseSrc`, `Gst.Base.PushSrc`, `Gst.Base.BaseSink` and
`Gst.Base.BaseTransform`. Each one carries three things — a `DefineSubclass`
that registers a managed type, a `protected` constructor that builds instances
of it, and, per bound vfunc, an `OnX` virtual with a matching `ChainUpX`.

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

### The rules the surface enforces

* **Declaring and overriding are two statements of the same fact.** Only
  declared slots are patched, because GStreamer reads slot *presence* (§4.2);
  `PushSrc.CreateOverride` is what says "this element produces its own
  buffers", `BaseTransform.TransformIpOverride` what says "this element
  rewrites buffers in place". A declaration without an override costs a managed
  transition that chains up — harmless except on those presence-sensitive
  slots. An override without a declaration is never called. The analyzer that
  checks the pairing is stage 2.
* **A slot belongs to the class that hands it out.** Passing
  `BaseSink.RenderOverride` to `PushSrc.DefineSubclass` is refused: the offset
  only means anything inside `GstBaseSinkClass`.
* **Pad templates are mandatory for the GstBase bases** — `src` for `BaseSrc`
  and `PushSrc`, `sink` for `BaseSink`, both for `BaseTransform` — because
  their instance init creates the pads from them. `DefineSubclass` checks for
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
* **Exceptions never reach a native frame.** Each slot answers its documented
  error value and reports the exception through
  `GstSharp.UnhandledCallbackException`: `StateChangeReturn.Failure` for
  `OnChangeState`, `FlowReturn.Error` for `OnCreate`, `OnRender`, `OnPreroll`
  and `OnTransformIp`, `false` for the lifecycle and caps slots, and a dropped
  message for `OnHandleMessage`. The chain-up does not run afterwards.

### The vfuncs that are bound

| Base | Slots |
| --- | --- |
| `Gst.Element` | `change_state` |
| `Gst.Bin` | `handle_message` |
| `Gst.Base.BaseSrc` | `start`, `stop`, `is_seekable`, `set_caps` |
| `Gst.Base.PushSrc` | `create` |
| `Gst.Base.BaseSink` | `start`, `stop`, `set_caps`, `preroll`, `render` |
| `Gst.Base.BaseTransform` | `start`, `stop`, `set_caps`, `transform_ip` |

Everything else is stage 2, and the omissions that will be missed first are
`unlock` / `unlock_stop` (so a managed source must not block in `OnCreate`),
`BaseSrc.fill` (filling a buffer the pipeline provided), `query` and `event`,
`BaseTransform.transform` with the caps negotiation slots that an out of place
filter needs, and `request_new_pad`.

### The limits of stage 1

* **A managed subclass cannot be derived from by another managed subclass.**
  One level only: the chain-up resolves the parent class of the registration,
  and a managed parent's slot would be the same trampoline (§4.4). The surface
  cannot express it — `DefineSubclass` always derives from the wrapped native
  class it is called on.
* **C#-initiated construction only.** `new CounterSrc()` works;
  `gst_element_factory_make("mycountersrc")` does not exist, and neither does
  anything else that creates the instance natively — GES instantiating a type
  by name, a `gst_parse_launch` description naming it, a plugin. A natively
  created instance of a managed type would be wrapped as the closest registered
  ancestor, `GstSharp.TypeFallback` would report it once, and its vfuncs would
  chain up for ever: **functional never**. Closing this is stage 3 (§5.4).
* **No properties and no signals** on managed types, so a managed element
  cannot be configured with `g_object_set` or from a pipeline description.
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