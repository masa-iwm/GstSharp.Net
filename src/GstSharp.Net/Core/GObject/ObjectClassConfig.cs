using System.Globalization;
using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The class of a managed subclass while it is being initialised, at the
/// <c>GObject</c> level.
/// </summary>
/// <remarks>
/// <para>
/// An instance is handed to the <c>configureClass</c> delegate of
/// <c>DefineSubclass</c>, from inside <c>class_init</c>, and it is only usable
/// for the length of that call. A subclassable class whose ancestry reaches
/// <c>Gst.Element</c> is given the richer <see cref="ClassConfig"/>, which
/// derives from this one and adds the <c>GstElementClass</c> operations;
/// everything else — <c>Gst.Pad</c> and <c>GstBase.AggregatorPad</c> — is given
/// this facade, because the element operations would be written into a class
/// struct that has no such fields.
/// </para>
/// <para>
/// <b>Never create a wrapper from a class initialiser.</b> <c>class_init</c>
/// runs while GObject holds its type lock, and the wrapper interning table of
/// this binding is a lock that is taken <em>around</em> native calls, so
/// wrapping an object here would take the two locks in the reverse of the order
/// every other path takes them in. See <c>docs/subclassing.md</c> §5.5.
/// </para>
/// </remarks>
public class ObjectClassConfig
{
    private readonly nint _gClass;

    private readonly HashSet<uint> _propertyIds = [];

    /// <summary>The flags that say when the class handler of a signal runs.</summary>
    private const SignalFlags RunMask = SignalFlags.RunFirst | SignalFlags.RunLast | SignalFlags.RunCleanup;

    /// <summary>
    /// The bit that says a value of the type is borrowed for the emission,
    /// which is not part of the type itself.
    /// </summary>
    private const nuint SignalTypeStaticScope = 1;

    /// <summary>Wraps the class that is being initialised.</summary>
    /// <param name="gClass">The <c>GObjectClass</c> under construction.</param>
    /// <remarks>
    /// The constructor is internal: the facade is handed out by
    /// <c>class_init</c> and stands for a class that is being built at that
    /// very moment, so there is no state an application could construct one
    /// from.
    /// </remarks>
    internal ObjectClassConfig(nint gClass) => _gClass = gClass;

    /// <summary>Gets the class that is being initialised.</summary>
    internal nint GClass => _gClass;

    /// <summary>Gets the type that is being initialised.</summary>
    internal unsafe GType OwnType => new(((GTypeClassRaw*)_gClass)->GType);

    /// <summary>
    /// Installs a property on the class that is being initialised.
    /// </summary>
    /// <param name="propertyId">
    /// The identifier the property slots are given, which has to be greater
    /// than zero and unique within this class.
    /// </param>
    /// <param name="spec">
    /// The specification of the property, built with one of the
    /// <c>ParamSpecX.New</c> factories. The class takes its own references, so
    /// the wrapper may be disposed once this method has returned.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="propertyId"/> is zero.</exception>
    /// <exception cref="ArgumentException">
    /// The specification asks for <see cref="ParamFlags.Construct"/> or
    /// <see cref="ParamFlags.ConstructOnly"/>, it is neither
    /// <see cref="ParamFlags.Readable"/> nor <see cref="ParamFlags.Writable"/>,
    /// it has already been installed somewhere, the identifier is one this
    /// class already used, or this class already has a property of that name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The subclass did not declare
    /// <see cref="Object.SetPropertyOverride"/> or
    /// <see cref="Object.GetPropertyOverride"/> although the property is
    /// writable or readable. GObject would answer such a property out of the
    /// implementation of <c>GObject</c> itself, which knows nothing about it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Construct properties are refused.</b> GObject delivers every
    /// construct property — the value the caller named, or the default of the
    /// specification when it named none — from inside <c>g_object_new</c>,
    /// before anything managed can exist for an instance a C# constructor is
    /// building. The value would reach the managed setter of an instance native
    /// code created and vanish for one C# created, which is not a property but
    /// a coin toss. Take construct-time state through the constructor of the
    /// subclass instead.
    /// </para>
    /// <para>
    /// Redefining a property of an ancestor is legal and is what GObject calls
    /// an override by shadowing: the specification this class installs wins for
    /// every instance of this class, and the implementation of the ancestor is
    /// never consulted again — there is no chain up out of a property slot.
    /// </para>
    /// </remarks>
    public unsafe void InstallProperty(uint propertyId, ParamSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentOutOfRangeException.ThrowIfZero(propertyId);

        ParamFlags flags = spec.Flags;

        if ((flags & (ParamFlags.Construct | ParamFlags.ConstructOnly)) != 0)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' asks for CONSTRUCT or CONSTRUCT_ONLY, which a managed subclass cannot answer: "
                        + "GObject delivers construct properties before the wrapper of the instance exists.",
                    spec.Name),
                nameof(spec));
        }

        if ((flags & (ParamFlags.Readable | ParamFlags.Writable)) == 0)
        {
            // validate_pspec_to_install (gobject.c:1126-1140) asserts on this
            // and g_object_class_install_property answers nothing, so an
            // install would be a silent no-op that still left this class with
            // a specification it thinks it owns. The refusal comes before the
            // identifier is taken and before anything native is called, so a
            // caller may correct the flags and install the same specification.
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' is neither readable nor writable, so nothing could ever reach it.",
                    spec.Name),
                nameof(spec));
        }

        if (ParamSpec.ParamIdOf(spec.Handle) != 0)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property specification '{0}' has already been installed on '{1}'.",
                    spec.Name,
                    spec.OwnerType.IsValid ? spec.OwnerType.Name : "another class"),
                nameof(spec));
        }

        if (!_propertyIds.Add(propertyId))
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property identifier {0} was already used by this class.",
                    propertyId),
                nameof(propertyId));
        }

        GType ownType = OwnType;

        // The name needs no canonicalisation of its own: g_param_spec_internal
        // canonicalises it unless STATIC_NAME was asked for (gparam.c:476-479,
        // and New strips the static flags), and the pool retries a lookup with
        // the canonical form anyway (gparam.c:1110-1119).
        string canonical = spec.Name;

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using (Utf8Scope nameScope = GMarshal.StackUtf8(canonical, nameBuffer))
        {
            nint existing = GObjectNative.ObjectClassFindProperty(_gClass, nameScope.Pointer);
            if (existing != nint.Zero && ParamSpec.OwnerTypeOf(existing).Value == ownType.Value)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "This class already has a property called '{0}'.",
                        canonical),
                    nameof(spec));
            }
        }

        GObjectClassRaw* raw = (GObjectClassRaw*)_gClass;

        if ((flags & ParamFlags.Writable) != 0 && raw->SetProperty != Object.SetPropertyOverride.Function)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' is writable, so the subclass has to declare Object.SetPropertyOverride.",
                    canonical));
        }

        if ((flags & ParamFlags.Readable) != 0 && raw->GetProperty != Object.GetPropertyOverride.Function)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The property '{0}' is readable, so the subclass has to declare Object.GetPropertyOverride.",
                    canonical));
        }

        GObjectNative.ObjectClassInstallProperty(_gClass, propertyId, spec.Handle);

        // The installed specification lives as long as the class does, and the
        // property slots need a wrapper they may hand out on every call: one
        // that is built here, once, rather than one per call that nothing would
        // ever release.
        SubclassRegistry.RememberInstalledSpec(ownType, ParamSpec.FromNative(spec.Handle, Transfer.None));
    }

    /// <summary>
    /// Defines a signal on the class that is being initialised.
    /// </summary>
    /// <param name="name">
    /// The name of the signal, which has to be new in the whole ancestry of
    /// this class.
    /// </param>
    /// <param name="flags">
    /// When the class handler runs relative to the connected ones. At least one
    /// of <see cref="SignalFlags.RunFirst"/>, <see cref="SignalFlags.RunLast"/>
    /// and <see cref="SignalFlags.RunCleanup"/> has to be named.
    /// </param>
    /// <param name="returnType">
    /// The type the emission answers, or <see cref="GType.None"/> for a signal
    /// that answers nothing.
    /// </param>
    /// <param name="parameterTypes">The types of the arguments, without the instance.</param>
    /// <param name="classHandler">
    /// The class closure, which runs for every emission before or after the
    /// connected handlers, or <see langword="null"/> for a signal that has
    /// none.
    /// </param>
    /// <param name="accumulator">How the values of several handlers are folded.</param>
    /// <returns>The identifier of the new signal.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is <see langword="null"/> or empty, it is not a
    /// valid signal name, a signal of that name already exists on this class or
    /// one of its ancestors, no run flag was named, one of the types cannot be
    /// carried in a <c>GValue</c>, or an accumulator was asked for that the
    /// return type cannot serve.
    /// </exception>
    /// <exception cref="InvalidOperationException">GObject refused to create the signal.</exception>
    /// <remarks>
    /// <para>
    /// The signal is emitted with <c>Object.EmitSignal</c> and subscribed to
    /// with <c>Object.Connect</c>, exactly like a signal of a type GStreamer
    /// itself defines: nothing about it is managed once it exists.
    /// </para>
    /// <para>
    /// The class handler is invoked on the emitting thread, and it is given the
    /// wrapper of the instance the signal was emitted on — the interned one, so
    /// it is reference equal to whatever else holds that instance.
    /// <see cref="SignalFlags.MustCollect"/> is dropped: it describes a
    /// variadic collection this binding never performs.
    /// </para>
    /// </remarks>
    public unsafe uint AddSignal(
        string name,
        SignalFlags flags,
        GType returnType,
        ReadOnlySpan<GType> parameterTypes,
        DynamicSignalHandler? classHandler = null,
        SignalAccumulator accumulator = SignalAccumulator.None)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        GType ownType = OwnType;

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using (Utf8Scope nameScope = GMarshal.StackUtf8(name, nameBuffer))
        {
            if (GObjectNative.SignalIsValidName(nameScope.Pointer) == 0)
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture, "'{0}' is not a valid signal name.", name),
                    nameof(name));
            }

            if (GObjectNative.SignalLookup(nameScope.Pointer, ownType.Value) != 0)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "'{0}' already has a signal called '{1}'.",
                        ownType.Name,
                        name),
                    nameof(name));
            }
        }

        if ((flags & RunMask) == 0)
        {
            throw new ArgumentException(
                "A signal has to name one of RunFirst, RunLast and RunCleanup.",
                nameof(flags));
        }

        if (returnType.Value == GType.None.Value)
        {
            if (accumulator != SignalAccumulator.None)
            {
                throw new ArgumentException(
                    "A signal that returns nothing has nothing for an accumulator to fold.",
                    nameof(accumulator));
            }
        }
        else
        {
            RequireValueType(returnType, nameof(returnType), "The return type");
        }

        if (accumulator == SignalAccumulator.TrueHandled && returnType.Value != GType.Boolean.Value)
        {
            throw new ArgumentException(
                "The TrueHandled accumulator reads the answer of every handler as a boolean, "
                    + "so the signal has to return a boolean.",
                nameof(accumulator));
        }

        for (int i = 0; i < parameterTypes.Length; i++)
        {
            RequireValueType(parameterTypes[i], nameof(parameterTypes), "A parameter type");
        }

        nint closure = classHandler is null ? nint.Zero : DynamicSignalClosure.Create(classHandler, settle: false);
        uint signalId;

        try
        {
            signalId = NewSignal(name, ownType, flags, returnType, parameterTypes, closure, accumulator);
        }
        catch
        {
            DynamicSignalClosure.Sink(closure);
            throw;
        }

        if (signalId == 0)
        {
            // g_signal_newv only takes the closure over once every check has
            // passed, so a refusal leaves it floating and this frame owning it.
            DynamicSignalClosure.Sink(closure);

            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "GObject refused to create the signal '{0}' on '{1}'.",
                    name,
                    ownType.Name));
        }

        return signalId;
    }

    /// <summary>
    /// Calls <c>g_signal_newv</c> with the parameter types laid out the way it
    /// wants them.
    /// </summary>
    private static unsafe uint NewSignal(
        string name,
        GType ownType,
        SignalFlags flags,
        GType returnType,
        ReadOnlySpan<GType> parameterTypes,
        nint closure,
        SignalAccumulator accumulator)
    {
        nuint[]? types = parameterTypes.Length == 0 ? null : new nuint[parameterTypes.Length];

        for (int i = 0; i < parameterTypes.Length; i++)
        {
            types![i] = parameterTypes[i].Value;
        }

        Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope nameScope = GMarshal.StackUtf8(name, nameBuffer);

        fixed (nuint* typePointer = types)
        {
            return GObjectNative.SignalNewV(
                nameScope.Pointer,
                ownType.Value,
                (uint)(flags & ~SignalFlags.MustCollect),
                closure,
                SignalAccumulators.AddressOf(accumulator),
                nint.Zero,
                nint.Zero,
                returnType.Value,
                (uint)parameterTypes.Length,
                typePointer);
        }
    }

    /// <summary>
    /// Refuses a type that no <c>GValue</c> can carry, which
    /// <c>g_signal_newv</c> would only report as a critical.
    /// </summary>
    private static void RequireValueType(GType type, string parameterName, string what)
    {
        GType bare = new(type.Value & ~SignalTypeStaticScope);

        if (GObjectNative.TypeCheckIsValueType(bare.Value) == 0)
        {
            throw new ArgumentException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}, '{1}', cannot be carried in a GValue.",
                    what,
                    bare.IsValid ? bare.Name : "an invalid type"),
                parameterName);
        }
    }
}
