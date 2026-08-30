# Ownership and lifetime

Who owns a wrapper, who releases it, and when. This is the reference the rest
of the documentation and the XML comments point at.

The binding has two object models, and there is one rule per model. Which rule
applies follows from the base type of the wrapper and from nothing else:

| Base type | Examples | What the wrapper owns | Disposed by consumers |
| --- | --- | --- | --- |
| `Gst.MiniObject`, `Gst.GObject.Boxed` | `Buffer`, `Caps`, `Sample`, `Message`, `Event`, `Structure`, `SDPMessage`, `GLib.Bytes` | a reference of its own | **always** |
| `Gst.GObject.Object` | `Element`, `Pipeline`, `Bus`, `Pad`, `Clock`, `Device` | one reference shared by the whole process | **normally never** |

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
buffer handed to `BaseSink.OnRender` or `BaseTransform.OnTransformIp`, and the
caps handed to an `OnSetCaps`, are *borrowed* for the length of the call —
GStreamer keeps owning them, the wrapper takes no reference of its own, and it
is released when the override returns. Using one afterwards throws
`ObjectDisposedException` rather than touching an object somebody else owns, so
keeping the data means copying it. Disposing such a wrapper early is harmless,
and `MakeWritable` on one throws: it would release a reference the wrapper does
not own. See
[`docs/subclassing.md`](subclassing.md#11-using-it-stage-1).

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
every `FromMeta` cast — throws `ObjectDisposedException`. **The generated field
accessors do not check it**: reading `Meta.Flags`, or a field of a typed record,
through a wrapper of an item that was removed is undefined rather than an
exception. A generator-side guard for the forced-opaque records would close that
gap and is on the backlog.

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

`Buffer.IterateMeta` is **read only for the length of the enumeration**. The
cursor of `gst_buffer_iterate_meta` is the metadata item itself, so removing one
while the enumeration is open frees the node the cursor stands on and the next
step is a use after free. `Buffer.ForeachMeta` is the way to remove while
walking, because the library captures the successor of an item before it hands
it to the function.

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

Both girs are right, and the difference is not smoothed over: the reference the
wrapper holds is one reference either way, and disposing it is correct in both.

That reference is a lifetime of its own. The wrapper takes it at construction
when the pointer it was given is borrowed (`g_param_spec_ref_sink`) and gives
it up in `Dispose` and nowhere else, so what `Lookup` or `LookupChild` — or a
signal — handed out stays readable after the object or the child it describes
is gone: a `GParamSpec` lives by its own reference count, not by that of any
instance. The wrapper has no finalizer, which makes its leak the one silent
one in the runtime: an instance that is never disposed holds that reference
until the process exits. Little is lost when that happens, since an installed
specification belongs to a class and lives as long as the process anyway, but
dispose it as you would any other wrapper.

A lookup that finds nothing answers `false` and leaves **both** out parameters
`null`. The C functions do not touch the storage they were given on that path,
so the binding zeroes it before the call and reads a null pointer back as
`null`; there is no stale value to guard against and no wrapper to dispose.

## Fields a wrapper reads

A generated field accessor is a get-only property that reads through the handle
of the wrapper. It hands out a **copy** of what the field holds and owns
nothing; a disposed wrapper refuses it rather than dereferencing the null
pointer. Two shapes are worth spelling out.

* **A fixed size field is answered as inline storage.** `VideoInfo.Stride`,
  `VideoInfo.Offset` and `VideoFormatInfo.Depth` hand out a struct nested in
  the wrapper that carries the length in its own definition, the same type an
  out parameter of a caller allocated array uses. What comes back is a copy of
  the elements, so writing into it changes nothing native: the fields are set
  through the calls that own them, `VideoInfo.SetFormat` and `VideoInfo.Align`.
* **A wrapper handed out for a field is borrowed.** `VideoInfo.FormatInfo` and
  `AudioInfo.FormatInfo` point at the per format description the library keeps
  for the life of the process. The wrapper takes no part in its ownership,
  there is nothing to dispose, and what it reads says nothing about the
  `VideoInfo` it came from, which may have moved on to another format by then.
  There is always one to hand out, so the property is not nullable: an instance
  that carries no description is a zeroed block of memory and is reported as an
  `InvalidOperationException`.

A public field the generator binds nothing for is listed in the `## Fields`
section of `girs/skip-report.md`, under the shape that kept it out. A field a
hand written member reads through stays listed there, the same way a hand bound
entry point stays on the skip list: what the ledger measures is the generated
surface.

## Calls that consume their argument

A call whose C function takes ownership of a parameter
(`transfer-ownership="full"`) consumes the wrapper it is given instead of
borrowing it. **The generator emits these members**, for a mini object, a boxed
value or a GObject, and every one of them follows one contract: the call is
handed a value minted for it — a mini object and a GObject are handed a
reference of their own, a boxed value is handed a copy, since it has no
reference count to raise — and the argument is disposed when the member
returns, **whatever the call answered**, because the C function offers no way
back. After the call the wrapper owns nothing, which is precisely what its
disposed state means, and the member says so on its parameter:
`Caps.Append(caps2)` consumes the caps it appends,
`StreamCollection.AddStream(stream)` the stream, `Pad.Push(buffer)` the buffer.

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
| `EncodingContainerProfile.AddProfile` | the stream profile |

`SetSimpleCallbacks` has a second overload whose parameters are the individual
callbacks and are all optional, so a bare `null` is a compile-time ambiguity
between the two. That is by design on both `AppSink` and `AppSrc`: taking the
callbacks off again is a different intention from installing them, and
`ClearSimpleCallbacks()` is the call that spells it.

The ones that take a **GObject** over — `AddProfile` above, and generated
members such as `StreamCollection.AddStream` — work the same way, with the
reach `Dispose` has on a GObject wrapper: the object is given up for the whole
process rather than for one holder, so there is no wrapper for that object
anywhere afterwards and a fresh lookup (`GetProfiles`, `GetStream`) is the way
back to it. Where a consuming argument is nullable, `null` is the absence of a
payload and there is nothing to consume.

`Dispose` is idempotent, so a `using` around the argument stays correct and
stays the recommended shape — the analyzer sees the disposal, and an early
return before the consuming call still releases the wrapper.

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
receives — owns no reference to give, so it raises `InvalidOperationException`;
what such a vfunc receives is writable already. And when the object is shared
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

## Members that take or return a `GValue`

A `Gst.GObject.Value` is a struct that owns its contents, and a generated
member never takes that ownership over: the call is handed a pointer into the
caller's own storage, nothing is allocated for it, and nothing is disposed
after it. One rule per shape:

* **An `in` value is read.** The callee copies what it keeps —
  `caps.SetValue`, `Global.ValueIsFixed` — so the caller keeps the value and
  still disposes it. An empty value has no type for the call to read and
  throws `ArgumentException`.
* **A `ref` value has to arrive initialized** with the type the call expects:
  `Global.ValueSetFraction` wants a `GST_TYPE_FRACTION`, and
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
  release.

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
read-only, has no setter at all.

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

A callback parameter the gir marks `nullable` is a `Gst.Foo?` and is not
guarded: the absence of a function is a value the C side acts on, not a mistake.
`Gst.Meta.RegisterCustom` is the one such member — its `transformFunc` may be
`null`, and `gst_meta_register_custom` then copies the meta and its backing
structure on a copy transform and discards every other one. The call site hands
the library the null function pointer, a null `user_data` and no destroy
notification, so no `GCHandle` is allocated for a callback that is not there.

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
