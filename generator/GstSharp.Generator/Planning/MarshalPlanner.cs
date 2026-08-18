using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Planning;

/// <summary>
/// The type a callable is emitted into.
/// </summary>
/// <param name="Module">The module that is being emitted.</param>
/// <param name="Namespace">The gir namespace of the module.</param>
/// <param name="OwnerKind">The classification of the declaring type.</param>
/// <param name="OwnerType">The C# type of the declaring type, if any.</param>
/// <param name="SignalHost">
/// The C# type that carries the support declarations of the signals of the
/// declaring type: the arguments classes, the handler delegates and the
/// trampolines. It is the declaring type itself for a class, and the extension
/// class for a gir interface, which cannot carry them. Defaults to
/// <paramref name="OwnerType"/>.
/// </param>
internal readonly record struct PlanningContext(
    ModuleInfo Module,
    GirNamespace Namespace,
    TypeKind OwnerKind,
    string? OwnerType,
    string? SignalHost = null);

/// <summary>
/// The trampoline of one <c>&lt;callback&gt;</c>.
/// </summary>
internal sealed class CallbackPlan
{
    /// <summary>Gets the gir declaration.</summary>
    internal required GirCallback Callback { get; init; }

    /// <summary>Gets the C# name of the delegate type.</summary>
    internal required string DelegateName { get; init; }

    /// <summary>Gets the fully qualified C# name of the delegate type.</summary>
    internal required string DelegateType { get; init; }

    /// <summary>Gets the fully qualified C# name of the trampoline holder.</summary>
    internal required string TrampolineType { get; init; }

    /// <summary>Gets the arguments of the native signature, in gir order.</summary>
    internal required IReadOnlyList<ArgumentPlan> Arguments { get; init; }

    /// <summary>Gets the return value.</summary>
    internal required ReturnPlan Return { get; init; }
}

/// <summary>
/// Decides how each callable of a gir namespace is projected onto C#, and
/// rejects the ones whose signature this milestone cannot marshal.
/// </summary>
/// <remarks>
/// <para>
/// The planner is deliberately conservative: a callable is only planned when
/// every parameter and the return value have a marshalling that is known to be
/// correct. Everything else is reported as
/// <see cref="SkipReason.UnsupportedSignature"/> and left for a later
/// milestone. Half emitted members would compile and then corrupt memory,
/// which is much worse than a missing binding.
/// </para>
/// <para>
/// The rules that are not obvious from the code:
/// </para>
/// <list type="bullet">
/// <item><description>An <c>in</c> parameter that takes ownership of a handle
/// (<c>transfer-ownership="full"</c>) is rejected. The wrapper owns the only
/// reference it has, so handing it over would free the instance twice. Adding a
/// reference is not a fix either: <c>gst_caps_append</c> and its relatives empty
/// the instance they consume.</description></item>
/// <item><description>A <c>floating</c> parameter is passed as it is: every
/// wrapper sinks the floating reference when it is created, and the callee only
/// ever adds one of its own.</description></item>
/// <item><description>Reference typed <c>out</c> parameters are always nullable.
/// A call that fails leaves them untouched, so a non-null annotation of the gir
/// cannot be trusted for them. The arguments a callback receives are nullable
/// for the same reason: <c>gst_caps_foreach</c> passes a <c>NULL</c>
/// <c>GstCapsFeatures</c> for every structure that carries none.</description></item>
/// <item><description>A <c>caller-allocates</c> out parameter is only bound
/// when the storage the callee writes into has a C# spelling of the same size,
/// which is a plain struct. A record that is bound behind a handle is one
/// pointer wide in C# and several hundred bytes in C, so the call would write
/// past the end of the local it is given.</description></item>
/// <item><description>A pointer to a plain struct is passed by value: the
/// member copies the argument into a local and hands the address of that local
/// over, so a callee that writes through the pointer writes into a temporary
/// the caller never sees. Where the C function does write - <c>align</c> of
/// <c>gst_video_info_align</c> is updated with the padding the call raised -
/// the overlays give the parameter a <c>direction</c> of <c>out</c> or
/// <c>ref</c>, and the local becomes the caller's own storage. The correction
/// is refused for anything but a pointer to a plain struct, because every other
/// out shape needs a projection of its own.</description></item>
/// <item><description>A parameter the gir spells as a pointer to one value and
/// the C function fills with several is only bound when the overlays state how
/// many: <c>gst_video_format_info_component</c> writes four <c>gint</c> through
/// a <c>gint*</c>, so an <c>out int</c> would corrupt twelve bytes of the
/// caller's stack on every call. With a <c>fixedArraySize</c> the parameter is
/// planned as an <c>out</c> of an <c>[InlineArray]</c> struct of that length,
/// which is storage of the size the callee writes and says so at the call
/// site.</description></item>
/// <item><description>An instance the callable takes ownership of
/// (<c>transfer-ownership="full"</c> on the instance parameter), and a method
/// named <c>ref</c>, <c>unref</c> or <c>free</c>, are only bound on a wrapper
/// that owns nothing. Every other wrapper releases its reference when it is
/// disposed, so a second release path can only corrupt the reference
/// count.</description></item>
/// <item><description>A <c>GList</c> is bound in the return position only, and
/// only when its elements are wrappers the runtime knows how to adopt or
/// strings. The list is materialized eagerly and its spine is released before
/// the first element is adopted, so no managed value ever points into it. A
/// <c>GList</c> parameter and every <c>GSList</c> stay
/// unsupported.</description></item>
/// <item><description>Only the <c>call</c> and <c>notified</c> callback scopes
/// are supported. A <c>async</c> callback would have to release its state from
/// the trampoline, but the same delegate type is used at notified and async call
/// sites, so a single trampoline cannot decide that.</description></item>
/// </list>
/// </remarks>
internal sealed class MarshalPlanner
{
    private const string NativeInt = "nint";

    /// <summary>
    /// Handles of the hand written runtime that generated code may refer to even
    /// though their module is not generated.
    /// </summary>
    /// <remarks>
    /// The entries cover handles only, which is what <see cref="PlanHandle"/>
    /// consults. An enumeration of such a module is named by
    /// <see cref="RuntimeEnums"/> instead: a handle crosses as a pointer, an
    /// enumeration as its underlying integer, so the two need different plans.
    /// </remarks>
    private static readonly Dictionary<string, string> RuntimeTypes = new(StringComparer.Ordinal)
    {
        ["GObject.Object"] = "Gst.GObject.Object",
        ["GObject.InitiallyUnowned"] = "Gst.GObject.InitiallyUnowned",
        ["Gio.Cancellable"] = "Gst.Gio.Cancellable",
        ["Gio.Socket"] = "Gst.Gio.Socket",
        ["Gio.SocketAddress"] = "Gst.Gio.SocketAddress",
        ["Gio.SocketControlMessage"] = "Gst.Gio.SocketControlMessage",
        ["Gio.TlsCertificate"] = "Gst.Gio.TlsCertificate",
        ["Gio.TlsConnection"] = "Gst.Gio.TlsConnection",
        ["Gio.TlsDatabase"] = "Gst.Gio.TlsDatabase",
        ["Gio.TlsInteraction"] = "Gst.Gio.TlsInteraction",
    };

    /// <summary>
    /// Enumerations of the hand written runtime that generated code may refer to
    /// even though their module is not generated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The plan an entry produces is the one a generated enumeration gets: the
    /// public type is the hand written wrapper, and the value crosses as the
    /// underlying integer the gir declares for it, so the call site casts exactly
    /// as it would for an enumeration of a module that is emitted.
    /// </para>
    /// <para>
    /// That makes the underlying type of the hand written enumeration part of the
    /// contract of an entry: it has to be declared with the type
    /// <see cref="EnumFacts.GetUnderlyingType"/> derives from the members of the
    /// gir. <c>Gio.TlsCertificateFlags</c>, whose largest member is 127, is
    /// <see langword="int"/> on both sides.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, string> RuntimeEnums = new(StringComparer.Ordinal)
    {
        ["Gio.TlsCertificateFlags"] = "Gst.Gio.TlsCertificateFlags",
    };

    /// <summary>
    /// Wrappers that carry no typed <c>FromNative</c>, because they are hand
    /// written and abstract. They cannot appear in a generated signature.
    /// </summary>
    private static readonly HashSet<string> UnusableTypes = new(StringComparer.Ordinal)
    {
        "Gst.MiniObject",
    };

    /// <summary>The names a signal trampoline uses for its own parameters and locals.</summary>
    private static readonly HashSet<string> TrampolineLocals = new(StringComparer.Ordinal)
    {
        "instance", "userData", "handler", "sender", "exception",
    };

    /// <summary>The names every arguments class already carries from <c>object</c>.</summary>
    private static readonly HashSet<string> ArgsMemberNames = new(StringComparer.Ordinal)
    {
        "Equals", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "ToString",
    };

    /// <summary>
    /// The gir names of the methods that are a lifetime primitive of their
    /// declaring type rather than API. The gir annotates the instance of some
    /// of them <c>transfer-ownership="full"</c> and of others <c>none</c>
    /// (<c>gst_video_info_free</c> is one of the latter), so the annotation
    /// alone does not find them.
    /// </summary>
    private static readonly HashSet<string> LifetimePrimitives = new(StringComparer.Ordinal)
    {
        "free", "ref", "unref",
    };

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly TypeMap _types;
    private readonly Overlays _overlays;
    private readonly SkipRules _skipRules;
    private readonly DiagnosticBag _diagnostics;
    private readonly SortedDictionary<string, CallbackPlan> _callbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<GirCallback, CallbackPlan?> _callbackCache = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Why the callable that is being planned was rejected, when a rule has a
    /// more precise answer than <see cref="SkipReason.UnsupportedSignature"/>.
    /// The rules run deep inside the projection of a single argument, so the
    /// reason travels back to <see cref="TryPlan"/> through this field rather
    /// than through every return value on the way.
    /// </summary>
    private SkipReason? _rejection;

    /// <summary>Initializes a new instance of the <see cref="MarshalPlanner"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="types">The type map.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="skipRules">The skip rules.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal MarshalPlanner(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        TypeMap types,
        Overlays overlays,
        SkipRules skipRules,
        DiagnosticBag diagnostics)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _types = types;
        _overlays = overlays;
        _skipRules = skipRules;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the callbacks that at least one planned callable takes, ordered by
    /// name.
    /// </summary>
    internal IReadOnlyDictionary<string, CallbackPlan> UsedCallbacks => _callbacks;

    /// <summary>Plans one callable.</summary>
    /// <param name="callable">The callable to plan.</param>
    /// <param name="form">The C# shape it is emitted in.</param>
    /// <param name="context">The type it is emitted into.</param>
    /// <param name="reason">Why the callable is skipped, if it is.</param>
    /// <param name="ignoreShadowedBy">
    /// <see langword="true"/> to plan a callable that another one shadows. The
    /// caller passes this once it knows that the shadowing callable cannot be
    /// bound.
    /// </param>
    /// <returns>The plan, or <see langword="null"/> when the callable is skipped.</returns>
    internal MarshalPlan? TryPlan(
        GirCallable callable,
        CallableForm form,
        PlanningContext context,
        out SkipReason reason,
        bool ignoreShadowedBy = false)
    {
        _rejection = null;
        MarshalPlan? plan = TryPlanCore(callable, form, context, out reason, ignoreShadowedBy);
        if (plan is null && _rejection is { } rejected)
        {
            reason = rejected;
            ReportRejection(callable, rejected);
        }

        return plan;
    }

    private MarshalPlan? TryPlanCore(
        GirCallable callable,
        CallableForm form,
        PlanningContext context,
        out SkipReason reason,
        bool ignoreShadowedBy)
    {
        reason = _skipRules.GetSkipReason(callable, ignoreShadowedBy);
        if (reason != SkipReason.None)
        {
            return null;
        }

        if (callable.CIdentifier is not { Length: > 0 } entryPoint)
        {
            reason = SkipReason.NoCIdentifier;
            return null;
        }

        if (RejectsLifetime(callable, form, context))
        {
            return null;
        }

        reason = SkipReason.UnsupportedSignature;

        IReadOnlyList<GirParameter> parameters = callable.Parameters;
        ArgumentKind[] forced = new ArgumentKind[parameters.Count];
        int[] owners = new int[parameters.Count];
        Array.Fill(forced, ArgumentKind.Void);
        Array.Fill(owners, int.MinValue);

        if (!MarkHiddenArguments(callable, forced, owners))
        {
            return null;
        }

        List<ArgumentPlan> arguments = [];
        if (form is CallableForm.InstanceMethod or CallableForm.ExtensionMethod)
        {
            if (callable.InstanceParameter is null || context.OwnerType is null)
            {
                return null;
            }

            arguments.Add(new ArgumentPlan
            {
                Kind = ArgumentKind.Instance,
                Name = NameMapper.ParameterName(callable.InstanceParameter.Name),
                PublicType = context.OwnerType,
                RawType = NativeInt,
                IsHidden = form == CallableForm.InstanceMethod,
                Doc = callable.InstanceParameter.Doc,
            });
        }

        int offset = arguments.Count;
        for (int i = 0; i < parameters.Count; i++)
        {
            ArgumentPlan? argument = forced[i] switch
            {
                ArgumentKind.ArrayLength => PlanLength(parameters, i, context, owners[i], offset),
                ArgumentKind.UserData => PlanUserData(parameters[i], owners[i] + offset),
                ArgumentKind.DestroyNotify => PlanDestroyNotify(parameters[i], owners[i] + offset),
                _ => PlanParameter(callable, parameters[i], i, context, offset),
            };

            if (argument is null)
            {
                return null;
            }

            arguments.Add(argument);
        }

        ReturnPlan? returnPlan = PlanReturn(callable, context, offset);
        if (returnPlan is null)
        {
            return null;
        }

        if (callable.Throws)
        {
            arguments.Add(new ArgumentPlan
            {
                Kind = ArgumentKind.Error,
                Name = "error",
                RawType = NativeInt + "*",
                IsHidden = true,
            });
        }

        reason = SkipReason.None;
        return new MarshalPlan
        {
            Callable = callable,
            Form = form,
            Name = _names.CallableName(callable),
            EntryPoint = entryPoint,
            NativeName = NameMapper.EscapeIdentifier(NameMapper.ToPascalCase(entryPoint)),
            Arguments = arguments,
            Return = returnPlan,
            Throws = callable.Throws,
            InstanceType = form == CallableForm.ExtensionMethod ? context.OwnerType : null,
        };
    }

    /// <summary>
    /// Records why the callable that is being planned is rejected. The rules
    /// that find out are several calls deep, so the answer travels back to
    /// <see cref="TryPlan"/> through a field. The first rule to speak wins,
    /// which keeps the reason of a run independent of the order the arguments
    /// happen to be projected in.
    /// </summary>
    /// <param name="reason">Why the callable cannot be bound.</param>
    /// <returns>Always <see langword="null"/>, so that a rule can write <c>return Reject(...)</c>.</returns>
    private ArgumentPlan? Reject(SkipReason reason)
    {
        _rejection ??= reason;
        return null;
    }

    /// <summary>Reports a rejected callable, once per callable and reason.</summary>
    /// <param name="callable">The callable that is not bound.</param>
    /// <param name="reason">Why it is not bound.</param>
    private void ReportRejection(GirCallable callable, SkipReason reason)
    {
        string name = callable.CIdentifier ?? callable.Name;
        switch (reason)
        {
            case SkipReason.CallerAllocates:
                _diagnostics.Warn(
                    "GEN0012",
                    $"'{name}' has a caller-allocates out parameter whose storage has no C# spelling of the size "
                    + "of the C type; the callee would write past the end of the local. The member is skipped.");
                return;

            case SkipReason.InstanceTransferFull:
                _diagnostics.Warn(
                    "GEN0013",
                    $"'{name}' consumes the instance and returns a replacement of the same type; binding it needs "
                    + "the wrapper to reference the instance before the call. The member is skipped.");
                return;

            case SkipReason.LifetimePrimitive:
                _diagnostics.Warn(
                    "GEN0014",
                    $"'{name}' releases or references the instance, which the wrapper already does when it is "
                    + "disposed. The member is skipped.");
                return;

            default:
                return;
        }
    }

    /// <summary>
    /// Tests whether a callable takes part in the lifetime of its instance in a
    /// way the wrapper cannot allow.
    /// </summary>
    /// <param name="callable">The callable to inspect.</param>
    /// <param name="form">The C# shape it would be emitted in.</param>
    /// <param name="context">The type it is emitted into.</param>
    /// <returns><see langword="true"/> when the callable is rejected.</returns>
    /// <remarks>
    /// <para>
    /// Two shapes are told apart. <c>gst_caps_make_writable</c> and its
    /// relatives consume the instance and hand a replacement of the same type
    /// back; binding one needs the wrapper to add a reference before the call
    /// and to adopt the returned handle afterwards, so that the instance the
    /// caller holds stays valid whichever of the two the call returns. That is
    /// a change of the ownership model rather than of one signature, so they
    /// are rejected until it lands.
    /// </para>
    /// <para>
    /// Everything else that consumes the instance releases it, and so does
    /// every <c>ref</c>, <c>unref</c> and <c>free</c>. A wrapper that owns a
    /// reference releases it when it is disposed, so a second release path can
    /// only corrupt the reference count. A wrapper that owns nothing, which is
    /// what an opaque record gets, keeps them: <c>gst_poll_free</c> is the only
    /// way of releasing a <c>GstPoll</c>.
    /// </para>
    /// </remarks>
    private bool RejectsLifetime(GirCallable callable, CallableForm form, PlanningContext context)
    {
        if (form is not (CallableForm.InstanceMethod or CallableForm.ExtensionMethod)
            || callable.InstanceParameter is not { } instance)
        {
            return false;
        }

        bool consumes = instance.Transfer is GirTransfer.Full;
        if (consumes && ReturnsInstanceType(callable, instance, context))
        {
            _rejection ??= SkipReason.InstanceTransferFull;
            return true;
        }

        // The name alone is not enough: gst_allocator_free releases the memory
        // it is handed rather than the allocator it is called on. A lifetime
        // primitive takes nothing besides its instance.
        bool primitive = callable.Kind == GirCallableKind.Method
            && callable.Parameters.Count == 0
            && LifetimePrimitives.Contains(callable.Name);
        if ((consumes || primitive) && OwnsAReference(context.OwnerKind))
        {
            _rejection ??= SkipReason.LifetimePrimitive;
            return true;
        }

        return false;
    }

    /// <summary>Tests whether a wrapper of a kind owns the reference it holds.</summary>
    /// <param name="kind">The classification of the declaring type.</param>
    /// <returns><see langword="true"/> when the wrapper releases its instance when it is disposed.</returns>
    private static bool OwnsAReference(TypeKind kind) =>
        kind is TypeKind.GObjectClass or TypeKind.Interface or TypeKind.MiniObject or TypeKind.Boxed;

    /// <summary>
    /// Tests whether a callable hands back an owned handle of the type of its
    /// instance, which is what the <c>make_writable</c> family does.
    /// </summary>
    /// <param name="callable">The callable to inspect.</param>
    /// <param name="instance">Its instance parameter.</param>
    /// <param name="context">The type it is emitted into.</param>
    /// <returns><see langword="true"/> when the returned type is the type of the instance.</returns>
    private bool ReturnsInstanceType(GirCallable callable, GirInstanceParameter instance, PlanningContext context)
    {
        if (TransferOf(callable) is not GirTransfer.Full
            || instance.Type.Name is not { } instanceName
            || callable.ReturnValue.Type.Name is not { } returnName)
        {
            return false;
        }

        GirSymbol? instanceSymbol = _repository.Resolve(instanceName, context.Namespace);
        GirSymbol? returnSymbol = _repository.Resolve(returnName, context.Namespace);
        return instanceSymbol is not null
            && returnSymbol is not null
            && string.Equals(instanceSymbol.QualifiedName, returnSymbol.QualifiedName, StringComparison.Ordinal);
    }

    /// <summary>Plans the trampoline of a callback, caching the result.</summary>
    /// <param name="callback">The callback declaration.</param>
    /// <param name="context">The module that is being emitted.</param>
    /// <returns>The plan, or <see langword="null"/> when the callback cannot be bound.</returns>
    internal CallbackPlan? TryPlanCallback(GirCallback callback, PlanningContext context)
    {
        if (_callbackCache.TryGetValue(callback, out CallbackPlan? cached))
        {
            return cached;
        }

        CallbackPlan? plan = PlanCallbackCore(callback, context);
        _callbackCache[callback] = plan;
        return plan;
    }

    /// <summary>Plans the event of one <c>&lt;glib:signal&gt;</c>.</summary>
    /// <param name="signal">The signal declaration.</param>
    /// <param name="owner">The type that declares the signal.</param>
    /// <param name="context">The type the event is emitted into.</param>
    /// <param name="reason">Why the signal is skipped, if it is.</param>
    /// <returns>The plan, or <see langword="null"/> when the signal is skipped.</returns>
    internal SignalPlan? TryPlanSignal(
        GirSignal signal,
        GirTypeDeclaration owner,
        PlanningContext context,
        out SkipReason reason)
    {
        _rejection = null;
        reason = _skipRules.GetSkipReason(signal);
        if (reason != SkipReason.None)
        {
            return null;
        }

        // An action signal is a call API: g_signal_emit is how the C API of
        // GstAppSrc spells gst_app_src_push_buffer for language bindings that
        // have nothing better. The method it stands for is already bound, and
        // subscribing to it would connect a handler that native code never
        // raises.
        if (signal.IsAction)
        {
            reason = SkipReason.ActionSignal;
            return null;
        }

        reason = SkipReason.UnsupportedSignature;
        if (signal.Throws || context.OwnerType is not { } ownerType)
        {
            return null;
        }

        string host = context.SignalHost ?? ownerType;
        string name = _names.SignalName(context.Namespace, owner, signal);
        string? argsName = signal.Parameters.Count > 0 ? ArgsClassName(name) : null;
        HashSet<string> taken = new(ArgsMemberNames, StringComparer.Ordinal);
        if (argsName is not null)
        {
            taken.Add(argsName);
        }

        List<SignalArgument> arguments = [];
        foreach (GirParameter parameter in signal.Parameters)
        {
            ArgumentPlan? argument = PlanSignalArgument(parameter, context);

            // The trampoline names its own locals, and the arguments class
            // cannot carry two properties of one name or one that its own type
            // name would shadow.
            if (argument is null || TrampolineLocals.Contains(argument.Name))
            {
                return null;
            }

            string property = NameMapper.EscapeIdentifier(NameMapper.ToPascalCase(parameter.Name));
            if (!taken.Add(property))
            {
                return null;
            }

            arguments.Add(new SignalArgument(argument, property));
        }

        ReturnPlan? returnPlan = PlanSignalReturn(signal, context);
        if (returnPlan is null)
        {
            return null;
        }

        string? handlerName = returnPlan.IsVoid ? null : name + "Handler";
        string argsType = argsName is null ? "System.EventArgs" : host + "." + argsName;
        string trampolineName = name + "Trampoline";

        // A member cannot be named after the type that declares it.
        string simpleName = host[(host.LastIndexOf('.') + 1)..];
        foreach (string? member in new[] { name, argsName, handlerName, trampolineName })
        {
            if (string.Equals(member, simpleName, StringComparison.Ordinal))
            {
                return null;
            }
        }

        reason = SkipReason.None;
        return new SignalPlan
        {
            Signal = signal,
            SignalName = signal.Name,
            Name = name,
            ArgsName = argsName,
            ArgsType = argsType,
            TrampolineName = trampolineName,
            HandlerName = handlerName,
            EventType = handlerName is not null
                ? host + "." + handlerName
                : argsName is null ? "System.EventHandler" : "System.EventHandler<" + argsType + ">",
            Arguments = arguments,
            Return = returnPlan,
            IsDetailed = signal.IsDetailed,
        };
    }

    /// <summary>Returns the name of the class that carries the arguments of an event.</summary>
    /// <param name="eventName">The C# name of the event.</param>
    /// <returns>The name of the arguments class.</returns>
    /// <remarks>
    /// The name is derived from the resolved event name, which a rename in
    /// <c>fixups.json</c> may already have given a <c>Signal</c> suffix to
    /// (<c>Gst.Element::no-more-pads</c> becomes <c>NoMorePadsSignal</c>,
    /// because <c>NoMorePads</c> is taken). Appending <c>SignalArgs</c> to that
    /// would spell <c>NoMorePadsSignalSignalArgs</c>, so a trailing
    /// <c>Signal</c> is dropped first and the suffix is written exactly once.
    /// A collision that this creates is caught where every other one is, in the
    /// name check of the surface builder.
    /// </remarks>
    internal static string ArgsClassName(string eventName) =>
        (eventName.EndsWith("Signal", StringComparison.Ordinal)
            ? eventName[..^"Signal".Length]
            : eventName)
        + "SignalArgs";

    private static bool IsIntegral(MappedType mapped) =>
        mapped.Kind == MarshalKind.Blittable
        && mapped.RawType is "int" or "uint" or "long" or "ulong" or "short" or "ushort" or "sbyte" or "byte"
            or "nint" or "nuint";

    private static string? WrapperConversion(string publicType) => publicType switch
    {
        "Gst.ClockTime" => "Nanoseconds",
        "Gst.GObject.GType" => "Value",
        "Gst.GLib.Quark" => "Value",
        _ => null,
    };

    private static ArgumentPlan PlanUserData(GirParameter parameter, int owner) => new()
    {
        Source = parameter,
        Kind = ArgumentKind.UserData,
        Name = NameMapper.ParameterName(parameter.Name),
        RawType = NativeInt,
        IsHidden = true,
        OwnerArgument = owner,
    };

    private static ArgumentPlan PlanDestroyNotify(GirParameter parameter, int owner) => new()
    {
        Source = parameter,
        Kind = ArgumentKind.DestroyNotify,
        Name = NameMapper.ParameterName(parameter.Name),
        RawType = NativeInt,
        IsHidden = true,
        OwnerArgument = owner,
    };

    /// <summary>
    /// Marks the parameters that carry an array length, the user data of a
    /// callback or its destroy notification. Those never reach the public
    /// signature.
    /// </summary>
    /// <param name="callable">The callable to inspect.</param>
    /// <param name="forced">Receives the role of each parameter.</param>
    /// <param name="owners">Receives the array a length belongs to.</param>
    /// <returns><see langword="false"/> when the annotations contradict each other.</returns>
    private static bool MarkHiddenArguments(GirCallable callable, ArgumentKind[] forced, int[] owners)
    {
        IReadOnlyList<GirParameter> parameters = callable.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            GirParameter parameter = parameters[i];
            if (parameter.Type is GirArrayRef { LengthParameterIndex: int length })
            {
                if (length < 0 || length >= parameters.Count || length == i)
                {
                    return false;
                }

                forced[length] = ArgumentKind.ArrayLength;
                owners[length] = i;
            }

            if (parameter.ClosureIndex is int closure && closure != i)
            {
                if (closure < 0 || closure >= parameters.Count)
                {
                    return false;
                }

                forced[closure] = ArgumentKind.UserData;
                owners[closure] = i;
            }

            if (parameter.DestroyIndex is int destroy)
            {
                if (destroy < 0 || destroy >= parameters.Count || destroy == i)
                {
                    return false;
                }

                forced[destroy] = ArgumentKind.DestroyNotify;
                owners[destroy] = i;
            }
        }

        if (callable.ReturnValue.Type is GirArrayRef { LengthParameterIndex: int returnLength })
        {
            if (returnLength < 0 || returnLength >= parameters.Count)
            {
                return false;
            }

            forced[returnLength] = ArgumentKind.ArrayLength;
            owners[returnLength] = -1;
        }

        return true;
    }

    private static ArgumentDirection ToDirection(GirDirection direction) => direction switch
    {
        GirDirection.Out => ArgumentDirection.Out,
        GirDirection.InOut => ArgumentDirection.Ref,
        _ => ArgumentDirection.In,
    };

    /// <summary>
    /// Returns the name an annotation override of a callable is keyed by, or
    /// <see langword="null"/> when the callable carries no name an overlay
    /// could address.
    /// </summary>
    /// <param name="callable">The callable an annotation is read for.</param>
    /// <returns>The key prefix, without the <c>#</c> and the member.</returns>
    /// <remarks>
    /// A function is named by its <c>c:identifier</c>. A callback has none —
    /// the gir spells a <c>&lt;callback&gt;</c> with a <c>c:type</c> and
    /// nothing else — so it is addressed by that type, which is the name of the
    /// C typedef and is what a correction of the gir has to name. The two
    /// cannot collide: an identifier is <c>snake_case</c> and a callback type
    /// is <c>CamelCase</c>.
    /// </remarks>
    private static string? AnnotationKeyOf(GirCallable callable) =>
        callable.CIdentifier ?? (callable as GirCallback)?.CType;

    /// <summary>Reads the annotation correction of one parameter, if there is one.</summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter to look up.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    private AnnotationOverride? OverrideOf(GirCallable callable, GirParameter parameter) =>
        AnnotationKeyOf(callable) is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#" + parameter.Name)
            : null;

    private GirTransfer TransferOf(GirCallable callable, GirParameter parameter) =>
        ParseTransfer(OverrideOf(callable, parameter)?.Transfer) ?? parameter.Transfer;

    private GirTransfer TransferOf(GirCallable callable)
    {
        AnnotationOverride? overlay = AnnotationKeyOf(callable) is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#return")
            : null;

        return ParseTransfer(overlay?.Transfer) ?? callable.ReturnValue.Transfer;
    }

    private bool NullableOf(GirCallable callable, GirParameter parameter) =>
        OverrideOf(callable, parameter)?.Nullable ?? parameter.IsNullable;

    private bool CallerAllocatesOf(GirCallable callable, GirParameter parameter) =>
        OverrideOf(callable, parameter)?.CallerAllocates ?? parameter.IsCallerAllocates;

    private bool NullableOf(GirCallable callable)
    {
        AnnotationOverride? overlay = AnnotationKeyOf(callable) is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#return")
            : null;

        return overlay?.Nullable ?? callable.ReturnValue.IsNullable;
    }

    private static GirTransfer? ParseTransfer(string? value) => value switch
    {
        "none" => GirTransfer.None,
        "container" => GirTransfer.Container,
        "full" => GirTransfer.Full,
        "floating" => GirTransfer.Floating,
        _ => null,
    };

    private static ArgumentDirection? ParseDirection(string? value) => value switch
    {
        "in" => ArgumentDirection.In,
        "out" => ArgumentDirection.Out,
        "ref" or "inout" => ArgumentDirection.Ref,
        _ => null,
    };

    /// <summary>
    /// Tests whether a symbol is emitted by this run, so that generated code may
    /// name it.
    /// </summary>
    /// <param name="symbol">The symbol to test.</param>
    /// <returns><see langword="true"/> when the type exists in the output.</returns>
    private bool IsEmitted(GirSymbol symbol)
    {
        // Any module of the run may declare the type: GstAppSink returns a
        // Gst.FlowReturn and takes a Gst.Caps, and both are generated. Only the
        // GLib stack, whose runtime layer is hand written, is out of reach.
        if (ModuleMap.Find(symbol.Namespace.Name) is not { IsGenerated: true }
            || _overlays.IsSkipped(symbol.QualifiedName)
            || !symbol.Declaration.IsIntrospectable
            || (symbol.Declaration is GirRecord record && Classifier.IsPrivateShell(record)))
        {
            return false;
        }

        return _classifier.Classify(symbol.Declaration) is TypeKind.GObjectClass or TypeKind.MiniObject
            or TypeKind.Boxed or TypeKind.PlainStruct or TypeKind.OpaqueRecord or TypeKind.EnumType
            or TypeKind.FlagsType or TypeKind.Interface or TypeKind.Callback;
    }

    private ArgumentPlan? PlanLength(
        IReadOnlyList<GirParameter> parameters,
        int index,
        PlanningContext context,
        int owner,
        int offset)
    {
        GirParameter parameter = parameters[index];
        MappedType mapped = _types.Map(parameter.Type, context.Namespace);
        if (!IsIntegral(mapped) || owner == int.MinValue)
        {
            return null;
        }

        // The length of an array the call produces comes back through a
        // pointer; the length of an array the call reads is computed from the
        // span at the call site. A length that does not agree with its array,
        // as in gst_buffer_extract where the caller states the size of the
        // buffer it passes, is not one of the two.
        bool produced = owner < 0 || parameters[owner].Direction != GirDirection.In;
        if (produced != (parameter.Direction != GirDirection.In))
        {
            return null;
        }

        return new ArgumentPlan
        {
            Source = parameter,
            Kind = ArgumentKind.ArrayLength,
            Name = NameMapper.ParameterName(parameter.Name),
            RawType = produced ? mapped.RawType + "*" : mapped.RawType,
            PublicType = mapped.RawType,
            Direction = produced ? ArgumentDirection.Out : ArgumentDirection.In,
            IsHidden = true,
            OwnerArgument = owner < 0 ? -1 : owner + offset,
        };
    }

    private ArgumentPlan? PlanParameter(
        GirCallable callable,
        GirParameter parameter,
        int index,
        PlanningContext context,
        int offset)
    {
        if (parameter.IsVarArgs || parameter.Type.IsVarArgs)
        {
            return null;
        }

        string name = NameMapper.ParameterName(parameter.Name);
        ArgumentDirection direction = ToDirection(parameter.Direction);
        GirTransfer transfer = TransferOf(callable, parameter);
        bool nullable = NullableOf(callable, parameter);
        MappedType mapped = _types.Map(parameter.Type, context.Namespace);
        AnnotationOverride? overlay = OverrideOf(callable, parameter);

        if (mapped.Kind == MarshalKind.Callback)
        {
            return PlanCallbackArgument(parameter, name, context);
        }

        if (overlay?.FixedArraySize is int size
            && PlanFixedArray(callable, parameter, name, mapped, direction, size, context) is { } fixedArray)
        {
            return fixedArray;
        }

        direction = OverriddenDirection(callable, parameter, mapped, direction, overlay);

        if (parameter.Type is GirArrayRef array)
        {
            return PlanArrayArgument(
                parameter,
                array,
                mapped,
                name,
                direction,
                transfer,
                index,
                context,
                offset,
                CallerAllocatesOf(callable, parameter));
        }

        return PlanScalar(
            parameter.Type,
            mapped,
            name,
            direction,
            transfer,
            nullable,
            context,
            callerAllocates: CallerAllocatesOf(callable, parameter));
    }

    /// <summary>
    /// Applies the <c>direction</c> correction of the overlays to a parameter
    /// the gir spells as a bare pointer to a plain structure.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter being projected.</param>
    /// <param name="mapped">Its mapping.</param>
    /// <param name="declared">The direction the gir states.</param>
    /// <param name="overlay">The correction of the parameter, if any.</param>
    /// <returns>The direction the argument is passed in.</returns>
    /// <remarks>
    /// <para>
    /// The gir has one spelling for both halves of a structure pointer: the
    /// value a call reads and the storage it fills are both
    /// <c>&lt;type name="VideoAlignment" c:type="GstVideoAlignment*"/&gt;</c>
    /// with no direction on it. The planner passes such a parameter by value,
    /// which is right for the first half and silently wrong for the second: the
    /// copy the callee wrote into is a local of the member and is discarded when
    /// it returns.
    /// </para>
    /// <para>
    /// Which of the two a parameter is, is a fact about the C function, so it
    /// is stated in the overlays. <c>out</c> is the parameter the callee fills
    /// and <c>ref</c> the one it reads and updates; both make the argument the
    /// caller's own storage, which is what the C function was handed all along.
    /// </para>
    /// <para>
    /// The correction stops at a plain structure. A handle, a string, an array
    /// or a scalar has an out projection of its own with a conversion on either
    /// side of the call, and turning one into a bare pointer to managed storage
    /// would hand native code the address of something whose size and layout it
    /// does not agree with.
    /// </para>
    /// </remarks>
    private ArgumentDirection OverriddenDirection(
        GirCallable callable,
        GirParameter parameter,
        MappedType mapped,
        ArgumentDirection declared,
        AnnotationOverride? overlay)
    {
        if (ParseDirection(overlay?.Direction) is not { } overridden || overridden == declared)
        {
            return declared;
        }

        if (mapped.Kind != MarshalKind.PlainStruct || parameter.Type is GirArrayRef || !parameter.Type.IsPointer)
        {
            _diagnostics.Warn(
                "GEN0017",
                $"The direction override of '{AnnotationKeyOf(callable)}#{parameter.Name}' is ignored: only a "
                + "pointer to a plain structure can be re-planned as an out or a ref parameter.");
            return declared;
        }

        return overridden;
    }

    /// <summary>
    /// Plans a parameter the gir spells as a pointer to one value and the C
    /// function fills with a fixed number of them.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter being projected.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="mapped">Its mapping, whose element carries the payload.</param>
    /// <param name="direction">The direction the gir states.</param>
    /// <param name="size">The number of elements the callee writes.</param>
    /// <param name="context">The type the member is emitted into.</param>
    /// <returns>The plan, or <see langword="null"/> when the override does not apply.</returns>
    /// <remarks>
    /// <para>
    /// The storage is an <c>[InlineArray]</c> struct of the stated length,
    /// declared beside the members of the declaring type and named after the
    /// parameter, exactly as the inline storage of a fixed size field is. That
    /// makes the size part of the type the caller declares: there is no span
    /// whose length the caller has to know and no allocation per call, and the
    /// member cannot be called with storage that is too small.
    /// </para>
    /// <para>
    /// Only an out parameter of a blittable element is planned. The declaring
    /// type has to be known as well, because the storage type is nested in it;
    /// a global function has none, so the override is reported and ignored
    /// there rather than silently dropping the array somewhere else.
    /// </para>
    /// </remarks>
    private ArgumentPlan? PlanFixedArray(
        GirCallable callable,
        GirParameter parameter,
        string name,
        MappedType mapped,
        ArgumentDirection direction,
        int size,
        PlanningContext context)
    {
        if (size > 0
            && direction == ArgumentDirection.Out
            && parameter.Type is not GirArrayRef
            && parameter.Type.IsPointer
            && mapped.Kind == MarshalKind.Blittable
            && string.Equals(mapped.RawType, mapped.PublicType, StringComparison.Ordinal)
            && context.OwnerType is { } owner)
        {
            InlineArrayInfo storage = new(
                NameMapper.EscapeIdentifier(NameMapper.ToPascalCase(parameter.Name)) + "Array",
                mapped.PublicType,
                size);

            return new ArgumentPlan
            {
                Source = parameter,
                Kind = ArgumentKind.FixedArrayOut,
                Name = name,
                PublicType = owner + "." + storage.TypeName,
                RawType = owner + "." + storage.TypeName + "*",
                Direction = ArgumentDirection.Out,
                ElementType = mapped.PublicType,
                FixedArray = storage,
            };
        }

        _diagnostics.Warn(
            "GEN0017",
            $"The fixed array size of '{AnnotationKeyOf(callable)}#{parameter.Name}' is ignored: only an out "
            + "parameter of a blittable value, declared on a type that can carry the storage, is planned as a "
            + "caller allocated array.");
        return null;
    }

    /// <summary>
    /// Plans a scalar value, that is anything that is not an array and not a
    /// callback. The same projection is used for parameters, for return values
    /// and for the arguments of a callback.
    /// </summary>
    /// <param name="type">The gir type reference.</param>
    /// <param name="mapped">Its mapping.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="direction">How the argument is passed.</param>
    /// <param name="transfer">The ownership transfer.</param>
    /// <param name="nullable">Whether the value may be null.</param>
    /// <param name="context">The module that is being emitted.</param>
    /// <param name="isReturn">Whether the value is the return value of the callable.</param>
    /// <param name="callerAllocates">
    /// Whether the caller provides the storage of an out parameter, which only
    /// a plain struct can do.
    /// </param>
    /// <returns>The plan, or <see langword="null"/> when the type is not supported.</returns>
    private ArgumentPlan? PlanScalar(
        GirTypeRef type,
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        PlanningContext context,
        bool isReturn = false,
        bool callerAllocates = false)
    {
        bool byPointer = direction != ArgumentDirection.In;
        string pointerSuffix = byPointer ? "*" : string.Empty;

        switch (mapped.Kind)
        {
            case MarshalKind.Blittable when string.Equals(mapped.RawType, mapped.PublicType, StringComparison.Ordinal):
                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Value,
                    Name = name,
                    PublicType = mapped.PublicType,
                    RawType = mapped.RawType + pointerSuffix,
                    Direction = direction,
                };

            case MarshalKind.Blittable:
            case MarshalKind.GType:
            case MarshalKind.Quark:
                if (WrapperConversion(mapped.PublicType) is null)
                {
                    return null;
                }

                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Wrapper,
                    Name = name,
                    PublicType = mapped.PublicType,
                    RawType = mapped.RawType + pointerSuffix,
                    Direction = direction,
                };

            case MarshalKind.Boolean:
                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Boolean,
                    Name = name,
                    PublicType = "bool",
                    RawType = mapped.RawType + pointerSuffix,
                    Direction = direction,
                };

            case MarshalKind.Pointer:
                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Pointer,
                    Name = name,
                    PublicType = NativeInt,
                    RawType = NativeInt + pointerSuffix,
                    Direction = direction,
                };

            case MarshalKind.Enum:
            case MarshalKind.Flags:
                if (mapped.Symbol is not { } enumeration)
                {
                    return null;
                }

                // The hand written enumerations come first, exactly as they do
                // for a handle: their module emits nothing, so IsEmitted rejects
                // them although the runtime declares them.
                if (!RuntimeEnums.TryGetValue(enumeration.QualifiedName, out string? runtimeEnum)
                    && !IsEmitted(enumeration))
                {
                    return null;
                }

                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Enumeration,
                    Name = name,
                    PublicType = runtimeEnum ?? mapped.PublicType,
                    RawType = mapped.RawType + pointerSuffix,
                    Direction = direction,
                };

            case MarshalKind.Utf8String:
            case MarshalKind.FilenameString:
                if (direction == ArgumentDirection.Ref)
                {
                    return null;
                }

                if (direction == ArgumentDirection.Out)
                {
                    return new ArgumentPlan
                    {
                        Kind = ArgumentKind.Utf8,
                        Name = name,
                        PublicType = "string?",
                        RawType = NativeInt + "*",
                        Direction = direction,
                        Transfer = transfer,
                        IsNullable = true,
                    };
                }

                return new ArgumentPlan
                {
                    Kind = transfer == GirTransfer.Full ? ArgumentKind.Utf8Owned : ArgumentKind.Utf8,
                    Name = name,
                    PublicType = nullable ? "string?" : "string",
                    RawType = transfer == GirTransfer.Full ? NativeInt : "byte*",
                    Direction = direction,
                    Transfer = transfer,
                    IsNullable = nullable,
                };

            case MarshalKind.GObject:
            case MarshalKind.MiniObject:
            case MarshalKind.Boxed:
            case MarshalKind.OpaqueRecord:
                return PlanHandle(mapped, name, direction, transfer, nullable, isReturn, callerAllocates);

            case MarshalKind.PlainStruct:
                if (mapped.Symbol is not { } record || !IsEmitted(record))
                {
                    return null;
                }

                if (!type.IsPointer && direction == ArgumentDirection.In)
                {
                    return new ArgumentPlan
                    {
                        Kind = ArgumentKind.PlainStruct,
                        Name = name,
                        PublicType = mapped.PublicType,
                        RawType = mapped.PublicType,
                        Direction = direction,
                    };
                }

                if (!type.IsPointer)
                {
                    return null;
                }

                return new ArgumentPlan
                {
                    Kind = ArgumentKind.PlainStruct,
                    Name = name,
                    PublicType = mapped.PublicType,
                    RawType = mapped.PublicType + "*",
                    Direction = direction,
                };

            default:
                return null;
        }
    }

    private ArgumentPlan? PlanHandle(
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        bool isReturn,
        bool callerAllocates)
    {
        if (mapped.Symbol is not { } symbol || UnusableTypes.Contains(mapped.PublicType))
        {
            return null;
        }

        HandleFlavor flavor;
        string publicType;
        if (RuntimeTypes.TryGetValue(symbol.QualifiedName, out string? runtimeType))
        {
            flavor = HandleFlavor.GObject;
            publicType = runtimeType;
        }
        else if (!IsEmitted(symbol))
        {
            return null;
        }
        else
        {
            flavor = mapped.Kind switch
            {
                MarshalKind.GObject => HandleFlavor.GObject,
                MarshalKind.OpaqueRecord => HandleFlavor.Opaque,
                _ => HandleFlavor.Wrapper,
            };
            publicType = mapped.PublicType;
        }

        if (direction == ArgumentDirection.Ref)
        {
            return null;
        }

        if (direction == ArgumentDirection.Out)
        {
            // The callee writes a whole C structure into storage the caller
            // provides. A handle is one pointer wide in C#, and a GstVideoFrame
            // is some six hundred bytes, so the call would run off the end of
            // the local it is handed. Only a plain struct, which C# spells with
            // the size of the C type, can carry that contract.
            if (callerAllocates)
            {
                return Reject(SkipReason.CallerAllocates);
            }

            return new ArgumentPlan
            {
                Kind = ArgumentKind.Handle,
                Name = name,
                PublicType = publicType + "?",
                RawType = NativeInt + "*",
                Direction = direction,
                Transfer = transfer,
                Flavor = flavor,
                IsNullable = true,
            };
        }

        // Handing the only reference of a wrapper over would free the instance
        // twice; a floating reference is safe, because every wrapper sinks it
        // when it is created. A returned handle is the other way round: the
        // wrapper adopts whatever the call transferred.
        if (!isReturn && transfer is GirTransfer.Full or GirTransfer.Container)
        {
            return null;
        }

        return new ArgumentPlan
        {
            Kind = ArgumentKind.Handle,
            Name = name,
            PublicType = nullable ? publicType + "?" : publicType,
            RawType = NativeInt,
            Direction = direction,
            Transfer = transfer,
            Flavor = flavor,
            IsNullable = nullable,
        };
    }

    private ArgumentPlan? PlanArrayArgument(
        GirParameter parameter,
        GirArrayRef array,
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        int index,
        PlanningContext context,
        int offset,
        bool callerAllocates)
    {
        _ = index;

        if (mapped.Kind != MarshalKind.Array || array.FixedSize is not null || mapped.ElementType is not { } element)
        {
            return null;
        }

        // An out array the caller allocates is written into storage the caller
        // provides; there is no pointer coming back that a managed array could
        // be copied out of, so reading one would read the storage as a pointer.
        if (callerAllocates && direction != ArgumentDirection.In)
        {
            return Reject(SkipReason.CallerAllocates);
        }

        // A NULL terminated array of strings is the one container that the
        // runtime knows how to read.
        if (element.Kind is MarshalKind.Utf8String or MarshalKind.FilenameString)
        {
            if (!array.IsZeroTerminated || direction == ArgumentDirection.In)
            {
                return null;
            }

            return new ArgumentPlan
            {
                Source = parameter,
                Kind = ArgumentKind.Strv,
                Name = name,
                PublicType = "string[]?",
                RawType = NativeInt + "*",
                Direction = ArgumentDirection.Out,
                Transfer = transfer,
                IsNullable = true,
            };
        }

        if (element.Kind != MarshalKind.Blittable
            || !string.Equals(element.RawType, element.PublicType, StringComparison.Ordinal)
            || array.LengthParameterIndex is not int length)
        {
            return null;
        }

        if (direction == ArgumentDirection.In)
        {
            // An array the callee takes over cannot be a span: the caller keeps
            // owning the memory a span points at, and freeing it inside the
            // library would corrupt the heap.
            if (transfer is GirTransfer.Full or GirTransfer.Container)
            {
                return null;
            }

            // The gir spells several output buffers as plain in parameters
            // (gst_control_source_get_value_array fills the array it is given),
            // so a writable span is only ruled out by a const C type.
            bool readOnly = array.CType?.Contains("const", StringComparison.Ordinal) ?? false;
            return new ArgumentPlan
            {
                Source = parameter,
                Kind = ArgumentKind.Span,
                Name = name,
                PublicType = (readOnly ? "System.ReadOnlySpan<" : "System.Span<") + element.PublicType + ">",
                RawType = element.RawType + "*",
                Direction = ArgumentDirection.In,
                ElementType = element.PublicType,
                LengthArgument = length + offset,
            };
        }

        return new ArgumentPlan
        {
            Source = parameter,
            Kind = ArgumentKind.ArrayOut,
            Name = name,
            PublicType = element.PublicType + "[]?",
            RawType = NativeInt + "*",
            Direction = ArgumentDirection.Out,
            Transfer = transfer,
            ElementType = element.PublicType,
            LengthArgument = length + offset,
            IsNullable = true,
        };
    }

    private ArgumentPlan? PlanCallbackArgument(GirParameter parameter, string name, PlanningContext context)
    {
        if (parameter.Scope is not (GirScope.Call or GirScope.Notified)
            || parameter.ClosureIndex is null
            || (parameter.Scope == GirScope.Notified && parameter.DestroyIndex is null))
        {
            return null;
        }

        GirSymbol? symbol = _repository.Resolve(parameter.Type.Name, context.Namespace);
        if (symbol is not { Declaration: GirCallback callback } || !IsEmitted(symbol))
        {
            return null;
        }

        CallbackPlan? plan = TryPlanCallback(callback, context);
        if (plan is null)
        {
            return null;
        }

        _callbacks[plan.DelegateName] = plan;
        return new ArgumentPlan
        {
            Source = parameter,
            Kind = ArgumentKind.Callback,
            Name = name,
            PublicType = plan.DelegateType,
            RawType = NativeInt,
            Scope = parameter.Scope,
            DelegateType = plan.DelegateType,
            TrampolineType = plan.TrampolineType,
            Doc = parameter.Doc,
        };
    }

    private ReturnPlan? PlanReturn(GirCallable callable, PlanningContext context, int offset)
    {
        GirReturnValue value = callable.ReturnValue;
        MappedType mapped = _types.Map(value.Type, context.Namespace);
        GirTransfer transfer = TransferOf(callable);
        bool nullable = NullableOf(callable);

        if (mapped.Kind == MarshalKind.Void)
        {
            return new ReturnPlan
            {
                Kind = ArgumentKind.Void,
                PublicType = "void",
                RawType = "void",
                Doc = value.Doc,
            };
        }

        // Before the array branch: a gir may spell a GLib container as an
        // <array name="GLib.List">, and reading that as a C array would take the
        // head of a linked list for the first element of a block.
        if (mapped.Kind == MarshalKind.GList)
        {
            return PlanListReturn(value, mapped, transfer);
        }

        if (value.Type is GirArrayRef array)
        {
            if (mapped.ElementType is not { } element || array.FixedSize is not null)
            {
                return null;
            }

            if (element.Kind is MarshalKind.Utf8String or MarshalKind.FilenameString)
            {
                if (!array.IsZeroTerminated)
                {
                    return null;
                }

                return new ReturnPlan
                {
                    Kind = ArgumentKind.Strv,
                    PublicType = "string[]?",
                    RawType = NativeInt,
                    Transfer = transfer,
                    IsNullable = true,
                    Doc = value.Doc,
                };
            }

            if (element.Kind != MarshalKind.Blittable
                || !string.Equals(element.RawType, element.PublicType, StringComparison.Ordinal)
                || array.LengthParameterIndex is not int length)
            {
                return null;
            }

            return new ReturnPlan
            {
                Kind = ArgumentKind.ArrayOut,
                PublicType = element.PublicType + "[]?",
                RawType = NativeInt,
                Transfer = transfer,
                ElementType = element.PublicType,
                LengthArgument = length + offset,
                IsNullable = true,
                Doc = value.Doc,
            };
        }

        // A returned string is read and, when the call transfers it, released;
        // the ownership rules of a string parameter do not apply to it.
        if (mapped.Kind is MarshalKind.Utf8String or MarshalKind.FilenameString)
        {
            return new ReturnPlan
            {
                Kind = ArgumentKind.Utf8,
                PublicType = nullable ? "string?" : "string",
                RawType = NativeInt,
                Transfer = transfer,
                IsNullable = nullable,
                Doc = value.Doc,
            };
        }

        ArgumentPlan? scalar = PlanScalar(
            value.Type,
            mapped,
            "result",
            ArgumentDirection.In,
            transfer,
            nullable,
            context,
            isReturn: true);

        if (scalar is null)
        {
            return null;
        }

        // A returned structure is only understood when it comes back by value.
        if (scalar.Kind == ArgumentKind.PlainStruct && value.Type.IsPointer)
        {
            return null;
        }

        return new ReturnPlan
        {
            Kind = scalar.Kind,
            PublicType = OverriddenReturnType(callable, context, scalar),
            RawType = scalar.RawType,
            Transfer = transfer,
            IsNullable = nullable,
            Flavor = scalar.Flavor,
            Doc = value.Doc,
        };
    }

    /// <summary>
    /// Plans a <c>GList</c> that a call hands back.
    /// </summary>
    /// <param name="value">The gir return value.</param>
    /// <param name="mapped">Its mapping, whose element type carries the payload.</param>
    /// <param name="transfer">What the call transfers along with the list.</param>
    /// <returns>The plan, or <see langword="null"/> when the element is not supported.</returns>
    /// <remarks>
    /// <para>
    /// Only the return position is planned. A <c>GList</c> parameter would have
    /// to be built in managed code and handed over, which needs an allocation
    /// the callee agrees with and an ownership rule per callee, so
    /// <c>gst_element_factory_list_filter</c> and its relatives stay skipped. A
    /// <c>GSList</c> is skipped as well: the only one in a bound module carries
    /// an element this projection would refuse anyway.
    /// </para>
    /// <para>
    /// The element decides everything: a wrapper that the runtime can adopt
    /// (a <c>GObject</c>, a mini object or a boxed record), an opaque record, or
    /// a string. Anything else, a plain record above all, is refused, because
    /// there is no projection of a bare pointer into it that the generator can
    /// check. The list itself is never nullable on the public surface:
    /// <c>NULL</c> is how C spells the empty list, so the member returns an
    /// empty list rather than <see langword="null"/>.
    /// </para>
    /// <para>
    /// <paramref name="transfer"/> is carried through unchanged and the emitter
    /// reads both halves of it: <c>full</c> owns the spine and the elements,
    /// <c>container</c> owns the spine alone, and <c>none</c> owns neither and
    /// leaves the spine to the library. That second half is what bounds the
    /// opaque element: the wrapper of an opaque record is a bare pointer holder
    /// that owns nothing and is never disposed, so a list that hands its
    /// elements over has nobody to release them and is refused. Only a list
    /// whose elements stay the library's - <c>none</c> and <c>container</c> - is
    /// planned, which is what <c>gst_element_factory_get_static_pad_templates</c>
    /// returns: a <c>const GList</c> of the static pad templates the factory
    /// keeps in its own storage.
    /// </para>
    /// </remarks>
    private ReturnPlan? PlanListReturn(GirReturnValue value, MappedType mapped, GirTransfer transfer)
    {
        if (mapped.ElementType is not { } element)
        {
            return null;
        }

        ArgumentKind elementKind;
        HandleFlavor flavor = HandleFlavor.None;

        switch (element.Kind)
        {
            case MarshalKind.Utf8String:
                elementKind = ArgumentKind.Utf8;
                break;

            case MarshalKind.OpaqueRecord when transfer is GirTransfer.Full or GirTransfer.Floating:
                return null;

            case MarshalKind.GObject:
            case MarshalKind.MiniObject:
            case MarshalKind.Boxed:
            case MarshalKind.OpaqueRecord:
                if (element.Symbol is not { } symbol
                    || !IsEmitted(symbol)
                    || UnusableTypes.Contains(element.PublicType))
                {
                    return null;
                }

                elementKind = ArgumentKind.Handle;
                flavor = element.Kind switch
                {
                    MarshalKind.GObject => HandleFlavor.GObject,
                    MarshalKind.OpaqueRecord => HandleFlavor.Opaque,
                    _ => HandleFlavor.Wrapper,
                };
                break;

            default:
                return null;
        }

        return new ReturnPlan
        {
            Kind = ArgumentKind.GListReturn,
            PublicType = mapped.PublicType,
            RawType = NativeInt,
            Transfer = transfer,
            ElementType = element.PublicType,
            ElementKind = elementKind,
            Flavor = flavor,
            IsNullable = false,
            Doc = value.Doc,
        };
    }

    /// <summary>
    /// Narrows the C# type of a returned handle onto the type the overlays name.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="context">The type it is emitted into.</param>
    /// <param name="scalar">The projection of the returned value.</param>
    /// <returns>The C# type of the return value.</returns>
    /// <remarks>
    /// The gir spells the factory of a subclass with the return type of its
    /// base: <c>gst_pipeline_new</c> returns a <c>GstElement*</c>, although it
    /// only ever returns a <c>GstPipeline</c>. The override only narrows the
    /// type onto the one that declares the member, because that is the one the
    /// C implementation is known to construct; anything else would be a claim
    /// the generator cannot check.
    /// </remarks>
    private string OverriddenReturnType(GirCallable callable, PlanningContext context, ArgumentPlan scalar)
    {
        if (callable.CIdentifier is not { } identifier
            || !_overlays.TryGetReturnTypeOverride(identifier, out string? overridden))
        {
            return scalar.PublicType;
        }

        if (scalar.Kind != ArgumentKind.Handle || scalar.Flavor != HandleFlavor.GObject)
        {
            _diagnostics.Warn(
                "GEN0015",
                $"The return type override of '{identifier}' is ignored: only a returned GObject can be narrowed.");
            return scalar.PublicType;
        }

        if (!string.Equals(overridden, context.OwnerType, StringComparison.Ordinal))
        {
            _diagnostics.Warn(
                "GEN0015",
                $"The return type override of '{identifier}' names '{overridden}', which is not the declaring type "
                + $"'{context.OwnerType}'; the override is ignored.");
            return scalar.PublicType;
        }

        return scalar.PublicType.EndsWith('?') ? overridden + "?" : overridden;
    }

    private CallbackPlan? PlanCallbackCore(GirCallback callback, PlanningContext context)
    {
        if (callback.IsFieldSlot || callback.Throws || callback.HasVarArgs || !callback.IsIntrospectable)
        {
            return null;
        }

        GirSymbol? symbol = _repository.Resolve(callback.Name, context.Namespace);
        if (symbol is null || symbol.Declaration != callback || !IsEmitted(symbol))
        {
            return null;
        }

        string name = _names.TypeName(symbol);
        List<ArgumentPlan> arguments = [];
        bool sawUserData = false;

        for (int i = 0; i < callback.Parameters.Count; i++)
        {
            GirParameter parameter = callback.Parameters[i];
            if (parameter.ClosureIndex == i)
            {
                sawUserData = true;
                arguments.Add(PlanUserData(parameter, -1));
                continue;
            }

            if (parameter.Direction != GirDirection.In || parameter.Type is GirArrayRef)
            {
                return null;
            }

            MappedType mapped = _types.Map(parameter.Type, context.Namespace);
            ArgumentPlan? argument = PlanScalar(
                parameter.Type,
                mapped,
                NameMapper.ParameterName(parameter.Name),
                ArgumentDirection.In,
                parameter.Transfer,
                NullableOf(callback, parameter),
                context);

            if (argument is null || argument.Kind is ArgumentKind.Utf8Owned or ArgumentKind.Callback)
            {
                return null;
            }

            // A structure reaches the callback through a pointer into memory
            // that the caller owns, so the delegate takes it by reference.
            if (argument.Kind == ArgumentKind.PlainStruct)
            {
                if (!parameter.Type.IsPointer)
                {
                    return null;
                }

                argument = new ArgumentPlan
                {
                    Source = parameter,
                    Kind = ArgumentKind.PlainStruct,
                    Name = argument.Name,
                    PublicType = argument.PublicType,
                    RawType = argument.PublicType + "*",
                    Direction = ArgumentDirection.Ref,
                    Doc = parameter.Doc,
                };
            }
            else
            {
                // The nullability of the delegate is the one the gir states: a
                // parameter without a nullable annotation is a value the C
                // contract promises, and a handler that has to null check it
                // anyway is a handler that cannot say what it means. Native
                // code that passes NULL there all the same is caught by the
                // trampoline, which reports it through the trap and answers the
                // failure value without calling the handler at all, rather than
                // handing a null to a signature that excludes it.
                //
                // Where the gir is known to be wrong the correction belongs in
                // the annotation overrides, keyed by the c:type of the callback:
                // gst_caps_foreach hands its callback a NULL GstCapsFeatures for
                // every structure that carries none, and the three caps
                // callbacks are marked nullable there for it.
                argument = new ArgumentPlan
                {
                    Source = parameter,
                    Kind = argument.Kind,
                    Name = argument.Name,
                    PublicType = argument.PublicType,
                    RawType = argument.RawType,
                    Direction = ArgumentDirection.In,
                    Transfer = argument.Transfer,
                    Flavor = argument.Flavor,
                    IsNullable = argument.IsNullable,
                    Doc = parameter.Doc,
                };
            }

            // A handle the callback receives is only borrowed; taking ownership
            // of it would free what the caller still uses.
            if (argument.Kind == ArgumentKind.Handle && argument.Transfer == GirTransfer.Full)
            {
                return null;
            }

            arguments.Add(argument);
        }

        if (!sawUserData)
        {
            return null;
        }

        MappedType returnMapped = _types.Map(callback.ReturnValue.Type, context.Namespace);
        ReturnPlan returnPlan;
        if (returnMapped.Kind == MarshalKind.Void)
        {
            returnPlan = new ReturnPlan
            {
                Kind = ArgumentKind.Void,
                PublicType = "void",
                RawType = "void",
                Doc = callback.ReturnValue.Doc,
            };
        }
        else
        {
            ArgumentPlan? scalar = PlanScalar(
                callback.ReturnValue.Type,
                returnMapped,
                "result",
                ArgumentDirection.In,
                callback.ReturnValue.Transfer,
                callback.ReturnValue.IsNullable,
                context);

            if (scalar is null
                || scalar.Kind is not (ArgumentKind.Value or ArgumentKind.Boolean or ArgumentKind.Enumeration
                    or ArgumentKind.Wrapper or ArgumentKind.Pointer))
            {
                return null;
            }

            returnPlan = new ReturnPlan
            {
                Kind = scalar.Kind,
                PublicType = scalar.PublicType,
                RawType = scalar.RawType,
                Doc = callback.ReturnValue.Doc,
            };
        }

        return new CallbackPlan
        {
            Callback = callback,
            DelegateName = name,
            DelegateType = context.Module.ClrNamespace + "." + name,
            TrampolineType = context.Module.ClrNamespace + "." + name + "Trampoline",
            Arguments = arguments,
            Return = returnPlan,
        };
    }

    /// <summary>
    /// Plans one argument of a signal. Everything a handler receives is
    /// borrowed for the duration of the emission, exactly like the arguments of
    /// a callback, so an argument that transfers ownership is rejected instead
    /// of guessed at.
    /// </summary>
    /// <param name="parameter">The gir parameter.</param>
    /// <param name="context">The module that is being emitted.</param>
    /// <returns>The plan, or <see langword="null"/> when the argument is not supported.</returns>
    private ArgumentPlan? PlanSignalArgument(GirParameter parameter, PlanningContext context)
    {
        if (parameter.IsVarArgs
            || parameter.Type.IsVarArgs
            || parameter.Direction != GirDirection.In
            || parameter.Type is GirArrayRef)
        {
            return null;
        }

        string name = NameMapper.ParameterName(parameter.Name);
        MappedType mapped = _types.Map(parameter.Type, context.Namespace);

        // A notify style signal hands the handler the GParamSpec of the
        // property that changed. It is neither a GObject nor a generated
        // record, but the runtime wraps it, so it is planned here rather than
        // in the shared scalar projection, which would let it into every
        // method signature too.
        if (mapped.Symbol is { QualifiedName: "GObject.ParamSpec" } && parameter.Transfer == GirTransfer.None)
        {
            return new ArgumentPlan
            {
                Source = parameter,
                Kind = ArgumentKind.Handle,
                Name = name,
                PublicType = "Gst.GObject.ParamSpec",
                RawType = NativeInt,
                Transfer = GirTransfer.None,
                Flavor = HandleFlavor.ParamSpec,
                Doc = parameter.Doc,
            };
        }

        ArgumentPlan? argument = PlanScalar(
            parameter.Type,
            mapped,
            name,
            ArgumentDirection.In,
            parameter.Transfer,
            parameter.IsNullable,
            context);

        if (argument is null
            || argument.Kind is not (ArgumentKind.Value or ArgumentKind.Boolean or ArgumentKind.Enumeration
                or ArgumentKind.Wrapper or ArgumentKind.Pointer or ArgumentKind.Utf8 or ArgumentKind.Handle))
        {
            return null;
        }

        return new ArgumentPlan
        {
            Source = parameter,
            Kind = argument.Kind,
            Name = argument.Name,
            PublicType = argument.PublicType,
            RawType = argument.RawType,
            Transfer = argument.Transfer,
            Flavor = argument.Flavor,
            IsNullable = argument.IsNullable,
            Doc = parameter.Doc,
        };
    }

    /// <summary>
    /// Plans the value a signal handler returns. Only the values that are
    /// blittable on their own are supported: handing a handle back would make
    /// the handler transfer ownership into native code, which needs the
    /// accumulator of the signal to be known.
    /// </summary>
    /// <param name="signal">The signal declaration.</param>
    /// <param name="context">The module that is being emitted.</param>
    /// <returns>The plan, or <see langword="null"/> when the value is not supported.</returns>
    private ReturnPlan? PlanSignalReturn(GirSignal signal, PlanningContext context)
    {
        GirReturnValue value = signal.ReturnValue;
        MappedType mapped = _types.Map(value.Type, context.Namespace);
        if (mapped.Kind == MarshalKind.Void)
        {
            return new ReturnPlan
            {
                Kind = ArgumentKind.Void,
                PublicType = "void",
                RawType = "void",
                Doc = value.Doc,
            };
        }

        ArgumentPlan? scalar = PlanScalar(
            value.Type,
            mapped,
            "result",
            ArgumentDirection.In,
            value.Transfer,
            value.IsNullable,
            context);

        if (scalar is null
            || scalar.Kind is not (ArgumentKind.Value or ArgumentKind.Boolean or ArgumentKind.Enumeration
                or ArgumentKind.Wrapper or ArgumentKind.Pointer))
        {
            return null;
        }

        return new ReturnPlan
        {
            Kind = scalar.Kind,
            PublicType = scalar.PublicType,
            RawType = scalar.RawType,
            Doc = value.Doc,
        };
    }
}
