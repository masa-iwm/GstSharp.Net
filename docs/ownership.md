# Ownership and lifetime

Who owns a wrapper, who releases it, and when. This is the reference the rest
of the documentation and the XML comments point at.

The binding has two object models, and there is one rule per model. Which rule
applies follows from the base type of the wrapper and from nothing else:

| Base type | Examples | What the wrapper owns | Disposed by consumers |
| --- | --- | --- | --- |
| `Gst.MiniObject`, `Gst.GObject.Boxed` | `Buffer`, `Caps`, `Sample`, `Message`, `Event`, `Structure`, `SDPMessage`, `GLib.Bytes` | a reference of its own | **always** |
| `Gst.GObject.Object` | `Element`, `Pipeline`, `Bus`, `Pad`, `Clock`, `Device` | one reference shared by the whole process | **never** — not even by a call that takes the object over |

## Mini objects and boxed values

Every `MiniObject` or `Boxed` wrapper handed to user code owns a reference of
its own: a mini object is reffed, a boxed value is copied. Those wrappers are
not interned, so two lookups of the same object produce two wrappers holding
two references, and each one has to be released.

```csharp
using Sample? sample = sink.TryPullSample(timeout);
using Gst.Buffer? buffer = sample?.GetBuffer();

if (buffer is not null)
{
    using Gst.Buffer.MapScope map = buffer.Map(MapFlags.Read);
    Consume(map.Span);
}
```

`GST0001` reports a local of such a type that is never disposed and never
escapes; `GST0002` reports a `Buffer.Map` scope that is never released. See
[the analyzer rules](analyzers.md).

Three consequences worth stating:

* **No owning properties.** A property that produced an owned wrapper would
  produce one per read, in the one place the analyzer cannot watch. The
  generator keeps those getters as methods — `appsrc.GetCaps()`, not
  `appsrc.Caps` — and lists them under `OwningProperty` in
  `girs/skip-report.md`.
* **A `Structure` from `Caps.GetStructure` is a copy**, not a window into the
  caps. Writing to it does not write back, whether the caps are writable or
  not.
* **A generated field getter reads through the live handle** and owns nothing:
  `segment.Start` reads the C structure the wrapper points at, not a snapshot
  of it. On a wrapper that owns its value — a boxed one or a mini object — the
  getter therefore throws `ObjectDisposedException` once the wrapper is
  disposed, exactly as every other member that needs the handle does.

Release is synchronous. `Dispose` unrefs on the calling thread and the
finalizer unrefs directly; nothing is ever deferred through a GLib timeout or
an idle callback.

**One kind of wrapper owns nothing: the one a vfunc override is given.** A
buffer handed to `BaseSink.OnRender` or `BaseTransform.OnTransformIp`, the caps
handed to an `OnSetCaps`, the query handed to an `OnQuery` — every mini object a
vfunc lends rather than hands over, whatever its type — is *borrowed* for the
length of the call: GStreamer keeps owning it, the wrapper takes no reference of
its own, and it is released when the override returns. Using one afterwards throws
`ObjectDisposedException` rather than touching an object somebody else owns, so
keeping the data means copying it. Disposing such a wrapper early is harmless,
and `MakeWritable` on one throws: it would release a reference the wrapper does
not own. See
[`docs/subclassing.md`](subclassing.md#11-using-it).

Three shapes sit beside that borrow, one per direction the ownership can
travel in:

* **A parameter the slot takes over.** The message of `Bin.OnHandleMessage`,
  the event of `BaseSink.OnEvent`, the caps of `BaseSrc.OnFixate`: the
  override owns the wrapper it is given, chaining up hands the ownership on
  and returning without chaining up releases it. Copy it to keep it beyond
  the call. The documentation of the parameter says which of the two it is.
* **A buffer the slot may hand back unchanged.** `BaseSrc.OnCreate` is given
  the buffer downstream provided and `BaseTransform.OnPrepareOutputBuffer`
  the input buffer, and answering that very wrapper is what filling or
  transforming in place looks like. The caller compares the two pointers and
  only releases the one it passed in when they differ, so the binding takes
  no reference for an answer that did not change.
* **An object the caller parents, adds or references.** The ring buffer of
  `AudioBaseSink.OnCreateRingbuffer` and `AudioBaseSrc.OnCreateRingbuffer`, the
  pad of `Aggregator.OnCreateNewPad`, the element of `Source.OnCreateSource`
  and `TrackElement.OnCreateElement` are answered *without* a reference being
  added on the way out, because the base class takes one of its own from the
  answer: `gst_object_set_parent`, `gst_element_add_pad` and `gst_bin_add` all
  `ref_sink` it. What a managed override answers is never floating — the
  wrapper sank it when it was built — so that `ref_sink` is a plain reference
  and element and wrapper co-own the object, which is the "returned GObject"
  case below. Answer an object that has no parent yet, keep no extra reference
  to it, and read it back from the element instead.
* **A mini object the slot answers.** The buffer of `Aggregator.OnClip`, the
  caps of `BaseTransform.OnTransformCaps`, the buffer of
  `AudioBaseSink.OnPayload`: the wrapper you return is *handed over*, not
  referenced a second time. The element takes the reference the wrapper held
  and the wrapper is detached, so it throws from then on exactly like the
  wrapper of a consumed argument — copy or ref the object first if you need it
  afterwards. That is what keeps a buffer an override produced writable
  downstream and a pooled one out of the finalizer queue. Answering the very
  mini object the slot lent you is allowed: a borrowed wrapper has no reference
  to give away, so one is minted for the element and the borrow stays what it
  was. A returned **GObject** is the other way round — the wrapper is interned
  and the toggle ref owns its reference, so the element gets one of its own and
  the wrapper goes on working.

A record with no reference count of its own — a video frame, a ring buffer
specification, a metadata item — is lent as a bare pointer holder: the
wrapper takes no part in the ownership of what it points at, and the pointer
is regularly an address on the stack of the caller, so it stops meaning
anything once the call returns. **The trampoline detaches such a wrapper when
the call returns**, whatever the override did with it: read what is needed out
of it before then, because every member of a wrapper that outlived the call
throws `ObjectDisposedException` rather than reading an address that means
nothing any more. It is the same detach a borrowed mini object gets from the
`using` around it, and the three records it applies to are named by
`lentOpaqueRecords` in `girs/overlays/fixups.json`; a record a slot lends and
that list does not name fails the run with `GEN0045`.

## Metadata items and the buffer that owns them

A metadata wrapper — `Gst.Meta` and every typed `*Meta` record — **owns
nothing**. It addresses storage inside the buffer that carries the item, it
takes no reference and it is never disposed, so it takes no part in ownership at
all. Its lifetime is the lifetime of the item: it dies with the buffer, and it
dies with the item when `Buffer.RemoveMeta` removes it or a
`Buffer.ForeachMeta` function answers `MetaForeachAction.Remove` and the walk
honours it. The library frees a removed item synchronously, before the call that
removed it returns.

A wrapper whose handle is zero is dead, and that is the one convention the
hand-written metadata surface shares: `RemoveMeta` zeroes the handle when, and
only when, it answers `true`, and a honoured `Remove` does the same, after which
every hand-written member — `Meta.Info`, `Meta.ApiType`, `Meta.Serialize` and
every `FromMeta` cast — throws `ObjectDisposedException`. **`Gst.Meta` says the
same through its generated members**: it is one of the records
`lentOpaqueRecords` names, so its handle sits behind the checking accessor that
list gives a lent record, and `Meta.Flags` reads the item through it. **The
typed `*Meta` records still read the field unchecked**: they are not on that
list and keep a plain handle field, so reading `VideoMeta.Width` or
`AudioMeta.Samples` through a wrapper of an item that was removed is undefined
rather than an exception. Extending the guard to them is on the backlog.

The converse holds as well: a removal the walk **refuses** leaves the wrapper
alive. `MetaForeachAction.Remove` needs a writable buffer and an item that is not
flagged `GST_META_FLAG_LOCKED`, and when either fails the library answers `false`
and aborts the walk before it frees anything, so the item is still attached.
`ForeachMeta` restores the handle it had provisionally zeroed in that case, and
the wrapper keeps reading the item it always addressed.

The typed casts are reinterpretations and nothing else. `GstMeta` is the first
field of every typed metadata structure, so `VideoMeta.FromMeta(meta)` hands out
a second wrapper over the same address; both are alive exactly as long as the
item is.

An item that carries a **managed callback** owns the state of it, and the
buffer owns the item. `VideoGlobal.BufferAddVideoGLTextureUploadMeta` is one
of those: the upload delegate is held by a `CallbackHandle` the library releases
through the user data free the attach hands it, so the delegate stays reachable
for as long as any buffer carries the item — including every copy, each of which
takes a handle of its own to the same delegate. The caller keeps nothing alive.
The one path native code never releases is the attach the library refuses, which
is a shared buffer: it answers NULL before it stores the state, so the member
releases the handle itself and answers `null`.

`Buffer.IterateMeta` is **read only for the length of the enumeration**. The
cursor of `gst_buffer_iterate_meta` is the metadata item itself, so removing one
while the enumeration is open frees the node the cursor stands on and the next
step is a use after free. `Buffer.ForeachMeta` is the way to remove while
walking, because the library captures the successor of an item before it hands
it to the function.

### Authoring a metadata implementation

`Gst.Meta.Register<T>` registers an implementation whose item is a `GstMeta`
header followed by exactly one `T`, and `Gst.Meta.Payload<T>()` is how that `T`
is reached again. `T` has to be `unmanaged`, and its alignment requirement must
not exceed eight bytes: the payload starts at the size of the header rounded up
to eight, and the library allocates an item with `g_malloc`, which promises
nothing stronger.

The registration is **for the life of the process**. The library keeps every
implementation it registered in a table it only empties in `gst_deinit` and
offers no way of taking one back, so the delegates handed to `Register<T>` are
never released and whatever they capture is rooted for as long. A name is a
`GType` name, so it can only be registered once: a second registration under the
same name throws `InvalidOperationException`, and the implementation that owns
the name is untouched by the refusal. Register once, at start-up, and keep the
`Gst.MetaInfo` that comes back.

What each delegate has to promise:

* **`MetaInitFunction`** runs inside `Buffer.AddMeta`, on the item that was just
  attached, with the `params` pointer of that call. The payload is zero filled
  before it runs. Answering `false` makes `AddMeta` answer `null` and the
  library frees the item **without calling the release delegate**, so an
  initialisation that fails half way has to undo its own work itself. The item
  wrapper it was handed is detached on that path, because the memory behind it
  is freed as the refusal returns.
* **`MetaFreeFunction`** runs immediately before the item memory is freed, from
  a buffer being finalised, from `RemoveMeta` and from a removal a
  `ForeachMeta` walk honoured. The buffer wrapper it is handed is disposed when
  the delegate returns — the buffer is being freed or has just lost the item, so
  the delegate must not keep, reference or return it. The **item** wrapper is
  detached when the delegate returns as well, so a caller that filed it away
  meets `ObjectDisposedException` rather than freed memory.
* **`MetaTransformFunction`** is called on the item of the **source** buffer and
  has to add an item to the destination buffer itself; answering `false` is only
  logged by the library and the copy goes on. A registration **without** a
  transformation is not carried across a copy at all, which is what a null
  `transform_func` means in C. A copy passes the quark of `"gst-copy"` and the
  address of a `Gst.MetaTransformCopy`.
* **`MetaSerializeFunction`** appends the payload through
  `Gst.ByteArrayInterface.AppendData` and may write a version byte;
  **`MetaDeserializeFunction`** reads it back, adds an item to the buffer it is
  handed and answers that item. Both arrived in GStreamer 1.24.
* **`MetaClearFunction`** is called only by `GstBufferPool`, when a buffer goes
  back to its pool, and takes the buffer first, as it does in C.

All six run on whatever thread touches the buffer, which is usually a streaming
thread and never one the caller chose. An exception that escapes one of them is
caught on the boundary and handed to `Gst.Interop.ExceptionTrap`, and the
callback answers its own default: `false` for the initialisation, the
transformation and the serialisation, `null` for the deserialisation, and
nothing at all for the release and for the reset, which answer nothing to begin
with.

## GObject wrappers

A `GObject` wrapper is **interned**. Every lookup of the same native object
hands out the same instance, and that instance owns one reference for the whole
process, held through a toggle reference. While native code holds a reference
besides that one, the wrapper is kept alive, so managed state attached to it
survives a round trip through GStreamer; once the toggle reference is the only
one left, the collector may take the wrapper and the runtime releases the
object.

`Dispose` on such a wrapper therefore does not mean "release my reference". It
means "this process is done with the object": it disconnects the handlers the
wrapper connected and gives up its part in the lifetime — **for every holder at
once**, because there is only ever one wrapper. Normally do not call it. Let
the collector take the wrapper and the runtime release the object.

The one sanctioned case is an object this code created and is finished with,
the pipeline being the example that matters:

```csharp
if (Global.ParseLaunch(description) is not Pipeline pipeline)
{
    return 1;
}

using (pipeline)
{
    Bus bus = pipeline.GetBus();   // interned, not disposed
    pipeline.SetState(State.Playing);
    // ...
    pipeline.SetState(State.Null); // before the pipeline is released
}
```

Order matters: a pipeline that is still `PLAYING` when its last reference goes
away leaves its streaming threads running. Set it to `NULL` first.

### Fabricated wrappers

The wrapper of an instance of a managed subclass that native code created — an
element an element factory made, a pad a base class built from its class
template — is **fabricated** on first contact rather than constructed from C#.
It owns nothing extra: the instance belongs to whoever created it, so the
wrapper never sinks it and only takes the one reference its toggle reference
holds. The reference the call that reached the instance was handed is settled
exactly as it is for a wrapper that existed already: a `transfer full` one is
dropped, and a floating instance is sunk first, which is what an element factory
answers. What is left is the state a `new MyElement()` leaves behind — one
reference, held by the wrapper — so nothing about the paragraphs above changes
for a fabricated wrapper, including `Dispose`. See
[`docs/subclassing.md`](subclassing.md) §5.4.

`GES.Asset.Extract<T>()` is handed a **floating** `GObject`, as an element
factory is. The editing services build the object with `g_object_new` and give
it back without sinking it, so the wrapper sinks it and
owns the single reference that exists; the asset is untouched and keeps
describing what it described. The caller owns the result exactly as it owns a
`new MyElement()`, which is what makes it safe to answer from
`GES.Clip.OnCreateTrackElement`: the container that takes the child takes a
reference of its own. Disposing it before the slot returns would leave the
container with nothing.

A property getter can hand out a **floating** `GObject` as well, when the owner
that created it never sank it. As of 1.28 `webrtcbin` builds its ICE agent with
`g_object_new` and never calls `gst_object_ref_sink` on it, so the one
reference the element holds for its `ice-agent` property is still the floating
one, which GObject defines as owned by nobody. The binding does what it does
everywhere and sinks the handle, after which the wrapper owns the only
reference that exists. A caller of such a property must not dispose that
wrapper while the owner is still using the object, and can hand the owner back
the reference it should have taken with one `g_object_ref` that is never
released; `tests/GstSharp.IntegrationTests/WebRTCICECandidateTests.cs` is the
worked example, with the compensation documented beside the read and removable
the day upstream sinks the agent.

A `(transfer full)` annotation is a promise about the C, and one call of that
agent did not keep it. `WebRTCICE.AddStream` is annotated transfer full in the
gir and in the base class, but the libnice implementation below it returned the
reference its own stream map holds without taking one: it is missing in every
1.24.x release and in 1.26.0 through 1.26.9. Upstream took the missing
reference in `ea6200de`, which shipped in 1.27.50 and was backported to the
1.26 branch as `dbc83c3ec1`, first tagged 1.26.10; the reference is therefore
present from 1.26.10 and from 1.27.50 / 1.28.0 on. The gir this binding is
generated from describes the C of 1.28, while the runtime floor is 1.24, so the
generated member adopted the agent's only reference on an older runtime and
disposing the wrapper freed a stream the agent still listed. The binding is hand written for that reason and takes the
reference itself on anything that predates the fix, which it decides from the
version of the loaded library. The gate errs toward taking it: a reference too
many leaks one small object, a reference too few frees a live one, and the
count a caller sees is two either way — one for the agent, one for the wrapper.

`Object.As<T>()` owns nothing of its own. When the wrapper class does not
declare the interface, the cast hands back a small view that holds a strong
reference to the wrapper it came from and reads the handle through it, so the
view keeps the wrapper alive and takes no reference of its own on the native
object. The lifetime stays the wrapper's: there is nothing to dispose on a
view, and disposing the wrapper makes every view of it throw
`ObjectDisposedException`. When the wrapper class does declare the interface,
the cast is the wrapper itself and there is no view at all.

### Detecting an over-unref

A toggle reference is a strong native reference, so an object cannot die while
a wrapper holds one. If it dies anyway, somebody dropped a reference they did
not own, and the failure surfaces much later — usually as a crash inside
`g_object_remove_toggle_ref` when the wrapper is released or the pending
release queue is drained, with nothing left to say which object it was.

Set `GSTSHARP_DETECT_OVER_UNREF=1` in the environment of the process, before it
starts, to move the report to the moment of the death — the variable is read
once, so setting it from inside the process is too late. Every wrapper then
installs a weak notification next to its toggle reference. The notification runs inside the
unref that killed the object, while the instance can still be read, and reports
an `InvalidOperationException` through `Gst.Interop.ExceptionTrap` naming the
native type, the wrapper type, the handle, the managed stack the wrapper was
built on (`Wrapper constructed at`) and the managed stack of the unref that
killed the object (`Object destroyed at`). Combine it with
`GSTSHARP_FAILFAST=1` to stop the process on the first report.

The wrapper itself is fenced off at the same moment: it reads as disposed, its
`Handle` throws an `ObjectDisposedException` that says the object was destroyed
underneath it instead of handing out a dangling pointer, it leaves the interning
table so that a later object at the same address gets a wrapper of its own, and
its `Dispose` — and its finalizer — call nothing on the corpse.

The switch is an environment variable rather than a `GstSharpOptions` member
because wrappers are built before and during `GstSharp.Initialize`, which an
option read at the end of the initialisation would miss.

What it costs while it is on: one extra native call when a wrapper is built
(`g_object_weak_ref`), one when it is released (`g_object_weak_unref`), and one
captured managed stack per live wrapper. That is why it is off by default, and
off costs no native call, no capture, no allocation: nothing is installed and
nothing is captured.

Two limits are worth knowing before reading a report:

* **The false positive.** `g_object_run_dispose` fires weak notifications on an
  object that stays alive, and the detector cannot tell that from a death.
  Inside GStreamer itself there is one such call, on the fence cache a Vulkan
  device owns (`gst-libs/gst/vulkan/gstvkdevice.c`), so a report naming an
  object the Vulkan backend owns may be this rather than a defect. The wrapper
  is fenced off all the same, although its object is alive: its toggle
  reference is never removed, so the object is leaked, and a later lookup of
  the same object builds a second wrapper. The detector is a diagnostic run,
  not a mode to ship.
* **What it cannot see.** The opposite defect — the binding releases its
  reference first and the object dies later, of a reference somebody else
  dropped afterwards — leaves no weak notification to fire while the wrapper is
  still watching. It is structurally undetectable from here. So is a death that
  lands in the narrow window inside the release itself, after the bookkeeping
  of the toggle reference is done: the notification finds nothing to report,
  exactly as the removal of a toggle reference has always raced that window. A
  death that lands after a `Dispose` has passed its guard but before that
  bookkeeping is reported, but that `Dispose` can no longer be stopped from
  touching the object.

## Parameter specifications

`Gst.GObject.ParamSpec` wraps a `GParamSpec`, the description of one property.
It is the one wrapper of the runtime that is neither interned nor made by a
factory: a member that hands one out constructs it, and the instance owns one
reference of its own. `Dispose` releases that reference and nothing else — a
specification belongs to the class that registered it, so disposing the wrapper
never takes the description away from the class.

The two directions read as they do everywhere else. A `ParamSpec` a member
**takes** is borrowed for the call: the wrapper keeps its reference, the callee
takes one of its own if it keeps the specification — `TimelineElement.AddChildProperty`
is that shape — and the caller still disposes what it passed. A `ParamSpec` a
member **hands out** is the caller's to dispose, whether the C function lent its
reference or transferred one:

| Member | What C transfers | What the wrapper does |
| --- | --- | --- |
| `ChildProxyExtensions.Lookup` | nothing; the specification belongs to the class of the child | takes a reference of its own |
| `TimelineElement.LookupChild` | a reference (`g_param_spec_ref`) | adopts it |
| `TimelineElement.ListChildrenProperties` | one reference per element and the block (`ges-timeline-element.c:293-310`) | adopts every element and frees the block |

Both girs are right, and the difference is not smoothed over: the reference the
wrapper holds is one reference either way, and disposing it is correct in both.

That reference is a lifetime of its own. The wrapper takes it at construction
when the pointer it was given is borrowed (`g_param_spec_ref_sink`) and gives
it up in `Dispose` and nowhere else, so what `Lookup` or `LookupChild` — or a
signal — handed out stays readable after the object or the child it describes
is gone: a `GParamSpec` lives by its own reference count, not by that of any
instance. The wrapper has no finalizer, which makes its leak one of the two
silent ones in the runtime: an instance that is never disposed holds that
reference until the process exits. Little is lost when that happens, since an
installed specification belongs to a class and lives as long as the process
anyway, but dispose it as you would any other wrapper.

`Gst.ByteArrayInterface()` is the other one. An instance the public
constructor built owns the record it points at and the `GByteArray` behind it,
`Dispose` releases both, and there is no finalizer to do it later — so a
serialisation sink that is dropped without being disposed leaks the bytes it
collected. A wrapper handed to a `MetaSerializeFunction` is lent instead: it
owns nothing and disposing it does nothing.

`ListChildrenProperties` is the plural of that row: it answers a
`ParamSpec[]`, never `null` — an element with no child properties answers the
empty array — sorted by name, with every element the caller's to dispose. A
`foreach` that disposes as it goes is the shape, exactly as it is for
`Object.ListProperties`, whose elements the class owns instead: the two look
alike and release differently, and the wrapper is what makes them read the
same.

The same three shapes reach a managed override of the child property slots of
a timeline element (`docs/subclassing.md`), with the ownership mirrored: a
specification a slot is **lent** is call-scoped and the trampoline disposes the
wrapper when the override returns, and one an override **produces** or
**returns** is consumed — one reference is handed to the caller and the wrapper
is disposed right after. Hand out a wrapper of your own there, never one a
field of the class keeps.

A lookup that finds nothing answers `false` and leaves **both** out parameters
`null`. The C functions do not touch the storage they were given on that path,
so the binding zeroes it before the call and reads a null pointer back as
`null`; there is no stale value to guard against and no wrapper to dispose.

What comes back is the derived class that matches the native one:
`ParamSpec.FromNative` reads `G_PARAM_SPEC_TYPE` and hands out a
`ParamSpecInt`, a `ParamSpecEnum` or one of their siblings, so a caller can
pattern match on it and read the range or the table it carries. That changes
nothing about the reference: a derived wrapper owns exactly the one reference
the base class owns, and `Dispose` is the same call on all of them. The public
constructor `ParamSpec(nint, Transfer)` still wraps in `ParamSpec` itself, and
is the one shape that does not look at the type of what it is given.

One member of a specification lends what it answers, one hands a wrapper over,
and the two tables own nothing at all:

* `DefaultValue` is **borrowed**. The `GValue` behind the `ValueView` belongs to
  the specification, which builds it once and keeps it, so the view is valid
  only while the wrapper holds its reference and only for reading. Copy it into
  a `Value` of your own to keep it or to write to it — writing through the view
  would change what every later reader of that specification sees.
* `RedirectTarget` and `ParamSpecArray.ElementSpec` are **handed over**. C
  lends its reference in both cases; the wrapper takes one of its own, as
  everything a member hands out does, and the caller disposes it. The array
  holds a reference of its own on the specification of its elements, so what
  comes back outlives the wrapper it was read from either way.
* `GType.GetEnumValues` and `GType.GetFlagsValues` own nothing at all. They
  reference count the class of the type for the duration of the call, copy the
  names and the nicknames out of it, and release it before they return, so what
  the caller is left with points at no native storage.

A specification a `New` builds is owned the same way, and the ownership is
settled inside `New` rather than left to the caller: every
`g_param_spec_*` constructor hands out a **floating** specification, and `New`
wraps it with `Transfer.None`, which sinks it, so what comes back holds one
ordinary reference and nothing floats afterwards.

* `ParamSpecInt.New` and its siblings — one per kind, plus
  `Gst.ParamSpecFraction.New` and `Gst.ParamSpecArray.New` — answer a wrapper
  whose reference count is 1. `Dispose` releases it, and a specification nothing
  else took a reference on is freed there.
* Installing a specification on a class makes the class and GObject's pool take
  references of their own, and the runtime interns a long-lived wrapper of its
  own for the property slots to hand out, so an installed specification is held
  four times: once by the wrapper the caller built it with, once by the class
  (`g_param_spec_ref_sink`), once by the pool, and once by that interned
  wrapper. The caller's is theirs to dispose right after the install — three
  references are left, the property answers as it did, and nothing the class
  holds is touched by it.
* `ParamSpecArray.New` likewise takes a reference of its own on the
  specification of its elements, so the wrapper that was passed in stays valid
  and is disposed by whoever created it.
* The `G_PARAM_STATIC_*` flags are stripped silently. They would tell GObject to
  keep the caller's `name`, `nick` and `blurb` pointers, and those belong to
  buffers `New` releases as soon as the call has returned; without them
  GObject copies all three.
* `ValueRef` is the **write** view a property implementation is handed:
  `get_property` is given somewhere to write its answer, and the view neither
  owns the `GValue` nor may change its type.

## Fields a wrapper reads

A generated field accessor reads through the handle of the wrapper at the
moment it is called, so it answers what the library has put there rather than a
snapshot; a disposed wrapper refuses it rather than dereferencing the null
pointer. A scalar field is a get-only property that hands out a copy and owns
nothing. Five further shapes are worth spelling out, because each says
something different about what the caller is left holding.

* **A fixed size field is answered as inline storage.** `VideoInfo.Stride`,
  `VideoInfo.Offset` and `VideoFormatInfo.Depth` hand out a struct nested in
  the wrapper that carries the length in its own definition, the same type an
  out parameter of a caller allocated array uses. What comes back is a copy of
  the elements, so writing into it changes nothing native: the fields are set
  through the calls that own them, `VideoInfo.SetFormat` and `VideoInfo.Align`.
* **A string is copied on read.** `RTSPUrl.Host`, `PluginDesc.Name` and the
  strings of a session description hand out a managed string built from the
  UTF-8 the field points at. Nothing is borrowed and nothing is freed: the
  storage belongs to the C structure and is released or replaced with it, while
  what comes back is the caller's and outlives it. A string field is nullable
  unless `fieldAnnotations` in `girs/overlays/fixups.json` states otherwise
  with the C file and line the claim rests on, because no gir spells `nullable`
  on a field at all; a non-nullable one reports the null pointer as an
  `InvalidOperationException` rather than handing it out. The same table states
  a `name` when the member a field is read through would carry the name of one
  that shipped: `ProtectionMeta.GetStructure()` and `AudioMeta.GetAudioInfo()`
  are named after the type they hand out, because `GetInfo()` on both is the
  older binding of the metadata registration and answers something else.
* **A wrapper handed out for a field is projected the way a `transfer none`
  return of the same type is**, which is what decides both the ownership and
  the shape of the member.
  * A **`GObject`** is interned, so the field is a **property** and the wrapper
    it answers is the same instance every other lookup of that object hands
    out. It owns a reference of its own and stays valid after the structure the
    field sits in is gone; leave it to the garbage collector unless this code
    created the object. `Memory.Allocator` and `CollectData.Pad` are these.
  * An **opaque record** owns nothing, so the field is a **property** as well
    and the wrapper is a borrow. `VideoInfo.FormatInfo` and
    `AudioInfo.FormatInfo` point at the per format description the library
    keeps for the life of the process. There is nothing to dispose, and what it
    reads says nothing about the `VideoInfo` it came from, which may have moved
    on to another format by then. There is always one to hand out, so the
    property is not nullable: an instance that carries no description is a
    zeroed block of memory and is reported as an `InvalidOperationException`.
  * A **mini object or a boxed value** comes back owning a reference of its
    own — a mini object is referenced, a boxed value copied — so the caller
    disposes what a read produced and the member is a **`Get` method**, the
    same rule the generated properties follow. `Memory.GetParent()` and
    `VideoMeta.GetBuffer()` are these.
* **The read has to happen while the structure means what the caller thinks.**
  A structure the library only fills for the length of one call holds nothing
  outside it: `MapInfo.GetMemory()` answers the mapped memory between
  `Gst.Memory.Map` and `Gst.Memory.Unmap` and nothing afterwards, and
  `VideoMetaTransform.GetInInfo()` answers the info of the transform inside the
  `GstMetaTransformFunction` it was handed to and nothing afterwards. What the
  read produces is the caller's and survives the scope; reading after it does
  not.
* **An embedded record is copied.** A structure another one embeds by value is
  handed out as a copy of itself: a plain structure by the assignment, as
  `RTSPTransport.ClientPort` and `VideoInfo.Colorimetry` do, and a boxed value
  through `g_boxed_copy`, as `VideoInfoDmaDrm.GetVinfo()` and
  `CollectData.GetSegment()` do, which is why those are `Get` methods the
  caller disposes. Either way the copy outlives the structure it came out of
  and writing into it changes nothing native. An embedded record whose wrapper
  owns nothing is not handed out at all: it would be a borrow of storage the
  declaring record owns, with no lifetime this document could state for it. A
  **pointer to a plain structure** is copied out the same way and is nullable,
  because the null pointer is the structure saying it carries none:
  `VideoCodecState.ContentLightLevel` and `.MasteringDisplayInfo` answer the
  HDR metadata of a stream that has some and `null` for one that has not. Only
  a wrapper reads a pointer this way; a value projected structure keeps the
  address it publishes, which is why `RTCPPacket.RtcpPtr` is still a `nint`. A
  borrowed pointer **return** of a plain structure is copied out the same way —
  `FormatExtensions.GetDetails`, `RTPPayloadInfo.ForName`,
  `MIKEYMessage.GetCsSrtp` and `VideoColorPrimariesExtensions.GetInfo` answer a
  copy of a row the library goes on owning, nullable exactly when the gir says
  the call may find none — while a return the call transfers stays unbound,
  because nothing names the free the caller would then owe.

A public field the generator binds nothing for is listed in the `## Fields`
section of `girs/skip-report.md`, under the shape that kept it out, or — when
no shape accounts for it — under the cause: `HandWritten` for a wrapper the
generator is never asked for accessors of, `NoLayout` for a record whose mirror
collapsed, `CrossNamespaceEnum` for an enumeration this run does not emit. A
field a hand written member reads through stays listed there, the same way a
hand bound entry point stays on the skip list: what the ledger measures is the
generated surface. There are two exceptions. A field registered under
`fieldSkips` in `girs/overlays/fixups.json` names what does answer it and moves
to the `## Fields exposed elsewhere` section of the same report. A field
registered under `fieldAnnotations` with `accessor: false` stays on the ledger
under its own shape and is deliberately left unbound; the `$comment` of the
entry says why, and two reasons remain. `Iterator.pushed` is a pointer the
header keeps to the implementation of the structure, where the boxed copy an
accessor takes would alias a child the owner frees. `AudioCdSrcTrack.tags` is
storage the user fills and hands to the library, which takes it over and then
refuses to write a list a reference of ours made unwritable.

Two more reasons are not entries at all: the generator refuses them itself, and
no overlay lifts either. A mini object a record embeds by value is one. It is
the header a derived record carries first, so the field starts at the address
of the record that declares it and an accessor for it would be an identity
cast, described by the remark of an embedded record as a copy the caller owns —
the opposite of the alias a mini object wrapper is. The six `pt` of the derived
MIKEY payloads are the whole of that shape, and the direction a reader wants is
the other one, `Custom/MikeyCasts.cs` declaring `FromPayload` on each of the
six. A field the gir marks with a `version` newer than the oldest GStreamer the
binding supports is the other. It gets no accessor whatever its shape, and its
ledger line says which version put it there. The library on an older machine
allocates the structure without that field, so the read would be past the end
of it, and unlike a late entry point a field access has nothing to fail on.
What lifts one is a version the binding asks the library for at run time, and
there is exactly one such field in the tree today: `ReferenceTimestampMeta.info`,
which `Custom/ReferenceTimestampMeta.cs` hands out as `GetInfoStructure()`
behind `GstSharp.NativeVersion.IsAtLeast(1, 28)` and which throws
`EntryPointNotFoundException` below that, the same exception the same library
answers a member that arrived after the floor with.

The instance fields of the GObject classes are a third section of the same
report, `## Class fields`. A wrapper of a class holds a native instance and
mirrors no part of the structure, so none of what a class declares is projected
and the reason is the same one for every line; what the lines carry is the
shape, which says what an exposure would have to marshal. Padding, the fields
the gir marks `private` or `readable="0"` and the instance structure of the
base class are left out — the last of the three is the inheritance chain the
wrapper hierarchy already carries — and a field registered under `fieldSkips`
moves to `## Fields exposed elsewhere` the way a record field does. Reading one
today means writing the read by hand, against the mirror of the instance head
that checklist item 9 of `docs/modules.md` describes; the three fields of
`ColorBalanceChannel` are the model and are listed as answered elsewhere for
that reason.

## Fields the library rewrites

A field accessor reads at the moment of the call, which is the same raw read a
C subclass performs on the same structure. What comes back owns its reference,
so it stays valid — but what it names is only the value the structure held
during the window the C contract gives it, and the window is a place as much as
a time. The read has to happen where C reads the field: on the streaming thread
that handed the structure over, inside the callback or virtual method that did,
under the lock that call holds — `STREAM_LOCK` for a codec frame, a codec state
or collect data, `OBJECT_LOCK` inside `acquire` for a ring buffer spec. A read
from another thread, or from a structure fetched outside that call, is outside
the contract even inside the window. Every one of these is nullable, and for
most of them `null` is a normal answer inside the window too: a buffer no pool
handed out, an output buffer the subclass has not produced yet, allocation caps
no negotiation has written. `VideoCodecFrame.GetInputBuffer()` is the one
exception — the base class assigns the input buffer before it hands the frame
to `handle_frame` (`gstvideodecoder.c:3436-3447` called from `:2500`,
`gstvideoencoder.c:1532`), so inside that call it is never `null`. It answers
`null` only on a frame the assignment has not reached yet, or one a subclass
has taken the buffer out of itself.

* `Buffer.Pool` — as long as the buffer reference lives: the field holds a
  strong reference (`gstbufferpool.c:1285`), and the only thing that clears it
  is the compare and exchange in `gst_buffer_pool_release_buffer`
  (`gstbufferpool.c:1373`), which `_gst_buffer_dispose` reaches
  (`gstbuffer.c:802`) at a reference count of zero.
* `AudioRingBufferSpec.GetCaps()` — inside the `acquire` vfunc, or until the
  next `parse_caps` or `release` (`gstaudioringbuffer.c:496`, `:943`).
* `BaseParseFrame.GetBuffer()` and `GetOutBuffer()` — until the subclass calls
  finish or push, or `handle_frame` returns (`gstbaseparse.c:2397`, `:2627`,
  `:2814`).
* `VideoCodecFrame.GetInputBuffer()` — until the last frame unref, or the next
  subframe re-delivery (`gstvideodecoder.h:217-219`).
* `VideoCodecFrame.GetOutputBuffer()` — until `finish_frame`, `finish_subframe`
  or the frame is freed (`gstvideodecoder.c:3546`, `gstvideoencoder.c:2881`).
* `VideoCodecState.GetCaps()` and `GetAllocationCaps()` — until the element's
  next negotiation, or the last state unref (`gstvideodecoder.c:4517`,
  `:4533`).

The setters of those fields — `BaseParseFrame.SetBuffer()` and
`SetOutBuffer()`, `VideoCodecFrame.SetInputBuffer()` and `SetOutputBuffer()`,
`VideoCodecState.SetCaps()` and `SetAllocationCaps()` — go the other way and
**take** the reference of the wrapper they are given, exactly as a call that
consumes its argument does, and unref the value that was in the field, which is
what `gst_buffer_replace` and `gst_caps_replace` do in C. An owned wrapper is
detached by the call and throws when it is used afterwards; a borrowed one — a
vfunc argument — owns no reference to hand over, so it stays usable and the
setter mints a reference for the field instead. Passing `null` clears the field
the same way. To keep a usable wrapper of what was set, read the field back
with the matching getter after the call, which hands out a reference of its
own.

## Calls that consume their argument

A call whose C function takes ownership of a parameter
(`transfer-ownership="full"`) is handed a value minted for it rather than the
wrapper's own: a mini object and a GObject are handed a reference of their own,
a boxed value a copy through `g_boxed_copy`. **The generator emits these
members.** What becomes of the wrapper afterwards follows from its model, and
there are two answers.

**A mini object or a boxed value is consumed**: the argument is disposed when
the member returns, **whatever the call answered**, because the C function
offers no way back. After the call the wrapper owns nothing, which is precisely
what its disposed state means. What a
copy of a boxed value costs is decided by the copy function its type
registered: most boxed types duplicate the value, so the copy is what a
reference is there, while some — `GDateTime`, `GBytes`, `GstVideoCodecFrame`,
`GstVideoCodecState`, `GstAtomicQueue`, `GstFlowCombiner` — registered their
own `_ref`, so copying one of them takes a reference. The contract is the same
either way, and the generated remark says which of the two it is. The member
states the consumption on its parameter: `Caps.Append(caps2)` consumes the caps
it appends, `Pad.Push(buffer)` the buffer.

Five of the C functions below tell their caller to give the argument up —
"`@target` will steal a reference to the `@profile`", "`@factory` should not be
used after calling this function". That is the rule for a C caller, whose own
reference the call took, and it is not the rule of this binding. The generated
documentation does not carry those sentences: the `docStrip` overlay takes each
of them out of the gir text before it is rendered, quoting it exactly and
reporting itself when an upstream rewording moves it, so what the member says
about its argument is the paragraph below rather than two paragraphs that
disagree.

**A GObject is handed over and never consumed.** The call keeps the reference
minted for it for as long as it needs the object, while the wrapper keeps the
one it holds and stays the caller's: it is usable after the call, the handlers
connected to it keep firing, and the native reference count lands exactly where
the C call leaves it — one reference for the caller, and whatever the library
keeps, which for two of these calls is nothing at all: `GES.Project.Save`
(ges-project.c:1257-1258) and `RTSPServer.TransferConnection`
(rtsp-server.c:1197) release what they were handed before they return, so what
they keep it for is the length of the call. That is what an interned wrapper
requires: it stands for the object across the whole process, so disposing it
would take the object away from every other holder and run `DisconnectAll` on
handlers that are none of the call's business.
`StreamCollection.AddStream(stream)`, `Allocator.Register(name, allocator)`,
`EncodingTarget.AddProfile(profile)`,
`EncodingContainerProfile.AddProfile(profile)`,
`RTSPMountPoints.AddFactory(path, factory)`,
`RTSPServer.TransferConnection(socket, ...)`,
`RTSPSession.ManageMedia(path, media)`, `RTSPSessionMedia.New(path, media)` and
the formatter asset of `GES.Project.Save` all work this way, and the lookup
that reads the object back — `GetStream`, `GetProfiles`, `Allocator.Find` —
hands out the very wrapper that was handed over for as long as that wrapper
lives, interning being what makes those the same object; once the caller has
disposed it, the lookup finds no wrapper for the object and builds a fresh one.
Disposing it afterwards stays a caller's choice and is rarely the right one
while the library still calls back into managed code through it.

A few of these calls refuse what they are handed before they take it — a
duplicate profile name, a media that is not prepared — and the reference minted
for the call would then be left with no owner. **The binding releases it again
on that path**: `EncodingTarget.AddProfile` (encoding-target.c:387-395),
`RTSPSession.ManageMedia` (rtsp-session.c:268-272) and `RTSPSessionMedia.New`
(rtsp-session-media.c:149-153) are the three whose C states that its refusal
return — `FALSE`, `NULL` — means the reference was not taken, so the generated
body unreferences the mint right after the call and hands the result on
unchanged. The reading is per member and lives in the `handOverRefusals`
overlay, because it is not one a binding may guess: `GES.Project.Save` and
`RTSPServer.TransferConnection` also answer `FALSE` on a refusal, but their C
releases what it was handed itself (ges-project.c:1257-1258,
rtsp-server.c:1211, :1219), and a release here would be the second one. Where
the C answers such an argument with a `g_return_if_fail` the binding refuses it
before the call instead, which is what `AddFactory` does with a path that does
not begin with `/`. `RTSPSession.ManageMedia` and `RTSPSessionMedia.New` answer
a refusal with an `InvalidOperationException`, their return being non-nullable;
the release runs before the throw.

A handful of consuming calls shipped as hand written members before the
generator learned the shape. They carry the same contract and stay the binding
for their entry points:

| Call | Consumes |
| --- | --- |
| `AppSrc.PushBuffer` | the buffer |
| `Element.SendEvent`, `Pad.SendEvent`, `Pad.PushEvent` | the event |
| `Element.PostMessage` | the message |
| `Event.NewCustom`, `Message.NewApplication`, `Message.NewCustom`, `Query.NewCustom` | the payload structure |
| `Promise.Reply` | the reply structure |
| `BufferPool.SetConfig` | the configuration structure, on refusal as well |
| `AppSink.SetSimpleCallbacks`, `AppSrc.SetSimpleCallbacks` | the callbacks builder |
| `WebRTCSessionDescription.New` | the SDP message |

`SetSimpleCallbacks` has a second overload whose parameters are the individual
callbacks and are all optional, so a bare `null` is a compile-time ambiguity
between the two. That is by design on both `AppSink` and `AppSrc`: taking the
callbacks off again is a different intention from installing them, and
`ClearSimpleCallbacks()` is the call that spells it.

Every one of them takes a mini object or a boxed value over; none takes a
GObject, which is the family that is handed over rather than consumed. Where a
consuming argument is nullable, `null` is the absence of a payload and there is
nothing to consume.

`Dispose` is idempotent, so a `using` around the argument stays correct and
stays the recommended shape — the analyzer sees the disposal, and an early
return before the consuming call still releases the wrapper.

## What a virtual method is handed

A slot of a class struct is the same boundary read the other way round, and two
of its shapes have no equivalent among ordinary calls.

The **third form** is an in/out mini object that is `transfer full` in both
directions: `AudioEncoder.OnPrePush` and `AudioDecoder.OnPrePush` are handed a
`ref Gst.Buffer?` whose reference the caller has given up, and whatever is in the handle when the override returns is
what the caller takes over. Leaving it alone hands the very buffer on and costs
no reference; assigning another buffer releases the one that came in; setting
it to `null` drops it. The wrapper that ends up in the handle is detached by
the hand-over, so it means nothing after the override returns. An override that
throws is the fourth case, and the trampoline closes it: the trap answers
`FlowReturn.Error`, the handle is cleared and the buffer that was handed in is
released, so a failing override leaks nothing.

A **boxed value lent to a slot is borrowed for the length of the call**. Where
the C code hands a slot a `GstAudioInfo`, a `GstVideoInfo`, a `GstSegment` or a
`GstBaseParseFrame` by pointer for the override to read *and write*, the
wrapper is built over that very value rather than over a copy of it — that is
what makes `AudioFilter.OnSetup`, `VideoFilter.OnSetInfo`,
`VideoSink.OnSetInfo`, `BaseSrc.OnDoSeek`, `BaseSrc.OnPrepareSeekSegment` and
`BaseParse.OnHandleFrame` able to change what their caller reads. The codec
classes lend the same way: the `VideoCodecState` of `VideoDecoder.OnSetFormat`
and `VideoEncoder.OnSetFormat`, the `VideoCodecFrame` of
`VideoEncoder.OnPrePush`, the `OnTransformMeta` of both video codecs and
`VideoDecoder.OnParse`, the `AudioInfo` of `AudioEncoder.OnSetFormat`, and the
`BaseParseFrame` of `BaseParse.OnPrePushFrame`. The wrapper
owns nothing and frees nothing; the trampoline detaches it when the override
returns, so reading through a wrapper that was kept past the call throws
`ObjectDisposedException` rather than reading memory the library has since
reused. Anything that has to outlive the call is read out of the value while
the call is running, or kept through `Copy()` — which for the reference counted
`VideoCodecFrame` and `VideoCodecState` hands back a wrapper holding its own
reference to the same value rather than a copy of it.

## What a typed signal handler is handed

A generated event — `Bus.Message`, `AppSink.ProposeAllocation`, every
`<glib:signal>` the binding emits as a C# event — hands its handler a mini
object or boxed argument as a wrapper that is **scoped to the call**: the
trampoline builds it before the handler runs and disposes it when the handler
returns, so a wrapper kept past the handler throws `ObjectDisposedException`.
(A `GObject` argument, the pad of `pad-added` among them, is the interned
wrapper of that object instead and is not disposed.) What differs between two
scoped arguments is what the wrapper holds while it lives.

By default it holds **a reference or a boxed copy of its own**, taken over the
value the emission carries and released again by that disposal. That is the
right reading of what GObject does: unless the signal registered its argument
`G_SIGNAL_TYPE_STATIC_SCOPE`, the emission hands every handler a copy — a
`g_boxed_copy` for a boxed value, a reference for a mini object — on both of
its paths, the collecting one and the fast one. Such an argument can therefore
never be writable in place, whatever the binding does with it: a mini object is
held by GObject's reference as well as by the emitter's, so every write into it
is refused, and a boxed value the emission duplicated takes writes that are
dropped with the duplicate. `Copy()` it, or read out of it what is needed, to
keep anything past the handler.

The exception is an argument that is **lent** rather than copied, and there are
two such arguments today: the query of `AppSink.ProposeAllocation`, which the
overlays mark `borrow`, and the message of `RTSPClient.SendingMessage`, whose
event is written by hand; a third lent wrapper, `RTSPContext.BorrowRequest()`,
is asked for rather than handed and is described below. Such a wrapper
**borrows** — no
reference, no copy, the object of the emitter itself — which is what leaves it
**writable in place**, so that a handler can call `Query.AddAllocationMeta` and
have the element that sent the query read it back. It is as writable as that
sender left it: an element that holds a second reference of its own makes the
query unwritable for a C handler too, which is what `IsWritable` answers. The
wrapper is still scoped to the handler and must not be stored, and
`MakeWritable()` on it throws `InvalidOperationException`: it owns no reference
to give away. An argument is marked only where the C grants both halves
of it — the signal registers the argument `G_SIGNAL_TYPE_STATIC_SCOPE`, so no
emission path copies it, *and* the query's sender reads it back —
which is why an argument that is merely `STATIC_SCOPE`, such as the segment of
`Aggregator.SamplesSelected`, is left alone: it is the live segment of the
source pad, and nothing reads it back.

`RTSPClient.SendingMessage` reaches the same shape from the other side. Its
event is not generated at all: the C registers the signal with
`(GST_TYPE_RTSP_CONTEXT, G_TYPE_POINTER)` and emits the context of the request
as its first argument, while the introspection data has called that argument a
`GstRTSPSession` since 2014 — an upstream documentation bug, worked around in
this binding while a fix for it is drafted — so a generated event wrapped a
stack structure as a `GObject` and raised before the handler was reached. The
overlays skip the signal and the event is hand written beside the generated
ones, which is what lets its `Message` be lent: the client emits the signal,
then writes that very message to the connection, so a header the handler adds
is a header the peer receives. `Ctx.GetResponse()` answers a copy of the same
message, so it is the lent one a handler has to edit. The arguments carry the
context as `Ctx`, the way every other context carrying signal of the class
does, and the `Session` the generated shape promised is `[Obsolete]` and
answers `Ctx.Session`, which is `null` whenever the context carries none.

`RTSPContext.BorrowRequest()` is the third lent wrapper, and the only one a
caller asks for rather than is handed. The context every request signal of
`RTSPClient` carries is a value snapshot of a structure on the stack of the
emitter, and its `GetRequest()` answers a deep copy: correct, and of no use to a
handler that wants to change what the server serves, because the C reads
`ctx->request` itself after the `pre-*-request` handler returns and never looks
at a copy. `BorrowRequest()` lends that message instead — no reference, no copy
— so a header a handler adds is a header the server reads, which is what the C
does to its own request as well. It is `null` where the context carries none, as
`handle-response` and a server originated `send-message` do. The borrow is
scoped to the handler even more sharply than the others: the request goes back to
the watch when the handler returns, so it has to be disposed before that, and
calling it on a stored snapshot reads a pointer that dangles — the one case the
binding cannot check for. There is deliberately no response twin: when a
`*-request` signal runs the response is already unset, or for `TEARDOWN` not yet
built, and one `OPTIONS` failure path frees it, so the message to edit on the
way out is the one `RTSPClient.SendingMessage` lends.

The dynamic path draws the line elsewhere on purpose: `ConnectSignal` borrows
**every** mini object and boxed argument, because it marshals by `GType` at
emission time and has no per-signal knowledge to select with. The consequence
for a handler is described under
[An argument a dynamic signal lends](#an-argument-a-dynamic-signal-lends).

## Calls that consume the instance they are called on

A handful of C functions take the reference of the object they are called on
and hand one back: the instance is `transfer full` and so is the return, which
is of the type of the instance. `caps = gst_caps_make_writable (caps)` is the
shape, and `caps = gst_caps_truncate (caps)` is the other half of it. The
binding tells the two apart, because a caller does something different with
each.

**`MakeWritable` adopts in place and answers this wrapper.** The wrapper gives
the reference it owns to the call and takes whatever comes back, so the same
wrapper stands for possibly different caps, a different buffer or a different
memory afterwards, and the return value only exists so that the call can be
chained. **Any handle read before the call is stale**, and a mapping or a raw
field address taken from the old object must not be used again. It is single
owner surgery: it is correct only while no other wrapper and no other thread
uses this one, which is the rule the C API imposes as well.

Two things refuse it. A **borrowed** wrapper — the one a vfunc override
receives, the one a borrowed signal argument is handed as, and the one the
dynamic signal path builds — owns no reference to give, so it raises
`InvalidOperationException`. What is lent that way is writable only where
whoever lends it holds the only reference: an in place vfunc lends the value of
its caller and normally promises exactly that — a base transform running in
passthrough is the exception, as it calls `transform_ip` on a buffer it did not
make writable — and so do the `ProposeAllocation` query,
as long as the element that sent it does not share it, and the
`SendingMessage` message, which the client owns alone, while the dynamic signal
path lends the reference or the boxed copy GObject took for the emission, which
is a second one for every argument but a `STATIC_SCOPE` one, whose emission
takes no reference of its own — `Copy()` such a value and edit the copy. This
holds for a boxed wrapper as much as for a mini object one: `Gst.Uri` is the
one boxed type with a `MakeWritable`, and a borrowed uri refuses it rather than
letting the C release a reference the wrapper never owned. And when the object
is shared
and the copy fails, the C function has spent the reference all the same: the
wrapper is left **disposed** and `InvalidOperationException` is raised rather
than a wrapper handed back that stands for nothing. `Gst.Memory.MakeWritable`
on an allocator that cannot copy is the one way to reach it.

**A conversion mints the reference it hands over and answers a new wrapper.**
`Caps.Truncate`, `Caps.Normalize`, `Caps.Simplify`, `Caps.Merge`,
`Caps.MergeStructure`, `Caps.MergeStructureFull`, `Buffer.Append`,
`Buffer.AppendRegion` and `Memory.MakeMapped` leave the wrapper they are called
on exactly as it was — it keeps the reference it owns — and hand back a second
wrapper that the caller owns and disposes. The two may stand for **the same
native object**, which is what the C functions answer when they had nothing to
change; it is then shared and, being shared, not writable. Passing the same
wrapper as the instance and as the argument is legal: two references are minted
and the books balance. The wrapper is disposed all the same, because it was the
argument too, and the argument of a conversion is consumed.

`Memory.MakeMapped` is the one of them whose `null` is a normal answer: it
means the memory could neither be mapped nor copied into one that can be.
What it answers otherwise is mapped, and unmapping it is the caller's.
`Caps.Fixate` is hand written rather than generated, because it is the one of
the family that refuses ANY caps without consuming anything; it raises
`InvalidOperationException` on them, and `Caps.IsAny()` is the test to make
first.

`GES.Container.Ungroup` is the counter example of the whole section: the gir
marks its instance `transfer full` and the C releases nothing at all, so the
consuming shape would dispose a wrapper the caller still owns. It is hand
written for that and for the answer it hands back, whose elements do not share
one ownership - a childless clip is returned without the added reference every
other element carries. An annotation alone therefore never decides that a call
consumes its instance: the C has to be read.

## Members that take or return a `GValue`

A `Gst.GObject.Value` is a struct that owns its contents, and a generated
member never takes that ownership over: the call is handed a pointer into the
caller's own storage, nothing is allocated for it, and nothing is disposed
after it. One rule per shape:

* **An `in` value is read.** The callee copies what it keeps —
  `caps.SetValue`, `Global.ValueIsFixed` — so the caller keeps the value and
  still disposes it. An empty value has no type for the call to read and
  throws `ArgumentException`. `Message.NewPropertyNotify` is read the same way
  although its C function takes the `GValue` over: what the message adopts is a
  copy the member initialises for it, because the storage of a managed value
  belongs to the caller and cannot be given away. Its value is nullable, and
  `null` is the notification that carries no value at all.
* **A `ref` value has to arrive initialized** with the type the call expects:
  `Global.ValueSetFraction` wants a `GST_TYPE_FRACTION` (`Value.SetFraction`
  is the typed accessor over the same store), and
  `Global.ValueDeserialize` reads the type of its destination to pick the
  parser. Like the C API, the call raises a warning and does nothing on a
  value of the wrong type.
* **An `out` value is storage the member zeroes and the callee fills.** On
  success the caller owns the contents and disposes the value; on failure —
  `Global.ValueIntersect` answering `false` — it is left empty, and disposing
  an empty value does nothing.
* **A returned value is the caller's own**, whether the C function handed out
  a borrowed pointer, which is copied (`Global.ValueGetFractionRangeMin`), or
  transferred an owned one, whose contents are adopted and whose shell is
  freed (`Gst.Object.GetValue`). Either way dispose it; a call that had
  nothing to return produces the empty value rather than a null.

The container of those values, `Gst.GObject.ValueArray`, is an ordinary boxed
wrapper — owned, not interned, disposed by its consumer — and the members that
carry one follow the two rules its C functions really have:

* **An out array is newly allocated for the caller.** `Structure.GetArray`,
  `Structure.GetList` and `Global.UtilGetObjectArray` convert the field or the
  property into a fresh `GValueArray` that the caller owns and disposes; the
  conversion is deliberate in the C implementation, which builds the array and
  never releases it itself. On refusal — no such field, a field that does not
  hold the `GST_TYPE_ARRAY` (`GetArray`) or `GST_TYPE_LIST` (`GetList`) being
  converted, or a missing or non-convertible property for
  `UtilGetObjectArray` — the out parameter is `null` and there is nothing to
  dispose.
* **An in array is only read.** `Structure.SetArray`, `Structure.SetList` and
  `Global.UtilSetObjectArray` copy the contents into the field or the
  property, so the caller keeps the array and still disposes it. The structure
  setters require a writable structure, with the same C parity — a warning and
  no write — as every generated setter, and their remarks say so.

The wrapper itself keeps the same discipline element-wise: `Get` hands out an
independent copy of the element, because the pointer the C accessor returns is
interior to storage the array reallocates and frees, and `Append` stores a copy
of the value it is given, so the caller disposes both its own value and, in
time, the array.

### A `GValue` a callback is handed

The rules above are for the values a *caller* provides. A callback is on the
other side of the pointer: `Structure.Foreach`, `Structure.MapInPlace`,
`Structure.FilterAndMapInPlace`, their three `_id_str` twins and
`Iterator.Fold` / `Iterator.Foreach` hand the delegate a `GValue` that belongs
to whoever is running the walk. It cannot be a `Gst.GObject.Value`, which owns
its contents and would release them, so it arrives as one of two views:

* **`Gst.GObject.ValueView`** for a `const GValue*`, which the C contract says
  the callback may only read. It carries the readers of `Value` under the same
  names.
* **`Gst.GObject.ValueRef`** for a writable `GValue*`, which the caller invites
  the callback to change in place — that is what makes `MapInPlace` different
  from `Foreach`. It carries the same readers, `AsView()`, and the setters.

Three things follow, and all three are enforced rather than documented:

* **A view is only valid while the callback runs.** Both are `ref struct`s, so
  the compiler refuses to let one be stored in a field, in an array, in a
  closure or in an `async` state machine. The storage really does go away: the
  item `gst_iterator_fold` hands out is a stack `GValue` that is reset after
  every call, and a structure field is gone with its structure. To keep what a
  view holds, copy it with `ToValue()` and dispose the copy — that copy is an
  ordinary owned `Value`.
* **A view owns nothing**, so there is no `Dispose` and no `using`. The
  wrappers its `GetObject`, `GetBoxed<T>` and `GetMiniObject<T>` hand out are
  the caller's own, exactly as they are on `Value`.
* **The type of the value cannot be changed.** Every setter of `ValueRef`
  throws `InvalidOperationException` unless the value already holds the type it
  is about to write, and there is no `Unset`. `gst_structure_map_in_place`
  writes the field back without checking anything, so a callback that unset a
  field and answered `true` would leave the structure holding a field with no
  type at all. A field that should go away is removed by answering `false` from
  `FilterAndMapInPlace`, which is the supported way to say so. `SetBoxed` and
  `SetMiniObject` check the wrapper they are handed as well as the value,
  because `g_value_set_boxed` copies its argument with the copy function of the
  type the value already holds: a wrapper of another boxed type would be handed
  to the wrong copy function, silently, rather than be refused.

An exception a handler throws does not reach the caller of the walk. A managed
exception must never unwind through a native frame, so the trampoline catches
it, reports it through `Gst.Interop.ExceptionTrap` and answers the call with the
failure value of the callback — `false` for the ones that return a `gboolean`,
nothing for the ones that return `void`. That is the shape of every `scope=call`
trampoline of this binding; see
[Callbacks and the state they carry](#callbacks-and-the-state-they-carry).

**What that failure value means is the C caller's to say, and for one of these
it is not benign.** `FilterAndMapInPlace` reads `false` as "remove this field",
so a handler that throws loses the field it was visiting; a handler that has to
fail without losing data has to catch its own exceptions. The four plain
structure walks read it as "stop", and the walk then answers `false`, which is
indistinguishable from a deliberate stop. `Iterator.Fold` reads it as "stop" as
well and still answers `GST_ITERATOR_OK`. `Iterator.Foreach` has no failure
value at all, so its walk carries on with the next element. Each member says so
in its own remarks.

### A `GValue` a signal lends

A signal handler is on the same side of the pointer as a callback.
`notify-meta`, the signal every GES meta container carries, hands its handler
the value that was just written under a key, and the emission owns that value:
the args object carries `HasValue` and a `Value` that is a
`Gst.GObject.ValueView` over the emission's own storage. What differs from a
callback is that the args object is an ordinary class, which a handler is free
to store, so the compiler cannot fence the lifetime for it. The args object
does it instead: `Value` throws `InvalidOperationException` once the emission
has ended, and `ToValue()` is still how a handler keeps what the view holds.
`HasValue` stays readable afterwards, and it is `false` when the field was
removed rather than written — GES emits the signal for a removal with no value
at all.

`notify-meta` is declared detailed, but the library only ever emits it with
detail 0. Connecting the handler therefore never narrows it to one key: it runs
for every field of the container that changes, and a handler that cares about
one field tests `Key` itself.

### An argument a dynamic signal lends

A handler connected by name — `Gst.GObject.Object.ConnectSignal`, and the class
handler of a signal a managed subclass defines — is handed every argument the
way `Gst.GObject.Value.GetContent` reads it, with one addition: a boxed argument
whose type an initialised module registered arrives as the wrapper of that type
rather than as a raw `nint`. A mini object is a boxed type as far as GObject is
concerned, so `GstCaps` and `GstStructure` take the same route.

This is a deliberate divergence from the generated events of
[What a typed signal handler is handed](#what-a-typed-signal-handler-is-handed),
which borrow only where an overlay entry says the C grants it: the dynamic path
resolves the argument by `GType` while the emission runs and has no per-signal
knowledge to select with, so it borrows uniformly.

That wrapper **borrows**. It holds the value the emission carries rather than a
copy or a reference of its own, and the emission disposes it as soon as the
handler returns, which detaches the wrapper and frees nothing; using it
afterwards throws `ObjectDisposedException`. Three rules follow:

* **Keeping the value means copying it** — `Caps.Copy()`, `Structure.Copy()` —
  or reading out the fields that are wanted.
* **Writing into it reaches the emitter** where the signal lends its argument,
  which is what makes the `handle-request`, `on-sdp` and `before-send` signals
  of `rtspsrc`, and `handle-request` and `update-sdp` of `rtspclientsink`,
  usable from managed code at all.
* **Releasing it is the emitter's business, never the handler's.** No
  `RTSPMessage.Unset()`, no `SDPMessage.Uninit()`, no other clearing call.
  `Dispose()` on the argument is pointless rather than dangerous — a borrowed
  wrapper frees nothing — but it detaches the wrapper early, and the emission
  disposes it anyway.
* **A mini object argument is borrowed as well**, which it was not before: the
  wrapper used to hold a reference of its own. `MakeWritable()` on such an
  argument now throws `InvalidOperationException` instead of quietly answering a
  private copy that the emitter never saw. `Copy()` it and edit the copy when a
  writable value is what is wanted. This holds for a type whose module
  registered a borrowing factory for it, which every type of this binding is; a
  mini object of a module registered without one still arrives as a wrapper
  holding a reference of its own, and `MakeWritable()` on that one answers a
  private copy the way it used to — see `ModuleTypeEntry.BorrowedFactory` and
  [`docs/modules.md`](modules.md).

A boxed type nothing registered still arrives as its raw `nint`, unchanged and
nobody's to free. Since what is registered is what the initialised modules put
there, a handler for an `rtspsrc` signal sees `Gst.Rtsp.RTSPMessage` only if
`GstRtsp.Initialize()` ran, and an SDP argument needs `GstSdp.Initialize()` the
same way. The registry is consulted on every emission rather than when the
handler is connected, so the requirement is really "before the emission";
initialising the module before connecting is the way to be sure of it. The
**return** value of an emission is a different question and is unchanged:
`Object.EmitSignal` hands a boxed result back as an owned `nint`, except for a
mini object, which comes back as its wrapper.

## Tracks a timeline is answered with

`GES.Timeline.SelectTracksForObject` is answered with an array of tracks, and
the timeline does not take that answer at face value. A track that appears
twice in it, and a track that belongs to another timeline, are dropped with a
warning on the GStreamer debug log; the element joins the tracks that are left.
The reference the binding minted for a dropped track is released with it, so a
rejected answer strands nothing. Answering `null` or an empty array puts the
element in no track at all, which the member documents.

Which handler the timeline asks depends on the version it runs against. From
GStreamer 1.28, a timeline that has a `SelectElementTrack` handler connected
does not emit `SelectTracksForObject` at all — not even when that handler
answered null. On 1.24 and 1.26 only an answer that names a track stops it, so
an application that connects both handlers and runs there sees
`SelectTracksForObject` emitted after a null answer.

## Errors that cross the boundary

A `GError` is not a wrapper and is never owned by a `Gst.GLib.GException`: the
exception carries a copy of the three fields the error holds — the domain, the
code and the message — and the pointer it was read from is nobody's to keep.
Four shapes reach the surface, and each says who frees what.

* **A member that throws** takes a hidden `GError**`, and a call that fills it
  raises the error as a `Gst.GLib.GException` and frees it on the way out
  (`GException.ThrowIfSet`). The exception outlives the pointer, because it
  shares nothing with it. A call that also returned something releases that
  first: the caller cannot be handed both.
* **An error handed to a handler, or returned borrowed**, is the library's own
  for as long as the emission or the call runs. `GES.Project.ErrorLoading`,
  `Gst.Pbutils.Discoverer.Discovered` and their relatives read domain, code
  and message inside the trampoline and free nothing, and so does
  `GES.Asset.GetError()`, whose error `ges_asset_needs_reload` clears out from
  under a caller who kept the pointer. The value the handler sees is a
  managed exception object and stays valid for as long as anything holds it.
* **An error passed in** — `Gst.Message.NewError`, `Gst.Object.DefaultError`
  and their siblings — is built into a temporary `GError` that the member
  frees again when the call returns. The library copies what it keeps
  (`gst_message_new_error` through `g_error_copy`), so the exception object is
  never retained. Such an error needs a registered error domain and a message:
  an exception built by any constructor but
  `GException(Quark, int, string)` carries no domain, and passing one throws
  `ArgumentException` before anything is allocated.
* **An error taken out of a message** is the fourth shape and is hand written:
  `Gst.Message.ParseError()`, `ParseWarning()` and `ParseInfo()` answer a
  tuple of the exception and the debug string, and the `GError` the C function
  transferred is freed inside the member. Nothing is left for the caller to
  release. `Gst.Transcoder.TranscoderMessageExtensions.ParseError()` and
  `ParseWarning()` are the same shape in another form: they answer the
  exception as an `out Gst.GLib.GException`, and beside it the `issue-details`
  of the message as an `out Gst.Structure?` that is a copy of the caller's own
  and is disposed like any other boxed wrapper — `null` when the message
  carries none, which is every error the transcoder raises itself rather than
  forwards from the bus of its pipeline. The
  `GError` is not the caller's there either: it is read out of a `GValue` copy
  of the field, which releases it. `Gst.Play.PlayMessageExtensions.ParseError()`
  and `ParseWarning()` are the same pair for the API bus of a
  `Gst.Play.Play`, with the same ownership: the details are `null` for a
  message GStreamer 1.24 posted without them and a copy of the caller's own
  from 1.26 on, where they always carry the `uri`.

## A play and its API bus

`Gst.Play.Play` is a small state machine around `playbin3` on a thread of its
own, and three of its members do not follow the shape of the rest of the
bindings. All three are hand written in `src/GstSharp.Net.Play/Custom`.

* **`new Play(renderer)`** does not consume the renderer, although
  `gst_play_new` consumes the reference of its C caller. The binding raises one
  reference before the call, so the renderer wrapper stays the caller's and
  `Expose()`, `SetWindowHandle()` and the render rectangle of a
  `PlayVideoOverlayVideoRenderer` are still reachable while the play runs. That
  is the general rule of the section above for a GObject argument rather than
  an exception of this constructor; what keeps the constructor hand written is
  its interface-typed renderer, which the generator has no wrapper factory
  for.
* **`Play.SetConfig(config)`** borrows its argument. The C function documents
  that it takes the structure over and only does so on success, so the binding
  hands over a copy and frees that copy itself when the play answers `false` —
  which it does for every play that is not stopped. `Play.GetConfig()` is an
  owned copy as its transfer says.
* **`Play.Dispose()` sets the API bus flushing** before it releases the play.
  Every message the play posts names the play as its source, so a message that
  is still queued holds the play, and the play holds the bus: an unread bus is
  a reference cycle. An application that polls `Play.GetMessageBus()` itself
  has to stop that loop before it disposes the play, because the bus answers
  nothing afterwards. The finalizer does not flush, for the reason no finalizer
  in this binding calls native code.

**Stop a play and wait until it reports `PlayState.Stopped` before disposing
it.** `Play.Stop()` is asynchronous: `gst_play_stop` only queues the work on the
thread of the play, and GStreamer 1.28 queues it *without* taking a reference of
the play. Every message that thread posts in the meantime does hold one, so a
play that is disposed while its thread is still working can have its last
reference dropped by that thread and be finalised underneath its own running
dispatch, which then reads freed memory: a crash inside `libgstplay`, not a
managed exception. The safe order is stop, wait for the `state-changed` message
of the API bus that carries `PlayState.Stopped` — or for the `StateChanged`
event of an adapter, which is the only way to see it when a `NewSyncEmit()`
adapter owns the bus — and dispose only then. A play that has already reported
`Stopped`, which is what every play does after end of stream and after an error,
does not report it again: `gst_play_stop` returns without a state change for a
play that is stopped, so an application tracks the last state it saw rather than
waiting for a fresh message that never comes.

This is an upstream limitation rather than a contract of the binding, and
`Dispose()` does not wait on the caller's behalf: nothing in the C API joins the
thread of a play, and the barrier that is left — polling the state of the
pipeline — would block a disposal to work around a defect of the library the
binding binds. The application is the one that knows when it has seen `Stopped`.

`PlaySignalAdapter` keeps the `Play` wrapper it was built with, because the C
adapter stores the play without referencing it and
`gst_play_signal_adapter_get_play` hands that field back as transfer none.
`GetPlay()` and the `Play` property are hand written for it and answer the kept
wrapper — an adapter that was disposed answers `ObjectDisposedException`
instead. **Dispose the adapters of a play before the play**: nothing on the C
side keeps the play alive for them, and the bus watch of an adapter that
outlives its play runs against an object that is gone. `PlaySignalAdapter.New()` binds its bus watch to the thread-default main
context as it is at that moment, so in an application that runs no GLib main
loop none of its signals ever fires; `NewSyncEmit()` takes the one sync handler
of the API bus and drops every message, which makes it exclusive with polling
that bus. Disposing either adapter flushes the bus for every other consumer.

`Play.GetVisualizations()` answers wrappers the caller disposes. The C array
they are copied out of is freed inside the member, so nothing the caller holds
points into it.

## Dates that cross the boundary

A `GDate` is **converted, not wrapped**: no type of this binding stands for one,
and nothing about it is ever the caller's to release. A member that takes a date
takes a `System.DateOnly` and builds a temporary the call reads and the member
frees; a member that produces one — `Structure.GetDate`, `TagList.GetDate`,
`TagList.GetDateIndex` and the GES meta container's `GetDate` — reads the value
the call allocated, frees it, and hands out a `System.DateOnly?`. The answer is
nullable because a `true` answer does not promise a date: a generic structure may
hold a date field whose value is `NULL`. A year beyond 9999 has no `DateOnly` and
throws `ArgumentOutOfRangeException`, after the native value was released.

## Variants that cross the boundary

A `GVariant` is **owned, and its ownership starts out unclaimed**. Every
`g_variant_new_*` constructor — including the `g_variant_new_variant` that
`gst_discoverer_info_to_variant` returns — answers a *floating* reference,
which is a reference nobody owns yet and which the first owner claims rather
than adds to. `Gst.GLib.Variant` therefore never plainly refs what it is
handed: a transferred value is claimed with `g_variant_take_ref`, the API GLib
provides for a return that may or may not be floating, and a borrowed one with
`g_variant_ref_sink`, which sinks a floating value and references an owned one.
Either way the wrapper ends up owning exactly one reference and releases it on
`Dispose`, with a finalizer as the safety net — releasing a variant is an
atomic decrement that reaches no GStreamer state.

The annotation of `gst_discoverer_info_to_variant` says `transfer full` and the
C sinks nothing, so a wrapper that believed the annotation would leave the
value floating forever; `DiscovererInfo.ToVariant` hands out a value that
`g_variant_is_floating` answers `false` for. `DiscovererInfo.FromVariant` only
reads its argument, so the caller keeps it and still disposes it, and the bytes
of `Variant.ToBytes` are a `GLib.Bytes` of the caller's own. `Variant.FromBytes`
references the block it is given rather than copying it, which the block's own
wrapper is unaffected by.

## Properties without a C accessor

Some properties exist only on the GObject property system: the gir names no C
getter for them, so they are read through `g_object_get_property` into a
`GValue` and written through `g_object_set_property` out of one. The value is
an implementation detail of the accessor — it never reaches the caller — but
what comes out of it follows the same rules as everywhere else. Reading an
object hands back the interned wrapper, which the binding keeps and the reader
does not dispose. Reading a boxed value or a mini object builds a wrapper that
owns a copy or a reference of its own, exactly as `Value.GetBoxed<T>` and
`Value.GetMiniObject<T>` do, so the reader disposes it and reading twice
produces two wrappers. Writing any of the three copies or references the
argument, so the caller keeps what it passed and still disposes it, and `null`
clears the property. A property that is construct-only, or that the gir marks
read-only, has no setter at all; a construct-only one is written at
construction instead, through `ElementFactory.MakeWithProperties` or
`CreateWithProperties`, which hand out the same floating-sunk, interned
wrapper `Make` and `Create` do.

## Out parameters whose storage the caller provides

A C function that fills a structure the caller declared —
`gst_base_src_get_allocator` writes a `GstAllocationParams`,
`gst_video_info_dma_drm_to_video_info` a `GstVideoInfo` — has no out parameter
in the usual sense: it is handed the address of storage that already exists and
writes into it. The binding provides that storage from the zero argument
constructor the record declares (`gst_allocation_params_new`,
`gst_video_info_new`, `gst_video_info_dma_drm_new`), which is what makes the
size the library's business and pairs the allocation with the registered boxed
free:

| Shape | What the caller gets | Who releases it |
| --- | --- | --- |
| caller-allocated boxed out | a wrapper that owns the record the call filled | **the caller**, by disposing it |

The parameter is not nullable when the C function returns `void`, because the
record is filled by the time the call returns. It **is** nullable when the C
function answers a `gboolean`: a false answer means the record was never
written, so the binding releases the storage rather than handing back a zeroed
value, and the parameter is `null`. `BufferPool.ConfigGetAllocator` is that
shape, and the allocator beside it is `transfer none`, so its wrapper is the
interned one and is not the caller's to dispose.

Three of these out parameters are not storage but a *mapping*, and those are
hand written scopes rather than out parameters — see below.

## Arrays of strings

A `string[]?` result — `BufferPool.GetOptions`, `ElementFactory.GetUriProtocols`,
`PresetExtensions.GetPresetNames` — is a decoded copy of what the C function
handed back, so it holds no native memory and there is nothing to release; a
result the C function answers with `NULL` is `null` rather than an empty array,
and the two mean different things often enough that the distinction is kept. An
`in string[]` — `Global.ParseLaunchv`, `Meta.ApiTypeRegister`,
`Plugin.AddDependency` — is copied into a `NULL` terminated native vector that
lives for the one call and is released whether the call returns or throws, which
is why only the `transfer-ownership="none"` direction is bound at all. A `null`
element inside such an array is rejected with an `ArgumentException`, because a
C array of strings ends at the first `NULL` and native code would never see the
elements behind it.

## Lists a call is given

A member that takes a `GList` takes an `IEnumerable<T>` — `ElementFactory.ListFilter`,
`Container.Group`, `Uri.ToStringWithKeys`, `VideoEncoder.SetHeaders` — and
there are exactly two shapes behind it. A `null` sequence and an empty one are
the same value in both, because C spells the empty list `NULL` and GLib has no
non-null empty list; every such parameter is nullable, so none of them throws
`ArgumentNullException`.

A **borrowed** list is what the call only reads. The binding builds a native
list for the length of that one call, out of the handles of the wrappers passed
or out of fresh UTF-8 copies of the strings passed, and releases the list and
everything allocated for it when the call returns — including when it throws.
Nothing native outlives the call, the wrappers are the caller's throughout, and
what the callee decided to keep it copied for itself.

A **consumed** list is what the call takes over: `Uri.SetPathSegments`,
`AudioEncoder.SetHeaders` and `VideoEncoder.SetHeaders`. The binding hands over
a native list of its own and one value minted per element — a fresh string, or a
fresh reference for a mini object — and releases neither afterwards. The callee
owns the list and the minted values from the moment the call is made, which
includes the case where it answers `false`: `gst_uri_set_path_segments` takes
ownership before it tests whether the URI is writable, so a failed call has
consumed the list all the same. The objects the caller passed keep their own
references and stay usable; a buffer handed to `SetHeaders` is simply no longer
writable, because the encoder now holds a reference to it as well.

## Tables a call is given or answers

A `GHashTable` of string keys reads as a dictionary on both sides, and neither
side of it follows the list rule above.

A table a call is **given** — `Uri.SetQueryTable` — is an
`IReadOnlyDictionary<string, string?>?`, and `null` and an empty dictionary are
two different arguments: the first takes the query out of the URI and the second
gives it a query with no keys at all. The binding copies the entries into a
native table built for that one call, hands it over, and drops its own reference
when the call returns, including when it throws. That reference is the only one
the binding holds; a callee that keeps the table takes one of its own first, as
`gst_uri_set_query_table` does, so the table goes on living inside the URI while
the dictionary the caller passed is theirs to change or drop straight away. A
value of `null` is a key that stands in the query string with no `=` after it,
which is a state C keeps apart from an empty value; a `null` key is refused with
`ArgumentException`.

A table a call **answers** — `Uri.GetQueryTable`, `Uri.GetMediaFragmentTable`,
`TrackElement.GetAllControlBindings` — is a `Dictionary<K, V>` and always a
snapshot, never a live view: C hands out the table it keeps (or, for the media
fragment, one it builds for the call), and the binding copies every entry out
before the member returns. Editing the dictionary
afterwards changes nothing, and two calls answer two independent dictionaries.
A table of strings is answered as `Dictionary<string, string?>?`, where `null`
is the absence of a table and an empty dictionary a table with no entries, and
the reference the call transferred is released once the copy is made. A table of
GObjects is answered as `Dictionary<string, T>`, never `null`, and its values
follow the GObject rule above: each is an interned wrapper holding a reference of
its own, so it is the very instance the single-key member answers and the caller
does not dispose it. The order of the entries is the order GLib iterates its
table in and is not specified.

## Callbacks and the state they carry

A callback that is handed to native code is a `GCHandle` on a delegate, and the
one question every such member has to answer is who frees that handle. The gir
answers it with a `scope` annotation, and the binding emits one of four shapes:

| Scope | When the handle is freed | Example |
| --- | --- | --- |
| `call` | when the call that received the callback returns | `Gst.Caps.Foreach` |
| `notified` | by the destroy notification the library runs | `Gst.Element.CallAsync` |
| `async` | by the single invocation, in the trampoline itself | `Gst.Global.CallAsync` |
| `forever` | never; one handle is leaked per call | the `Gst.Base.CollectPads` setters |

Only the last one costs the caller anything, and it is not a choice the binding
made: `gst_collect_pads_set_function` and its four siblings store the function
pointer for the life of the object and offer nothing that releases the state
again, so `SetFunction`, `SetCompareFunction`, `SetEventFunction`,
`SetFlushFunction` and `SetQueryFunction` keep it alive for the life of the
process. Install those once, at construction; a call per buffer or per state
change leaks a handle each time. Their documentation says so on the parameter.
`Gst.Audio.AudioBaseSink.SetCustomSlavingCallback` is annotated `notified`
and degrades to `forever` on a replace and on a clear: the library discards
the previous notification along with the previous callback
(`gstaudiobasesink.c:761-765`) and runs only the last one it was left with,
at dispose (`:315-316`).

A callback parameter the gir marks `nullable` is a `Gst.Foo?` and is not
guarded: the absence of a function is a value the C side acts on, not a mistake.
`Gst.Meta.RegisterCustom` is the one such member — its `transformFunc` may be
`null`, and `gst_meta_register_custom` then copies the meta and its backing
structure on a copy transform and discards every other one. The call site hands
the library the null function pointer, a null `user_data` and no destroy
notification, so no `GCHandle` is allocated for a callback that is not there.

A handle a callback *receives* follows the transfer the gir states: one marked
`transfer full` is adopted, and the wrapper releases it when the handler
returns unless the handler handed it on to a member that consumes it, while one
that transfers nothing is wrapped without taking anything over. `Gst.Buffer`,
`Gst.BufferList`, `Gst.Event` and `Gst.Query` are the four the wrapper borrows
outright rather than referencing: every writer of a mini object refuses a value
that more than one reference names, so a `PadQueryFunction` that took a
reference of its own could not answer the query it was called for. The price is
that those four wrappers are only valid while the invocation runs: the
trampoline disposes them when the handler returns, exactly as a class struct
slot does, so a handler that filed one away meets an
`ObjectDisposedException` rather than a released pointer. Copy what has to
outlive the call. Every other untransferred mini object a callback is handed —
a `Gst.Message` on a bus watch, a `Gst.TagList` in a tag walk — keeps a
reference, so a handler may file the wrapper away and read it later.

### Memory the caller lends to the pipeline

`Gst.Buffer.NewWrappedFull` and `Gst.Memory.NewWrapped` are the reverse
direction: the caller keeps owning a block of memory and lends it to GStreamer
without a copy. The contract is the caller's to keep:

* the block has to stay valid, and at the same address, until the `notify`
  delegate runs — a managed array has to be pinned by a `GCHandle` of its own
  for exactly that long;
* `notify` runs once, on an arbitrary streaming thread, whichever one drops the
  last reference of the memory. It does not run at all if the memory is never
  released;
* the range is validated before anything is allocated, because the C functions
  answer a bad one with a critical warning and a null pointer that
  `gst_buffer_new_wrapped_full` then dereferences itself. `data` must not be
  `0`, and `offset` plus `size` must fit into `maxsize`.

`Gst.Video.VideoCodecFrame.SetUserData` is the third member of that family. Its
notification runs *synchronously* when the slot is written again, so replacing
one releases the previous state on the calling thread, and `GetUserData` answers
the binding's own handle rather than something to dereference.

**A file descriptor handed to `Gst.Allocators` is lent the same way, and by
default it is not lent but given**: `FdAllocator.Alloc`, `FdAllocator.AllocFull`
and `DmaBufAllocator.AllocWithFlags` close the descriptor when the last
reference of the memory goes, unless `FdMemoryFlags.DontClose` says to leave it
alone — which is what a descriptor a `SafeHandle` still owns needs.
`DmaBufAllocator.Alloc` takes no flags and always closes, so a descriptor to
keep has to go through `AllocWithFlags`. An allocation that answers `null`,
which is every one of them on Windows, never took the descriptor at all.

### `CallAsync` on an element

`Gst.Object.CallAsync` and `Gst.Element.CallAsync` are overloads that differ by
delegate type, `Gst.ObjectCallAsyncFunc` against `Gst.ElementCallAsyncFunc`. A
lambda written on a `Gst.Element` binds the `Element` overload, because C#
drops the base type candidate once a derived one applies; reaching the other one
from an element needs a variable typed `Gst.ObjectCallAsyncFunc`. Both invoke
the callback exactly once, on a thread of the shared pool, and both release the
state with that invocation.

## The two mapping scopes

`Gst.Video.VideoFrame.MapScope` and `Gst.Audio.AudioBuffer.MapScope` are what
`gst_video_frame_map`, `gst_video_frame_map_id` and `gst_audio_buffer_map`
become. The C functions fill a `GstVideoFrame` or a `GstAudioBuffer` the caller
declared, and what is in it is a mapping that `gst_video_frame_unmap` or
`gst_audio_buffer_unmap` has to release again; the plane spans point into
memory that belongs to the buffer and are only valid until then. Both scopes
therefore follow `Gst.Buffer.MapScope`: a `ref struct` that carries the
structure, hands out `Span<byte>` planes, releases the mapping in `Dispose` and
refuses every accessor afterwards. Releasing twice does nothing the second
time.

Each scope also holds the `Gst.Buffer` wrapper it was created from, and for the
audio one that is a correctness requirement rather than a convenience:
`gst_audio_buffer_map` takes no reference of the buffer at all, and
`gst_video_frame_map` takes none either when
`Gst.Video.VideoFrameMapFlags.NoRef` is set. Without the scope holding it,
nothing would stop the collector from finalizing a wrapper whose last use was
the call that produced the mapping.

## RTP mapped structures

`Gst.Rtp.RTPBuffer` and `Gst.Rtp.RTCPBuffer` are not scopes: they are the plain
structures the C API declares, and the binding hands them out as they are.
Declare one as a local variable, map it once with `RTPBuffer.MapBuffer` or
`RTCPBuffer.MapBuffer`, and unmap it exactly once when it is done. Never copy
one, store it in a field or capture it in a lambda or an `async` method: the
generated members pin the variable for the duration of a single call, and the
internal `ensure_buffers` of `gstrtpbuffer.c`, which `SetExtensionData` and the
`AddExtension*` members reach, unmaps and remaps through the very structure it
is handed, so a call made on a copy unmaps a second time. The `Gst.Buffer`
wrapper the mapping came from has to stay alive until after the unmap - the
library stores the raw pointer and takes no reference of it
(`rtp->buffer = buffer` and `rtcp->buffer = buffer`, nothing else), so
disposing the wrapper before the unmap leaves the mapping pointing at a freed
`GstBuffer`. Garbage collection is the same hazard without a `Dispose` in
sight: a wrapper that nothing references any more - a buffer obtained inline as
the argument of `MapBuffer`, for one - is finalizable the moment that call
returns, and its finalizer drops the reference while the mapping is still in
use. Keep the wrapper reachable until after the unmap - what keeps it reachable
is its last use and not a variable, and one that is declared and never read
again counts for nothing once the collector has passed that last read. A
`using` declaration whose scope encloses the unmap does it, the disposal at the
end of the scope being a use that comes after the unmap, and so does a
`GC.KeepAlive(buffer)` placed after the unmap.

`Gst.Rtp.RTCPPacket` borrows the address of the `RTCPBuffer` it was taken from:
`GetFirstPacket` and `AddPacket` write that address into the packet, the
writing members update the size of the mapping through it and `Unmap` resizes
the buffer from it. A packet is therefore usable only inside the scope where
its `RTCPBuffer` variable lives, and never after the unmap. `MapBuffer` for
RTCP requires `Gst.MapFlags.Read` to be among the flags - a write only mapping
raises a critical and answers `false` - so build a compound packet with
`Gst.MapFlags.Read | Gst.MapFlags.Write`. The spans that `FbGetFci` and
`AppGetData` hand out point into the mapped buffer as well, and any change to
the packet list of that buffer invalidates them.

Calling a header accessor on an `RTPBuffer` that was never mapped is not a
managed error: the C side dereferences `rtp->data[0]` without a guard and the
process crashes. Use the structure only after `MapBuffer` has answered `true`.
The RTCP half is guarded - every member answers `false` or `0` for an unmapped
structure - and so is
`Gst.Rtp.RTPHeaderExtension.GetSdpCapsFieldName`, which raises a critical and
answers `null` until `SetId` has been called.

The RTCP extended report readers trust the block length field of the block they
stand on. `XrFirstRb` checks that field against the packet in the wrong unit -
`offset = 8 + (block_len * 1) + 4` against `packet->length << 2`
(`gstrtcpbuffer.c:2753-2760`), words against bytes - and so accepts a first
block about four times longer than the packet. `XrNextRb` measures only the
block it leaves: it advances by `(block_len + 1) * 4` and refuses when that
lands outside the packet (`:2791-2797`), so the block it stops on goes
unmeasured until the next `XrNextRb`, after the readers have run. No other
guard uses that field: `XrGetBlockType` and `XrGetBlockLength` check only that
the block header word is inside the packet (`:2827`, `:2876`), and the packet
walk (`:444`) and `RTCPBuffer.Validate` (`:118-121`) bound packets, not blocks.
Every per-item reader bounds its read on that field alone - the fixed size ones
require an exact length (`XrGetRrt` 2 words, `:3095`; the four `XrGetSummary*`
9, `:3182`, `:3228`, `:3282`, `:3349`; the eight `XrGetVoip*` 8, `:3398`
through `:3683`), the indexed ones derive their range from it
(`XrGetRleNthChunk` takes any index below the `(block_len - 2) * 2` chunks
`XrGetRleInfo` computes, `:2919` and `:2967`; `XrGetDlrrBlock` any `nth` with
`nth * 3 < block_len`, `:3136`, and then reads three words, so a length that
is not a multiple of three lets the last sub-block run past the block). A
block that claims more words than the packet has left is read to the length it
claims - a 16-bit field reaches about 256 KB - into the next packet of the
compound or, for the last packet, past the end of the mapped buffer: garbage or
an access violation, never a managed error.

A consumer that reads XR blocks from a peer it does not trust does the bounds
check itself. `GetLength()` is the packet length in 32-bit words minus one, so
`GetLength() - 1` words remain after the SSRC and each block consumes
`XrGetBlockLength() + 1` of them; subtract as you walk and call the per-item
readers only while the block `XrFirstRb` or `XrNextRb` stopped on still fits,
and for a DLRR block only when `XrGetBlockLength()` is a multiple of three.
`XrGetPrtBySeq` needs a second check: it indexes by `(seq - begin_seq) * 4`
from the block start (`:3070`) and its only length test is the three word
minimum of `XrGetPrtInfo` (`:3010`), so an honest length with an oversized
sequence range overreads even when the walk passes - require
`endSeq - beginSeq` to be at most `XrGetBlockLength() - 2`, the words left
after the SSRC and the sequence pair, before you call it.

## RTSP server

`RTSPMountPoints.AddFactory` mounts a media factory, and the wrapper it is
handed stays the caller's, which is what every `transfer-ownership="full"`
GObject argument of the binding does. It matters here more than anywhere else:
`Dispose` runs `DisconnectAll`, so a consuming shape would strip the
`MediaConfigure` and `MediaConstructed` handlers a caller had just connected to
the factory — the exact arrangement `test-launch.c` uses, where the hook is
connected before the mount. The member mints exactly one reference and hands
that one over; the mount item keeps it in a bare pointer
(`rtsp-mount-points.c:358`) and releases it in `data_item_free` (`:71`) when
the path is unmounted, replaced, or the mount points are finalised, so the
reference count lands where the C call leaves it. It is generated, with one
addition the overlays carry: a path that does not begin with `/` is refused
with an `ArgumentException` before the call, because `rtsp-mount-points.c:354`
answers such a path with a `g_return_if_fail` before it takes the factory and
the minted reference would then have no owner.

`RTSPServer.TransferConnection`, `RTSPSession.ManageMedia` and
`RTSPSessionMedia.New` keep their wrapper the same way, and each leaves the
object in a state worth knowing about. The socket a connection was transferred
on is driven by the server's own thread from then on
(`rtsp-server.c:1200-1203`), so using that wrapper afterwards races the server.
A media a session manages is co-owned by the session media, which sets it to
`NULL` and unprepares it when it is finalised
(`rtsp-session-media.c:106-108`). `RTSPMedia.Prepare` is a different shape
altogether: its argument is a thread, and a thread is released by a stop rather
than by an unreference, which is the paragraph below.

**A thread the pool hands out is released by a stop and not by an unreference.**
`RTSPThreadPool.GetThread` answers a wrapper that owes exactly one
`gst_rtsp_thread_stop`: one `get_thread` adds one reference and one reuse
count, and one stop releases both — straight away, or from the destroy
notification of the idle source that quits the loop
(`rtsp-thread-pool.c:174-190`). `RTSPThread.Stop`, and disposing the wrapper,
perform that stop; a second call is a no operation, and the OS thread behind it
stays alive while any other holder still counts a use of it.
`RTSPMedia.Prepare` **consumes** such a thread on every path it takes, whether
it reports success or failure, so the wrapper is detached when the call
returns; a thread that owes no stop is refused with `ArgumentException` rather
than consumed. `RTSPThread.Reuse` is the one member that steps outside this
ledger: a `true` answer adds one reference and one reuse count that the single
stop of this wrapper does not release, and the binding offers no second stop to
release them with, so calling it leaves the thread held for good. The member is
therefore `[Obsolete]` and is scheduled for removal in `1.30`; it stays in
`1.28.x` because it is part of the published surface the packages promise.
`RTSPThread.Context` is a
read of the main context the thread runs its sources on; the loop is not
offered, because quitting it from outside leaves the idle source of a stop with
nothing to run it.

`RTSPMedia.New` reads the other way round. Its generated XML documentation
repeats the gir remark "Ownership is taken of @element", but the binding hands
the element over with transfer none, because the C constructor takes a
reference of its own (`gst_object_ref_sink` on the `element` property,
`rtsp-media.c:695-696`) and a wrapper of this binding never holds a floating
reference for it to sink. The caller's element wrapper therefore stays valid
after the call and is disposed by its owner as usual.

`RTSPMediaFactory.Construct` answers a media that is **locked**. That is the C
contract and not an accident of the binding: the factory hands a shared media
out with `gst_rtsp_media_lock` held so that the caller can finish configuring it
before another request reaches it. Call `Unlock()` on the media before the next
request arrives, or the server deadlocks on the second client. The same applies
to a media obtained from `Construct` in a test or a tool that never runs a
server. The lock is a plain, non-recursive `GMutex` (`priv->global_lock`,
`rtsp-media.c:100`, taken by `gst_rtsp_media_lock` at `:4520`), so leaving it
held makes every later call that takes it hang — including a second `Lock()`
from the very thread that already holds it, which deadlocks outright rather
than nesting.

**No signal of this module is marshalled to the thread that called `Attach`.**
By default the thread pool gives all clients **one shared thread** — its
`max-threads` is 1 (`rtsp-thread-pool.c:201`) and the client branch recycles the
thread at the head of the queue once that many exist (`:455-468`) — so
`MediaConfigure`, `MediaConstructed`, `NewStream`, `Prepared` and the request
signals of `RTSPClient` run on a pool thread and never on the thread that called
`Attach`, while `ClientConnected` runs on
whichever thread iterates the attached context and `HandleMessage` runs on a
media thread. `RTSPThreadPool.SetMaxThreads(0)` collapses that: a client is then
attached to the context of the source that is dispatching it, which is the
server's own, and iterating that context is the only thing that drives the
server. Handlers of `MediaConfigure`, `MediaConstructed` and `HandleMessage`
run with a C lock held and **must not call `Lock()`, `Construct()` or
`Prepare()`**. Which lock stops which call differs per signal.
`media-constructed` is emitted before the media is locked but under the
factory's `medias_lock` (`rtsp-media-factory.c:1576`, emitted at `:1612`,
`gst_rtsp_media_lock` only at `:1617`), so `Construct()` deadlocks on that
plain mutex and `Lock()` returns — and then deadlocks the factory, which takes
that same media lock the moment the handler gives control back.
`media-configure` (`:1624`) is emitted with both held, so `Lock()` blocks in
the handler itself. `HandleMessage` is
emitted under the media's `state_lock` (`rtsp-media.c:3705`), and that one is a
`GRecMutex` (`:128`), so `Prepare()` does not deadlock on it — it re-enters and
is punished by the media's state instead. While the media is still preparing it
drops the lock and waits for the preroll messages (`:4243`) that the very bus
watch it is running inside would have to deliver, and hangs there. Once the
media is prepared or suspended it returns `TRUE` at once (`:4208-4210`,
`:4260-4267`) — having already incremented the prepare count (`:4206`), which
only a matching `Unprepare()` brings back down, so the media then outlives the
sessions that were meant to own it. The rule is the same in all three cases;
only the way it is punished differs.

Shutting a server down is an ordered five steps, and the binding offers no
single call for it because the middle of it is application shaped. First
`Detach(sourceId, context)` — the counterpart of `Attach`, taking the same
context that `Attach` was given, since `g_source_remove` searches the default
context only. Then `ClientFilter` answering `Remove` for every client, which
closes the connections; then `SessionPool.Filter` answering `Remove`, because a
closing client does not remove its session and `Cleanup()` only expires the
timed out ones, and it is the session going away that unprepares the media and
stops the pipeline. A filter answering `Remove` answers no list — only
`RTSPFilterResult.Ref`, and a `null` filter function, put an item in the list
either call returns — but that list is built with transfer full, so the
wrappers in it hold native references and have to be disposed for the client or
session to go away on the spot rather than at the next collection. Then poll
`ClientFilter(null)` until it is empty, disposing what each poll answers,
because the close of a client completes asynchronously on the client's own
thread; only
then, and only if the process is ending, `RTSPThreadPool.Cleanup()`, which joins
every pool thread and blocks forever if it is called while a client is still
closing. `gst_rtsp_server_create_source` is not bound — `Attach` and `Detach`
are the pair that replaces it. Disposing the server instead of detaching it is a
safe no-op with the wrong effect: the attached source and every managed client
hold a native reference of their own, so the server keeps serving while the
wrapper's handlers are disconnected. `Detach` reads this wrapper's handle like
every other instance member, so after the `Dispose` it throws
`ObjectDisposedException` instead: detach first, dispose second.

`RTSPClient.GetConnection` and `RTSPStreamTransport.GetTransport` answer
**borrowed** wrappers over memory the binding does not own. The connection is
freed when the client is finalised, and the transport when the next `SETUP`
replaces it or the stream transport is finalised, in both cases without telling
the wrapper. Read what is needed from them inside the handler that was given the
client or the transport and do not store either wrapper.

## The GType registry

Every binding assembly fills a `GType` to managed-type registry from a
`[ModuleInitializer]`. On CoreCLR the runtime runs a module initializer before
the first *call* into that assembly — and naming one of its types in a cast is
not a call. An application whose only use of `Gst.App` is
`GetByName("sink") as AppSink` therefore never executes a line of that
assembly: the registry has no entry for `GstAppSink`, the wrapper is built as
the closest registered ancestor (`Gst.Element`), and the cast is silently
`null`. Nothing throws and nothing is logged. The same holds for
`message.Src is BaseSrc` and for every wrapper that arrives from a property or
a signal of an element in another assembly.

Three things close that hole:

* `GstSharp.Initialize()` runs the module initializer of every loaded
  `GstSharp.Net*` assembly and subscribes to `AppDomain.AssemblyLoad` to do the
  same for assemblies loaded later. Under NativeAOT they have all run by the
  time the entry point does, so the sweep finds nothing to do and is skipped.
* The per-module entry points — `Gst.App.GstApp.Initialize()`,
  `Gst.Base.GstBase.Initialize()`, and one next to every other module — are a
  call into that assembly and say the same thing deterministically. `GES` needs
  its own for a second reason: `GES.GstGES.Initialize()` also runs `ges_init`.
* `GstSharp.TypeFallback` reports, once per `GType`, that an object was wrapped
  as an ancestor. That is how the otherwise silent case becomes visible.

Registration order is not important — adding a module unfreezes the registry
and the next lookup rebuilds it — but **initialization must precede wrapping**.
A wrapper keeps the type it was created with, so an object that was wrapped
before its module registered stays the base type it was built as.

## Adding and removing event handlers

A handler is remembered on the wrapper instance it was added to and has to be
removed from that same instance. Looking the object up again normally hands out
the same wrapper, because GObject wrappers are interned, so
`bus.SyncMessage -= handler` works across a fresh `pipeline.GetBus()`. What
does not survive is a wrapper that was disposed in between: the next lookup
builds a new one, which knows nothing of the handler.

Disposing a wrapper disconnects whatever handlers are left on it. Setting a
pipeline to `NULL` before disconnecting is the safe order for handlers that run
on a streaming thread: that thread is gone once the pipeline is stopped.

## Applications without a main loop

A GObject finalizer must not unref — removing the toggle reference races with
the toggle notification and would call into GStreamer from the finalizer
thread. It enqueues the release instead, and the queue is drained on a thread
that may call native code: on every GObject wrapper lookup, on every mini
object that is adopted, from the idle callback of a running main loop, and from
`GstSharp.DrainPendingReleases()`.

An application that pulls samples in a loop drains the queue constantly and
needs nothing. **An application with no main loop that also goes long stretches
without touching a wrapper should call `GstSharp.DrainPendingReleases()`
periodically** — once per poll of the bus is the natural place. The queue holds
one small record per pending object, never a copy of the media, and draining an
empty queue is cheap.

The bus itself is the other thing such an application has to keep an eye on. A
bus that nobody reads holds every message the pipeline posted for as long as the
pipeline lives, and polling it is the usual answer. `Bus.SubscribeSyncDrop` is
the answer for an application that would rather be told:

```csharp
using IDisposable subscription = pipeline.GetBus().SubscribeSyncDrop(
    (_, message) => Handle(message));
```

Every message reaches the handler in the thread that posted it — a streaming
thread, while that thread is blocked in the post, so the handler has to be quick
and safe there — and the binding then drops it, so the queue stays empty.
Disposing the subscription takes the handler off. The message wrapper the
handler is given is released when the handler returns; `Message.Copy` is how to
keep one.

There is one sync handler per bus and this takes it. **A second
`SubscribeSyncDrop` on a bus that still carries an undisposed subscription
throws `InvalidOperationException`** rather than silencing the first subscriber:
dispose the one that is live, or fan out from inside the one handler. The raw
`SetSyncHandler` and `ClearSyncHandler` mirror `gst_bus_set_sync_handler` and
keep its swap semantics untouched, so they are not guarded — calling either of
them (or `EnableSyncMessageEmission`, whose handler is installed the same way)
while a subscription is live replaces the handler on the bus and the subscriber
stops seeing messages, while the subscription object still holds the slot until
it is disposed. Disposing it clears whatever is installed at that moment rather
than putting the previous handler back, because C offers no exchange.

A subscriber that throws is a leak if the binding passes the message on, since
there is no queue consumer behind a subscription to take it off again, so
`SubscribeSyncDrop` reports the exception through `GstSharp.UnhandledCallbackException`
and drops the message anyway. That is the one deliberate difference from
`SetSyncHandler`, whose handler is answered `Pass` when it throws: there the
queue is still read by somebody, and swallowing an error or an end-of-stream
message would hang the application waiting for it.

## Finalizers and the garbage collector

**An application that follows the rules above never asks the collector for
anything.** `Dispose` on a mini object or a boxed value unrefs on the calling
thread, so the native memory is freed — or handed back to its pool — at the
closing brace of the `using`, whatever the collector happens to be doing at the
time. What is left over is a managed wrapper of a few dozen bytes, and when
that is collected is nobody's problem.

The finalizer is the safety net, and it is worth knowing what the net costs. A
wrapper that is dropped without being disposed is finalizable, so it takes
**two collections** to go away: the first finds it unreachable and puts it on
the finalizer queue, the finalizer thread unrefs, and only the second one takes
the object. The native memory is alive for the whole of that. The arithmetic of
the delay is the part that matters: a `Sample` and the `Buffer` pulled out of
it are on the order of a hundred managed bytes together, so a pipeline at 30
frames a second takes minutes to fill a gen0 budget — while every leaked 1080p
frame it is holding on to is about three megabytes. The collector paces itself
against the wrapper and not against what the wrapper owns.

**The wrappers this happens to are the ones that escaped.** `GST0001` reports a
local that is never disposed, and it deliberately stops reporting the moment
the wrapper is returned, stored in a field, passed to a method or captured by a
lambda — see [the analyzer rules](analyzers.md). A `Sample` put into a
`List<Sample>` and forgotten is exactly the shape the analyzer cannot see, and
exactly the shape that grows without bound. Dispose where the wrapper stops
being needed, wherever that turns out to be; the analyzer covers the easy half
of that and no more.

A GObject wrapper is a different story, and its finalizer does not unref at
all: it enqueues the release, which `GstSharp.DrainPendingReleases()` performs
on a thread that may call into GStreamer. A collection on its own therefore
releases nothing. The section above says where the queue is drained and when an
application has to drain it itself.

**The binding does not call `GC.AddMemoryPressure`, and that is deliberate.**
Pressure is additive while references are shared, so several wrappers on one
buffer — `sample.GetBuffer()` is a second wrapper on the same memory — would
report that memory two or three times over on the hottest path there is.
Pool-backed buffers break the model outright: their unref returns the buffer to
its `GstBufferPool` rather than to the operating system, so a collection would
free nothing the pressure could be taken back off, and the induced collections
would repeat. And an induced gen2 collection is a blocking pause in the one
kind of application that is built not to have them. An application that really
does hold a large buffer for a long time — a stored snapshot, say — can say
so for that buffer alone, with `buffer.GetSize()` and a matched
`GC.AddMemoryPressure` / `GC.RemoveMemoryPressure` pair of its own.
