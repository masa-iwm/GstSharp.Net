# Gio asynchronous operations as tasks

Gio's asynchronous pattern is a pair of C functions: a `*_async` that takes a
`GAsyncReadyCallback` and a `*_finish` that turns the `GAsyncResult` it is
handed back into a value or a `GError`. GstSharp.Net does not expose that pair.
It exposes one `Task`-returning method per operation, and this document is the
contract behind it: where the callback runs, how errors and cancellation are
mapped, who owns what, and why the binding runs a thread of its own instead of
telling applications to run a main loop.

The shipped surface of this machinery is `GES.Asset.RequestAsync` and
`GES.UriClipAsset.NewAsync`. The runtime half lives in
`src/GstSharp.Net/Core/Gio/`, in `Gst.Gio`:

| File | Contents |
| --- | --- |
| `GioAsyncDispatcher.cs` | The private main context, the thread that iterates it, and the hand over onto it. |
| `GioAsync.cs` | The two static unmanaged callbacks, and the state object every operation is built on. |

Nothing here is generated. The generator skips `scope="async"` callbacks by
design (see the remarks in `Planning/MarshalPlanner.cs`), so the four entry
points of the two methods above appear in `girs/skip-report.md` and are bound by
hand, as `partial` extensions of the generated classes in
`src/GstSharp.Net.GES/Custom/`.

## 1. The dispatcher: enforce, do not document

A `GTask`-based `*_async` captures the main context that is thread default on
the thread that starts it, and delivers its callback in a *later iteration* of
that context. Never synchronously, never on an arbitrary thread — cancellation
included, which Gio documents as "the operation's `GAsyncReadyCallback` will not
be invoked until the application returns to the main loop".

A .NET caller on a thread pool thread has pushed no thread default context, so
the operation would capture the global default one — and a GStreamer
application frequently iterates no main context at all. This repository already
institutionalises that fact elsewhere: `Gst.GObject.Object.DrainPendingReleases`
exists because finalizers cannot assume a loop is running. For an asynchronous
callback the consequence is worse than a delayed release: the callback is never
dispatched, the returned `Task` never completes, and the state behind it leaks
until the process ends. The call site cannot detect it and user code cannot
repair it.

So the binding owns a context rather than documenting a requirement:

* **One background thread, started on the first asynchronous call, running for
  the life of the process.** It creates a private `GMainContext`, makes it
  thread default on itself and iterates it forever. There is no teardown;
  ending the process is the teardown, which is the same stance the binding
  takes on `ges_deinit`.
* **Operations are started on that thread**, through
  `g_main_context_invoke_full`. The `GTask` therefore captures the dispatcher's
  context because it is genuinely running under it.
* **Both halves of an operation run there**: the `*_async` call and the
  `*_finish` call in its callback. That also suits the libraries that are least
  thread safe — the editing services ask to be used from one thread, and this
  gives them one.
* **User continuations never run there.** Every completion source is built with
  `TaskCreationOptions.RunContinuationsAsynchronously`, so an `await`
  continuation is scheduled rather than run inline in the GLib dispatch frame.
  User code can neither stall the dispatcher nor re-enter the binding from
  inside it.

### Why the operation is not merely bracketed on the calling thread

The obvious alternative is to leave the call on the caller's thread and wrap it
in `g_main_context_push_thread_default` / `pop_thread_default`, so that the
`GTask` captures the dispatcher's context without a thread hop. **That does not
work**, and it fails loudly:

```
GLib-CRITICAL **: g_main_context_push_thread_default: assertion 'acquired_context' failed
```

`g_main_context_push_thread_default` *acquires* the context, and a `GMainContext`
can be owned by one thread at a time. The dispatcher thread holds ownership
across the blocking poll of every iteration, so a push from any other thread
fails, the matching pop then unbalances GLib's per thread stack, and the
process is one native call away from a crash. GLib says as much in the
documentation of that function: "normally you would call this function shortly
after creating a new thread, passing it a `GMainContext` which will be run by a
`GMainLoop` in that thread". Pushing a context somebody else runs is not a
supported shape, and the dispatcher is the thread that runs this one.

Starting the operation on the dispatcher thread costs one idle source per call
and buys the mechanism GLib actually supports.

## 2. The two callbacks

The C to managed edge is two `[UnmanagedCallersOnly]` function pointers for the
whole surface — no delegate is marshalled, nothing is discovered by reflection,
and both are NativeAOT clean by construction:

* **`GioAsync.Starter`**, a `GSourceFunc`, makes the `*_async` call on the
  dispatcher thread.
* **`GioAsync.Bridge`**, the one `GAsyncReadyCallback` of the binding,
  completes it.

Neither is typed for a particular operation. The `user_data` pointer is a
`Gst.Interop.CallbackHandle` — the repository's existing `GCHandle` protocol —
to a `GioAsyncState`, and the per operation typing lives in that object:

```csharp
internal abstract class GioAsyncState                 // what the callbacks see
internal abstract class GioAsyncState<T> : GioAsyncState   // + the completion source
```

Each operation declares one sealed subclass of `GioAsyncState<T>` that
overrides two methods:

* `Invoke(cancellable, userData)` — calls that operation's `*_async` import.
* `Finish(sourceObject, result)` — calls that operation's `*_finish` import and
  marshals the result.

The dispatch from the shared callbacks to the right entry point is therefore an
ordinary virtual call on a managed object. Only the C to managed direction needs
an unmanaged callback; the managed to C direction is a plain `LibraryImport`.

`CallbackScope.Async`, which the repository already documents on
`Gst.Interop.CallbackScope`, is the lifetime rule: the callback runs exactly
once and releases the state. The gir also guarantees that `*_finish` may be
called at most once; the bridge is the only caller of either, so both
guarantees are structural rather than a rule somebody has to keep.

## 3. Cancellation

The public surface takes a `CancellationToken`, which the binding translates.
Per call:

1. If the token *can* be cancelled, the state creates a private `Cancellable`
   and registers `token.Register(static s => ((Cancellable)s!).Cancel(), …)`.
   `g_cancellable_cancel` is documented thread safe, so the registration may
   fire on any thread.
2. The registration is created **before** the operation is handed to the
   dispatcher. A token that is already cancelled therefore cancels the
   `Cancellable` before the operation starts, which is harmless: Gio guarantees
   the callback still runs, with `G_IO_ERROR_CANCELLED`.
3. Cleanup disposes the registration **first** and the `Cancellable` second.
   Disposing the registration waits out a `Cancel` that is in flight on another
   thread, so the wrapper is never released underneath a call that is using it.
   It cannot deadlock, because `g_cancellable_cancel` returns without waiting
   for the operation to complete.

The `Cancellable` is created, held and released by the binding and is never
handed out, which is what makes the binding disposing a GObject wrapper here the
right thing rather than a breach of the "wrappers are not disposed by users"
doctrine — the same reasoning as the consuming callback install of
`Gst.App.AppSink.SetSimpleCallbacks`.

There is a second overload of each method that takes a `Gst.Gio.Cancellable`
instead, for a caller who already holds one. That object is **borrowed**: the
state takes a `g_object_ref` of its own on the calling thread — the operation
starts and completes on another thread, and a caller writing `using var
cancellable = Cancellable.New();` is writing ordinary code — and drops that
reference in `Cleanup`. It never cancels, resets or disposes the caller's
object; cancelling the request is the caller's own `Cancel()`. A request
cancelled that way completes as an `OperationCanceledException` whose
`CancellationToken` is `None`, because no token was involved. Gio's rule about
the object holds unchanged: a `GCancellable` that has been cancelled is not
reused for a new operation. `g_cancellable_reset` exists, but it may only be
called when no operation is running, so a fresh `Cancellable.New()` per
operation is the simpler shape.

Whether an operation *honours* the token is the library's business, not the
binding's. A `ges_asset_request_async` that the asset cache can answer straight
away, for instance, hands the asset over regardless.

## 4. Error mapping

Every `*_finish` is `throws="1"`, so `Finish` passes a `GError**` and calls
`Gst.GLib.GException.ThrowIfSet` — byte for byte the pattern the generator
emits for synchronous throwing calls. There is no new exception hierarchy: a
`GioException` would diverge from the whole existing surface for no benefit, and
callers who care match on `Domain` and `Code` as they already do for GStreamer
errors.

What the completion does with the exception:

| Outcome | Task state |
| --- | --- |
| `Finish` returned | `TrySetResult` |
| `GException` in `g-io-error-quark` with code 19 (`G_IO_ERROR_CANCELLED`), our token cancelled | `TrySetCanceled(token)` — the awaiter sees an `OperationCanceledException` carrying the caller's token |
| The same error, our token not cancelled | `TrySetCanceled()` — cancelled by something else is still a cancellation, not a fault |
| Any other exception | `TrySetException` |

The `g-io-error-quark` quark is interned once, in a static of the non generic
state class, so it costs one call for the whole surface rather than one per
closed generic type.

## 5. Ownership and lifetime

Three things keep the operation alive, and two keep it from being released
twice.

1. **The native guarantee.** The gir promises that the operation holds a
   reference on its source object from the `*_async` call until after the
   callback returns. The native object cannot die mid operation whatever
   managed code does.
2. **The state roots everything managed.** The chain is `GCHandle` → state →
   { completion source, owner wrapper, `Cancellable`, registration, the
   marshalled arguments }. So the wrapper an operation was started on — and
   everything a user hung off it — survives until the callback, without any
   cooperation from the caller. This is deliberately stronger than the
   `GC.KeepAlive` the generator emits, which only covers the synchronous extent
   of a call; the last use of an asynchronous call is its callback, not its
   return.
3. **The arguments are copied.** The call happens on the dispatcher thread, so
   a string cannot live on the caller's stack. It is copied with
   `GMarshal.StringToUtf8Ptr` on the calling thread, which also keeps the
   rejection of a string containing a null character a synchronous
   `ArgumentException`, and released by the state's cleanup.

The `GCHandle` is freed exactly once, on whichever of three paths the operation
takes:

* **It completed.** The bridge frees it in a `finally` — the `CallbackScope.Async`
  contract.
* **The `*_async` call threw** (a missing entry point, say). The operation never
  started, so its callback will never run: the state fails the task with that
  exception, cleans up, and frees the handle itself.
* **The hand over to the dispatcher threw.** Nothing took the state over and the
  caller releases it — the rule `Gst.GObject.Object.ConnectSignal` and
  `Gst.GLib.GLibSynchronizationContext.Post` already document and implement.

If a callback never fires at all — a third party `GAsyncInitable` that never
returns its task — the state graph leaks until the process ends. There is no
safe way to free it unilaterally, because Gio may still fire later. That residual
risk is the reason section 1 enforces the dispatcher rather than documenting a
requirement; no watchdog is added, and a caller who wants one can compose
`Task.WaitAsync`.

## 6. The shape of a consumer

`GES.Asset.RequestAsync` is the whole pattern, in the two pieces every
consumer has. The public method builds the state and starts it:

```csharp
public static Task<GES.Asset> RequestAsync(
    Gst.GObject.GType extractableType,
    string? id,
    CancellationToken cancellationToken = default) =>
    new RequestState(extractableType, id, cancellationToken).Start();
```

and the state supplies the two entry points and takes back what it allocated:

```csharp
private sealed class RequestState : GioAsyncState<GES.Asset>
{
    private readonly nuint _extractableType;
    private nint _id;

    internal RequestState(Gst.GObject.GType extractableType, string? id, CancellationToken cancellationToken)
        : base(owner: null, cancellationToken)
    {
        _extractableType = extractableType.Value;
        _id = Gst.Interop.GMarshal.StringToUtf8Ptr(id);
    }

    protected override void Invoke(nint cancellable, nint userData) =>
        GesAssetRequestAsync(_extractableType, (byte*)_id, cancellable, GioAsync.Bridge, userData);

    protected override GES.Asset Finish(nint sourceObject, nint result)
    {
        nint errorNative = 0;
        nint nativeResult = GesAssetRequestFinish(result, &errorNative);
        Gst.GLib.GException.ThrowIfSet(ref errorNative);

        return Gst.GObject.Object.FromNative<GES.Asset>(nativeResult, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("ges_asset_request_finish returned no value.");
    }

    internal override void Cleanup()
    {
        Gst.Interop.GMarshal.Free(Interlocked.Exchange(ref _id, nint.Zero));
        base.Cleanup();
    }
}
```

The `Cancellable` overload is one more public method and one more constructor
of the same state, chaining to `base(owner, cancellable)` instead of
`base(owner, cancellationToken)`; nothing else in the state changes, because
which `GCancellable` reaches `Invoke` is the base class's answer.

`owner` is `null` here because `ges_asset_request_async` is a function of the
library rather than a method on an object, so there is no wrapper to keep
reachable. An instance operation passes `this` instead, and needs no
`GC.KeepAlive`: the state roots the wrapper for the whole operation, which is
strictly more than the barrier would give.

Note also that `ges_asset_request_finish` takes the `GAsyncResult` alone. The
`sourceObject` the callback carries is available to `Finish` for the entry
points that want it, and ignored by the ones that do not.

## 7. What a consumer must document

Every `Task`-returning wrapper says three things in its XML documentation,
because none of them is visible from the signature:

* which initialisation must have run first (`GstGES.Initialize` for the two GES
  methods);
* that the operation runs on the binding's dispatcher thread, and that the
  application therefore needs no main loop;
* whether the returned wrapper is owned;
* for a `Cancellable` overload, that the object is borrowed and that the
  cancellation it produces carries no token.

## 8. Known library hazard

`ges_asset_request_async` in the editing services 1.28.6 dereferences a null
pointer, taking the process down, when it is given no identifier **and** the
asset is already in its cache. The first request for a given type succeeds and
populates the cache; the second one crashes. The synchronous `ges_asset_request`
and every request that names its identifier are unaffected, and the crash
reproduces from a raw `LibraryImport` with none of this machinery involved.

`GES.Asset.RequestAsync` therefore documents the hazard rather than working
around it. Deriving the identifier a `null` stands for is the extractable type's
own answer — the default is the name of the type, but types are free to override
it — so guessing it in the binding would be wrong for exactly the types that
need it most.

## 9. Deliberate omissions

* **No generator support.** The pinned reference girs carry `glib:finish-func`
  only in `GES-1.0.gir`; pairing `*_async` with `*_finish` mechanically across
  the whole surface would need either a gir refresh or name convention matching.
  The state-subclass-per-operation shape above is what a generator would emit
  well, so this is a question of when, not whether.
* **No dispatcher opt out.** An application that does run a GLib main loop still
  gets the binding's thread. An option to suppress it is a later design; the
  cost today is one idle thread.
* **`GstPromise` is not this.** The WebRTC promise machinery is a separate
  pattern and is not part of this contract.
