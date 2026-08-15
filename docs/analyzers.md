# GstSharp.Net analyzers

The `GstSharp.Net.Analyzers` assembly ships as an analyzer asset inside the
binding packages and flags the two leak classes that GStreamer applications
hit most often. Both rules follow the binding's ownership policy: every
wrapper handed to user code owns a reference and must be disposed.

## GST0001 — undisposed GstSharp wrapper

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

## GST0002 — unmapped MapScope

The result of `Buffer.Map(...)` must be disposed so that the underlying
`gst_buffer_unmap` runs. Discarding the returned `MapScope` or leaving a
local without `Dispose` leaks the mapping.

```csharp
using var map = buffer.Map(MapFlags.Read);        // ok
buffer.Map(MapFlags.Read);                        // GST0002
```

Passing the scope to another method counts as consumption (the callee may
dispose it).
