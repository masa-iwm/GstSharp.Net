using System.Collections.Concurrent;
using System.Collections.Frozen;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// One wrapper that was created for a base type of the object it wraps, because
/// the exact type of the object is not registered.
/// </summary>
/// <param name="InstanceType">The type of the native instance.</param>
/// <param name="WrapperType">
/// The registered type the wrapper was built from, an ancestor of
/// <paramref name="InstanceType"/>.
/// </param>
public readonly record struct TypeFallback(GType InstanceType, GType WrapperType);

/// <summary>
/// Maps native types to the factories that create their managed wrappers.
/// </summary>
/// <remarks>
/// <para>
/// Every generated binding assembly registers its <see cref="NativeModule"/>
/// from a module initialiser. Registration is cheap and does not touch native
/// code: the <c>get_type</c> functions of the entries are only called when the
/// registry is frozen, which happens the first time a wrapper is needed and
/// therefore after the native libraries have been loaded.
/// </para>
/// <para>
/// Registering a module after the registry was frozen unfreezes it, and the
/// next lookup builds the table again with the new entries. Wrappers that were
/// created before that keep the type they were created with: a wrapper is
/// interned for as long as the application holds it, and retyping a live object
/// is not something a binding can do behind the application's back. Initialise
/// the modules first — see <c>GstSharp.Initialize</c>.
/// </para>
/// <para>
/// The lookup is a frozen dictionary of function pointers, so no reflection is
/// involved at any point.
/// </para>
/// </remarks>
public static class TypeRegistry
{
    private static readonly object Sync = new();
    private static readonly List<NativeModule> Modules = [];
    private static readonly ConcurrentDictionary<nuint, byte> ReportedFallbacks = new();

    /// <summary>
    /// Whether the wrapper of a registered boxed type is a mini object, asked
    /// once per type. See <see cref="TryCreateMiniObjectWrapper"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<nuint, bool> MiniObjectTypes = new();

    /// <summary>
    /// The factories of the managed subclasses that native code may create
    /// instances of, keyed by the type they were registered as.
    /// </summary>
    /// <remarks>
    /// This table is separate from the frozen one of the modules and is never
    /// rebuilt: a managed subclass is registered after the native libraries are
    /// loaded, so its type is a resolved <c>GType</c> and not a <c>get_type</c>
    /// function that still has to be called, and a registration lives for the
    /// process. The lookup is exact, because a managed subclass cannot be
    /// derived from.
    /// </remarks>
    private static readonly ConcurrentDictionary<nuint, Func<SubclassCtorArgs, Object>> SubclassFactories = new();

    private static FrozenDictionary<nuint, TypeEntry> _types = FrozenDictionary<nuint, TypeEntry>.Empty;
    private static FrozenDictionary<Type, ModuleInterfaceEntry> _interfaces =
        FrozenDictionary<Type, ModuleInterfaceEntry>.Empty;

    private static volatile bool _frozen;

    /// <summary>
    /// Raised the first time a wrapper is created for a base type of the object
    /// it wraps, once per exact native type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Wrapping an object as one of its base types is the normal case and not
    /// an error: every element a plugin implements has a type of its own that
    /// no binding assembly wraps, so <c>filesrc</c> arrives as the closest type
    /// that is registered. Nothing is written anywhere without a handler, and
    /// applications do not need one.
    /// </para>
    /// <para>
    /// It is a diagnostic for the one case where the fallback is a bug: a type
    /// that a binding assembly does wrap, seen as its base type because the
    /// module of that assembly had not registered yet. That failure is
    /// otherwise silent — the cast to the concrete wrapper is simply
    /// <see langword="null"/> — and this is how to see it. Handlers must not
    /// throw.
    /// </para>
    /// </remarks>
    public static event Action<TypeFallback>? Fallback;

    /// <summary>
    /// Gets the logical names of the modules that have been registered.
    /// </summary>
    /// <remarks>
    /// Which binding assemblies have handed their types over is otherwise only
    /// observable by wrapping an object of a type one of them covers, and
    /// naming such a type is itself enough to make the assembly register. This
    /// is how the tests of the module sweep ask the question without answering
    /// it on the way.
    /// </remarks>
    internal static string[] RegisteredModules
    {
        get
        {
            lock (Sync)
            {
                string[] names = new string[Modules.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = Modules[i].LogicalName;
                }

                return names;
            }
        }
    }

    /// <summary>
    /// Adds the types of one binding assembly.
    /// </summary>
    /// <param name="module">The module to register.</param>
    public static void RegisterModule(NativeModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        lock (Sync)
        {
            Modules.Add(module);

            // The next lookup rebuilds the table with the new entries.
            _frozen = false;
        }
    }

    /// <summary>
    /// Resolves the <c>get_type</c> function of every registered entry and
    /// builds the lookup table. This loads the native libraries.
    /// </summary>
    /// <remarks>
    /// Freezing again is allowed and rebuilds the table from scratch, which is
    /// what a module that registers late needs. A lookup does it on its own, so
    /// the only reason to call this is to pay the cost at a moment of the
    /// application's choosing.
    /// </remarks>
    public static unsafe void Freeze()
    {
        lock (Sync)
        {
            Dictionary<nuint, TypeEntry> map = [];
            Dictionary<Type, ModuleInterfaceEntry> interfaces = [];

            foreach (NativeModule module in Modules)
            {
                foreach (ModuleInterfaceEntry entry in module.Interfaces)
                {
                    // The interface table is keyed by the managed interface, so
                    // building it needs no native call: the get_type function of
                    // an entry is only called when a cast asks for it.
                    if (entry.IsValid && entry.InterfaceType is { } interfaceType)
                    {
                        interfaces[interfaceType] = entry;
                    }
                }

                foreach (ModuleTypeEntry entry in module.Types)
                {
                    if (!entry.IsValid)
                    {
                        continue;
                    }

                    nuint type;
                    try
                    {
                        type = entry.GetNativeType();
                    }
                    catch (EntryPointNotFoundException)
                    {
                        // The installed GStreamer predates this type (for
                        // example GstIdStr needs 1.26 while Ubuntu 24.04
                        // ships 1.24). Skip the registration: instances fall
                        // back to an ancestor wrapper, and calling one of the
                        // type's methods still surfaces the missing export at
                        // call time, which is the documented contract for
                        // older-than-recommended installations.
                        continue;
                    }

                    if (type != GType.InvalidValue)
                    {
                        map[type] = new TypeEntry(entry.Factory);
                    }
                }
            }

            _types = map.ToFrozenDictionary();
            _interfaces = interfaces.ToFrozenDictionary();
            _frozen = true;
        }
    }

    /// <summary>
    /// Registers the factory that builds the wrapper of a managed subclass, so
    /// that an instance native code created is wrapped as the type it actually
    /// is.
    /// </summary>
    /// <param name="type">The type of the managed subclass.</param>
    /// <param name="factory">The factory the subclass handed to its definition.</param>
    /// <remarks>
    /// The registration is made by <see cref="SubclassRegistry.Register"/> and
    /// stays for the process, like the type itself. It is consulted before the
    /// walk up the ancestry of an instance, so a registered managed subclass
    /// never falls back to an ancestor wrapper and never raises
    /// <see cref="Fallback"/>.
    /// </remarks>
    internal static void RegisterSubclass(GType type, Func<SubclassCtorArgs, Object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        SubclassFactories[type.Value] = factory;
    }

    /// <summary>
    /// Answers the live wrapper of an instance, and builds the one of a managed
    /// subclass that native code created when there is none.
    /// </summary>
    /// <param name="handle">The instance to wrap.</param>
    /// <param name="wrapper">The wrapper, new or the one that was interned.</param>
    /// <returns>
    /// <see langword="true"/> when the instance already has a live wrapper, or
    /// when its exact type is a managed subclass with a factory and nothing
    /// suppresses the fabrication.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The interning table is consulted before the type of the instance is
    /// read, so an instance that is already wrapped costs one dictionary lookup
    /// and nothing else: no type read, no native call, no gate. That is what
    /// every vfunc argument of a managed element is on a streaming thread. The
    /// wrapper of a hit is settled by the caller exactly like a fresh one, so
    /// that both answers carry the same ownership rule.
    /// </para>
    /// <para>
    /// Nothing is fabricated while an instance of the type is being constructed
    /// on this thread — the constructor of the wrapper is about to install the
    /// toggle reference — and nothing is fabricated for an instance whose
    /// wrapper was disposed, which gave up its part in the lifetime of the
    /// object for good. A dispose that is under way is covered as well: the
    /// wrapper sets its flag before it writes the marker on the instance, so a
    /// table entry whose wrapper is already disposed answers for the window in
    /// which the marker is not there yet. All of these answer
    /// <see langword="false"/>, and the caller falls back to the ancestor
    /// wrapper or to chaining up, exactly as before.
    /// </para>
    /// <para>
    /// Nothing here adjusts references beyond the one the wrapper takes for
    /// itself, and nothing here sinks: the instance belongs to whoever created
    /// it. A caller that was handed a reference of its own — which is
    /// <see cref="TryCreateWrapper(nint, Transfer, out object?)"/>, never a
    /// trampoline — settles it afterwards.
    /// </para>
    /// </remarks>
    internal static bool TryFabricate(nint handle, out Object? wrapper)
    {
        wrapper = null;

        if (handle == nint.Zero || SubclassFactories.IsEmpty)
        {
            return false;
        }

        if (Object.IsInterningLockHeld)
        {
            // The gate of a fabrication is always taken before the interning
            // lock. This thread holds that lock, which means it came through
            // Gst.GObject.Object.FromNative - which tried to fabricate before
            // it took it - so there is nothing left to do here and the order of
            // the two locks stays the same everywhere. This is asked before
            // anything below because it is also what keeps every native call
            // here - the qdata read - out from under that lock.
            return false;
        }

        if (Object.TryGetInterned(handle) is { } interned)
        {
            wrapper = interned;
            return true;
        }

        GType type = GetInstanceType(handle);
        if (!SubclassFactories.TryGetValue(type.Value, out Func<SubclassCtorArgs, Object>? factory))
        {
            return false;
        }

        if (SubclassRegistry.IsConstructing(type) || Object.HasDisposedInterned(handle))
        {
            return false;
        }

        if (Object.WasSubclassDisposed(handle))
        {
            return false;
        }

        wrapper = Object.Fabricate(handle, factory);
        return true;
    }

    /// <summary>
    /// Settles the reference a call was handed for an object that a wrapper
    /// already owns.
    /// </summary>
    /// <param name="handle">The instance that was wrapped.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <remarks>
    /// The wrapper — the one that was just fabricated, or the one that was
    /// interned already — owns the object through its own toggle reference, so
    /// a <see cref="Transfer.Full"/> reference is dropped. A floating instance
    /// is sunk first whatever the annotation says: "transfer floating" is
    /// spelled <c>none</c> in the gir, which is how
    /// <c>gst_element_factory_make</c> arrives here, and the wrapper a
    /// constructor builds itself sinks such an instance as well. What is left
    /// either way is the single reference the toggle reference holds.
    /// </remarks>
    internal static void SettleHandedReference(nint handle, Transfer transfer)
    {
        if (GObjectNative.ObjectIsFloating(handle) != 0)
        {
            GObjectNative.ObjectRefSink(handle);
            GObjectNative.ObjectUnref(handle);
        }
        else if (transfer == Transfer.Full)
        {
            GObjectNative.ObjectUnref(handle);
        }
    }

    /// <summary>
    /// Reads the type of a native instance.
    /// </summary>
    /// <param name="handle">The <c>GTypeInstance</c> to inspect.</param>
    /// <returns>
    /// The type of the instance, or <see cref="GType.Invalid"/> when
    /// <paramref name="handle"/> is <see cref="nint.Zero"/>.
    /// </returns>
    public static unsafe GType GetInstanceType(nint handle)
    {
        if (handle == nint.Zero)
        {
            return GType.Invalid;
        }

        // A GTypeInstance starts with a pointer to its GTypeClass, and a
        // GTypeClass starts with the GType of the class.
        nint typeClass = *(nint*)handle;
        return typeClass == nint.Zero ? GType.Invalid : new GType(*(nuint*)typeClass);
    }

    /// <summary>
    /// Creates the managed wrapper for a native instance.
    /// </summary>
    /// <param name="handle">The instance to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <param name="wrapper">The new wrapper.</param>
    /// <returns>
    /// <see langword="true"/> when the type of the instance is a managed
    /// subclass that stated how its wrapper is built, or when that type, or one
    /// of the types it derives from, is registered by a module.
    /// </returns>
    public static unsafe bool TryCreateWrapper(nint handle, Transfer transfer, out object? wrapper)
    {
        wrapper = null;

        if (handle == nint.Zero)
        {
            return false;
        }

        if (TryFabricate(handle, out Object? fabricated))
        {
            // This is the entry point that was handed a reference, so this is
            // where it is settled: doing it in the caller would leak one for
            // whoever calls this directly rather than through
            // Gst.GObject.Object.FromNative.
            SettleHandedReference(handle, transfer);
            wrapper = fabricated;
            return true;
        }

        FrozenDictionary<nuint, TypeEntry> types = EnsureFrozen();
        nuint instanceType = GetInstanceType(handle).Value;
        nuint type = instanceType;

        while (type != GType.InvalidValue)
        {
            if (types.TryGetValue(type, out TypeEntry entry))
            {
                if (type != instanceType)
                {
                    ReportFallback(instanceType, type);
                }

                wrapper = entry.Factory(handle, transfer);
                return wrapper is not null;
            }

            type = GObjectNative.TypeParent(type);
        }

        return false;
    }

    /// <summary>
    /// Creates the managed wrapper of a value whose type is known from
    /// somewhere other than the value itself.
    /// </summary>
    /// <param name="type">The registered type of the value.</param>
    /// <param name="handle">The value to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <param name="wrapper">The new wrapper.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="type"/> is registered.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <see cref="TryCreateWrapper(nint, Transfer, out object?)"/> reads the
    /// type out of the instance, which only a <c>GTypeInstance</c> carries: a
    /// boxed value is a plain structure, and the first word of one is a field
    /// of the structure rather than a pointer to its class. The type of a boxed
    /// value therefore has to come from whatever produced it — the
    /// <c>GType</c> of the <c>GValue</c> it sits in, which is what
    /// <see cref="Value.GetBoxed{T}"/> passes here.
    /// </para>
    /// <para>
    /// The lookup is exact and raises no <see cref="Fallback"/>. Boxed types do
    /// not derive from one another, so walking to the parent type would only
    /// ever reach <c>G_TYPE_BOXED</c>, which nothing registers, and reporting a
    /// fallback for a type that has no ancestry to fall back to would say
    /// nothing.
    /// </para>
    /// </remarks>
    internal static unsafe bool TryCreateWrapper(GType type, nint handle, Transfer transfer, out object? wrapper)
    {
        wrapper = null;

        if (handle == nint.Zero || !type.IsValid)
        {
            return false;
        }

        if (!EnsureFrozen().TryGetValue(type.Value, out TypeEntry entry))
        {
            return false;
        }

        wrapper = entry.Factory(handle, transfer);
        return wrapper is not null;
    }

    /// <summary>
    /// Creates the managed wrapper of a mini object that sits in a
    /// <c>GValue</c> as a boxed value, and answers whether that is what the
    /// value holds.
    /// </summary>
    /// <param name="type">The registered type of the value.</param>
    /// <param name="handle">The mini object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <param name="wrapper">The new wrapper.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="type"/> is registered and
    /// its wrapper is a <see cref="Gst.MiniObject"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A mini object is a boxed type as far as GObject is concerned, so
    /// nothing about the <c>GType</c> of the value says which of the two
    /// object models it belongs to. The registry answers the question the only
    /// way it can — by building the wrapper and looking at it — and remembers
    /// the answer, because which managed type a <c>GType</c> maps onto does
    /// not change once a module has registered it.
    /// </para>
    /// <para>
    /// The probe of a boxed type that turns out not to be a mini object costs
    /// one <c>g_boxed_copy</c> and the <c>g_boxed_free</c> that releases it
    /// again, once per type and never again. A type that is not registered at
    /// all is not remembered: the module that binds it may register later, and
    /// a cached "no" would outlive the reason for it.
    /// </para>
    /// </remarks>
    internal static bool TryCreateMiniObjectWrapper(
        GType type,
        nint handle,
        Transfer transfer,
        out object? wrapper)
    {
        wrapper = null;

        if (handle == nint.Zero || !type.IsValid)
        {
            return false;
        }

        if (MiniObjectTypes.TryGetValue(type.Value, out bool isMiniObject) && !isMiniObject)
        {
            return false;
        }

        if (!TryCreateWrapper(type, handle, transfer, out object? candidate))
        {
            return false;
        }

        if (candidate is MiniObject)
        {
            MiniObjectTypes[type.Value] = true;
            wrapper = candidate;
            return true;
        }

        MiniObjectTypes[type.Value] = false;

        // The factory built a value of its own, and nothing else holds it.
        (candidate as IDisposable)?.Dispose();
        return false;
    }

    /// <summary>
    /// Raises <see cref="Fallback"/>, once per exact native type.
    /// </summary>
    /// <param name="instanceType">The type of the native instance.</param>
    /// <param name="wrapperType">The registered ancestor that was used.</param>
    private static void ReportFallback(nuint instanceType, nuint wrapperType)
    {
        Action<TypeFallback>? handler = Fallback;
        if (handler is null)
        {
            // Nothing is remembered while nobody listens, so a handler that is
            // attached later still sees every type once.
            return;
        }

        if (!ReportedFallbacks.TryAdd(instanceType, 0))
        {
            return;
        }

        try
        {
            handler(new TypeFallback(new GType(instanceType), new GType(wrapperType)));
        }
        catch (Exception exception)
        {
            // A diagnostic must not break the lookup it reports on, and this
            // runs on whichever thread happened to wrap an object, which is
            // often a streaming thread of GStreamer.
            ExceptionTrap.Report(exception);
        }
    }

    /// <summary>
    /// Looks a generated GObject interface up.
    /// </summary>
    /// <param name="interfaceType">The managed interface to look up.</param>
    /// <param name="entry">The entry of the interface.</param>
    /// <returns>
    /// <see langword="true"/> when a registered module binds
    /// <paramref name="interfaceType"/>.
    /// </returns>
    internal static bool TryGetInterface(Type interfaceType, out ModuleInterfaceEntry entry)
    {
        EnsureFrozen();
        return _interfaces.TryGetValue(interfaceType, out entry);
    }

    private static FrozenDictionary<nuint, TypeEntry> EnsureFrozen()
    {
        // The table is rebuilt only when a module was registered after the last
        // freeze, so the lookup that every wrapper goes through reads one
        // volatile flag and one reference on the common path.
        if (_frozen)
        {
            return _types;
        }

        lock (Sync)
        {
            if (!_frozen)
            {
                Freeze();
            }

            return _types;
        }
    }

    private readonly unsafe struct TypeEntry
    {
        private readonly delegate*<nint, Transfer, object> _factory;

        internal TypeEntry(delegate*<nint, Transfer, object> factory) => _factory = factory;

        internal delegate*<nint, Transfer, object> Factory => _factory;
    }
}
