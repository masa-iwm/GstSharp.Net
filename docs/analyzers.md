# GstSharp.Net analyzers

The `GstSharp.Net.Analyzers` assembly ships as an analyzer asset inside the
binding packages and carries five rules. `GST0001` and `GST0002` flag
the two leak classes that GStreamer applications hit most often; they follow
the mini object half of the binding's ownership policy, where every
`MiniObject` or `Boxed` wrapper handed to user code owns a reference of its
own and must be disposed. GObject wrappers are interned and shared, are not
covered by those rules, and are normally left to the collector — see
[ownership and lifetime](ownership.md). `GST0003` and `GST0004` check the
other contract the compiler cannot see on its own: that a subclass declares
exactly the vfunc slots it overrides. `GST0005` guards the other half of the
subclassing contract, the factory that adopts an instance GStreamer created —
see [subclassing](subclassing.md).

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

## GST0003

**Overridden vfunc is not declared in DefineSubclass.**

A class that derives from a subclassable binding class overrides `On<X>`, but
the `overrides` argument of its `DefineSubclass` call does not name
`<X>Override`. Only declared slots are patched into the class structure, so
the override is dead code and GStreamer keeps calling the implementation of
the base class.

Fix: add the declaration, or delete the override.

```csharp
internal sealed class MySource : PushSrc
{
    private static readonly SubclassType Definition =
        DefineSubclass("MySource", ConfigureClass, CreateOverride, StartOverride);

    protected override FlowReturn OnCreate(out Gst.Buffer? buffer) { ... }  // ok
    protected override bool OnStart() { ... }                              // ok
    protected override bool OnStop() { ... }                               // GST0003
}
```

## GST0004

**Declared vfunc slot is not overridden.**

The converse: `<X>Override` appears in the `overrides` argument while neither
the class nor a class between it and the wrapped base overrides `On<X>`. The
slot is patched all the same, so the element pays a managed transition that
only chains up — and on the slots GStreamer reads for presence, such as
`BaseSrc.alloc`, `BaseSrc.fill` or `BaseTransform.transform_ip`, a slot that
exists changes what the base class does, even when the implementation behind
it does nothing of its own.

Fix: override the method, or drop the declaration.

```csharp
internal sealed class MySink : BaseSink
{
    private static readonly SubclassType Definition =
        DefineSubclass("MySink", ConfigureClass, RenderOverride, PrepareOverride);  // GST0004 on PrepareOverride

    protected override FlowReturn OnRender(Gst.Buffer buffer) { ... }
}
```

Both rules pair a declaration with an override by name stem alone —
`<Stem>Override` against `On<Stem>` — because the class and the offset a
`VfuncOverride` carries only resolve at run time. They therefore only read an
`overrides` argument written out at the call site, as separate arguments, an
array creation or a collection expression of plain property references.
Anything else — a local, a helper call, a spread — silences both directions
rather than guessing, and a `DefineSubclass` call outside a class that derives
from the class it registers against, such as the negative registration tests,
is never looked at.

The stem rule is what makes the pairing open-ended: it covers every slot a
subclassable class declares, including the two `Gst.GObject.Object` itself
contributes — `SetPropertyOverride` with `OnSetProperty(uint, ValueView,
ParamSpec)` and `GetPropertyOverride` with `OnGetProperty(uint, ValueRef,
ParamSpec)`, which every class that installs a property has to declare and
override together.

The override may live in a base class between the declaring class and the
wrapped base: the search walks that stretch of the hierarchy, so an
intermediate abstract class carrying the implementation pairs with a leaf that
declares the slot. Because the pairing is by name stem, a slot whose managed
name hides a parent slot of the same stem — `AudioSink.PrepareOverride` over
`BaseSink.PrepareOverride` — is paired by that stem, and either `OnPrepare`
satisfies either declaration.

## GST0005

**`CreateWrapper` ignores its `SubclassCtorArgs`.**

An implementation of `IManagedSubclass<TSelf>.CreateWrapper` never reads the
`args` parameter. That parameter is the instance GStreamer just created, on its
way into the constructor: it carries the handle and how ownership is
transferred. A wrapper built any other way does not adopt that instance, so the
fabrication either fails or hands out a wrapper of a different handle.

Fix: pass `args` to the constructor that takes it.

```csharp
internal sealed class MySource : PushSrc, IManagedSubclass<MySource>
{
    public MySource() : this(Definition.NewInstance()) { }

    private MySource(SubclassCtorArgs args) : base(args) { }

    public static MySource CreateWrapper(SubclassCtorArgs args) => new MySource(args);  // ok
    // public static MySource CreateWrapper(SubclassCtorArgs args) => new MySource();   // GST0005
}
```

The rule is syntactic: any reference to the parameter — passed to the
constructor, forwarded to a helper, copied into a local, or only read from —
silences it. It fires on the implementation the type contributes for the
interface member, whether it was written implicitly or as an explicit
implementation, and says nothing about a method named `CreateWrapper` on a type
that does not implement `IManagedSubclass<TSelf>`.
