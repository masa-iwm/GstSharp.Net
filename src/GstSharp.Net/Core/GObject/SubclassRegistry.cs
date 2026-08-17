using System.Collections.Concurrent;
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
    private nint _parentClass;
    private nuint _registeredType;

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
    /// Runs at the end of <c>class_init</c>, with the raw class pointer, for
    /// the class level configuration that GStreamer needs (metadata, pad
    /// templates). Stage 0 has no facade over it, see §5.5.
    /// </param>
    internal SubclassDescriptor(
        GType parentType,
        string typeName,
        VfuncSlot[] slots,
        Action<nint>? classInitializer = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(typeName);
        ArgumentNullException.ThrowIfNull(slots);

        ParentType = parentType;
        TypeName = typeName;
        _slots = slots;
        ClassInitializer = classInitializer;
    }

    /// <summary>Gets the type the subclass derives from.</summary>
    internal GType ParentType { get; }

    /// <summary>Gets the <c>GType</c> name the subclass is registered under.</summary>
    internal string TypeName { get; }

    /// <summary>Gets the hook that runs at the end of <c>class_init</c>.</summary>
    internal Action<nint>? ClassInitializer { get; }

    /// <summary>Gets the vfunc slots the subclass takes over.</summary>
    internal ReadOnlySpan<VfuncSlot> Slots => _slots;

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

    internal void SetRegisteredType(GType type) => Volatile.Write(ref _registeredType, type.Value);

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
internal readonly struct SubclassCtorArgs
{
    internal SubclassCtorArgs(nint handle) => Handle = handle;

    /// <summary>Gets the new instance.</summary>
    internal nint Handle { get; }

    /// <summary>
    /// Gets how ownership is transferred, which is always
    /// <see cref="Transfer.Full"/>: <c>g_object_new</c> hands its reference to
    /// the caller, and a floating one is sunk by the wrapper's constructor.
    /// </summary>
    internal Transfer Transfer => Transfer.Full;
}

/// <summary>
/// Registers managed subclasses of wrapped GObject types with GObject, and
/// creates their instances.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is internal: this is the stage 0 runtime of
/// <c>docs/subclassing.md</c>, proven by the integration tests and by the
/// NativeAOT smoke sample, and it promises no public API yet.
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

    private static long _nextClassDataId;

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

        nint handle = GObjectNative.ObjectNewWithProperties(type.Value, 0, null, null);
        if (handle == nint.Zero)
        {
            throw new InvalidOperationException($"g_object_new returned nothing for {type.Name}.");
        }

        return new SubclassCtorArgs(handle);
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

            descriptor.ClassInitializer?.Invoke(gClass);
        }
        catch (Exception exception)
        {
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
