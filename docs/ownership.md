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
* **A floating object the caller sinks.** The ring buffer of
  `AudioBaseSink.OnCreateRingbuffer` and `AudioBaseSrc.OnCreateRingbuffer` is
  answered *without* a reference being added, because
  `gst_object_set_parent` sinks it and the element becomes its only owner.
  Keep no reference of your own to what such a slot answers; read it back
  from the element instead.
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
anything once the call returns.

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

`Object.As<T>()` owns nothing of its own. When the wrapper class does not
declare the interface, the cast hands back a small view that holds a strong
reference to the wrapper it came from and reads the handle through it, so the
view keeps the wrapper alive and takes no reference of its own on the native
object. The lifetime stays the wrapper's: there is nothing to dispose on a
view, and disposing the wrapper makes every view of it throw
`ObjectDisposedException`. When the wrapper class does declare the interface,
the cast is the wrapper itself and there is no view at all.

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
  address it publishes, which is why `RTCPPacket.RtcpPtr` is still a `nint`.

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
entry says why, and three reasons remain. `Iterator.pushed` is a pointer the
header keeps to the implementation of the structure, where the boxed copy an
accessor takes would alias a child the owner frees. `AudioCdSrcTrack.tags` is
storage the user fills and hands to the library, which takes it over and then
refuses to write a list a reference of ours made unwritable. The six `pt` of
the derived MIKEY payloads are the header a derived record embeds, whose
wrapper nothing on this surface can reach.

A fourth reason is not an entry at all. A field the gir marks with a `version`
newer than the oldest GStreamer the binding supports gets no accessor from the
generator, whatever its shape, and its ledger line says which version put it
there — `ReferenceTimestampMeta.info — Pointer, since 1.28`. The library on an
older machine allocates the structure without that field, so the read would be
past the end of it, and unlike a late entry point a field access has nothing to
fail on. No overlay lifts this; what would lift it is a version the binding
asks the library for at run time.

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
  `PlayVideoOverlayVideoRenderer` are still reachable while the play runs. The
  consume-in contract of the section above is deliberately not used here: it
  disposes the wrapper process-wide, and the play offers no readable
  `video-renderer` property to get it back from.
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

`RTSPMountPoints.AddFactory` is the one call of this module whose C half takes
a `transfer-ownership="full"` GObject over and whose wrapper survives it
anyway — the second such call in the binding, after `new Play(renderer)`
above. It is written by hand for that reason: the generated consuming shape
disposes the argument, and `Dispose` runs `DisconnectAll`, so mounting a
factory would strip the `MediaConfigure` and `MediaConstructed` handlers a
caller had just connected to it — the exact arrangement `test-launch.c` uses,
where the hook is connected before the mount. The member mints exactly one
reference and hands that one over; the mount item keeps it in a bare pointer
and releases it when the path is unmounted, replaced, or the mount points are
finalised, so the reference count lands where the C call leaves it. The
consuming rule above still holds everywhere else in the module, including
`RTSPServer.TransferConnection`, `RTSPSession.ManageMedia`,
`RTSPSessionMedia.New` and `RTSPMedia.Prepare`, whose arguments — a socket, an
internal media, a thread — are handed over and not expected back.

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
