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
/// <see cref="SkipReason.UnsupportedSignature"/> and left for the milestone
/// that brings container marshalling. Half emitted members would compile and
/// then corrupt memory, which is much worse than a missing binding.
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
    /// Types of the hand written runtime that generated code may refer to even
    /// though their module is not generated.
    /// </summary>
    private static readonly Dictionary<string, string> RuntimeTypes = new(StringComparer.Ordinal)
    {
        ["GObject.Object"] = "Gst.GObject.Object",
        ["GObject.InitiallyUnowned"] = "Gst.GObject.InitiallyUnowned",
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

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly TypeMap _types;
    private readonly Overlays _overlays;
    private readonly SkipRules _skipRules;
    private readonly SortedDictionary<string, CallbackPlan> _callbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<GirCallback, CallbackPlan?> _callbackCache = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="MarshalPlanner"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="types">The type map.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="skipRules">The skip rules.</param>
    internal MarshalPlanner(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        TypeMap types,
        Overlays overlays,
        SkipRules skipRules)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _types = types;
        _overlays = overlays;
        _skipRules = skipRules;
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
    /// <returns>The plan, or <see langword="null"/> when the callable is skipped.</returns>
    internal MarshalPlan? TryPlan(
        GirCallable callable,
        CallableForm form,
        PlanningContext context,
        out SkipReason reason)
    {
        reason = _skipRules.GetSkipReason(callable);
        if (reason != SkipReason.None)
        {
            return null;
        }

        if (callable.CIdentifier is not { Length: > 0 } entryPoint)
        {
            reason = SkipReason.NoCIdentifier;
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
        reason = _skipRules.GetSkipReason(signal);
        if (reason != SkipReason.None)
        {
            return null;
        }

        reason = SkipReason.UnsupportedSignature;
        if (signal.Throws || context.OwnerType is not { } ownerType)
        {
            return null;
        }

        string host = context.SignalHost ?? ownerType;
        string name = _names.SignalName(context.Namespace, owner, signal);
        string? argsName = signal.Parameters.Count > 0 ? name + "SignalArgs" : null;
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

    private GirTransfer TransferOf(GirCallable callable, GirParameter parameter)
    {
        AnnotationOverride? overlay = callable.CIdentifier is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#" + parameter.Name)
            : null;

        return ParseTransfer(overlay?.Transfer) ?? parameter.Transfer;
    }

    private GirTransfer TransferOf(GirCallable callable)
    {
        AnnotationOverride? overlay = callable.CIdentifier is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#return")
            : null;

        return ParseTransfer(overlay?.Transfer) ?? callable.ReturnValue.Transfer;
    }

    private bool NullableOf(GirCallable callable, GirParameter parameter)
    {
        AnnotationOverride? overlay = callable.CIdentifier is { } identifier
            ? _overlays.GetAnnotationOverride(identifier + "#" + parameter.Name)
            : null;

        return overlay?.Nullable ?? parameter.IsNullable;
    }

    private bool NullableOf(GirCallable callable)
    {
        AnnotationOverride? overlay = callable.CIdentifier is { } identifier
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
            || !symbol.Declaration.IsIntrospectable)
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

        if (mapped.Kind == MarshalKind.Callback)
        {
            return PlanCallbackArgument(parameter, name, context);
        }

        if (parameter.Type is GirArrayRef array)
        {
            return PlanArrayArgument(parameter, array, mapped, name, direction, transfer, index, context, offset);
        }

        ArgumentPlan? plan = PlanScalar(parameter.Type, mapped, name, direction, transfer, nullable, context);
        if (plan is null)
        {
            return null;
        }

        return plan;
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
    /// <returns>The plan, or <see langword="null"/> when the type is not supported.</returns>
    private ArgumentPlan? PlanScalar(
        GirTypeRef type,
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        PlanningContext context,
        bool isReturn = false)
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
                if (mapped.Symbol is not { } enumeration || !IsEmitted(enumeration))
                {
                    return null;
                }

                return new ArgumentPlan
                {
                    Kind = ArgumentKind.Enumeration,
                    Name = name,
                    PublicType = mapped.PublicType,
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
                return PlanHandle(mapped, name, direction, transfer, nullable, context, isReturn);

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
        PlanningContext context,
        bool isReturn)
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
        int offset)
    {
        _ = index;

        if (mapped.Kind != MarshalKind.Array || array.FixedSize is not null || mapped.ElementType is not { } element)
        {
            return null;
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
            PublicType = scalar.PublicType,
            RawType = scalar.RawType,
            Transfer = transfer,
            IsNullable = nullable,
            Flavor = scalar.Flavor,
            Doc = value.Doc,
        };
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
                parameter.IsNullable,
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
                // What a callback receives is not what the gir promises:
                // gst_caps_foreach hands the callback a NULL GstCapsFeatures for
                // every structure that carries none, although the annotation
                // says otherwise. A handle and a string therefore always reach
                // the delegate as a nullable value, so that native code passing
                // NULL is something the callback can handle instead of an
                // exception that swallows the invocation.
                bool nullable = argument.Kind is ArgumentKind.Handle or ArgumentKind.Utf8 || argument.IsNullable;
                string publicType = nullable && !argument.PublicType.EndsWith('?')
                    ? argument.PublicType + "?"
                    : argument.PublicType;

                argument = new ArgumentPlan
                {
                    Source = parameter,
                    Kind = argument.Kind,
                    Name = argument.Name,
                    PublicType = publicType,
                    RawType = argument.RawType,
                    Direction = ArgumentDirection.In,
                    Transfer = argument.Transfer,
                    Flavor = argument.Flavor,
                    IsNullable = nullable,
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
