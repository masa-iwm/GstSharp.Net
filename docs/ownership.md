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

Two consequences worth stating:

* **No owning properties.** A property that produced an owned wrapper would
  produce one per read, in the one place the analyzer cannot watch. The
  generator keeps those getters as methods — `appsrc.GetCaps()`, not
  `appsrc.Caps` — and lists them under `OwningProperty` in
  `girs/skip-report.md`.
* **A `Structure` from `Caps.GetStructure` is a copy**, not a window into the
  caps. Writing to it does not write back, whether the caps are writable or
  not.

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

`MakeWritable` on a mini object is the related case in the other direction: it
consumes the reference it is given and adopts whatever comes back, so the same
wrapper stands for possibly different caps or a different buffer afterwards.
Any handle read before the call is stale.

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
