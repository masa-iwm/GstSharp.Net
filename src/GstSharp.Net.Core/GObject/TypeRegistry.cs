using System.Collections.Frozen;
using Gst.Interop;

namespace Gst.GObject;

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
/// The lookup is a frozen dictionary of function pointers, so no reflection is
/// involved at any point.
/// </para>
/// </remarks>
public static class TypeRegistry
{
    private static readonly object Sync = new();
    private static readonly List<NativeModule> Modules = [];

    private static FrozenDictionary<nuint, TypeEntry> _types = FrozenDictionary<nuint, TypeEntry>.Empty;
    private static bool _frozen;

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
    public static unsafe void Freeze()
    {
        lock (Sync)
        {
            Dictionary<nuint, TypeEntry> map = [];

            foreach (NativeModule module in Modules)
            {
                foreach (ModuleTypeEntry entry in module.Types)
                {
                    if (!entry.IsValid)
                    {
                        continue;
                    }

                    nuint type = entry.GetNativeType();
                    if (type != GType.InvalidValue)
                    {
                        map[type] = new TypeEntry(entry.Factory);
                    }
                }
            }

            _types = map.ToFrozenDictionary();
            _frozen = true;
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
    /// <see langword="true"/> when the type of the instance, or one of the
    /// types it derives from, is registered.
    /// </returns>
    public static unsafe bool TryCreateWrapper(nint handle, Transfer transfer, out object? wrapper)
    {
        wrapper = null;

        if (handle == nint.Zero)
        {
            return false;
        }

        FrozenDictionary<nuint, TypeEntry> types = EnsureFrozen();
        nuint type = GetInstanceType(handle).Value;

        while (type != GType.InvalidValue)
        {
            if (types.TryGetValue(type, out TypeEntry entry))
            {
                wrapper = entry.Factory(handle, transfer);
                return wrapper is not null;
            }

            type = GObjectNative.TypeParent(type);
        }

        return false;
    }

    private static FrozenDictionary<nuint, TypeEntry> EnsureFrozen()
    {
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
