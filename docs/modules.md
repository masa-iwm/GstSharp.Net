# Binding modules

How to bind a GStreamer library that this repository does not cover, from an
assembly of your own, with no access to the internals of `GstSharp.Net`.

The audience is somebody who wants `libgstcontroller`, `libgstinsertbin`, a
library out of `gst-plugins-bad`, or an in-house library built on GStreamer, and
who would rather ship a package than wait for one. The runtime carries a small
extension surface — a **module SPI** — for exactly that, and this page is the
contract that comes with it.

`src/GstSharp.Net.Controller` is the worked example. It is a real shipping
module, it binds the useful heart of `libgstcontroller-1.0`, its wrappers derive
from the generated `Gst.ControlSource`, and **it is built with no
`InternalsVisibleTo` in either direction**: nothing grants it the internals of
`GstSharp.Net`, and it grants nothing to anybody. That it compiles is what
certifies that the surface below is complete; if a change to the runtime or to
the generator ever closes part of it, that project stops building.

## What a module is, and what it is not

A module is an ordinary assembly that references the `GstSharp.Net` package,
declares its own `[LibraryImport]` stubs against a native library, wraps the
types of that library in C# classes, and tells the runtime about both.

**Hand-written modules are the supported shape.** Everything on this page is
about writing the C# by hand, the way `src/GstSharp.Net/Core/Gio` and the
`Custom/` directories of this repository are written.

**Generator-backed modules are not supported yet.** The generator
(`generator/GstSharp.Generator`) is a tool of this repository, not a product: it
is not packaged, its `girs/` inputs and overlay format are not a public
interface, and it emits code that uses internals. Running it against your own
`.gir` is not a path that works today, and pointing it at a library and shipping
the result is not something this contract covers. That may change; until it
does, a module is written the way the example is.

## The contract

A module does three things at start-up and then stays out of the way. All three
belong in the same `[ModuleInitializer]`, which is the whole of
[`GstControllerModule.cs`](../src/GstSharp.Net.Controller/GstControllerModule.cs):

```csharp
[ModuleInitializer]
internal static void Initialize()
{
    // 1. Route the [LibraryImport] stubs of this assembly through the loader.
    NativeLoader.EnsureRegistered(typeof(GstControllerModule).Assembly);

    // 2. Teach the loader what the library is called on each platform.
    NativeLoader.RegisterLibrary("GstController", new NativeLibraryNames
    {
        Linux = "libgstcontroller-1.0.so.0",
        MacOs = "libgstcontroller-1.0.0.dylib",
        WindowsMsvc = "gstcontroller-1.0-0.dll",
        WindowsMinGW = "libgstcontroller-1.0-0.dll",
    });

    // 3. Hand the GType-to-wrapper table over.
    TypeRegistry.RegisterModule(new NativeModule("GstController", CreateEntries()));
}
```

**Use a module initialiser, not a static constructor and not an `Initialize()`
that callers have to remember.** A module initialiser runs before the first call
into its assembly, which is before any stub of that assembly can ask the loader
to resolve anything; under NativeAOT every module initialiser has run before the
entry point does. That is what makes the ordering rule below unbreakable rather
than merely documented.

Ship a public `Initialize()` as well — see
[`GstController.cs`](../src/GstSharp.Net.Controller/GstController.cs) — for the
same reason every module in this repository does: naming one of your types in a
cast is not a *call*, so an application that only ever writes
`something as YourType` never runs your initialiser and the cast is silently
`null`. See
[the GType registry](ownership.md#the-gtype-registry).

### 1. The native library name

`NativeLoader.RegisterLibrary` adds one logical name to the name space of the
loader. The logical name is what your `[LibraryImport("GstController", …)]`
stubs say and what your `NativeModule` carries; the four file names are the four
spellings the same library has on the platforms the binding runs on. The Windows
pair differs only in whether the `lib` prefix survived — the MSVC build drops it,
the MinGW build keeps it.

Four rules, all enforced:

* **The built-in names cannot be shadowed.** `Gst`, `GLib`, `GObject`,
  `GstBase`, `GES` and the rest of the table the runtime ships are refused, so
  no module can redirect a core library somewhere else.
* **Registering the same name with the same four file names again does
  nothing**, which two assemblies that both import from one library need.
  Registering it with *different* file names throws: one logical name is one
  library in a process.
* **Every name is a bare file name.** A name that carried a directory is
  refused, because the directory is the loader's business.
* **Register before the first call through the name.** A name that is still
  unknown when a stub resolves is left to the default resolution of the runtime,
  which normally fails that call; nothing negative is cached, so registering
  afterwards works for the next call, but the call that raced is lost. The
  module initialiser makes this impossible to get wrong.

**Your library is resolved out of the same installation as the core
libraries.** That is the point of registering it rather than letting the
operating system find it. On Windows the first module that loads pins a
directory and a flavor, and a registered name is then loaded from that directory
and with that flavor and from nowhere else — mixing an MSVC GLib with a MinGW
one inside a process does not end well. On Linux and macOS there is no handle to
pin, so what holds is that a registered name takes the same ordered walk of
candidate directories that every other module takes, and lands in the same
installation whenever that installation provides it.

**You may import from the built-in names too.** `[LibraryImport("Gst", …)]` in
your assembly resolves like any other, once `EnsureRegistered` has run for it.
A module that binds a library built on top of GStreamer usually needs an entry
point or two out of the core library, and nothing stops it.

### 2. The type table

`ModuleTypeEntry` pairs the `get_type` function of a native type with the
factory that builds its managed wrapper. Both are plain function pointers, so
the registry stays free of reflection and survives trimming and ahead-of-time
compilation:

```csharp
private static ModuleTypeEntry[] CreateEntries() =>
[
    new ModuleTypeEntry(&InterpolationControlSource.GetGType,
                        &InterpolationControlSource.CreateWrapper),
    new ModuleTypeEntry(&TimedValueControlSource.GetGType,
                        &TimedValueControlSource.CreateWrapper),
];
```

`GetGType` is your own `[LibraryImport]` stub returning `nuint`;
`CreateWrapper` is a static method of the shape
`static object CreateWrapper(nint handle, Transfer transfer)`. Neither has to be
public — they are yours, and only your own module initialiser takes their
address.

Nothing is resolved when you register. The `get_type` functions are called when
the registry is frozen, which happens the first time a wrapper is needed and
therefore after the native libraries are loaded. Registering after the registry
was frozen unfreezes it and the next lookup rebuilds the table, so *order* does
not matter; what matters is that registration precedes the first wrap of one of
your types, because a wrapper keeps the type it was built with.

**Know what registering costs everybody else.** The freeze resolves the
`get_type` of *every* registered entry, which loads your native library — so
once your module has registered, your library is load-bearing for every wrapper
lookup in the process, not only for the calls that go through your types. If it
cannot be found, the freeze fails for everyone. That is not new (the `GstWebRTC`
and `GES` modules of this repository behave the same way and the CI workflow
installs those libraries for exactly that reason), but it is the reason a module
should only be *referenced* by an application that actually has the library.

Two entries worth thinking about before you add them:

* **An abstract native type is worth registering.** No instance of
  `GstTimedValueControlSource` exists, but a type that derives from it and has no
  binding — `GstTriggerControlSource` — is then wrapped through your class
  instead of falling back further.
* **Do not register a type whose wrapper you did not write.** An entry replaces
  whatever the registry would otherwise have chosen for that `GType`, including
  a perfectly good wrapper from `GstSharp.Net`. The example binds
  `gst_direct_control_binding_new` as a factory returning the existing
  `Gst.ControlBinding` and deliberately registers no entry for
  `GstDirectControlBinding`, so that `Gst.Object.GetControlBinding` keeps
  answering what it always answered.

### 3. The wrapper classes

A wrapper derives from one of the runtime base classes, whose `(nint, Transfer)`
constructors are `protected` for this purpose:

| Base | For | The wrapper owns |
| --- | --- | --- |
| `Gst.GObject.Object` | a `GObject` | one reference, shared by the process, through a toggle reference |
| `Gst.GObject.InitiallyUnowned` | a `GObject` with floating references — **every `GstObject`** | the same |
| `Gst.MiniObject` | a `GstMiniObject`: buffers, events, caps and relatives | a reference of its own |
| `Gst.GObject.Boxed` | a boxed value | a copy of its own |

Which one you pick follows from the C type and from nothing else, and it decides
the ownership rule your users live under — see
[`docs/ownership.md`](ownership.md), which applies to your wrappers unchanged.

**Or from a generated class**, when the native type derives from one that this
repository already binds. `Gst.Element`, `Gst.Object`, `Gst.ControlSource`,
`Gst.Bin` and every other class under `src/*/Generated/` carries the same
`protected (nint, Transfer)` constructor, and it is the only part of a generated
class that is open. Deriving from the right one is what keeps your managed
hierarchy shaped like the native one — see
[Attaching to the generated hierarchy](#attaching-to-the-generated-hierarchy)
below, which is the interesting half of this page.

The obligations, which the XML documentation on each constructor states as well:

* **`FromNative` is the supported wrap path for a `GObject`.** GObject wrappers
  are interned: there is exactly one per native object, and it holds the toggle
  reference the whole lifetime rests on. Constructing a second wrapper for a
  handle that already has a live one **throws**, because two toggle references on
  one object suspend each other's toggling. So: keep your constructor out of your
  public surface, expose it to the registry as `CreateWrapper`, and write your
  own factories through `Gst.GObject.Object.FromNative<T>`:

  ```csharp
  public static InterpolationControlSource New() =>
      Object.FromNative<InterpolationControlSource>(
          GstInterpolationControlSourceNew(), Transfer.Full) ?? throw …;
  ```

  That call goes out to the registry, which comes back through your own
  `CreateWrapper`. It is a round trip on purpose: it is the one place that knows
  whether a wrapper exists already.
* **Mini objects and boxed values are not interned**, so a second wrapper is
  legal there and simply means a second reference or a second copy. What they
  impose instead is that **every wrapper you hand out has to be disposed by
  whoever received it**, which is why a module must not expose an owning wrapper
  from a *property*: a property produces one per read in the one place the
  `GST0001` analyzer cannot watch. Name such a member `GetSomething()`.
* **Pass the transfer the C function documented.** `transfer full` means the call
  handed a reference over and the wrapper adopts it; anything else means the
  wrapper takes one of its own, or — for a boxed value — copies. Getting it wrong
  leaks or double-frees, and no diagnostic will tell you.
* **Read `Handle` once and keep the wrapper alive.** `Handle` is public on all
  four bases and throws once the wrapper is disposed. Reading it is often the
  last use of the wrapper in a method, so the collector is free to finalize it
  while your native call is still running; every wrapper in this repository ends
  such a method with `GC.KeepAlive(this)`, and so should yours.

Everything else a module needs is already public: `GMarshal` and `Utf8Scope` for
UTF-8 parameters, `GException.ThrowIfSet` for a `GError**` out parameter,
`Gst.GObject.Value` and `GType` for properties and `GValue`s, `Transfer`,
`GstNativeLoadException`.

## Attaching to the generated hierarchy

**Derive from the generated wrapper of the nearest native ancestor**, and the
managed hierarchy of your module follows the native one.
`Gst.Controller.TimedValueControlSource` derives from `Gst.ControlSource`, which
is a `Gst.Object`, which is a `Gst.GObject.InitiallyUnowned` — the same chain
`GstTimedValueControlSource` has in C. It costs no `InternalsVisibleTo`: the
`(nint, Transfer)` constructor is `protected` and is the whole of what a
generated class opens.

What that buys, concretely:

* Methods of the generated ancestors are inherited. The example does not bind
  `gst_control_source_get_value` any more; it calls the
  `Gst.ControlSource.ControlSourceGetValue` it inherited, and `Name`, `Parent`
  and `SyncValues` arrive from `Gst.Object` the same way.
* Generated API that *takes* one of those ancestor types takes your wrapper.
  `GES.TrackElement.SetControlSource(Gst.ControlSource, …)` is the live example
  — a control source built by this module drives a property of a GES track
  element, across three assemblies and no grant of internals, which
  [`tests/GstSharp.IntegrationTests/GesControlSourceTests.cs`](../tests/GstSharp.IntegrationTests/GesControlSourceTests.cs)
  asserts end to end.
* Generated API that *returns* one of them still returns the generated wrapper
  rather than yours, because the registry decides that and the registry answers
  by `GType`. Register an entry for your own type when you want your wrapper
  back — and do not register one for a `GType` whose wrapper you did not write.

Two things to get right when you attach there:

* **Pick the ancestor the native type actually has.** Deriving from
  `Gst.Element` a type that is not a `GstElement` compiles and then hands every
  inherited method a handle of the wrong shape.
* **The generated class may not be the one that carries the constructor
  function.** Binding a constructor function as a factory that returns the
  existing generated wrapper is still the right shape for a type that adds no
  methods of its own — `DirectControlBinding` in the example does exactly that
  and stays a `Gst.ControlBinding`.

**Managed subclassing is separate and is closed to modules.**
`DefineSubclass` — deriving a *new* `GType` from `Gst.Element` and friends in C#
— is public and documented in [`docs/subclassing.md`](subclassing.md#11-using-it-stage-1),
but its closed set of base classes is the one that ships in `GstSharp.Net` and
`GstSharp.Net.Base`. A module cannot add a base class to that set.

## What stays closed, and why

* **The internals of the generated assemblies**, which is everything about a
  generated class except its `(nint, Transfer)` constructor: `CreateWrapper`,
  `GetGType`, the class-struct mirrors. They are regenerated on every gir refresh
  and are not a surface anybody can promise anything about yet. The constructor
  is the deliberate exception, because attaching to the hierarchy needs one
  member and no more.
* **The generator.** It is a tool, not a product; see above.
* **The name table of the core libraries.** `RegisterLibrary` cannot shadow it,
  because one process holds one GStreamer installation and the loader is what
  keeps it that way.
* **The vfunc-marshalling seams** — `Borrowed`, the `MiniObject(Borrowed)`
  constructor, the trampolines. A borrowed wrapper owns nothing and is only
  correct for the length of one native call; handing that out would be handing
  out a footgun with no way to check it.

## The worked example

[`src/GstSharp.Net.Controller`](../src/GstSharp.Net.Controller) binds the part of
`libgstcontroller-1.0` that most applications want: a control source whose timed
values drive a property of an element over stream time.

| File | What it shows |
| --- | --- |
| [`GstSharp.Net.Controller.csproj`](../src/GstSharp.Net.Controller/GstSharp.Net.Controller.csproj) | The proof: one `ProjectReference`, no `InternalsVisibleTo`. |
| [`GstControllerModule.cs`](../src/GstSharp.Net.Controller/GstControllerModule.cs) | The three registration calls, in a module initialiser. |
| [`GstController.cs`](../src/GstSharp.Net.Controller/GstController.cs) | The public `Initialize()` a module owes its users. |
| [`TimedValueControlSource.cs`](../src/GstSharp.Net.Controller/TimedValueControlSource.cs) | A wrapper class deriving from the *generated* `Gst.ControlSource`: the protected constructor, `GetGType` and `CreateWrapper`, a `Concrete` for the abstract type. |
| [`InterpolationControlSource.cs`](../src/GstSharp.Net.Controller/InterpolationControlSource.cs) | A factory through `FromNative`, and a `GValue` property round trip. |
| [`DirectControlBinding.cs`](../src/GstSharp.Net.Controller/DirectControlBinding.cs) | Binding a constructor function as a factory that returns the *generated* wrapper. |

Using it:

```csharp
using Gst;
using Gst.Controller;

GstController.Initialize();

Element volume = (Element)ElementFactory.Make("volume", "controlled")!;

InterpolationControlSource source = InterpolationControlSource.New();
source.Mode = InterpolationMode.Linear;
source.Set(ClockTime.Zero, 0.2);
source.Set(ClockTime.FromSeconds(1), 0.8);

volume.AddControlBinding(DirectControlBinding.NewAbsolute(volume, "volume", source));
```

From then on `gst_object_sync_values` — which every controllable element calls
once per buffer while it runs — walks the property from `0.2` to `0.8` over the
first second. `DirectControlBinding.New` instead of `NewAbsolute` reads the same
numbers as a fraction of the range the property declares, so on `volume`, which
runs from 0 to 10, they would be `2.0` and `8.0`.

The bound property has to be writable, controllable
(`GST_PARAM_CONTROLLABLE`) and not construct-only. That is a decision of whoever
wrote the element; a binding to a property that is not marked so is built, logs
a warning, and then does nothing.

[`tests/GstSharp.IntegrationTests/ControllerModuleTests.cs`](../tests/GstSharp.IntegrationTests/ControllerModuleTests.cs)
asserts all of it against the installed library, and
[`GesControlSourceTests.cs`](../tests/GstSharp.IntegrationTests/GesControlSourceTests.cs)
asserts the crossing the hierarchy makes possible: the same source handed to
`GES.TrackElement.SetControlSource`, which takes a `Gst.ControlSource`.

## Checklist

1. `ProjectReference` or `PackageReference` to `GstSharp.Net`, and nothing that
   asks for internals.
2. A `[ModuleInitializer]` with `EnsureRegistered`, `RegisterLibrary` and
   `RegisterModule`.
3. A public `Initialize()` that forwards to `GstSharp.Initialize`.
4. One wrapper class per native type that carries methods, derived from the
   generated wrapper of its nearest bound ancestor — or from the right runtime
   base when it has none — with a `CreateWrapper` for the registry and public
   factories that go through `FromNative`.
5. Ownership documented for anything that is a `MiniObject` or a `Boxed`, and no
   owning wrapper behind a property.
6. `GC.KeepAlive` after the last read of `Handle` in every method that calls
   native code.
7. `IsAotCompatible` on, and a publish that produces no trimming or AOT
   warnings — the runtime is reflection-free and a module has no reason not to
   be.
