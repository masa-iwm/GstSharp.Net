using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// One vfunc slot that a managed subclass takes over, as the base class that
/// owns the slot hands it out.
/// </summary>
/// <remarks>
/// <para>
/// The values are obtained from the base class the slot belongs to —
/// <c>Gst.Element.ChangeStateOverride</c>,
/// <c>Gst.Base.PushSrc.CreateOverride</c> — and passed to that class's
/// <c>DefineSubclass</c>. Each one names the class it was declared on, so a
/// slot of an unrelated class is refused rather than written at its own offset
/// into a class struct it does not belong to.
/// </para>
/// <para>
/// Declaring a slot and overriding the matching <c>OnX</c> method are two
/// statements of the same fact, and keeping them in sync is the caller's job
/// until the analyzer of stage 2 checks it. Only declared slots are patched,
/// because GStreamer reads slot <em>presence</em>: a non-null
/// <c>BaseTransform.transform_ip</c> selects in place operation, a non-null
/// <c>PushSrc.create</c> says that the subclass produces its own buffers. See
/// <c>docs/subclassing.md</c> §4.2.
/// </para>
/// </remarks>
public readonly unsafe struct VfuncOverride
{
    private readonly delegate*<nuint> _declaringType;

    /// <summary>Declares one slot of one base class.</summary>
    /// <param name="declaringType">
    /// The <c>get_type</c> function of the class that owns the slot. It is a
    /// function pointer rather than a resolved type because these values are
    /// built in static initialisers, which run before the native libraries are
    /// guaranteed to be loaded; the type is resolved when the subclass is
    /// defined.
    /// </param>
    /// <param name="offset">The byte offset of the slot within the class struct.</param>
    /// <param name="function">
    /// The address of the <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute"/>
    /// static that implements the slot.
    /// </param>
    internal VfuncOverride(delegate*<nuint> declaringType, int offset, nint function)
    {
        _declaringType = declaringType;
        Offset = offset;
        Function = function;
    }

    /// <summary>Gets the byte offset of the slot within the class struct.</summary>
    internal int Offset { get; }

    /// <summary>Gets the unmanaged entry point that is written into the slot.</summary>
    internal nint Function { get; }

    /// <summary>Gets a value indicating whether this is a default value.</summary>
    internal bool IsEmpty => _declaringType is null;

    /// <summary>Gets the class that owns the slot.</summary>
    internal GType DeclaringType => new(_declaringType());
}

/// <summary>
/// A managed subclass of a wrapped GObject type, as it was registered with
/// GObject.
/// </summary>
/// <remarks>
/// <para>
/// One of these is produced by the <c>DefineSubclass</c> of the base class
/// being derived from, normally into a <see langword="static"/>
/// <see langword="readonly"/> field of the subclass, and it is what the
/// subclass's constructor creates instances through:
/// </para>
/// <code>
/// internal sealed class CounterSrc : PushSrc
/// {
///     private static readonly SubclassType Type = DefineSubclass(
///         "DemoCounterSrc", ConfigureClass, CreateOverride, StartOverride, StopOverride);
///
///     public CounterSrc()
///         : base(Type.NewInstance())
///     {
///     }
/// }
/// </code>
/// <para>
/// A registration lives for the process: static <c>GType</c>s cannot be
/// unregistered, so there is nothing to release and nothing to unload. See
/// <c>docs/subclassing.md</c> §8.
/// </para>
/// </remarks>
public sealed class SubclassType
{
    private readonly SubclassDescriptor _descriptor;

    private SubclassType(SubclassDescriptor descriptor, GType type)
    {
        _descriptor = descriptor;
        GType = type;
    }

    /// <summary>Gets the type the subclass was registered as.</summary>
    public GType GType { get; }

    /// <summary>Gets the <c>GType</c> name of the subclass.</summary>
    public string Name => _descriptor.TypeName;

    /// <summary>
    /// Creates an instance of the managed subclass.
    /// </summary>
    /// <returns>
    /// The arguments of the wrapper's constructor. The handle never appears in
    /// the subclass's own code: this is the whole reason the arguments are a
    /// type of their own rather than a raw pointer, see
    /// <c>docs/subclassing.md</c> §5.3.
    /// </returns>
    /// <exception cref="InvalidOperationException">GObject returned no instance.</exception>
    public SubclassCtorArgs NewInstance() => SubclassRegistry.NewInstance(GType);

    /// <summary>
    /// Registers a managed subclass with GObject and runs its
    /// <c>class_init</c>.
    /// </summary>
    /// <param name="parent">
    /// The type to derive from, which is the <c>GType</c> of the wrapped base
    /// class whose <c>DefineSubclass</c> is calling this.
    /// </param>
    /// <param name="typeName">The <c>GType</c> name, unique in the process.</param>
    /// <param name="configureClass">
    /// Configures the class while it is being initialised, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="overrides">The slots the subclass takes over.</param>
    /// <returns>The registration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="typeName"/> or <paramref name="overrides"/> is
    /// <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The type name is not a legal <c>GType</c> name, a declared slot belongs
    /// to a class the parent does not derive from, or a slot is declared twice.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The type name is taken, or the class initialiser failed.
    /// </exception>
    internal static SubclassType Define(
        GType parent,
        string typeName,
        Action<ClassConfig>? configureClass,
        VfuncOverride[] overrides)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        ArgumentNullException.ThrowIfNull(overrides);

        VfuncSlot[] slots = new VfuncSlot[overrides.Length];

        for (int i = 0; i < overrides.Length; i++)
        {
            VfuncOverride declared = overrides[i];

            if (declared.IsEmpty)
            {
                throw new ArgumentException(
                    "A declared slot is a default value. Use the values the base class hands out, such as " +
                    "Element.ChangeStateOverride.",
                    nameof(overrides));
            }

            // A slot is an offset into the class struct of the class that
            // declared it. Writing the trampoline of GstBaseSink.render at its
            // own offset into a GstPushSrcClass would corrupt whichever field
            // happens to sit there, so the parent has to be of that class.
            GType declaring = declared.DeclaringType;
            if (!parent.IsA(declaring))
            {
                throw new ArgumentException(
                    $"A slot of {declaring.Name} cannot be overridden on {parent.Name}, which does not derive " +
                    "from it. Declare only the slots of the class being derived from and of its ancestors.",
                    nameof(overrides));
            }

            for (int seen = 0; seen < i; seen++)
            {
                if (slots[seen].Offset == declared.Offset)
                {
                    throw new ArgumentException(
                        $"A slot of {declaring.Name} is declared twice. Every slot is written once.",
                        nameof(overrides));
                }
            }

            slots[i] = new VfuncSlot(declared.Offset, declared.Function);
        }

        SubclassDescriptor descriptor = new(parent, typeName, slots, configureClass);

        return new SubclassType(descriptor, SubclassRegistry.Register(descriptor));
    }

    /// <summary>
    /// Fails when the class initialiser did not add a pad template that the
    /// base class needs to create its pads.
    /// </summary>
    /// <param name="name">The name of the template, <c>src</c> or <c>sink</c>.</param>
    /// <remarks>
    /// <c>GstBaseSrc</c> fetches the <c>src</c> template of its class in its
    /// instance init, <c>GstBaseSink</c> the <c>sink</c> one and
    /// <c>GstBaseTransform</c> both. Without them the failure is a
    /// <c>g_return_if_fail</c> inside <c>g_object_new</c> and a half built
    /// element; checking here turns it into a message that says what is
    /// missing. See <c>docs/subclassing.md</c> §5.5.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The template is missing.</exception>
    internal unsafe void RequirePadTemplate(string name)
    {
        // The class exists and is referenced for the process: the registration
        // referenced it so that class_init ran there and then.
        nint elementClass = GObjectNative.TypeClassPeek(GType.Value);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(name, buffer);

        if (elementClass == nint.Zero || GstNative.ElementClassGetPadTemplate(elementClass, scope.Pointer) == nint.Zero)
        {
            throw new InvalidOperationException(
                $"\"{Name}\" has no \"{name}\" pad template. The base class creates its pad from that template " +
                "when an instance is built, so the class initialiser has to add one with " +
                $"ClassConfig.AddPadTemplate. See docs/subclassing.md §5.5.");
        }
    }
}
