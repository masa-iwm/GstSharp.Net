using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// One vfunc slot a managed subclass takes over: where it sits in the class
/// struct, and the unmanaged function that is written there.
/// </summary>
/// <param name="Offset">
/// The byte offset of the slot within the class struct, for example
/// <c>ElementClassRaw.ChangeStateOffset</c>.
/// </param>
/// <param name="Function">
/// The address of an <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/>
/// static that implements the slot.
/// </param>
/// <remarks>
/// Only the declared slots are patched. Patching every slot with a default that
/// chains up would be wrong rather than merely slow: GStreamer reads slot
/// <em>presence</em> — a non-null <c>request_new_pad</c> means "has request
/// pads", a non-null <c>BaseTransform.transform_ip</c> selects in-place
/// operation — so a blanket patch would change what every managed subclass is.
/// See <c>docs/subclassing.md</c> §4.2.
/// </remarks>
internal readonly record struct VfuncSlot(int Offset, nint Function);

/// <summary>
/// One managed subclass of a wrapped GObject type: what to register, which
/// slots to take over, and the parent class its overrides chain up through.
/// </summary>
/// <remarks>
/// A descriptor is registered exactly once and then lives for the process:
/// static <c>GType</c>s cannot be unregistered, so there is no unload path and
/// nothing to release. See <c>docs/subclassing.md</c> §8.
/// </remarks>
internal sealed class SubclassDescriptor
{
    private readonly VfuncSlot[] _slots;
    private readonly InterfaceImplementation[] _interfaces;
    private nint _parentClass;
    private nuint _registeredType;
    private Exception? _classInitFailure;

    /// <summary>
    /// Describes a managed subclass.
    /// </summary>
    /// <param name="parentType">
    /// The type to derive from. It has to be a native, binding-wrapped type:
    /// deriving a managed subclass from another managed subclass is not
    /// supported yet, see <c>docs/subclassing.md</c> §4.4.
    /// </param>
    /// <param name="typeName">
    /// The <c>GType</c> name, which has to be unique in the process. There is
    /// no derivation from the CLR name, see §3.5.
    /// </param>
    /// <param name="slots">The vfunc slots the subclass takes over.</param>
    /// <param name="classInitializer">
    /// Runs at the end of <c>class_init</c>, with the facade over the class
    /// that is being initialised, for the class level configuration that
    /// GStreamer needs (metadata, pad templates). See §5.5.
    /// </param>
    /// <param name="wrapFactory">
    /// Builds the wrapper of an instance native code created, or
    /// <see langword="null"/> for a subclass that is only ever constructed from
    /// managed code. See §5.4.
    /// </param>
    /// <param name="interfaces">
    /// The GObject interfaces the subclass implements, which are attached
    /// between the registration of the type and the reference to its class.
    /// See §5.7.
    /// </param>
    internal SubclassDescriptor(
        GType parentType,
        string typeName,
        VfuncSlot[] slots,
        Action<ObjectClassConfig>? classInitializer = null,
        Func<SubclassCtorArgs, Object>? wrapFactory = null,
        InterfaceImplementation[]? interfaces = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(slots);

        ParentType = parentType;
        TypeName = typeName;
        _slots = slots;
        ClassInitializer = classInitializer;
        WrapFactory = wrapFactory;
        _interfaces = interfaces ?? [];
    }

    /// <summary>Gets the type the subclass derives from.</summary>
    internal GType ParentType { get; }

    /// <summary>Gets the <c>GType</c> name the subclass is registered under.</summary>
    internal string TypeName { get; }

    /// <summary>Gets the hook that runs at the end of <c>class_init</c>.</summary>
    internal Action<ObjectClassConfig>? ClassInitializer { get; }

    /// <summary>
    /// Gets the factory that builds the wrapper of an instance native code
    /// created, or <see langword="null"/> when the subclass was defined without
    /// one.
    /// </summary>
    internal Func<SubclassCtorArgs, Object>? WrapFactory { get; }

    /// <summary>Gets the vfunc slots the subclass takes over.</summary>
    internal ReadOnlySpan<VfuncSlot> Slots => _slots;

    /// <summary>Gets the GObject interfaces the subclass implements.</summary>
    internal ReadOnlySpan<InterfaceImplementation> Interfaces => _interfaces;

    /// <summary>
    /// Gets the type the subclass was registered as, or
    /// <see cref="GType.Invalid"/> before <see cref="SubclassRegistry.Register"/>
    /// ran.
    /// </summary>
    internal GType RegisteredType => new(Volatile.Read(ref _registeredType));

    /// <summary>
    /// Gets the class struct of the parent type, captured inside
    /// <c>class_init</c>, or <see cref="nint.Zero"/> before the class was
    /// initialised.
    /// </summary>
    /// <remarks>
    /// This, and not the instance's own class, is what an override chains up
    /// through: the instance's slot holds the trampoline, so reading it back
    /// would call the trampoline again.
    /// </remarks>
    internal nint ParentClass => Volatile.Read(ref _parentClass);

    /// <summary>
    /// Gets what <c>class_init</c> threw, or <see langword="null"/> when it
    /// succeeded.
    /// </summary>
    /// <remarks>
    /// <c>class_init</c> is called by GObject, so an exception cannot leave it;
    /// it is caught, kept here, and rethrown by
    /// <see cref="SubclassRegistry.Register"/>. Without that, a class
    /// initialiser that failed would leave a registered type whose metadata and
    /// pad templates are missing, and the first symptom would be a
    /// <c>g_return_if_fail</c> deep inside <c>g_object_new</c>.
    /// </remarks>
    internal Exception? ClassInitFailure => Volatile.Read(ref _classInitFailure);

    internal void SetRegisteredType(GType type) => Volatile.Write(ref _registeredType, type.Value);

    /// <summary>Remembers what <c>class_init</c> threw.</summary>
    /// <param name="failure">The exception the class initialiser raised.</param>
    internal void CaptureClassInitFailure(Exception failure) =>
        Interlocked.CompareExchange(ref _classInitFailure, failure, null);

    /// <summary>
    /// Remembers the parent class of the first class that was initialised from
    /// this descriptor.
    /// </summary>
    /// <param name="parentClass">The result of <c>g_type_class_peek_parent</c>.</param>
    /// <remarks>
    /// The first capture wins. GObject builds a derived class by copying the
    /// parent's class struct and by running every ancestor's <c>class_init</c>
    /// on it, so a descriptor whose type was derived from would see its own
    /// <c>class_init</c> run a second time, with a parent class whose slot is
    /// already the trampoline. Registration refuses a managed parent for that
    /// reason (§4.4); keeping the first capture makes the rule hold even if
    /// something else ever derives from a managed type.
    /// </remarks>
    internal void CaptureParentClass(nint parentClass) =>
        Interlocked.CompareExchange(ref _parentClass, parentClass, nint.Zero);
}

/// <summary>
/// The handle of a freshly constructed instance of a managed subclass, on its
/// way into the wrapper's constructor.
/// </summary>
/// <remarks>
/// The indirection is what keeps a subclass from wrapping arbitrary handles:
/// the only way to obtain one is
/// <see cref="SubclassRegistry.NewInstance(GType)"/>, so the handle never
/// appears in the subclass's own code. See <c>docs/subclassing.md</c> §5.3,
/// option (b).
/// </remarks>
public readonly struct SubclassCtorArgs
{
    private readonly bool _adopted;

    internal SubclassCtorArgs(nint handle)
    {
        Handle = handle;
        _adopted = false;
    }

    private SubclassCtorArgs(nint handle, bool adopted)
    {
        Handle = handle;
        _adopted = adopted;
    }

    /// <summary>Gets the new instance.</summary>
    internal nint Handle { get; }

    /// <summary>
    /// Gets whether the instance is one native code built and still owns.
    /// </summary>
    internal bool IsAdopted => _adopted;

    /// <summary>
    /// Gets how ownership is transferred: <c>g_object_new</c> hands its
    /// reference to the caller, and a floating one is sunk by the wrapper's
    /// constructor, while an adopted instance belongs to whoever created it and
    /// is only referenced by the wrapper.
    /// </summary>
    internal Transfer Transfer => _adopted ? Transfer.None : Transfer.Full;

    /// <summary>
    /// Wraps an instance native code created and still owns, for the factory of
    /// <see cref="IManagedSubclass{TSelf}"/>.
    /// </summary>
    /// <param name="handle">The instance to adopt.</param>
    /// <returns>The arguments of the wrapper's constructor.</returns>
    /// <remarks>
    /// The wrapper takes a reference of its own and sinks nothing: the
    /// instance may still be floating, and whoever created it — a factory, a
    /// base class building its pad — is the one that sinks it. See
    /// <c>docs/subclassing.md</c> §5.4 and <c>docs/ownership.md</c>.
    /// </remarks>
    internal static SubclassCtorArgs Adopt(nint handle) => new(handle, adopted: true);

    /// <summary>
    /// Gets the new instance, once it is known to be of the type the wrapper
    /// stands for.
    /// </summary>
    /// <param name="requiredType">The type of the base class being constructed.</param>
    /// <returns>The new instance.</returns>
    /// <remarks>
    /// <para>
    /// The registration and the wrapper are two independent statements — one
    /// says which native class to derive from, the other which managed class
    /// wraps it — and handing the arguments of one registration to the
    /// constructor of an unrelated wrapper would build a wrapper whose vfunc
    /// dispatch never matches. The check costs one <c>g_type_is_a</c> per
    /// construction and turns that into a message at the point of the mistake.
    /// </para>
    /// <para>
    /// The instance is destroyed on the way out. Nothing has wrapped it yet, so
    /// throwing without releasing it would leak an object nobody can reach; the
    /// vfuncs that run during its teardown find no wrapper and chain up, which
    /// is the same window as any other unwrapped instance.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The instance is not of <paramref name="requiredType"/>.
    /// </exception>
    internal nint HandleFor(nuint requiredType)
    {
        GType actual = TypeRegistry.GetInstanceType(Handle);

        if (!actual.IsA(new GType(requiredType)))
        {
            if (_adopted)
            {
                // An adopted instance belongs to native code, which is still
                // holding it: destroying it here would pull it out from under
                // its owner. Nothing was taken, so nothing is given back.
                throw new ArgumentException(
                    $"An instance of {actual.Name} cannot be wrapped as {new GType(requiredType).Name}. The " +
                    "factory of the managed subclass was handed an instance of an unrelated type.",
                    nameof(requiredType));
            }

            if (GObjectNative.ObjectIsFloating(Handle) != 0)
            {
                // g_object_new hands out a floating reference for anything
                // derived from GInitiallyUnowned, which every element is;
                // sinking it first is what makes the unref below the last one.
                GObjectNative.ObjectRefSink(Handle);
            }

            GObjectNative.ObjectUnref(Handle);

            throw new ArgumentException(
                $"An instance of {actual.Name} cannot be wrapped as {new GType(requiredType).Name}. The " +
                "registration these arguments came from derives from a different class than the wrapper being " +
                "constructed.",
                nameof(requiredType));
        }

        if (_adopted)
        {
            // The constructor of Gst.GObject.Object is reached through the
            // constructor chain of the subclass, which cannot carry an extra
            // argument, so the one bit it needs travels on the thread. It is
            // consumed and cleared there unconditionally: nothing else can run
            // between the evaluation of this argument and that constructor.
            Object.ArmAdopt(Handle);
        }

        return Handle;
    }
}

/// <summary>
/// Registers managed subclasses of wrapped GObject types with GObject, and
/// creates their instances.
/// </summary>
/// <remarks>
/// <para>
/// The registry itself stays internal. What applications use is the surface
/// stage 1 of <c>docs/subclassing.md</c> puts on the base classes —
/// <c>DefineSubclass</c>, <see cref="SubclassType"/>, <see cref="ClassConfig"/>
/// and the <c>OnX</c> virtuals — so that a subclass never names a descriptor,
/// a slot offset or a handle. Registering through a base class is also what
/// keeps a managed parent inexpressible: the parent is always the
/// <c>GType</c> of the wrapped native class.
/// </para>
/// <para>
/// NativeAOT rules out per-type code generation, so all managed subclasses
/// share one <c>class_init</c> and one <c>instance_init</c>. The
/// discriminator is the <c>class_data</c> pointer of
/// <see cref="GTypeInfo"/>, which carries an identifier from a counter into
/// <see cref="Descriptors"/> — never a <see cref="System.Runtime.InteropServices.GCHandle"/>,
/// following the same doctrine as the toggle references of
/// <see cref="Object"/>.
/// </para>
/// </remarks>
internal static unsafe class SubclassRegistry
{
    private static readonly object Sync = new();

    /// <summary>The registered subclasses, keyed by their <c>class_data</c> identifier.</summary>
    private static readonly ConcurrentDictionary<nint, SubclassDescriptor> Descriptors = new();

    /// <summary>The registered subclasses, keyed by the type they were registered as.</summary>
    private static readonly ConcurrentDictionary<nuint, SubclassDescriptor> ByType = new();

    /// <summary>
    /// The long lived wrappers of the specifications managed subclasses
    /// installed, keyed by the type that installed one and the specification
    /// itself.
    /// </summary>
    /// <remarks>
    /// A <c>GParamSpec</c> is never removed from a class once it is installed,
    /// and <see cref="ParamSpec"/> has no finalizer, so the property slots
    /// cannot afford to wrap the specification anew on every call — each wrap
    /// would take a reference nothing gives back. The table is filled by
    /// <see cref="ObjectClassConfig.InstallProperty"/> while the class is being
    /// initialised and read by the two property slots afterwards; it stays
    /// writable for the life of the process because installing a property after
    /// <c>class_init</c> is legal.
    /// </remarks>
    private static readonly ConcurrentDictionary<(nuint Owner, nint Spec), ParamSpec> InstalledSpecs = new();

    /// <summary>
    /// The interface implementations of the managed subclasses, keyed by the
    /// implementing type and the interface.
    /// </summary>
    /// <remarks>
    /// The table is filled by <see cref="Register"/> before the interface is
    /// attached, so that everything that runs later - <c>interface_init</c>,
    /// and the slots that are asked about a type rather than about an instance,
    /// such as the <c>get_type</c> and <c>get_protocols</c> of
    /// <c>GstURIHandler</c> - can find the implementation from the two types
    /// alone. Those slots run on whatever thread registers an element, outside
    /// the lock the registration holds.
    /// </remarks>
    private static readonly ConcurrentDictionary<(nuint Instance, nuint Interface), InterfaceImplementation>
        Interfaces = new();

    private static long _nextClassDataId;

    /// <summary>
    /// The types whose instances are being constructed on this thread, innermost
    /// last.
    /// </summary>
    /// <remarks>
    /// <c>g_object_new</c> returns into the constructor of the wrapper, which
    /// is where the toggle reference is installed; a wrapper fabricated for the
    /// same instance in between would install a second one and the constructor
    /// would refuse to wrap an object that is wrapped already. Every slot that
    /// fires while a type is on this stack therefore finds no wrapper and
    /// chains up, which is the construction window rule of
    /// <c>docs/subclassing.md</c> §4.1 unchanged. <c>g_object_new</c> is
    /// synchronous, so the window belongs to one thread and so does the stack.
    /// </remarks>
    [ThreadStatic]
    private static List<nuint>? t_constructing;

    /// <summary>
    /// Registers a managed subclass with GObject.
    /// </summary>
    /// <param name="descriptor">The subclass to register.</param>
    /// <returns>The new type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="descriptor"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The parent type is invalid, or the type name is not a legal
    /// <c>GType</c> name.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The parent type is itself a managed subclass.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The descriptor was registered already, the type name is taken, the
    /// parent type has no class, or GObject refused the registration.
    /// </exception>
    internal static GType Register(SubclassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        GType parent = descriptor.ParentType;
        if (!parent.IsValid)
        {
            throw new ArgumentException(
                "A managed subclass needs a valid parent type.",
                nameof(descriptor));
        }

        ValidateTypeName(descriptor.TypeName);

        lock (Sync)
        {
            if (descriptor.RegisteredType.IsValid)
            {
                throw new InvalidOperationException(
                    $"\"{descriptor.TypeName}\" is registered already, as {descriptor.RegisteredType.Name}. " +
                    "A subclass descriptor is registered exactly once and lives for the process.");
            }

            // Managed-derives-from-managed is the one shape the single level
            // chain-up cannot serve: the parent's slot would hold the shared
            // trampoline, so chaining up would call back into itself. See
            // docs/subclassing.md §4.4.
            if (ByType.TryGetValue(parent.Value, out SubclassDescriptor? managedParent))
            {
                throw new NotSupportedException(
                    $"\"{descriptor.TypeName}\" derives from \"{managedParent.TypeName}\", which is a managed " +
                    "subclass itself. Deriving a managed subclass from another managed subclass is not " +
                    "supported: derive from the wrapped native type instead.");
            }

            Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
            using Utf8Scope name = GMarshal.StackUtf8(descriptor.TypeName, buffer);

            if (GObjectNative.TypeFromName(name.Pointer) != GType.InvalidValue)
            {
                throw new InvalidOperationException(
                    $"The type name \"{descriptor.TypeName}\" is taken already. GType names are unique per " +
                    "process, so registering one twice is a caller bug.");
            }

            // The sizes come from the library that is loaded, not from the gir
            // and not from a constant: a managed subclass adds no native bytes,
            // and taking the parent's sizes at run time is what makes the
            // registration layout agnostic. See §3.2.
            GObjectNative.TypeQuery(parent.Value, out GTypeQuery query);
            if (query.Type == GType.InvalidValue)
            {
                throw new InvalidOperationException(
                    $"{parent.Name} has no class, so nothing can derive from it.");
            }

            if (query.ClassSize > ushort.MaxValue || query.InstanceSize > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"{parent.Name} is too large to derive from: GTypeInfo spells both sizes as guint16, and " +
                    $"the class is {query.ClassSize} and the instance {query.InstanceSize} bytes.");
            }

            nint classData = NextClassDataId();
            Descriptors[classData] = descriptor;

            GTypeInfo info = default;
            info.ClassSize = (ushort)query.ClassSize;
            info.ClassInit = &ClassInit;
            info.ClassData = classData;
            info.InstanceSize = (ushort)query.InstanceSize;
            info.InstanceInit = &InstanceInit;

            nuint type = GObjectNative.TypeRegisterStatic(parent.Value, name.Pointer, &info, 0);
            if (type == GType.InvalidValue)
            {
                Descriptors.TryRemove(classData, out _);
                throw new InvalidOperationException(
                    $"GObject refused to register \"{descriptor.TypeName}\" as a subclass of {parent.Name}.");
            }

            descriptor.SetRegisteredType(new GType(type));
            ByType[type] = descriptor;

            // From here on an instance of this type that turns up without a
            // wrapper is built into the managed subclass rather than into a
            // wrapper of the nearest registered ancestor. A subclass defined
            // without a factory keeps the ancestor behaviour, which is what
            // TypeRegistry.Fallback reports.
            if (descriptor.WrapFactory is { } wrapFactory)
            {
                TypeRegistry.RegisterSubclass(new GType(type), wrapFactory);
            }

            // g_type_add_interface_static refuses a type whose class is being
            // initialised, and referencing the class is what starts that, so
            // this window - after the registration, before the class - is the
            // only place an interface can still be attached. The table is
            // filled first: interface_init runs inside TypeClassRef below and
            // reads it, and so do the slots that answer for the type itself.
            foreach (InterfaceImplementation implementation in descriptor.Interfaces)
            {
                nuint interfaceType = implementation.InterfaceType.Value;
                Interfaces[(type, interfaceType)] = implementation;

                // GLib copies the info with g_memdup2, so the stack value is
                // enough. No finalize hook: the vtable lives for the process.
                GInterfaceInfo interfaceInfo = default;
                interfaceInfo.InterfaceInit = &InterfaceInit;

                GObjectNative.TypeAddInterfaceStatic(type, interfaceType, &interfaceInfo);

                if (GObjectNative.TypeIsA(type, interfaceType) == 0)
                {
                    throw new InvalidOperationException(
                        $"GObject refused to add {implementation.InterfaceType.Name} to " +
                        $"\"{descriptor.TypeName}\". The type stays registered: static GTypes cannot be " +
                        "unregistered.");
                }
            }

            // Referencing the class runs class_init now rather than when the
            // first instance is created: the parent class is captured and the
            // slots are patched before anything can construct an instance, and
            // a failure surfaces here instead of halfway through g_object_new.
            // The reference is never released, which is what §8 means by the
            // registration being immortal.
            if (GObjectNative.TypeClassRef(type) == nint.Zero)
            {
                throw new InvalidOperationException(
                    $"The class of \"{descriptor.TypeName}\" could not be created.");
            }

            if (descriptor.ClassInitFailure is { } failure)
            {
                throw new InvalidOperationException(
                    $"The class initialiser of \"{descriptor.TypeName}\" failed, so the class is not usable. " +
                    "The type stays registered: static GTypes cannot be unregistered.",
                    failure);
            }

            return new GType(type);
        }
    }

    /// <summary>
    /// Creates an instance of a managed subclass.
    /// </summary>
    /// <param name="type">The type to instantiate, as <see cref="Register"/> returned it.</param>
    /// <returns>The arguments of the wrapper's constructor.</returns>
    /// <remarks>
    /// <c>g_object_new_with_properties</c> is used rather than
    /// <c>g_object_new</c>: the latter is variadic, and calling a variadic
    /// entry point through a fixed signature is not portable.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="type"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    internal static SubclassCtorArgs NewInstance(GType type)
    {
        if (!type.IsValid)
        {
            throw new ArgumentException("An instance needs a valid type.", nameof(type));
        }

        return new SubclassCtorArgs(Construct(type, 0, null, null));
    }

    /// <summary>
    /// Creates an instance of a managed subclass and gives it its properties
    /// while it is being built.
    /// </summary>
    /// <param name="type">The type to instantiate.</param>
    /// <param name="properties">The properties to give the new instance, by name.</param>
    /// <returns>The arguments of the wrapper's constructor.</returns>
    /// <remarks>
    /// The values are converted the way
    /// <c>Gst.ElementFactory.MakeWithProperties</c> converts them, each one into
    /// the type its specification declares. A specification a managed subclass
    /// installed itself is refused: <c>g_object_set_property</c> dispatches to
    /// the class that owns it, and while the instance is being built there is
    /// no wrapper the managed implementation could run on.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="properties"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="type"/> is invalid, the type has no property of one of
    /// the given names, one of them cannot be written, one of them belongs to a
    /// managed subclass, or one of the values does not fit.
    /// </exception>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    internal static SubclassCtorArgs NewInstance(GType type, IReadOnlyDictionary<string, object?> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (!type.IsValid)
        {
            throw new ArgumentException("An instance needs a valid type.", nameof(type));
        }

        int count = properties.Count;
        if (count == 0)
        {
            return NewInstance(type);
        }

        string[] names = new string[count];
        GValueNative[] values = new GValueNative[count];

        // The specifications belong to the class and are borrowed, so the class
        // is held for as long as they are read. The class of a managed subclass
        // exists already - the registration referenced it - and referencing it
        // again is what makes that independent of the order of the calls.
        nint gClass = GObjectNative.TypeClassRef(type.Value);

        try
        {
            Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
            int index = 0;

            foreach (KeyValuePair<string, object?> property in properties)
            {
                using Utf8Scope scope = GMarshal.StackUtf8(property.Key, buffer);
                nint pspec = GObjectNative.ObjectClassFindProperty(gClass, scope.Pointer);

                if (pspec == nint.Zero)
                {
                    throw new ArgumentException(
                        $"\"{property.Key}\" is not a property of {type.Name}.",
                        nameof(properties));
                }

                if ((ParamSpec.FlagsOf(pspec) & ParamFlags.Writable) == 0)
                {
                    throw new ArgumentException(
                        $"The property \"{property.Key}\" of {type.Name} cannot be written.",
                        nameof(properties));
                }

                GType owner = ParamSpec.OwnerTypeOf(pspec);
                if (Find(owner) is not null)
                {
                    throw new ArgumentException(
                        $"The property \"{property.Key}\" belongs to the managed subclass {owner.Name}, so it " +
                        "cannot be given a value while the instance is being built: GObject dispatches the " +
                        "write to the class that owns the property, and the wrapper that would serve it does " +
                        "not exist yet. Write it after construction instead.",
                        nameof(properties));
                }

                names[index] = property.Key;

                GType declared = ParamSpec.ValueTypeOf(pspec);

                try
                {
                    // The converted value moves into the array, which owns it
                    // from here on and is what unsets it again in the exit
                    // below.
                    values[index] = Value.CreateFor(property.Value, declared).NativeValue;
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        $"The property \"{property.Key}\" of {type.Name} holds {declared.Name}: " +
                        exception.Message,
                        nameof(properties),
                        exception);
                }

                index++;
            }

            using StrvScope nameScope = GMarshal.AllocStrv(names);

            fixed (GValueNative* first = values)
            {
                return new SubclassCtorArgs(Construct(type, (uint)count, (byte**)nameScope.Pointer, first));
            }
        }
        finally
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i].TypeValue != GType.InvalidValue)
                {
                    GObjectNative.ValueUnset(ref values[i]);
                }
            }

            GObjectNative.TypeClassUnref(gClass);
        }
    }

    /// <summary>
    /// Tells whether an instance of a type is being constructed on this thread.
    /// </summary>
    /// <param name="type">The exact type of the instance in question.</param>
    /// <returns>
    /// <see langword="true"/> while a <c>g_object_new</c> of that type has not
    /// returned into the constructor of its wrapper yet.
    /// </returns>
    internal static bool IsConstructing(GType type)
    {
        List<nuint>? constructing = t_constructing;
        if (constructing is null)
        {
            return false;
        }

        for (int i = 0; i < constructing.Count; i++)
        {
            if (constructing[i] == type.Value)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs <c>g_object_new_with_properties</c> with the type on the
    /// construction stack of the thread.
    /// </summary>
    /// <param name="type">The type to instantiate.</param>
    /// <param name="count">The number of properties.</param>
    /// <param name="names">The property names, or <see langword="null"/>.</param>
    /// <param name="values">The property values, or <see langword="null"/>.</param>
    /// <returns>The new instance.</returns>
    /// <remarks>
    /// <c>g_object_new_with_properties</c> is used rather than
    /// <c>g_object_new</c>: the latter is variadic, and calling a variadic
    /// entry point through a fixed signature is not portable.
    /// </remarks>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    private static nint Construct(GType type, uint count, byte** names, GValueNative* values)
    {
        List<nuint> constructing = t_constructing ??= [];
        constructing.Add(type.Value);

        nint handle;
        try
        {
            handle = GObjectNative.ObjectNewWithProperties(type.Value, count, names, values);
        }
        finally
        {
            constructing.RemoveAt(constructing.Count - 1);
        }

        if (handle == nint.Zero)
        {
            throw new InvalidOperationException($"g_object_new returned nothing for {type.Name}.");
        }

        return handle;
    }

    /// <summary>
    /// Looks the descriptor of a registered managed subclass up.
    /// </summary>
    /// <param name="type">The type to look up.</param>
    /// <returns>The descriptor, or <see langword="null"/> for a native type.</returns>
    internal static SubclassDescriptor? Find(GType type) =>
        ByType.TryGetValue(type.Value, out SubclassDescriptor? descriptor) ? descriptor : null;

    /// <summary>
    /// Looks the descriptor of the managed subclass a native instance belongs
    /// to up.
    /// </summary>
    /// <param name="instance">The instance, read for its type.</param>
    /// <returns>The descriptor, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The lookup is one level: the exact type of the instance. That is
    /// correct exactly as long as a managed subclass cannot be derived from,
    /// which <see cref="Register"/> enforces. See §4.4.
    /// </remarks>
    internal static SubclassDescriptor? Find(nint instance) => Find(TypeRegistry.GetInstanceType(instance));

    /// <summary>
    /// Looks the descriptor of the managed subclass a native instance belongs
    /// to up, failing when there is none.
    /// </summary>
    /// <param name="instance">The instance, read for its type.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">
    /// The instance is not one of a registered managed subclass, so there is no
    /// captured parent class to chain up through.
    /// </exception>
    internal static SubclassDescriptor DescriptorFor(nint instance) =>
        Find(instance) ??
        throw new InvalidOperationException(
            $"{TypeRegistry.GetInstanceType(instance).Name} is not a registered managed subclass.");

    /// <summary>
    /// Keeps the wrapper of a specification a managed subclass just installed.
    /// </summary>
    /// <param name="owner">The type that installed the property.</param>
    /// <param name="spec">The wrapper to keep, which must not be disposed again.</param>
    internal static void RememberInstalledSpec(GType owner, ParamSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        InstalledSpecs.TryAdd((owner.Value, spec.Handle), spec);
    }

    /// <summary>
    /// Finds the kept wrapper of an installed specification.
    /// </summary>
    /// <param name="spec">The native <c>GParamSpec</c> a property slot was given.</param>
    /// <returns>
    /// The wrapper <see cref="RememberInstalledSpec"/> kept, or
    /// <see langword="null"/> when the specification was installed by something
    /// other than a managed subclass.
    /// </returns>
    internal static ParamSpec? InstalledSpecFor(nint spec)
    {
        if (spec == nint.Zero)
        {
            return null;
        }

        return InstalledSpecs.TryGetValue((ParamSpec.OwnerTypeOf(spec).Value, spec), out ParamSpec? kept)
            ? kept
            : null;
    }

    /// <summary>
    /// Initialises the class of a managed subclass: captures the parent class
    /// and writes the declared slots.
    /// </summary>
    /// <param name="gClass">The class being initialised.</param>
    /// <param name="classData">The <c>class_data</c> of the registration.</param>
    /// <remarks>
    /// This runs while GObject holds its type lock, so it stays narrow and
    /// never wanders into paths that wrap arbitrary objects. See §3.3.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void ClassInit(nint gClass, nint classData)
    {
        try
        {
            if (!Descriptors.TryGetValue(classData, out SubclassDescriptor? descriptor))
            {
                throw new InvalidOperationException(
                    "class_init ran for a managed subclass whose registration is unknown.");
            }

            descriptor.CaptureParentClass(GObjectNative.TypeClassPeekParent(gClass));

            foreach (VfuncSlot slot in descriptor.Slots)
            {
                *(nint*)(gClass + slot.Offset) = slot.Function;
            }

            if (descriptor.ClassInitializer is { } configure)
            {
                // A GstElementClass is what carries the metadata and the pad
                // templates, so the richer facade is only handed out for a
                // class that has those fields; Gst.Pad and its subclasses get
                // the GObject level one, which the generated DefineSubclass of
                // such a class asks for by its parameter type.
                configure(
                    descriptor.ParentType.IsA(new GType(Gst.Element.GetGType()))
                        ? new ClassConfig(gClass)
                        : new ObjectClassConfig(gClass));
            }
        }
        catch (Exception exception)
        {
            // The exception cannot leave this frame, because GObject called it,
            // so it is kept on the descriptor and rethrown by Register, which
            // is the call the author of the subclass made. Reporting it as well
            // keeps the trap the one place every callback failure shows up in.
            if (Descriptors.TryGetValue(classData, out SubclassDescriptor? failed))
            {
                failed.CaptureClassInitFailure(exception);
            }

            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// Initialises an instance of a managed subclass, which is deliberately
    /// nothing.
    /// </summary>
    /// <param name="instance">The instance being initialised.</param>
    /// <param name="gClass">The class of the instance.</param>
    /// <remarks>
    /// No wrapper is created here. The wrapper is interned by the constructor
    /// that <c>g_object_new</c> returns into, and a wrapper built here would
    /// install a toggle reference that collides with the one that constructor
    /// is about to install. Vfuncs that fire before then find no interned
    /// wrapper and chain up, which is the whole construction window rule. See
    /// §5.2 and §4.1.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void InstanceInit(nint instance, nint gClass)
    {
        _ = instance;
        _ = gClass;
    }

    /// <summary>
    /// Fills the vtable of one interface of one managed subclass.
    /// </summary>
    /// <param name="iface">The vtable, whose header names both types.</param>
    /// <param name="interfaceData">The <c>interface_data</c>, always null here.</param>
    /// <remarks>
    /// One trampoline serves every interface of every managed subclass: the
    /// pair of types it has to dispatch on is written into the head of the
    /// vtable by GLib before the hook is called, so no per registration data is
    /// needed. It runs after the class initialiser of the implementing type, on
    /// the thread that referenced the class - which is the thread inside
    /// <see cref="Register"/> - and the class initialiser rules apply: nothing
    /// here creates a wrapper.
    /// </remarks>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void InterfaceInit(nint iface, nint interfaceData)
    {
        _ = interfaceData;

        GTypeInterfaceRaw* header = (GTypeInterfaceRaw*)iface;
        nuint instanceType = header->InstanceType;

        try
        {
            if (!Interfaces.TryGetValue((instanceType, header->GType), out InterfaceImplementation? implementation))
            {
                throw new InvalidOperationException(
                    "interface_init ran for a managed subclass whose interface is unknown.");
            }

            implementation.InitializeVTable((void*)iface, new GType(instanceType));
        }
        catch (Exception exception)
        {
            // Same contract as ClassInit: the exception cannot cross back into
            // GObject, so it is kept on the descriptor and Register rethrows it
            // as the failure of the Define call that started all this.
            if (ByType.TryGetValue(instanceType, out SubclassDescriptor? failed))
            {
                failed.CaptureClassInitFailure(exception);
            }

            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// Returns the implementation of one interface of one managed subclass.
    /// </summary>
    /// <param name="instanceType">The implementing type.</param>
    /// <param name="interfaceType">The interface.</param>
    /// <param name="implementation">The implementation, when there is one.</param>
    /// <returns>Whether the type implements the interface through the binding.</returns>
    /// <remarks>
    /// This is how an interface slot that is handed a type rather than an
    /// instance - <c>GstURIHandler.get_type</c> and <c>get_protocols</c> are
    /// both of that shape - finds the managed side without a wrapper.
    /// </remarks>
    internal static bool TryGetInterface(
        GType instanceType,
        GType interfaceType,
        [NotNullWhen(true)] out InterfaceImplementation? implementation) =>
        Interfaces.TryGetValue((instanceType.Value, interfaceType.Value), out implementation);

    /// <summary>
    /// Rejects a name GObject would reject, with a message that says why.
    /// </summary>
    /// <param name="typeName">The proposed <c>GType</c> name.</param>
    private static void ValidateTypeName(string typeName)
    {
        bool legal = typeName.Length >= 3 && char.IsAsciiLetter(typeName[0]);

        for (int i = 1; legal && i < typeName.Length; i++)
        {
            char character = typeName[i];
            legal = char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '+';
        }

        if (!legal)
        {
            throw new ArgumentException(
                $"\"{typeName}\" is not a legal GType name: names are at least three characters long, start " +
                "with a letter, and are made of letters, digits, '_', '-' and '+'.",
                nameof(typeName));
        }
    }

    /// <summary>
    /// Hands out the next <c>class_data</c> identifier.
    /// </summary>
    /// <returns>The identifier, which is never zero.</returns>
    private static nint NextClassDataId()
    {
        nint id;

        do
        {
            id = (nint)Interlocked.Increment(ref _nextClassDataId);
        }
        while (id == nint.Zero || Descriptors.ContainsKey(id));

        return id;
    }
}
