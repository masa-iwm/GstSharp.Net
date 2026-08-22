using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// What a value backed property hands out when it is read and takes in when it
/// is written, which is what its documentation has to state.
/// </summary>
internal enum ValueOwnership
{
    /// <summary>A number, a string, a <c>GType</c> or an enumeration: nothing is owned.</summary>
    Plain,

    /// <summary>A <c>GObject</c>: the reader gets the interned wrapper, the writer takes a reference.</summary>
    GObject,

    /// <summary>A boxed value: the reader gets a copy of its own, the writer copies.</summary>
    Boxed,

    /// <summary>A mini object: the reader gets a reference of its own, the writer takes one.</summary>
    MiniObject,
}

/// <summary>
/// How the value of a property that has no C accessor crosses a <c>GValue</c>.
/// </summary>
/// <param name="Type">The C# type of the property.</param>
/// <param name="Read">The expression that reads the value out of <c>holder</c>.</param>
/// <param name="Write">The statement that writes <c>value</c> into <c>holder</c>.</param>
/// <param name="Ownership">What the two sides own.</param>
internal sealed record ValueAccess(string Type, string Read, string Write, ValueOwnership Ownership);

/// <summary>
/// A property that the GObject property system backs, because the gir names no
/// C getter for it or the one it names is not bound.
/// </summary>
/// <param name="GirName">The GObject name of the property, as <c>g_object_get_property</c> takes it.</param>
/// <param name="Access">How its value crosses the <c>GValue</c>.</param>
/// <param name="WritesValue">Whether the setter goes through the <c>GValue</c> as well.</param>
/// <param name="IsConstructOnly">Whether the property can only be given to the constructor.</param>
internal sealed record ValueBackedProperty(
    string GirName,
    ValueAccess Access,
    bool WritesValue,
    bool IsConstructOnly);

/// <summary>
/// One generated property, built from the accessor methods the gir names or,
/// when it names none, from the GObject property system.
/// </summary>
/// <param name="Property">The gir property.</param>
/// <param name="Name">The C# name of the property.</param>
/// <param name="Type">The C# type of the property.</param>
/// <param name="Getter">The method that reads it, when a bound one exists.</param>
/// <param name="Setter">The method that writes it, if any.</param>
/// <param name="IsNew">Whether the property hides an inherited generated member.</param>
/// <param name="ValueBacked">
/// How the property is read and written when no bound C getter backs it. A
/// value backed property never hides an inherited member: it is skipped
/// instead, so <paramref name="IsNew"/> is always <see langword="false"/> here.
/// </param>
internal sealed record PropertyEmission(
    GirProperty Property,
    string Name,
    string Type,
    MarshalPlan? Getter,
    MarshalPlan? Setter,
    bool IsNew,
    ValueBackedProperty? ValueBacked = null);

/// <summary>
/// One generated event, built from a <c>&lt;glib:signal&gt;</c>.
/// </summary>
/// <param name="Plan">How the signal is marshalled and named.</param>
/// <param name="IsNew">Whether the event hides an inherited generated member.</param>
/// <param name="ArgsAreNew">Whether the arguments class hides an inherited generated member.</param>
internal sealed record SignalEmission(SignalPlan Plan, bool IsNew, bool ArgsAreNew);

/// <summary>
/// The members one generated type carries.
/// </summary>
/// <param name="Members">The methods, in gir order.</param>
/// <param name="Properties">The properties, in gir order.</param>
/// <param name="Signals">The events, in gir order.</param>
internal sealed record TypeSurface(
    IReadOnlyList<MarshalPlan> Members,
    IReadOnlyList<PropertyEmission> Properties,
    IReadOnlyList<SignalEmission> Signals)
{
    /// <summary>Gets a value indicating whether the type carries any generated member.</summary>
    internal bool IsEmpty => Members.Count == 0 && Properties.Count == 0 && Signals.Count == 0;

    /// <summary>
    /// Gets the inline storage types the parameters of the members need, in
    /// declaration order and without repetitions.
    /// </summary>
    /// <remarks>
    /// A caller allocated out array is passed as a struct that is nested in the
    /// declaring type and named after the parameter, so two members that fill
    /// the same array share one storage type rather than declaring two of the
    /// same name.
    /// </remarks>
    internal IReadOnlyList<InlineArrayInfo> ParameterArrays
    {
        get
        {
            List<InlineArrayInfo> arrays = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (MarshalPlan member in Members)
            {
                foreach (ArgumentPlan argument in member.Arguments)
                {
                    if (argument.FixedArray is { } array && seen.Add(array.TypeName))
                    {
                        arrays.Add(array);
                    }
                }
            }

            return arrays;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the declaring type has to be compiled in
    /// an unsafe context, which every generated entry point and every signal
    /// trampoline needs.
    /// </summary>
    internal bool NeedsUnsafe => Members.Count > 0 || Signals.Count > 0;

    /// <summary>Gets the C# names of the emitted members.</summary>
    /// <returns>The names, for the reserved set of a derived type.</returns>
    internal IEnumerable<string> MemberNames
    {
        get
        {
            foreach (MarshalPlan member in Members)
            {
                yield return member.Name;
            }

            foreach (PropertyEmission property in Properties)
            {
                yield return property.Name;
            }

            foreach (SignalEmission signal in Signals)
            {
                yield return signal.Plan.Name;
            }
        }
    }

    /// <summary>
    /// Gets the emitted members as the keys a derived type needs to decide what
    /// it hides: <c>M:Name(type,type)</c> for a method and <c>P:Name</c> for a
    /// property. A method only hides an inherited method of the same signature,
    /// while a property hides every inherited member of its name.
    /// </summary>
    internal IEnumerable<string> MemberKeys
    {
        get
        {
            foreach (MarshalPlan member in Members)
            {
                yield return SurfaceBuilder.MethodKey(member);
            }

            foreach (PropertyEmission property in Properties)
            {
                yield return SurfaceBuilder.PropertyKey(property.Name);
            }

            // An event and the types that belong to it hide an inherited member
            // of their name whatever its signature, just like a property.
            foreach (SignalEmission signal in Signals)
            {
                yield return SurfaceBuilder.PropertyKey(signal.Plan.Name);

                if (signal.Plan.ArgsName is { } argsName)
                {
                    yield return SurfaceBuilder.PropertyKey(argsName);
                }

                if (signal.Plan.HandlerName is { } handlerName)
                {
                    yield return SurfaceBuilder.PropertyKey(handlerName);
                }
            }
        }
    }
}

/// <summary>
/// Turns the callables and properties of one gir type into the members that are
/// emitted for it, and counts what had to be left out.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds of name are already taken. The members of the runtime types are
/// reserved: <c>gst_caps_is_writable</c> would hide the <c>IsWritable</c> of
/// the mini object base, which already answers the same question, so it is
/// dropped. The members of a generated base class are not reserved but hidden
/// on purpose: <c>gst_pipeline_new</c> is the factory of <c>GstPipeline</c>
/// even though <c>GstBin</c> has one too, so it is emitted with the <c>new</c>
/// modifier that says as much.
/// </para>
/// <para>
/// Everything else that collides is skipped, because hiding a member without
/// saying so is a compiler warning, and warnings are errors in this repository.
/// </para>
/// </remarks>
internal sealed class SurfaceBuilder
{
    /// <summary>
    /// Names that every generated wrapper carries, from the runtime base types
    /// and from the generator itself.
    /// </summary>
    internal static readonly IReadOnlyList<string> WrapperNames =
    [
        "BoxedType",
        "CreateWrapper",
        "Dispose",
        "Equals",
        "FromNative",
        "GetGType",
        "GetHashCode",
        "GetType",
        "Handle",
        "IsDisposed",
        "MemberwiseClone",
        "ReferenceEquals",
    ];

    /// <summary>Names that a generated <c>GObject</c> wrapper inherits.</summary>
    internal static readonly IReadOnlyList<string> ObjectNames =
    [
        "AddNotifyHandler",
        "ConnectSignal",
        "DrainPendingReleases",
        "EnableIdleDrain",
        "GetProperty",
        "NativeType",
        "RemoveHandler",
        "SetProperty",
    ];

    /// <summary>Names that a generated mini object wrapper inherits.</summary>
    internal static readonly IReadOnlyList<string> MiniObjectNames =
    [
        "IsWritable",
        "MakeWritableHandle",
    ];

    private readonly MarshalPlanner _planner;
    private readonly NameMapper _names;
    private readonly TypeMap _types;
    private readonly EmissionCensus _census;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="SurfaceBuilder"/> class.</summary>
    /// <param name="planner">The marshal planner.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="types">The type map, which says what a property holds.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal SurfaceBuilder(
        MarshalPlanner planner,
        NameMapper names,
        TypeMap types,
        EmissionCensus census,
        DiagnosticBag diagnostics)
    {
        _planner = planner;
        _names = names;
        _types = types;
        _census = census;
        _diagnostics = diagnostics;
    }

    /// <summary>Builds the members of one type.</summary>
    /// <param name="declaration">The gir declaration.</param>
    /// <param name="context">The type the members are emitted into.</param>
    /// <param name="methodForm">The shape instance methods take.</param>
    /// <param name="reserved">The names that must not be used at all.</param>
    /// <param name="inherited">The names of the members of the generated base classes.</param>
    /// <param name="includeProperties">Whether gir properties become C# properties.</param>
    /// <param name="includeSignals">Whether gir signals become C# events.</param>
    /// <returns>The members to emit.</returns>
    internal TypeSurface Build(
        GirTypeDeclaration declaration,
        PlanningContext context,
        CallableForm methodForm,
        IEnumerable<string> reserved,
        IEnumerable<string> inherited,
        bool includeProperties,
        bool includeSignals = false)
    {
        string module = context.Module.GirNamespace;
        HashSet<string> used = new(reserved, StringComparer.Ordinal);
        HiddenMembers hidden = HiddenMembers.From(inherited);
        List<MarshalPlan> members = [];

        foreach (GirFunction constructor in declaration.Constructors)
        {
            Add(constructor, CallableForm.Constructor);
        }

        foreach (GirFunction method in declaration.Methods)
        {
            Add(method, methodForm);
        }

        foreach (GirFunction function in declaration.Functions)
        {
            Add(function, CallableForm.StaticMethod);
        }

        List<GirProperty> valueBacked = [];
        List<PropertyEmission> properties = includeProperties
            ? BuildProperties(declaration, context, used, hidden, members, valueBacked)
            : [];

        // The methods and the properties claim their names first, so that a
        // signal never renames a member that is already bound.
        List<SignalEmission> signals = includeSignals
            ? BuildSignals(declaration, context, used, hidden, methodForm)
            : [];

        // A property the GObject property system backs comes last of all,
        // because it is the weakest claim on a name: it exists only where the
        // gir names no C accessor, so a method, another property and an event
        // are all bindings of something the library named and it is not. That
        // is what makes GstAppSink::eos the Eos of that class and leaves the
        // "eos" property of the same name unbound.
        if (valueBacked.Count > 0)
        {
            BuildValueProperties(declaration, context, used, hidden, members, valueBacked, properties);
        }

        return new TypeSurface(members, properties, signals);

        void Add(GirFunction callable, CallableForm form)
        {
            MarshalPlan? plan = _planner.TryPlan(callable, form, context, out SkipReason reason);

            // The gir pairs a function that language bindings cannot use with
            // the one it shadows: gst_adapter_copy is shadowed by
            // gst_adapter_copy_bytes, which returns a GBytes that this
            // milestone cannot marshal. Skipping both leaves neither, so the
            // shadowed declaration is retried under the clean name once the
            // shadowing one is known not to bind. Every other rule still
            // applies to it, so one that is introspectable="0" stays out.
            if (plan is null && reason == SkipReason.ShadowedBy && !ShadowingBinds(callable, form))
            {
                plan = _planner.TryPlan(callable, form, context, out reason, ignoreShadowedBy: true);
            }

            if (plan is null)
            {
                _census.Skipped(module, reason, SymbolOf(declaration, callable));
                return;
            }

            if (string.Equals(plan.Name, "ToString", StringComparison.Ordinal))
            {
                // gst_caps_to_string and its relatives are the ToString of their
                // type; anything else of that name would hide object.ToString.
                if (form == methodForm && VisibleCount(plan) == 0 && plan.Return.Kind == ArgumentKind.Utf8)
                {
                    plan.IsOverride = true;
                    members.Add(plan);
                    _census.Emitted(module, "method");
                    return;
                }

                // A static one cannot override anything. It only hides
                // object.ToString when it takes no argument either; a static
                // ToString(value), which is what gst_video_format_to_string is,
                // has a signature of its own and binds under its natural name.
                if (form == CallableForm.StaticMethod && used.Add(plan.Name))
                {
                    plan.IsNew = VisibleCount(plan) == 0;
                    members.Add(plan);
                    _census.Emitted(module, "method");
                    return;
                }

                Collide(plan);
                return;
            }

            if (!used.Add(plan.Name))
            {
                Collide(plan);
                return;
            }

            plan.IsNew = hidden.HidesMethod(plan);
            members.Add(plan);
            _census.Emitted(module, "method");
        }

        // Plans the callable that shadows this one, only to find out whether it
        // binds. Nothing is emitted from the answer, so the census stays quiet.
        bool ShadowingBinds(GirFunction callable, CallableForm form)
        {
            if (callable.ShadowedBy is not { } shadowing)
            {
                return true;
            }

            foreach (GirFunction sibling in Siblings(declaration, form))
            {
                if (string.Equals(sibling.Name, shadowing, StringComparison.Ordinal))
                {
                    return _planner.TryPlan(sibling, form, context, out _) is not null;
                }
            }

            return true;
        }

        void Collide(MarshalPlan plan)
        {
            _census.Skipped(module, SkipReason.NameCollision, plan.EntryPoint);
            _diagnostics.Warn(
                "GEN0009",
                $"'{plan.EntryPoint}' would be emitted as '{declaration.Name}.{plan.Name}', which is already taken; "
                + "the member is skipped. Add a rename to fixups.json to bind it.");
        }
    }

    /// <summary>
    /// Returns the callables of a declaration that are emitted in one shape, so
    /// that a shadowed callable can find the one that shadows it.
    /// </summary>
    /// <param name="declaration">The declaring type.</param>
    /// <param name="form">The shape the callable is emitted in.</param>
    /// <returns>The candidates, in gir order.</returns>
    private static IReadOnlyList<GirFunction> Siblings(GirTypeDeclaration declaration, CallableForm form) =>
        form switch
        {
            CallableForm.Constructor => declaration.Constructors,
            CallableForm.StaticMethod => declaration.Functions,
            _ => declaration.Methods,
        };

    /// <summary>Returns the name a skipped callable is reported under.</summary>
    /// <param name="declaration">The declaring type.</param>
    /// <param name="callable">The callable that was not emitted.</param>
    /// <returns>The <c>c:identifier</c>, or the gir path when there is none.</returns>
    private static string SymbolOf(GirTypeDeclaration declaration, GirCallable callable) =>
        callable.CIdentifier is { Length: > 0 } identifier
            ? identifier
            : declaration.Name + "." + callable.Name;

    /// <summary>Returns the key a method is remembered under.</summary>
    /// <param name="plan">The method to key.</param>
    /// <returns>The key, which is the C# signature without the return type.</returns>
    /// <remarks>
    /// The nullable annotation of a parameter is dropped, because C# hiding
    /// ignores it: <c>gst_app_src_set_caps</c> takes a nullable
    /// <c>GstCaps</c> while <c>gst_base_src_set_caps</c> does not, and
    /// <c>AppSrc.SetCaps</c> still hides <c>BaseSrc.SetCaps</c>. Only reference
    /// types are ever annotated here, so nothing that is a distinct type is
    /// merged by this.
    /// </remarks>
    internal static string MethodKey(MarshalPlan plan)
    {
        List<string> parameters = [];
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (argument.IsHidden || argument.Kind == ArgumentKind.Instance)
            {
                continue;
            }

            string prefix = argument.Direction switch
            {
                ArgumentDirection.Out => "out ",
                ArgumentDirection.Ref => "ref ",
                _ => string.Empty,
            };

            string type = argument.PublicType;
            parameters.Add(prefix + (type.EndsWith('?') ? type[..^1] : type));
        }

        return "M:" + plan.Name + "(" + string.Join(",", parameters) + ")";
    }

    /// <summary>Returns the key a property is remembered under.</summary>
    /// <param name="name">The C# name of the property.</param>
    /// <returns>The key.</returns>
    internal static string PropertyKey(string name) => "P:" + name;

    /// <summary>Counts the parameters a member shows on the public surface.</summary>
    /// <param name="plan">The plan to inspect.</param>
    /// <returns>
    /// The number of visible parameters. The instance of an instance method is
    /// hidden and therefore not counted.
    /// </returns>
    private static int VisibleCount(MarshalPlan plan)
    {
        int count = 0;
        foreach (ArgumentPlan argument in plan.Arguments)
        {
            if (!argument.IsHidden)
            {
                count++;
            }
        }

        return count;
    }

    private static MarshalPlan? FindAccessor(IReadOnlyList<MarshalPlan> members, string? girName)
    {
        if (girName is null)
        {
            return null;
        }

        foreach (MarshalPlan member in members)
        {
            if (string.Equals(member.Callable.Name, girName, StringComparison.Ordinal))
            {
                return member;
            }
        }

        return null;
    }

    private List<SignalEmission> BuildSignals(
        GirTypeDeclaration declaration,
        PlanningContext context,
        HashSet<string> used,
        HiddenMembers hidden,
        CallableForm methodForm)
    {
        string module = context.Module.GirNamespace;

        // The signal of a gir interface is emitted as a pair of extension
        // methods rather than as an event, so a different set of names has to
        // be free for it.
        bool asExtension = methodForm == CallableForm.ExtensionMethod;
        List<SignalEmission> signals = [];

        foreach (GirSignal signal in declaration.Signals)
        {
            string symbol = context.Namespace.Name + "." + declaration.Name + "::" + signal.Name;
            SignalPlan? plan = _planner.TryPlanSignal(signal, declaration, context, out SkipReason reason);
            if (plan is null)
            {
                _census.Skipped(module, reason, symbol);
                continue;
            }

            // The event, its arguments class and its handler delegate are three
            // members of the declaring type, so all three names have to be free
            // before any of them is taken.
            List<string> names = asExtension
                ? [SignalEmitter.AddMethodName(plan), SignalEmitter.RemoveMethodName(plan)]
                : [plan.Name];
            if (plan.ArgsName is { } argsName)
            {
                names.Add(argsName);
            }

            if (plan.HandlerName is { } handlerName)
            {
                names.Add(handlerName);
            }

            names.Add(plan.TrampolineName);

            bool free = true;
            foreach (string name in names)
            {
                free &= !used.Contains(name);
            }

            if (!free)
            {
                _census.Skipped(module, SkipReason.NameCollision, symbol);
                _diagnostics.Warn(
                    "GEN0011",
                    $"The '{signal.Name}' signal of '{declaration.Name}' would be emitted as '{plan.Name}', which is "
                    + "already taken; the event is skipped. Add a rename to fixups.json to bind it.");
                continue;
            }

            foreach (string name in names)
            {
                used.Add(name);
            }

            signals.Add(new SignalEmission(
                plan,
                hidden.HidesName(plan.Name),
                plan.ArgsName is not null && hidden.HidesName(plan.ArgsName)));
            _census.Emitted(module, "signal");
        }

        return signals;
    }

    private List<PropertyEmission> BuildProperties(
        GirTypeDeclaration declaration,
        PlanningContext context,
        HashSet<string> used,
        HiddenMembers hidden,
        IReadOnlyList<MarshalPlan> members,
        List<GirProperty> valueBacked)
    {
        string module = context.Module.GirNamespace;
        List<PropertyEmission> properties = [];

        foreach (GirProperty property in declaration.Properties)
        {
            string symbol = context.Namespace.Name + "." + declaration.Name + ":" + property.Name;
            MarshalPlan? getter = FindAccessor(members, property.Getter);
            if (getter is null
                || getter.Form != CallableForm.InstanceMethod
                || getter.Return.IsVoid
                || VisibleCount(getter) != 0)
            {
                // Without a generated getter there is nothing to delegate to.
                // The property is still there on the GObject property system,
                // so it is decided in the second pass, once every member that
                // binds something the library named has claimed its name.
                valueBacked.Add(property);
                continue;
            }

            if ((getter.Return.Kind == ArgumentKind.Handle && getter.Return.Flavor == HandleFlavor.Wrapper)
                || getter.Return.Kind == ArgumentKind.GValue)
            {
                // A mini object or a boxed value reaches managed code as a
                // wrapper that owns a reference of its own and releases it when
                // it is disposed, whatever the call transferred. A property is
                // read, not acquired: nothing at the reading site says that a
                // resource was produced, GST0001 does not look at property
                // reads by design, and two reads of the same property are two
                // wrappers holding two references. The getter is emitted as a
                // method beside this, and Get* is where a caller sees that the
                // result is theirs to dispose. A returned GValue is the same
                // owning shape — the caller has to dispose it — so it never
                // backs a property either.
                _census.Skipped(module, SkipReason.OwningProperty, symbol);
                continue;
            }

            MarshalPlan? setter = FindAccessor(members, property.Setter);
            if (setter is not null
                && (setter.Form != CallableForm.InstanceMethod
                    || !setter.Return.IsVoid
                    || VisibleCount(setter) != 1
                    || !string.Equals(SetterType(setter), getter.Return.PublicType, StringComparison.Ordinal)))
            {
                setter = null;
            }

            string name = _names.PropertyName(context.Namespace, declaration, property);
            if (!used.Add(name))
            {
                _census.Skipped(module, SkipReason.NameCollision, symbol);
                _diagnostics.Warn(
                    "GEN0010",
                    $"The '{property.Name}' property of '{declaration.Name}' would be emitted as '{name}', which is "
                    + "already taken; the property is skipped.");
                continue;
            }

            properties.Add(new PropertyEmission(
                property,
                name,
                getter.Return.PublicType,
                getter,
                setter,
                hidden.HidesName(name)));
            _census.Emitted(module, "property");
        }

        return properties;
    }

    /// <summary>
    /// Emits the properties the gir declares without naming a C getter for
    /// them, or whose getter is not bound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There is nothing to delegate to, so the member goes through the GObject
    /// property system itself: <c>g_object_get_property</c> fills a
    /// <c>GValue</c> of the type the specification declares, and
    /// <c>g_object_set_property</c> reads one back. That is the route the hand
    /// written <c>WebRTCDataChannel.ReadyState</c> took before this, and it is
    /// the only one a property whose accessors exist solely as the
    /// <c>get_property</c> and <c>set_property</c> slots of a class has.
    /// </para>
    /// <para>
    /// The rules are deliberately narrow. A property that cannot be read is not
    /// a property; a type the <c>GValue</c> accessors of the runtime do not
    /// cover is left out rather than half bound; a name another member of the
    /// same type already took is a collision as it always is; and a name an
    /// inherited member carries is left to that member, because this shape
    /// never emits <c>new</c>: what it would hide is the binding of a C
    /// accessor and this is not one.
    /// </para>
    /// </remarks>
    /// <param name="declaration">The gir declaration.</param>
    /// <param name="context">The type the members are emitted into.</param>
    /// <param name="used">The names that are taken.</param>
    /// <param name="hidden">The members the generated base classes carry.</param>
    /// <param name="members">The methods that were planned for this type.</param>
    /// <param name="candidates">The properties the first pass could not delegate.</param>
    /// <param name="properties">The list the emitted properties are added to.</param>
    private void BuildValueProperties(
        GirTypeDeclaration declaration,
        PlanningContext context,
        HashSet<string> used,
        HiddenMembers hidden,
        IReadOnlyList<MarshalPlan> members,
        IReadOnlyList<GirProperty> candidates,
        List<PropertyEmission> properties)
    {
        string module = context.Module.GirNamespace;

        foreach (GirProperty property in candidates)
        {
            string symbol = context.Namespace.Name + "." + declaration.Name + ":" + property.Name;

            // A write only property has nothing to hand back, and
            // g_object_get_property on one warns and fills in nothing.
            if (!property.IsReadable)
            {
                _census.Skipped(module, SkipReason.UnsupportedSignature, symbol);
                continue;
            }

            if (MapValueAccess(property, context) is not { } access)
            {
                _census.Skipped(module, SkipReason.UnsupportedSignature, symbol);
                continue;
            }

            string name = _names.PropertyName(context.Namespace, declaration, property);
            if (used.Contains(name))
            {
                _census.Skipped(module, SkipReason.NameCollision, symbol);
                _diagnostics.Warn(
                    "GEN0010",
                    $"The '{property.Name}' property of '{declaration.Name}' would be emitted as '{name}', which is "
                    + "already taken; the property is skipped.");
                continue;
            }

            // An inherited member of the same name would have to be hidden, and
            // a property that reads the GObject property system is not what a
            // caller of the hidden member asked for: GstAppSrc declares an
            // "is-live" property and GstBaseSrc binds gst_base_src_is_live, so
            // the property is dropped and IsLive() stays the one answer.
            if (hidden.HidesName(name))
            {
                _census.Skipped(module, SkipReason.ShadowedBy, symbol);
                continue;
            }

            used.Add(name);

            // A setter the gir names and the planner bound is the better half
            // of the pair: it is the C call the library documents, and it takes
            // exactly what this property holds.
            MarshalPlan? setter = FindAccessor(members, property.Setter);
            if (setter is not null
                && (setter.Form != CallableForm.InstanceMethod
                    || !setter.Return.IsVoid
                    || VisibleCount(setter) != 1
                    || !string.Equals(SetterType(setter), access.Type, StringComparison.Ordinal)))
            {
                setter = null;
            }

            // Construct-only leaves the property read only: the runtime refuses
            // to write one after construction, as g_object_set_property does.
            bool writesValue = setter is null && property.IsWritable && !property.IsConstructOnly;

            properties.Add(new PropertyEmission(
                property,
                name,
                access.Type,
                Getter: null,
                setter,
                IsNew: false,
                new ValueBackedProperty(property.Name, access, writesValue, property.IsConstructOnly)));
            _census.Emitted(module, "property");
        }
    }

    /// <summary>
    /// Maps the gir type of a property onto the <c>GValue</c> accessors that
    /// read and write it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The primitives are keyed on the gir type name rather than on the mapped
    /// marshalling kind, because an alias maps onto the same kind while asking
    /// for a different C# type: <c>Gst.ClockTime</c> is a <c>guint64</c> that
    /// projects onto the <c>Gst.ClockTime</c> struct, while a property the gir
    /// spells <c>guint64</c> is a plain <c>ulong</c> here whatever it counts.
    /// Which of the durations among them deserve the time type is a decision
    /// about each property rather than about the width of its value.
    /// </para>
    /// <para>
    /// Everything the table does not name is left out: a fundamental such as
    /// <c>GstFraction</c>, an array, a nested <c>GValue</c>, a bare pointer and
    /// every container. Each of them needs a projection that the value
    /// accessors of the runtime do not have.
    /// </para>
    /// </remarks>
    /// <param name="property">The property to map.</param>
    /// <param name="context">The type the property is emitted into.</param>
    /// <returns>The accessors, or <see langword="null"/> when the type is not covered.</returns>
    private ValueAccess? MapValueAccess(GirProperty property, PlanningContext context)
    {
        if (property.Type is GirArrayRef || property.Type.Name is not { } girType)
        {
            return null;
        }

        switch (girType)
        {
            case "gboolean":
                return new ValueAccess("bool", "holder.GetBoolean()", "holder.SetBoolean(value);", ValueOwnership.Plain);
            case "gint":
                return new ValueAccess("int", "holder.GetInt()", "holder.SetInt(value);", ValueOwnership.Plain);
            case "guint":
                return new ValueAccess("uint", "holder.GetUInt()", "holder.SetUInt(value);", ValueOwnership.Plain);
            case "gint64":
                return new ValueAccess("long", "holder.GetInt64()", "holder.SetInt64(value);", ValueOwnership.Plain);
            case "guint64":
                return new ValueAccess("ulong", "holder.GetUInt64()", "holder.SetUInt64(value);", ValueOwnership.Plain);
            case "gfloat":
                return new ValueAccess("float", "holder.GetFloat()", "holder.SetFloat(value);", ValueOwnership.Plain);
            case "gdouble":
                return new ValueAccess("double", "holder.GetDouble()", "holder.SetDouble(value);", ValueOwnership.Plain);
            case "utf8":
                return new ValueAccess("string?", "holder.GetString()", "holder.SetString(value);", ValueOwnership.Plain);
            case "GType":
                return new ValueAccess(
                    "Gst.GObject.GType",
                    "holder.GetGType()",
                    "holder.SetGType(value);",
                    ValueOwnership.Plain);
            default:
                break;
        }

        MappedType mapped = _types.Map(property.Type, context.Namespace);
        string type = mapped.PublicType;

        return mapped.Kind switch
        {
            MarshalKind.Enum => new ValueAccess(
                type,
                "(" + type + ")holder.GetEnum()",
                "holder.SetEnum((int)value);",
                ValueOwnership.Plain),
            MarshalKind.Flags => new ValueAccess(
                type,
                "(" + type + ")holder.GetFlags()",
                "holder.SetFlags((uint)value);",
                ValueOwnership.Plain),
            MarshalKind.GObject => new ValueAccess(
                type + "?",
                "(" + type + "?)holder.GetObject()",
                "holder.SetObject(value);",
                ValueOwnership.GObject),
            MarshalKind.Boxed => new ValueAccess(
                type + "?",
                "holder.GetBoxed<" + type + ">()",
                "holder.SetBoxed(value);",
                ValueOwnership.Boxed),
            MarshalKind.MiniObject => new ValueAccess(
                type + "?",
                "holder.GetMiniObject<" + type + ">()",
                "holder.SetMiniObject(value);",
                ValueOwnership.MiniObject),
            _ => null,
        };
    }

    /// <summary>
    /// The members a generated base class already carries, in the two shapes
    /// the C# hiding rules need.
    /// </summary>
    private sealed class HiddenMembers
    {
        private readonly HashSet<string> _signatures = new(StringComparer.Ordinal);
        private readonly HashSet<string> _names = new(StringComparer.Ordinal);
        private readonly HashSet<string> _properties = new(StringComparer.Ordinal);

        /// <summary>Reads the keys a base class contributed.</summary>
        /// <param name="keys">The keys, as produced by <see cref="TypeSurface.MemberKeys"/>.</param>
        /// <returns>The members to compare against.</returns>
        internal static HiddenMembers From(IEnumerable<string> keys)
        {
            HiddenMembers members = new();
            foreach (string key in keys)
            {
                if (key.StartsWith("P:", StringComparison.Ordinal))
                {
                    members._properties.Add(key[2..]);
                    members._names.Add(key[2..]);
                    continue;
                }

                if (!key.StartsWith("M:", StringComparison.Ordinal))
                {
                    continue;
                }

                members._signatures.Add(key);
                int parenthesis = key.IndexOf('(', StringComparison.Ordinal);
                members._names.Add(parenthesis < 0 ? key[2..] : key[2..parenthesis]);
            }

            return members;
        }

        /// <summary>
        /// Tests whether a method hides an inherited member. A method only hides
        /// another method when their signatures agree, but it hides a property
        /// of the same name whatever it takes.
        /// </summary>
        /// <param name="plan">The method to test.</param>
        /// <returns><see langword="true"/> when the member needs <c>new</c>.</returns>
        internal bool HidesMethod(MarshalPlan plan) =>
            _signatures.Contains(MethodKey(plan)) || _properties.Contains(plan.Name);

        /// <summary>Tests whether a member hides an inherited one of the same name.</summary>
        /// <param name="name">The C# name of the member.</param>
        /// <returns><see langword="true"/> when the member needs <c>new</c>.</returns>
        internal bool HidesName(string name) => _names.Contains(name);
    }

    private static string SetterType(MarshalPlan setter)
    {
        foreach (ArgumentPlan argument in setter.Arguments)
        {
            if (!argument.IsHidden && argument.Kind != ArgumentKind.Instance)
            {
                return argument.Direction == ArgumentDirection.In ? argument.PublicType : string.Empty;
            }
        }

        return string.Empty;
    }
}
