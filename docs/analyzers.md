# GstSharp.Net analyzers

The `GstSharp.Net.Analyzers` assembly ships as an analyzer asset inside the
binding packages and flags the two leak classes that GStreamer applications
hit most often. Both rules follow the mini object half of the binding's
ownership policy: every `MiniObject` or `Boxed` wrapper handed to user code
owns a reference of its own and must be disposed. GObject wrappers are
interned and shared, are not covered by these rules, and are normally left to
the collector — see [ownership and lifetime](ownership.md).

## GST0001

**GstSharp.Net wrapper is never disposed.**

A local variable holding a type derived from `Gst.MiniObject` (Buffer, Caps,
Sample, Message, ...) or `Gst.GObject.Boxed` was created but is neither
disposed on any path nor allowed to escape (returned, stored in a field,
passed to a method, captured by a lambda). Each undisposed wrapper keeps a
native reference alive; in a pull loop this leaks one sample per frame.

Fix: wrap the local in a `using` declaration or call `Dispose()`.

```csharp
using var sample = sink.TryPullSample(timeout);   // ok
var leaked = sink.TryPullSample(timeout);         // GST0001
```

The analysis prefers false negatives over false positives: any escape and
any `Dispose` call on some path suppresses the diagnostic.

The rule looks at locals, not at property reads, and that is why the binding
emits no property whose value is a `MiniObject` or a `Boxed` wrapper. Such a
property would hand out an owned reference per evaluation, in the one place
the rule cannot see it. The generator drops those properties and keeps the
getter as a method — `appsrc.GetCaps()` rather than `appsrc.Caps` — so that
the name says a resource is produced and the result lands in a local the rule
does watch. The skip report lists them under `OwningProperty`.

## GST0002

**Buffer mapping is never released.**

The result of `Buffer.Map(...)` must be disposed so that the underlying
`gst_buffer_unmap` runs. Discarding the returned `MapScope` or leaving a
local without `Dispose` leaks the mapping.

```csharp
using var map = buffer.Map(MapFlags.Read);        // ok
buffer.Map(MapFlags.Read);                        // GST0002
```

Passing the scope to another method counts as consumption (the callee may
dispose it).
