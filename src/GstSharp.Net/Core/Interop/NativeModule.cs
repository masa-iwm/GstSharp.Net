namespace Gst.Interop;

/// <summary>
/// One native type of a <see cref="NativeModule"/>: the <c>get_type</c>
/// function that identifies it at run time, and the factory that creates its
/// managed wrapper.
/// </summary>
/// <remarks>
/// Both members are plain function pointers, so the registry stays free of
/// reflection and survives trimming and ahead of time compilation. The entries
/// are emitted by the generator, one per wrapped class.
/// </remarks>
public readonly unsafe struct ModuleTypeEntry : IEquatable<ModuleTypeEntry>
{
    private readonly delegate* unmanaged[Cdecl]<nuint> _nativeGetType;
    private readonly delegate*<nuint> _managedGetType;
    private readonly delegate*<nint, Transfer, object> _factory;

    /// <summary>
    /// Initialises a new entry from the raw address of the native
    /// <c>get_type</c> function.
    /// </summary>
    /// <param name="getTypeFunction">
    /// The <c>get_type</c> entry point of the native type. It is only called
    /// once the native library is loaded.
    /// </param>
    /// <param name="factory">
    /// Creates the managed wrapper for an instance of the native type.
    /// </param>
    public ModuleTypeEntry(
        delegate* unmanaged[Cdecl]<nuint> getTypeFunction,
        delegate*<nint, Transfer, object> factory)
    {
        _nativeGetType = getTypeFunction;
        _factory = factory;
    }

    /// <summary>
    /// Initialises a new entry from a generated interop stub.
    /// </summary>
    /// <param name="getTypeFunction">
    /// The address of the <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/>
    /// stub of the <c>get_type</c> function, which resolves the native library
    /// on its first call.
    /// </param>
    /// <param name="factory">
    /// Creates the managed wrapper for an instance of the native type.
    /// </param>
    public ModuleTypeEntry(
        delegate*<nuint> getTypeFunction,
        delegate*<nint, Transfer, object> factory)
    {
        _managedGetType = getTypeFunction;
        _factory = factory;
    }

    /// <summary>
    /// Gets the factory that creates the managed wrapper.
    /// </summary>
    public delegate*<nint, Transfer, object> Factory => _factory;

    /// <summary>
    /// Gets a value indicating whether the entry can be resolved.
    /// </summary>
    public bool IsValid =>
        _factory is not null && (_nativeGetType is not null || _managedGetType is not null);

    /// <summary>
    /// Calls the <c>get_type</c> function of the native type. This loads the
    /// native library.
    /// </summary>
    /// <returns>The type, or zero when the entry is incomplete.</returns>
    public nuint GetNativeType()
    {
        if (_nativeGetType is not null)
        {
            return _nativeGetType();
        }

        return _managedGetType is not null ? _managedGetType() : 0;
    }

    /// <summary>Compares two entries for equality.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when both describe the same type.</returns>
    public static bool operator ==(ModuleTypeEntry left, ModuleTypeEntry right) => left.Equals(right);

    /// <summary>Compares two entries for inequality.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when both describe different types.</returns>
    public static bool operator !=(ModuleTypeEntry left, ModuleTypeEntry right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ModuleTypeEntry other) =>
        (nint)_nativeGetType == (nint)other._nativeGetType &&
        (nint)_managedGetType == (nint)other._managedGetType &&
        (nint)_factory == (nint)other._factory;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ModuleTypeEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine((nint)_nativeGetType, (nint)_managedGetType, (nint)_factory);
}

/// <summary>
/// One GObject interface of a <see cref="NativeModule"/>: the <c>get_type</c>
/// function that identifies it at run time, and the factory that presents a
/// wrapper as that interface.
/// </summary>
/// <remarks>
/// The managed interface type is the key that <c>Gst.GObject.Object.As</c>
/// looks an entry up by; the other two members are plain function pointers, so
/// the registry stays free of reflection and survives trimming and ahead of
/// time compilation. The entries are emitted by the generator, one per
/// generated interface.
/// </remarks>
public readonly unsafe struct ModuleInterfaceEntry : IEquatable<ModuleInterfaceEntry>
{
    private readonly delegate* unmanaged[Cdecl]<nuint> _nativeGetType;
    private readonly delegate*<nuint> _managedGetType;
    private readonly delegate*<Gst.GObject.Object, object> _adapt;

    /// <summary>
    /// Initialises a new entry from the raw address of the native
    /// <c>get_type</c> function.
    /// </summary>
    /// <param name="interfaceType">The generated managed interface.</param>
    /// <param name="getTypeFunction">
    /// The <c>get_type</c> entry point of the native interface. It is only
    /// called once the native library is loaded.
    /// </param>
    /// <param name="adapt">
    /// Presents a wrapper whose instance implements the interface as
    /// <paramref name="interfaceType"/>.
    /// </param>
    public ModuleInterfaceEntry(
        Type interfaceType,
        delegate* unmanaged[Cdecl]<nuint> getTypeFunction,
        delegate*<Gst.GObject.Object, object> adapt)
    {
        InterfaceType = interfaceType;
        _nativeGetType = getTypeFunction;
        _adapt = adapt;
    }

    /// <summary>
    /// Initialises a new entry from a generated interop stub.
    /// </summary>
    /// <param name="interfaceType">The generated managed interface.</param>
    /// <param name="getTypeFunction">
    /// The address of the <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/>
    /// stub of the <c>get_type</c> function, which resolves the native library
    /// on its first call.
    /// </param>
    /// <param name="adapt">
    /// Presents a wrapper whose instance implements the interface as
    /// <paramref name="interfaceType"/>.
    /// </param>
    public ModuleInterfaceEntry(
        Type interfaceType,
        delegate*<nuint> getTypeFunction,
        delegate*<Gst.GObject.Object, object> adapt)
    {
        InterfaceType = interfaceType;
        _managedGetType = getTypeFunction;
        _adapt = adapt;
    }

    /// <summary>
    /// Gets the managed interface that the entry stands for.
    /// </summary>
    public Type? InterfaceType { get; }

    /// <summary>
    /// Gets the factory that presents a wrapper as the interface.
    /// </summary>
    public delegate*<Gst.GObject.Object, object> Adapt => _adapt;

    /// <summary>
    /// Gets a value indicating whether the entry can be resolved.
    /// </summary>
    public bool IsValid =>
        InterfaceType is not null && _adapt is not null &&
        (_nativeGetType is not null || _managedGetType is not null);

    /// <summary>
    /// Calls the <c>get_type</c> function of the native interface. This loads
    /// the native library.
    /// </summary>
    /// <returns>The type, or zero when the entry is incomplete.</returns>
    public nuint GetNativeType()
    {
        if (_nativeGetType is not null)
        {
            return _nativeGetType();
        }

        return _managedGetType is not null ? _managedGetType() : 0;
    }

    /// <summary>Compares two entries for equality.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when both describe the same interface.</returns>
    public static bool operator ==(ModuleInterfaceEntry left, ModuleInterfaceEntry right) => left.Equals(right);

    /// <summary>Compares two entries for inequality.</summary>
    /// <param name="left">The first entry.</param>
    /// <param name="right">The second entry.</param>
    /// <returns><see langword="true"/> when both describe different interfaces.</returns>
    public static bool operator !=(ModuleInterfaceEntry left, ModuleInterfaceEntry right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(ModuleInterfaceEntry other) =>
        InterfaceType == other.InterfaceType &&
        (nint)_nativeGetType == (nint)other._nativeGetType &&
        (nint)_managedGetType == (nint)other._managedGetType &&
        (nint)_adapt == (nint)other._adapt;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ModuleInterfaceEntry other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(InterfaceType, (nint)_nativeGetType, (nint)_managedGetType, (nint)_adapt);
}

/// <summary>
/// The type table of one generated binding assembly.
/// </summary>
/// <remarks>
/// A binding assembly builds its module in a module initialiser and hands it to
/// <c>Gst.GObject.TypeRegistry.RegisterModule</c>. Nothing in the module is
/// resolved at that point: the <c>get_type</c> functions are only called when
/// the registry is frozen, which happens after the native libraries are loaded.
/// </remarks>
public sealed class NativeModule
{
    private readonly ModuleTypeEntry[] _types;
    private readonly ModuleInterfaceEntry[] _interfaces;

    /// <summary>
    /// Initialises a new module description.
    /// </summary>
    /// <param name="logicalName">
    /// The logical library name of the module, as used by
    /// <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/> and
    /// by <see cref="NativeLoader"/>, for example <c>Gst</c>.
    /// </param>
    /// <param name="types">The types that the assembly wraps.</param>
    public NativeModule(string logicalName, ModuleTypeEntry[] types)
        : this(logicalName, types, [])
    {
    }

    /// <summary>
    /// Initialises a new module description that also names the GObject
    /// interfaces of the assembly.
    /// </summary>
    /// <param name="logicalName">
    /// The logical library name of the module, as used by
    /// <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/> and
    /// by <see cref="NativeLoader"/>, for example <c>Gst</c>.
    /// </param>
    /// <param name="types">The types that the assembly wraps.</param>
    /// <param name="interfaces">
    /// The GObject interfaces that the assembly binds, which
    /// <c>Gst.GObject.Object.As</c> can then cast to.
    /// </param>
    public NativeModule(string logicalName, ModuleTypeEntry[] types, ModuleInterfaceEntry[] interfaces)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(interfaces);

        LogicalName = logicalName;
        _types = types;
        _interfaces = interfaces;
    }

    /// <summary>
    /// Gets the logical library name of the module.
    /// </summary>
    public string LogicalName { get; }

    /// <summary>
    /// Gets the types that the assembly wraps.
    /// </summary>
    public ReadOnlySpan<ModuleTypeEntry> Types => _types;

    /// <summary>
    /// Gets the GObject interfaces that the assembly binds.
    /// </summary>
    public ReadOnlySpan<ModuleInterfaceEntry> Interfaces => _interfaces;
}
