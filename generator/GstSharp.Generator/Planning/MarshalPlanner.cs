using System.Diagnostics.CodeAnalysis;
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
/// <param name="StorageOwner">
/// The C# type that carries the inline storage of a caller allocated array,
/// which is nested in the type the member is declared on. It is the declaring
/// type for a class or a record, and the static holder for the functions that
/// belong to no type: those have no <paramref name="OwnerType"/> and would
/// have nowhere to put the storage otherwise. Defaults to
/// <paramref name="OwnerType"/>.
/// </param>
internal readonly record struct PlanningContext(
    ModuleInfo Module,
    GirNamespace Namespace,
    TypeKind OwnerKind,
    string? OwnerType,
    string? SignalHost = null,
    string? StorageOwner = null);

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

    /// <summary>
    /// Gets or sets a value indicating whether the trampoline releases the
    /// state of the callback after it has invoked it.
    /// </summary>
    /// <remarks>
    /// It is a property of the callback type rather than of one call site,
    /// because one trampoline is emitted per <c>&lt;callback&gt;</c> and every
    /// site that hands the callback over shares it. A type that is used at an
    /// asynchronous site is therefore self freeing everywhere, which is why a
    /// type that is used at both kinds of site is refused rather than emitted.
    /// </remarks>
    internal bool SelfFreeing { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the callback is handed to a call
    /// whose scope is not <c>async</c>, and whose state the trampoline must
    /// therefore leave alone.
    /// </summary>
    internal bool UsedOutsideAsync { get; set; }
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
/// (<c>transfer-ownership="full"</c>) is a consuming argument. The wrapper owns
/// the only reference it has, so the call is handed a value minted for it — a
/// reference for a mini object or a GObject, a copy for a boxed value — and the
/// wrapper is disposed when the member returns, which is the contract of the
/// hand written consuming members in docs/ownership.md. An opaque record owns
/// nothing to mint from and stays rejected, and so does
/// <c>transfer="container"</c>. The arguments of a callback and of a signal are
/// received rather than passed, so the consuming kind is rejected on
/// both.</description></item>
/// <item><description>A <c>GValue</c> crosses as a pointer into storage the
/// caller owns, so nothing is allocated for it and nothing is disposed after
/// the call. A <c>const GValue*</c> is only read and becomes an <c>in</c>
/// parameter; a non-const one is storage the callee may write under a contract
/// of its own and becomes a <c>ref</c>; a caller allocated out parameter is
/// zeroed and filled in place. A callee that takes the contents over
/// (<c>transfer-ownership="full"</c>, the <c>take_value</c> family) would leave
/// the caller's struct owning what the callee now owns, and a nullable
/// <c>GValue</c> has no <c>in</c> struct spelling, so both are rejected — see
/// <see cref="PlanGValue"/>. A returned <c>GValue</c> becomes a value of the
/// caller's own, copied or adopted by its transfer.</description></item>
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
/// <item><description>An inbound argument whose <c>c:type</c> ends in two
/// stars and whose <c>&lt;type&gt;</c> names something bound behind a handle is
/// refused, whether it is the <c>in</c> parameter of a callable, the argument
/// of a callback or the argument of a signal. A handle argument crosses as the
/// pointer the wrapper holds, so the member would hand one level of
/// indirection too few over and the callee would read the object as if it were
/// a pointer; <c>gst_play_visualizations_free</c>, whose gir writes a plain
/// <c>&lt;type c:type="GstPlayVisualization**"/&gt;</c> with no array
/// annotation, is the shape. See
/// <see cref="IsPointerToHandlePointer"/>.</description></item>
/// <item><description>A returned value whose <c>&lt;type&gt;</c> names a
/// scalar the binding passes by value while its <c>c:type</c> carries a star is
/// refused, whether the return is a member's or a callback's. Such a gir
/// describes an address and not a value: <c>gst_rtcp_packet_fb_get_fci</c>
/// answers a <c>guint8*</c> through a
/// <c>&lt;type name="guint8" c:type="guint8*"/&gt;</c>, so the member would
/// answer a <c>byte</c> and the pointer would be truncated to its lowest byte.
/// The in parameter side of the same shape - a
/// <c>&lt;type name="guint32" c:type="const guint32*"/&gt;</c> with no
/// direction, which would pass the number where the C function dereferences a
/// pointer - is knowingly not refused yet, because two published members, one
/// in GstAudio and one in GstVideo, project that shape and removing them is a
/// source break reserved for an
/// <c>[Obsolete]</c> bridge. See
/// <see cref="IsPointerToScalar"/>.</description></item>
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
/// <item><description>A <c>GList</c> is bound in the return position when its
/// elements are wrappers the runtime knows how to adopt or strings. The list is
/// materialized eagerly and its spine is released before the first element is
/// adopted, so no managed value ever points into it. In the parameter position
/// it is built out of an <c>IEnumerable</c> in exactly two shapes: borrowed,
/// where a scope releases the spine and everything allocated for it once the
/// call returns, and consumed, where one value is minted per element and the
/// callee owns all of it from the moment of the call. A <c>GSList</c> parameter
/// takes the same route; a <c>GSList</c> return stays
/// unsupported.</description></item>
/// <item><description>The four callback scopes are bound as four different
/// lifetimes of the managed state, and a callback with no closure argument to
/// attach that state to is not bound at all. <c>call</c> keeps the state for
/// the duration of the call, <c>notified</c> hands the release to the destroy
/// notification the callee is given, <c>async</c> makes the trampoline free
/// its own state after the one invocation, and <c>forever</c> keeps it for the
/// life of the process and documents that it does. Because the trampoline is
/// shared by every site of a delegate type, self freeing is a property of the
/// type rather than of the site: a type that is claimed at an async and at a
/// non-async site is reported as GEN0022 and the offending site stays
/// unbound.</description></item>
/// </list>
/// </remarks>
internal sealed class MarshalPlanner
{
    private const string NativeInt = "nint";

    /// <summary>
    /// One entry of <see cref="RuntimeTypes"/>: a hand written wrapper of the
    /// runtime, named by its public type and the flavour its handles are
    /// wrapped with.
    /// </summary>
    /// <param name="PublicType">The fully qualified C# type of the wrapper.</param>
    /// <param name="Flavor">The wrap flavour of a handle of the type.</param>
    /// <param name="BorrowedOnly">
    /// <see langword="true"/> for a wrapper that carries its <c>Handle</c> and
    /// nothing else, which is usable in the in argument position of a call this
    /// code makes and in no other position; see <see cref="PlanHandle"/>.
    /// </param>
    private sealed record RuntimeHandle(string PublicType, HandleFlavor Flavor, bool BorrowedOnly = false);

    /// <summary>
    /// The qualified name of <c>GParamSpec</c>, which is a fundamental type of
    /// its own rather than a <c>GObject</c> or a record, and is therefore
    /// recognised by name where a handle is planned.
    /// </summary>
    private const string ParamSpecType = "GObject.ParamSpec";

    /// <summary>
    /// Handles of the hand written runtime that generated code may refer to even
    /// though their module is not generated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entries cover handles only, which is what <see cref="PlanHandle"/>
    /// consults. An enumeration of such a module is named by
    /// <see cref="RuntimeEnums"/> instead: a handle crosses as a pointer, an
    /// enumeration as its underlying integer, so the two need different plans.
    /// </para>
    /// <para>
    /// An entry carries the flavour of its wrapper, because the flavour decides
    /// the wrap expression: a <see cref="HandleFlavor.GObject"/> goes through
    /// the interning <c>Gst.GObject.Object.FromNative&lt;T&gt;</c>, a
    /// <see cref="HandleFlavor.Wrapper"/> — a boxed value of the hand written
    /// runtime, such as <c>GObject.ValueArray</c> or <c>GLib.DateTime</c> —
    /// through the typed <c>FromNative</c> of its own class, exactly like a
    /// generated boxed type, and a <see cref="HandleFlavor.ParamSpec"/> through
    /// its own constructor, which is the only wrapper of the runtime that has
    /// no factory to call.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, RuntimeHandle> RuntimeTypes = new(StringComparer.Ordinal)
    {
        ["GObject.Object"] = new("Gst.GObject.Object", HandleFlavor.GObject),
        ["GObject.InitiallyUnowned"] = new("Gst.GObject.InitiallyUnowned", HandleFlavor.GObject),
        ["GObject.ValueArray"] = new("Gst.GObject.ValueArray", HandleFlavor.Wrapper),
        [ParamSpecType] = new("Gst.GObject.ParamSpec", HandleFlavor.ParamSpec),
        ["GLib.DateTime"] = new("Gst.GLib.DateTime", HandleFlavor.Wrapper),

        // The one consumer in the vendored girs is
        // gst_transcoder_get_signal_adapter, which takes a nullable const
        // GMainContext* and gives nothing back, so only the argument half of
        // the entry is exercised: the wrapper carries the Handle a call site
        // reads and the KeepAlive the epilogue emits. It carries nothing else -
        // no typed FromNative to adopt a handle the binding is handed with, and
        // no BoxedType for the copy a consumed argument is minted from - so the
        // entry is borrowed only in the in argument position of a call this
        // code makes, and every other position is refused rather than emitted:
        // a returned, out or transferred handle as an UnsupportedSignature
        // skip, and an inbound one - a parameter of a signal or of a callback,
        // which a trampoline would have to wrap - by taking its signal or its
        // callback with it. The fixtures of GLibMainContextRuntimeTypeTests are
        // what keeps both halves alive, since no vendored gir reaches them.
        ["GLib.MainContext"] = new("Gst.GLib.MainContext", HandleFlavor.Wrapper, BorrowedOnly: true),

        ["Gio.Cancellable"] = new("Gst.Gio.Cancellable", HandleFlavor.GObject),
        ["Gio.Socket"] = new("Gst.Gio.Socket", HandleFlavor.GObject),
        ["Gio.SocketAddress"] = new("Gst.Gio.SocketAddress", HandleFlavor.GObject),
        ["Gio.SocketControlMessage"] = new("Gst.Gio.SocketControlMessage", HandleFlavor.GObject),
        ["Gio.TlsCertificate"] = new("Gst.Gio.TlsCertificate", HandleFlavor.GObject),
        ["Gio.TlsConnection"] = new("Gst.Gio.TlsConnection", HandleFlavor.GObject),
        ["Gio.TlsDatabase"] = new("Gst.Gio.TlsDatabase", HandleFlavor.GObject),
        ["Gio.TlsInteraction"] = new("Gst.Gio.TlsInteraction", HandleFlavor.GObject),
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
    /// The keys of the array corrections this run has applied to a real
    /// <c>&lt;array&gt;</c>. It belongs to the run rather than to the overlays,
    /// which are shared and must stay immutable, and it is written by
    /// <see cref="EffectiveArray"/> alone: a lookup that found nothing to
    /// correct is not a use, or an entry that names a parameter which is no
    /// array would report itself as consumed.
    /// </summary>
    private readonly HashSet<string> _consumedArrayOverrides;

    /// <summary>
    /// The keys of the annotation corrections this run has read. Like
    /// <see cref="_consumedArrayOverrides"/> it belongs to the run rather than
    /// to the overlays, and it is written by <see cref="AnnotationOverrideFor"/>
    /// alone: a lookup that found no entry is not a use of one.
    /// </summary>
    private readonly HashSet<string> _consumedAnnotationOverrides;

    /// <summary>
    /// The callback uses the callable that is being planned has claimed, and
    /// the scope each of them claimed it under. Claiming a use decides how the
    /// shared trampoline of the callback type ends, so it is only written to
    /// the plans once the whole callable has been planned: a callable that a
    /// later argument or its return value rejects must leave the callback
    /// types it mentioned exactly as it found them.
    /// </summary>
    private readonly List<(CallbackPlan Plan, bool SelfFreeing)> _pendingCallbacks = [];

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
    /// <param name="consumedArrayOverrides">
    /// The set the applied array corrections are recorded in. A run shares one
    /// across its modules, because the planner is built per module and the
    /// stale entries are reported once for the whole run.
    /// </param>
    /// <param name="consumedAnnotationOverrides">
    /// The set the annotation corrections that were read are recorded in,
    /// shared across the modules of a run for the same reason.
    /// </param>
    internal MarshalPlanner(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        TypeMap types,
        Overlays overlays,
        SkipRules skipRules,
        DiagnosticBag diagnostics,
        HashSet<string>? consumedArrayOverrides = null,
        HashSet<string>? consumedAnnotationOverrides = null)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _types = types;
        _overlays = overlays;
        _skipRules = skipRules;
        _diagnostics = diagnostics;
        _consumedArrayOverrides = consumedArrayOverrides ?? new HashSet<string>(StringComparer.Ordinal);
        _consumedAnnotationOverrides =
            consumedAnnotationOverrides ?? new HashSet<string>(StringComparer.Ordinal);
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
        _pendingCallbacks.Clear();
        MarshalPlan? plan = TryPlanCore(callable, form, context, out reason, ignoreShadowedBy);
        if (plan is null && _rejection is { } rejected)
        {
            reason = rejected;
            ReportRejection(callable, rejected);
        }

        if (plan is not null)
        {
            CommitCallbackUses();
        }

        _pendingCallbacks.Clear();
        return plan;
    }

    /// <summary>
    /// Records the callback uses of a callable that could be planned. Nothing
    /// is written before this point, so a rejected callable leaves no trace on
    /// the callback types it mentioned.
    /// </summary>
    private void CommitCallbackUses()
    {
        foreach ((CallbackPlan plan, bool selfFreeing) in _pendingCallbacks)
        {
            if (selfFreeing)
            {
                plan.SelfFreeing = true;
            }
            else
            {
                plan.UsedOutsideAsync = true;
            }

            _callbacks[plan.DelegateName] = plan;
        }
    }

    /// <summary>
    /// Adds the callback types that only hand bound callables take to the used
    /// set, so that a delegate the bindings do hand out keeps being generated.
    /// </summary>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">Its gir namespace.</param>
    /// <remarks>
    /// <para>
    /// A callback type is emitted because a callable that could be planned
    /// takes one. An entry point that is skipped in favour of a hand written
    /// member therefore takes its callback type down with it, which is how
    /// <c>Gst.ClockCallback</c> and <c>Gst.CustomMetaTransformFunction</c> came
    /// to be copied into <c>Custom/</c> word for word.
    /// </para>
    /// <para>
    /// The hand bound ledger is the statement that the call exists all the
    /// same, so a callback type at least one hand bound consumer takes is
    /// planned here on that consumer's behalf: the hand written member binds
    /// the generated delegate and the generated trampoline rather than a copy
    /// of them. Nothing else changes - the scope rules, the plan and the
    /// self freeing decision are the ones the consumer would have produced had
    /// it been generated - so a type that cannot be planned stays absent.
    /// </para>
    /// </remarks>
    internal void PlanHandBoundCallbacks(ModuleInfo module, GirNamespace ns)
    {
        PlanningContext context = new(module, ns, TypeKind.Callback, null);
        foreach (GirCallable callable in HandBoundCallables(ns))
        {
            _pendingCallbacks.Clear();
            foreach (GirParameter parameter in callable.Parameters)
            {
                if (_repository.Resolve(parameter.Type.Name, ns) is not { Declaration: GirCallback })
                {
                    continue;
                }

                PlanCallbackArgument(
                    callable,
                    parameter,
                    NameMapper.ParameterName(parameter.Name),
                    nullable: false,
                    context);
            }

            // Unlike TryPlan, the claims are kept even when another parameter
            // of the same consumer could not be planned: the consumer is not a
            // member this run decides about, it is a hand written member that
            // exists either way, so what it does hand over is settled.
            CommitCallbackUses();
            _pendingCallbacks.Clear();
        }
    }

    /// <summary>
    /// The callables of a namespace whose managed surface is hand written, in
    /// gir document order.
    /// </summary>
    /// <param name="ns">The namespace to walk.</param>
    /// <returns>The hand bound callables.</returns>
    private IEnumerable<GirCallable> HandBoundCallables(GirNamespace ns)
    {
        foreach (GirFunction function in ns.Functions)
        {
            if (_overlays.IsHandBound(function.CIdentifier))
            {
                yield return function;
            }
        }

        IEnumerable<GirTypeDeclaration> declarations = ns.Classes
            .Cast<GirTypeDeclaration>()
            .Concat(ns.Interfaces)
            .Concat(ns.Records)
            .Concat(ns.Unions);
        foreach (GirTypeDeclaration declaration in declarations)
        {
            foreach (GirFunction callable in declaration.Constructors
                .Concat(declaration.Methods)
                .Concat(declaration.Functions))
            {
                if (_overlays.IsHandBound(callable.CIdentifier))
                {
                    yield return callable;
                }
            }
        }
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

        if (RejectsLifetime(callable, form, context, out InstanceConsumption consumption))
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

            // A value projected structure has no handle to read: the C
            // function takes a pointer to the structure itself, so the
            // instance travels as the pinned address of `this`. The import
            // declares a pointer to the public struct, which is legal because
            // the declaring type lives in the same assembly as the import.
            bool byValue = context.OwnerKind == TypeKind.PlainStruct && form == CallableForm.InstanceMethod;
            arguments.Add(new ArgumentPlan
            {
                Kind = byValue ? ArgumentKind.ValueInstance : ArgumentKind.Instance,
                Name = NameMapper.ParameterName(callable.InstanceParameter.Name),
                PublicType = context.OwnerType,
                RawType = byValue ? context.OwnerType + "*" : NativeInt,
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

        if (StrandsConsumedList(arguments))
        {
            return null;
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
            ObsoleteMessage = AnnotationKeyOf(callable) is { } annotationKey
                ? AnnotationOverrideFor(annotationKey)?.Obsolete
                : null,
            InstanceType = form == CallableForm.ExtensionMethod ? context.OwnerType : null,
            InstanceConsumption = consumption,
            InstanceIsBorrowable = context.OwnerKind == TypeKind.MiniObject,
        };
    }

    /// <summary>
    /// Tests whether the prologue of a callable would strand the list one of
    /// its arguments hands over.
    /// </summary>
    /// <param name="arguments">The planned arguments, in the order the prologue writes them.</param>
    /// <returns><see langword="true"/> when the callable cannot be bound.</returns>
    /// <remarks>
    /// <para>
    /// A consumed list is built element by element and handed over: the spine,
    /// the UTF-8 copies of a list of strings and the reference minted for every
    /// mini object in it belong to the callee from the moment the call is made,
    /// and nothing in the generated body releases them again. The three phase
    /// prologue is what makes that safe, because every guard and every handle
    /// read runs before the first allocation - but the phases only order the
    /// steps against each other. Inside the third phase the steps run in
    /// argument order, so a step that throws after the list was built strands
    /// the whole of it, and there is no epilogue that could release it.
    /// </para>
    /// <para>
    /// The steps of the third phase that can throw are the ones that encode a
    /// string or walk a sequence. A UTF-8 copy the callee takes over
    /// (<see cref="ArgumentKind.Utf8Owned"/>) and the transient copy of a
    /// borrowed string (<see cref="ArgumentKind.Utf8"/>) both refuse an
    /// embedded NUL; a string vector (<see cref="ArgumentKind.Strv"/>) and
    /// either shape of list (<see cref="ArgumentKind.ListIn"/>) refuse a null
    /// element as well. Minting a reference for a consumed handle and
    /// allocating the storage of a caller allocated out read the locals the
    /// second phase produced and call into C, so neither of them can throw, and
    /// everything else the third phase writes is an assignment.
    /// </para>
    /// <para>
    /// A callable that puts one of those steps after a consumed list is
    /// refused rather than emitted with a leak in it. No member of the sixteen
    /// modules has the shape - all three consumed lists are the last argument
    /// of their call - so a synthetic fixture is what keeps the refusal honest.
    /// </para>
    /// </remarks>
    private static bool StrandsConsumedList(List<ArgumentPlan> arguments)
    {
        bool handedOver = false;
        foreach (ArgumentPlan argument in arguments)
        {
            if (handedOver && ThrowsInPrologue(argument))
            {
                return true;
            }

            if (argument.Kind == ArgumentKind.ListIn && argument.Transfer == GirTransfer.Full)
            {
                handedOver = true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the third phase of the prologue can throw on an argument,
    /// which is what <see cref="StrandsConsumedList"/> weighs against a list
    /// that was already handed over.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentKind.GError"/> is deliberately absent: everything
    /// that would make <c>GMarshal.AllocError</c> throw (no domain, no message,
    /// an embedded null) is refused by <c>GException.ValidateForNative</c> in
    /// the first phase, before any list is handed over. That invariant is what
    /// keeps a GError after a consumed list safe; relaxing the guard means
    /// adding the kind here.
    /// </remarks>
    /// <param name="argument">The argument whose materialization is weighed.</param>
    /// <returns><see langword="true"/> when building the argument can throw.</returns>
    private static bool ThrowsInPrologue(ArgumentPlan argument) =>
        argument.Direction == ArgumentDirection.In
        && argument.Kind is ArgumentKind.Utf8 or ArgumentKind.Utf8Owned or ArgumentKind.Strv
            or ArgumentKind.ListIn;

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
                    $"'{name}' consumes the instance and returns a replacement of the same type, in a shape "
                    + "neither self consuming rule covers: only a mini object can mint the reference such a call "
                    + "takes over, and only a '_make_writable' hands the value of the wrapper over in place. The "
                    + "member is skipped.");
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
    /// <param name="consumption">
    /// How the call takes the reference of its instance over, for the two
    /// shapes that are bound; <see cref="InstanceConsumption.None"/> for every
    /// other callable, rejected or not.
    /// </param>
    /// <returns><see langword="true"/> when the callable is rejected.</returns>
    /// <remarks>
    /// <para>
    /// Two shapes are told apart. <c>gst_caps_make_writable</c> and its
    /// relatives consume the instance and hand a replacement of the same type
    /// back. Those are bound rather than rejected, through the two rules of
    /// <see cref="ClassifyConsumption"/>: the wrapper either follows the
    /// answer or mints the reference the call takes over. A shape neither rule
    /// matches — a boxed value that is not reference counted, a GObject — is
    /// still rejected, because minting for it means something else and the
    /// binding has no member to prove it on.
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
    private bool RejectsLifetime(
        GirCallable callable,
        CallableForm form,
        PlanningContext context,
        out InstanceConsumption consumption)
    {
        consumption = InstanceConsumption.None;
        if (form is not (CallableForm.InstanceMethod or CallableForm.ExtensionMethod)
            || callable.InstanceParameter is not { } instance)
        {
            return false;
        }

        bool consumes = instance.Transfer is GirTransfer.Full;
        if (consumes && ReturnsInstanceType(callable, instance, context))
        {
            consumption = ClassifyConsumption(callable, form, context);
            if (consumption != InstanceConsumption.None)
            {
                // The call is bound, and the rule that follows must not see it
                // again: it consumes its instance, which is the very thing that
                // makes a lifetime primitive.
                return false;
            }

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

    /// <summary>
    /// Decides which of the two self consuming shapes a callable has, once it
    /// is known to consume its instance and to return a value of the type of
    /// its instance.
    /// </summary>
    /// <param name="callable">The callable to classify.</param>
    /// <param name="form">The C# shape it would be emitted in.</param>
    /// <param name="context">The type it is emitted into.</param>
    /// <returns>
    /// How the call takes the reference over, or
    /// <see cref="InstanceConsumption.None"/> when neither rule matches.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The name is what tells the two apart, because the annotations cannot:
    /// every one of the eleven <c>_make_writable</c> entry points forwards to
    /// <c>gst_mini_object_make_writable</c> and answers the same logical
    /// object, while <c>gst_caps_truncate</c> and its relatives answer a
    /// converted one. Reading the suffix is what makes the rule survive a gir
    /// refresh; an overlay list of eleven identifiers would silently miss a
    /// twelfth.
    /// </para>
    /// <para>
    /// The kind of the declaring type is the other half of the rule.
    /// <em>In place</em> works for a mini object and for the one boxed type of
    /// the shape, <c>GstUri</c>, which is a mini object underneath —
    /// <c>GST_DEFINE_MINI_OBJECT_TYPE</c> registers its boxed copy as
    /// <c>gst_mini_object_ref</c> — so giving up the handle of the wrapper
    /// hands over exactly one reference either way. <em>Minted</em> is mini
    /// object only, because the mint of a boxed value is a copy and a
    /// conversion that consumed a copy would leave the original where it was.
    /// </para>
    /// </remarks>
    private static InstanceConsumption ClassifyConsumption(
        GirCallable callable,
        CallableForm form,
        PlanningContext context)
    {
        if (form != CallableForm.InstanceMethod)
        {
            return InstanceConsumption.None;
        }

        bool inPlace = callable.CIdentifier?.EndsWith("_make_writable", StringComparison.Ordinal) ?? false;
        return context.OwnerKind switch
        {
            TypeKind.MiniObject => inPlace ? InstanceConsumption.InPlace : InstanceConsumption.Minted,
            TypeKind.Boxed when inPlace => InstanceConsumption.InPlace,
            _ => InstanceConsumption.None,
        };
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

        // The GObject spelling of the signal, which is the key an overlay
        // addresses its arguments by. It is the same key NameMapper.SignalName
        // renames on and the same one the skip report prints, so a correction
        // is written once and reads the same everywhere.
        string signalKey = context.Namespace.Name + "." + owner.Name + "::" + signal.Name;
        string? argsName = signal.Parameters.Count > 0 ? ArgsClassName(name) : null;
        HashSet<string> taken = new(ArgsMemberNames, StringComparer.Ordinal);
        if (argsName is not null)
        {
            taken.Add(argsName);
        }

        List<SignalArgument> arguments = [];
        foreach (GirParameter parameter in signal.Parameters)
        {
            ArgumentPlan? argument = PlanSignalArgument(parameter, context, signalKey);

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
    /// <remarks>
    /// The lengths are read off the <em>effective</em> arrays, so that a length
    /// index the overlays supply hides its parameter before the per parameter
    /// loop projects it. The contradiction checks hold for an overlay supplied
    /// index exactly as they do for one the gir states.
    /// </remarks>
    private bool MarkHiddenArguments(GirCallable callable, ArgumentKind[] forced, int[] owners)
    {
        IReadOnlyList<GirParameter> parameters = callable.Parameters;
        for (int i = 0; i < parameters.Count; i++)
        {
            GirParameter parameter = parameters[i];
            if (EffectiveArrayOf(callable, parameter) is { LengthParameterIndex: int length })
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

        if (EffectiveArrayOf(callable) is { LengthParameterIndex: int returnLength })
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

    /// <summary>Reads one annotation correction and records that it was read.</summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Every lookup of an annotation correction goes through here, so that a
    /// key which never answers one can be reported as stale. Only a lookup
    /// that found an entry counts as a use: a key that matches nothing is
    /// exactly the case this records.
    /// </remarks>
    private AnnotationOverride? AnnotationOverrideFor(string key)
    {
        AnnotationOverride? correction = _overlays.GetAnnotationOverride(key);
        if (correction is not null)
        {
            _consumedAnnotationOverrides.Add(key);
        }

        return correction;
    }

    /// <summary>Reads the annotation correction of one parameter, if there is one.</summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter to look up.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    private AnnotationOverride? OverrideOf(GirCallable callable, GirParameter parameter) =>
        AnnotationKeyOf(callable) is { } identifier
            ? AnnotationOverrideFor(identifier + "#" + parameter.Name)
            : null;

    /// <summary>Reads the array correction of one parameter, if there is one.</summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter to look up.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    private ArrayOverride? ArrayOverrideOf(GirCallable callable, GirParameter parameter) =>
        AnnotationKeyOf(callable) is { } identifier
            ? _overlays.GetArrayOverride(identifier + "#" + parameter.Name)
            : null;

    /// <summary>
    /// Returns the array a parameter really is: the one the gir spells, with
    /// the corrections of <c>arrayOverrides</c> applied.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter being projected.</param>
    /// <returns>The array, or <see langword="null"/> when the parameter is none.</returns>
    private GirArrayRef? EffectiveArrayOf(GirCallable callable, GirParameter parameter) =>
        EffectiveArray(
            parameter.Type,
            AnnotationKeyOf(callable) is { } identifier ? identifier + "#" + parameter.Name : null);

    /// <summary>
    /// Returns the array the return value really is, with the corrections of
    /// <c>arrayOverrides</c> applied.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <returns>The array, or <see langword="null"/> when the return value is none.</returns>
    private GirArrayRef? EffectiveArrayOf(GirCallable callable) =>
        EffectiveArray(
            callable.ReturnValue.Type,
            AnnotationKeyOf(callable) is { } identifier ? identifier + "#return" : null);

    /// <summary>Applies an array correction to one gir type reference.</summary>
    /// <param name="type">The declared type of the parameter or return value.</param>
    /// <param name="key">The overlay key it is addressed by, if it has one.</param>
    /// <returns>The corrected array, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// <para>
    /// A correction of something the gir does not spell as an
    /// <c>&lt;array&gt;</c> is ignored: deciding that a bare pointer is an
    /// array is exactly the decision an overlay must not make on its own, and
    /// the entry is reported as stale because nothing consumed it.
    /// </para>
    /// <para>
    /// <c>length</c> and <c>fixed-size</c> are mutually exclusive in GIR, so an
    /// entry that states one clears the other. Everything else the correction
    /// leaves unsaid is carried over from the declared array.
    /// </para>
    /// </remarks>
    private GirArrayRef? EffectiveArray(GirTypeRef type, string? key)
    {
        GirArrayRef? array = type as GirArrayRef;
        if (key is null || _overlays.GetArrayOverride(key) is not { } correction)
        {
            return array;
        }

        if (array is null)
        {
            return null;
        }

        _consumedArrayOverrides.Add(key);
        return new GirArrayRef
        {
            Name = array.Name,
            CType = array.CType,
            IsVarArgs = array.IsVarArgs,
            LengthParameterIndex =
                correction.Length ?? (correction.FixedSize is null ? array.LengthParameterIndex : null),
            IsZeroTerminated = correction.ZeroTerminated ?? array.IsZeroTerminated,
            FixedSize = correction.FixedSize ?? (correction.Length is null ? array.FixedSize : null),
            InnerTypes = correction.ElementType is { } element
                ? [new GirTypeRef { Name = element }]
                : array.InnerTypes,
        };
    }

    private GirTransfer TransferOf(GirCallable callable, GirParameter parameter) =>
        ParseTransfer(OverrideOf(callable, parameter)?.Transfer) ?? parameter.Transfer;

    private GirTransfer TransferOf(GirCallable callable)
    {
        AnnotationOverride? overlay = AnnotationKeyOf(callable) is { } identifier
            ? AnnotationOverrideFor(identifier + "#return")
            : null;

        return ParseTransfer(overlay?.Transfer) ?? callable.ReturnValue.Transfer;
    }

    private bool NullableOf(GirCallable callable, GirParameter parameter) =>
        OverrideOf(callable, parameter)?.Nullable ?? parameter.IsNullable;

    private bool CallerAllocatesOf(GirCallable callable, GirParameter parameter) =>
        OverrideOf(callable, parameter)?.CallerAllocates ?? parameter.IsCallerAllocates;

    /// <summary>
    /// Reads how long the library keeps the callback it is handed, which the
    /// overlays may correct.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The callback parameter.</param>
    /// <returns>The effective scope.</returns>
    /// <remarks>
    /// Unlike a transfer or a nullability, a scope that is wrong is not a
    /// missing projection but a lifetime bug in shipped code, so an override
    /// that says nothing — an unknown spelling, or the value the gir already
    /// carries — is reported rather than dropped silently.
    /// </remarks>
    private GirScope ScopeOf(GirCallable callable, GirParameter parameter)
    {
        if (OverrideOf(callable, parameter)?.Scope is not { } value)
        {
            return parameter.Scope;
        }

        string key = (AnnotationKeyOf(callable) ?? callable.Name) + "#" + parameter.Name;
        if (ParseScope(value) is not { } scope)
        {
            _diagnostics.Warn(
                "GEN0021",
                $"the scope override of '{key}' names '{value}', which is not one of call, notified, async, "
                + "forever; the override is ignored.");
            return parameter.Scope;
        }

        if (scope == parameter.Scope)
        {
            _diagnostics.Warn(
                "GEN0021",
                $"the scope override of '{key}' states what the gir already says; the override is ignored.");
        }

        return scope;
    }

    private static GirScope? ParseScope(string? value) => value switch
    {
        "call" => GirScope.Call,
        "notified" => GirScope.Notified,
        "async" => GirScope.Async,
        "forever" => GirScope.Forever,
        _ => null,
    };

    private bool NullableOf(GirCallable callable)
    {
        AnnotationOverride? overlay = AnnotationKeyOf(callable) is { } identifier
            ? AnnotationOverrideFor(identifier + "#return")
            : null;

        return overlay?.Nullable ?? callable.ReturnValue.IsNullable;
    }

    /// <summary>
    /// Tests whether the overlays drop the return value of a callable, which
    /// makes the member void whatever the C function hands back.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <returns><see langword="true"/> when the return value is not bound.</returns>
    /// <remarks>
    /// The raw entry point is declared void as well. Ignoring a returned
    /// register is what a C caller that discards the value does, so nothing of
    /// the call changes; what the correction states is that the value is not
    /// worth a projection, which is the case when it is a pointer the caller
    /// passed in. A return the callee transfers the ownership of is not such a
    /// value, and <see cref="PlanReturn"/> reports and ignores the correction
    /// on one rather than leaking the allocation.
    /// </remarks>
    private bool DiscardsReturn(GirCallable callable) =>
        AnnotationKeyOf(callable) is { } identifier
        && AnnotationOverrideFor(identifier + "#return")?.DiscardReturn == true;

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
    private bool IsEmitted(GirSymbol symbol) => IsEmitted(symbol, _overlays, _classifier);

    /// <summary>
    /// Tests whether this run emits a wrapper of a symbol, without a planner to
    /// ask. The record emitter needs the same answer for the fields that hold a
    /// handle, and there is one rule for both.
    /// </summary>
    /// <param name="symbol">The symbol to test.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <returns><see langword="true"/> when the run emits the type.</returns>
    internal static bool IsEmitted(GirSymbol symbol, Overlays overlays, Classifier classifier)
    {
        // Any module of the run may declare the type: GstAppSink returns a
        // Gst.FlowReturn and takes a Gst.Caps, and both are generated. Only the
        // GLib stack, whose runtime layer is hand written, is out of reach.
        if (ModuleMap.Find(symbol.Namespace.Name) is not { IsGenerated: true }
            || overlays.IsSkipped(symbol.QualifiedName)
            || !symbol.Declaration.IsIntrospectable
            || (symbol.Declaration is GirRecord record && Classifier.IsPrivateShell(record)))
        {
            return false;
        }

        return classifier.Classify(symbol.Declaration) is TypeKind.GObjectClass or TypeKind.MiniObject
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

        // The array facts of the parameter are read once, here, and everything
        // downstream is handed the corrected reference: the mapping of the
        // element comes out of it as well, which is what makes an `elementType`
        // correction take effect.
        GirTypeRef effective = EffectiveArrayOf(callable, parameter) ?? parameter.Type;
        MappedType mapped = _types.Map(effective, context.Namespace);
        AnnotationOverride? overlay = OverrideOf(callable, parameter);

        if (mapped.Kind == MarshalKind.Callback)
        {
            return PlanCallbackArgument(callable, parameter, name, nullable, context);
        }

        // The overlay states the size of a parameter the gir spells as a
        // pointer to one value. An <array> of its own is what arrayOverrides
        // corrects, so this path keeps refusing one: the gate lives here
        // rather than inside PlanFixedArray, which the fixed size arm below
        // reaches with an array on purpose.
        if (overlay?.FixedArraySize is int size)
        {
            if (effective is GirArrayRef)
            {
                _diagnostics.Warn(
                    "GEN0017",
                    $"The fixed array size of '{AnnotationKeyOf(callable)}#{parameter.Name}' is ignored: the gir "
                    + "already spells the parameter as an array, whose size is corrected through "
                    + "'arrayOverrides'.");
            }
            else if (PlanFixedArray(callable, parameter, name, mapped, direction, size, context) is { } fixedArray)
            {
                return fixedArray;
            }
        }

        ArgumentDirection declared = direction;
        direction = OverriddenDirection(callable, parameter, effective, mapped, direction, overlay);

        // A destination the overlays moved off the out position is the storage
        // the C function works on, not a result the member produces.
        bool redirectedDestination = direction == ArgumentDirection.In && declared != ArgumentDirection.In;

        // A block the C declaration sizes itself. The count is part of the
        // type rather than of the call, so there is no length argument to hide
        // and no pointer coming back: an in array is a span of exactly that
        // many elements, and an out array the caller allocates is the inline
        // storage a fixedArraySize correction already produces. Anything else
        // falls through and stays unbound, silently: the girs spell a great
        // many fixed size arrays this cannot project.
        if (effective is GirArrayRef { FixedSize: int fixedSize } sized && fixedSize > 0)
        {
            if (direction == ArgumentDirection.In
                && mapped.ElementType is { } inElement
                && ArrayElementType(inElement) is { } inElementType)
            {
                // An array the callee takes over cannot be a span, whether the
                // count comes from a length argument or from the C declaration
                // itself: the caller keeps owning the memory a span points at,
                // and freeing it inside the library would corrupt the heap.
                // The counted arm below states the same rule.
                if (transfer is GirTransfer.Full or GirTransfer.Container)
                {
                    return null;
                }

                bool readOnlyFixed = sized.CType?.Contains("const", StringComparison.Ordinal) ?? false;
                return new ArgumentPlan
                {
                    Source = parameter,
                    Kind = ArgumentKind.Span,
                    Name = name,
                    PublicType = (readOnlyFixed ? "System.ReadOnlySpan<" : "System.Span<") + inElementType + ">",
                    RawType = inElementType + "*",
                    Direction = ArgumentDirection.In,
                    ElementType = inElementType,
                    FixedLength = fixedSize,
                    IsNullable = nullable,
                };
            }

            // The caller allocates gate is what makes this safe: an out array
            // the gir calls transfer=full caller-allocates=0 would otherwise be
            // read as a pointer the callee allocated, and the storage the
            // caller really passed would be read as that pointer.
            if (direction == ArgumentDirection.Out
                && CallerAllocatesOf(callable, parameter)
                && mapped.ElementType is { } outElement
                && PlanFixedArray(
                    callable,
                    parameter,
                    name,
                    outElement,
                    direction,
                    fixedSize,
                    context,
                    reportFailure: false) is { } sizedStorage)
            {
                return sizedStorage;
            }
        }

        if (effective is GirArrayRef array)
        {
            return PlanArrayArgument(
                parameter,
                array,
                mapped,
                name,
                direction,
                transfer,
                nullable,
                index,
                context,
                offset,
                CallerAllocatesOf(callable, parameter),
                ArrayOverrideOf(callable, parameter)?.Length is not null);
        }

        if (direction == ArgumentDirection.In && IsPointerToHandlePointer(effective, mapped))
        {
            return Reject(SkipReason.UnsupportedSignature);
        }

        return PlanScalar(
            effective,
            mapped,
            name,
            direction,
            transfer,
            nullable,
            context,
            // A destination that was moved onto `in` is not storage the member
            // produces, whatever the gir annotated beside the direction. The
            // overlay entry says so as well; clearing it here is what makes
            // that statement a second reading of the correction rather than
            // the only thing holding it up.
            callerAllocates: !redirectedDestination && CallerAllocatesOf(callable, parameter),
            booleanCallee: IsBooleanCallee(callable, context),
            redirectedDestination: redirectedDestination);
    }

    /// <summary>
    /// Tests whether an inbound argument is a pointer to a pointer to
    /// something that is bound behind a handle, which is a shape no
    /// marshalling covers.
    /// </summary>
    /// <param name="effective">The type of the argument, with the array corrections applied.</param>
    /// <param name="mapped">Its mapping.</param>
    /// <returns><see langword="true"/> when the argument cannot be bound.</returns>
    /// <remarks>
    /// <para>
    /// A handle argument crosses as the pointer the wrapper holds. A
    /// <c>c:type</c> that ends in two stars asks for the address of the
    /// caller's own pointer variable instead, which is one level of
    /// indirection more than a handle has: the callee would read the object
    /// itself as if it were a pointer. <c>gst_play_visualizations_free</c> is
    /// the shape — its gir spells the parameter as a plain
    /// <c>&lt;type name="PlayVisualization" c:type="GstPlayVisualization**"/&gt;</c>
    /// with no direction and no array annotation, and the C function walks the
    /// block it is handed to its <c>NULL</c> terminator — so the member would
    /// compile, look like an ordinary binding and corrupt memory on the first
    /// call.
    /// </para>
    /// <para>
    /// All three inbound entry points are guarded, because each reaches
    /// <c>PlanScalar</c> on its own: the <c>in</c> parameter of a callable, the
    /// argument of a callback and the argument of a signal. A trampoline hands
    /// its delegate what native code passed, so a handler would read the shape
    /// wrongly exactly the way a callee does.
    /// </para>
    /// <para>
    /// Only the <c>in</c> direction is refused, and only for the kinds the
    /// handle plan covers. An <c>out</c> parameter of the same <c>c:type</c> is
    /// the ordinary pointer the callee writes the handle back through and keeps
    /// its own projection, and an <c>inout</c> one is already refused by the
    /// handle plan, which binds no <c>ref</c> handle at all; a <c>gchar**</c>, a
    /// <c>GError**</c> and an <c>&lt;array&gt;</c> whose element type is a
    /// handle are all spelled with two stars as well and each has a
    /// marshalling of its own, reached before this test. A gir that means an
    /// array and forgets to say so is corrected through <c>arrayOverrides</c>
    /// rather than here. A callback and a signal refuse every direction but
    /// <c>in</c> before they reach this test at all.
    /// </para>
    /// </remarks>
    private static bool IsPointerToHandlePointer(GirTypeRef effective, MappedType mapped)
    {
        if (effective is GirArrayRef || !(effective.CType?.EndsWith("**", StringComparison.Ordinal) ?? false))
        {
            return false;
        }

        // The kinds PlanScalar routes to PlanHandle, which is what makes the
        // rule the exact complement of the one projection this shape would
        // otherwise fall into. A GParamSpec is a fundamental of its own and is
        // recognised by name there, so it is recognised by name here too.
        return mapped.Kind switch
        {
            MarshalKind.GObject or MarshalKind.MiniObject or MarshalKind.Boxed
                or MarshalKind.OpaqueRecord => true,
            MarshalKind.Fundamental => mapped.Symbol is { QualifiedName: ParamSpecType },
            _ => false,
        };
    }

    /// <summary>
    /// Tests whether a value is a scalar the binding passes by value while its
    /// <c>c:type</c> says it is a pointer, which is a shape no marshalling
    /// covers.
    /// </summary>
    /// <param name="type">The type of the value, with the array corrections applied.</param>
    /// <param name="mapped">Its mapping.</param>
    /// <returns><see langword="true"/> when the value cannot be bound.</returns>
    /// <remarks>
    /// <para>
    /// A pointer typed scalar cannot be projected: the value would stand where
    /// C means an address. The two shapes the rule was written for are both in
    /// GstRtp. <c>gst_rtcp_packet_fb_get_fci</c> and
    /// <c>gst_rtcp_packet_app_get_data</c> answer a <c>guint8*</c> through a
    /// <c>&lt;type name="guint8" c:type="guint8*"/&gt;</c>, so the return would
    /// be a <c>byte</c> and the pointer would be truncated to its lowest byte;
    /// <c>gst_buffer_add_rtp_source_meta</c> and
    /// <c>gst_rtp_source_meta_set_ssrc</c> spell their <c>ssrc</c> as a
    /// nullable <c>&lt;type name="guint32" c:type="const guint32*"/&gt;</c>
    /// with no direction and no array annotation, so the member would hand the
    /// number itself to a C function that dereferences it. Both would compile
    /// and neither would be a binding.
    /// </para>
    /// <para>
    /// Only the kinds <see cref="PlanScalar"/> itself passes by value are
    /// named, which is what makes the rule the exact complement of the
    /// projection this shape would otherwise fall into. <c>gpointer</c> and
    /// <c>gconstpointer</c> are pointers on purpose and are excluded with the
    /// rest; a string, a <c>GValue</c>, a <c>GError</c>, a <c>GDate</c> and
    /// anything bound behind a handle each carry a star of their own and have
    /// a marshalling that reads it. An <c>&lt;array&gt;</c> is excluded as
    /// well: a block of scalars is exactly what the array plans project, and a
    /// gir that means an array and forgets to say so has to be corrected in the
    /// gir itself, since <c>arrayOverrides</c> only refines an
    /// <c>&lt;array&gt;</c> that is already there.
    /// </para>
    /// <para>
    /// The test is general and only the return side acts on it today: the in
    /// parameter side of the same shape is knowingly not refused yet, because
    /// two published members project it -
    /// <c>gst_audio_base_sink_set_custom_slaving_callback</c>, through the
    /// <c>GstClockTimeDiff*</c> of
    /// <c>GstAudioBaseSinkCustomSlavingCallback#requested_skew</c>, and
    /// <c>gst_video_gl_texture_upload_meta_upload</c>, through the
    /// <c>guint*</c> of its <c>texture_id</c>. Removing them is a source break
    /// reserved for an <c>[Obsolete]</c> bridge rather than for this rule.
    /// </para>
    /// <para>
    /// The star is looked for with <c>Contains('*')</c> and not through
    /// <c>GirTypeRef.IsPointer</c>, which reads the last character only: a
    /// <c>c:type</c> that carries a qualifier after the star would hide it from
    /// the shorter test.
    /// </para>
    /// </remarks>
    private static bool IsPointerToScalar(GirTypeRef type, MappedType mapped)
    {
        if (type is GirArrayRef || !(type.CType?.Contains('*', StringComparison.Ordinal) ?? false))
        {
            return false;
        }

        return mapped.Kind switch
        {
            MarshalKind.Blittable or MarshalKind.Boolean or MarshalKind.GType or MarshalKind.Quark
                or MarshalKind.Enum or MarshalKind.Flags => true,
            _ => false,
        };
    }

    /// <summary>
    /// Tests whether the callable answers a <c>gboolean</c> the member hands
    /// on, which is what says that a caller allocated out parameter is only
    /// filled on success.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="context">The type the member is emitted into.</param>
    /// <returns><see langword="true"/> for a <c>gboolean</c> return that is bound.</returns>
    /// <remarks>
    /// <para>
    /// A discarded return makes the member void, so there is no answer to read
    /// the success of the call off: the storage is handed over unconditionally,
    /// the way it is for a callee that returns nothing of its own. Planning it
    /// against the return the gir states would spell an epilogue over a
    /// <c>nativeResult</c> that the member never declares.
    /// </para>
    /// <para>
    /// <see cref="PlanReturn"/> refuses the correction on a return the caller
    /// owns, which a <c>gboolean</c> is not; a gir that annotates one as such
    /// is reported as GEN0019 and keeps the return, and this reads it as the
    /// void callee it was corrected to be.
    /// </para>
    /// </remarks>
    private bool IsBooleanCallee(GirCallable callable, PlanningContext context) =>
        _types.Map(callable.ReturnValue.Type, context.Namespace).Kind == MarshalKind.Boolean
        && !DiscardsReturn(callable);

    /// <summary>
    /// Applies the <c>direction</c> correction of the overlays to a parameter
    /// the gir spells as a bare pointer to a plain structure or to a scalar.
    /// </summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The parameter being projected.</param>
    /// <param name="effective">Its type, with the array corrections applied.</param>
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
    /// The correction reaches a plain structure and a <c>GValue</c>, whose
    /// projection is a pointer into the caller's own storage as well — that is
    /// what moves <c>gst_value_deserialize</c> from a destination this would
    /// zero to the pre-initialized one its parser table reads, and
    /// <c>gst_value_fixate</c> from a read to the fill it really is. It reaches
    /// a scalar too: the out projection of a <c>guint32</c> or of a
    /// <c>gboolean</c> is a local of exactly the width the C declaration names,
    /// whose address is what the callee was handed all along, so nothing about
    /// its size or its layout is in doubt. The star of the <c>c:type</c> is the
    /// evidence that the C side writes through it, so a scalar whose
    /// <c>c:type</c> carries none keeps the value it is; an enumeration and a
    /// bitfield are refused as well, having no case that asks for it. A handle,
    /// a string or an array has an out projection of its own with a conversion
    /// on either side of the call, and turning one into a bare pointer to
    /// managed storage would hand native code the address of something whose
    /// size and layout it does not agree with.
    /// </para>
    /// <para>
    /// <c>in</c> is the one correction a pointer to a <em>record</em> takes.
    /// The gir of <c>gst_sdp_media_set_media_from_caps</c> calls its
    /// <c>media</c> a caller allocated out, and the C function frees the media
    /// string that is already there and appends to <c>media-&gt;fmts</c>: it
    /// requires an initialised <c>GstSDPMedia</c> and would walk uninitialised
    /// storage otherwise. Corrected onto <c>in</c> the parameter plans as the
    /// ordinary handle it always was, which is why the redirect is spelled here
    /// and not as a marshalling of its own.
    /// </para>
    /// </remarks>
    private ArgumentDirection OverriddenDirection(
        GirCallable callable,
        GirParameter parameter,
        GirTypeRef effective,
        MappedType mapped,
        ArgumentDirection declared,
        AnnotationOverride? overlay)
    {
        if (ParseDirection(overlay?.Direction) is not { } overridden || overridden == declared)
        {
            return declared;
        }

        // Each correction is checked on its own terms. A record the gir calls a
        // caller allocated out and the C function really reads and updates in
        // place is the third shape, and it is the only one `in` applies to: the
        // parameter plans as the ordinary handle it always was, and the
        // redirect clears caller-allocates with it, so nothing downstream still
        // believes the member produces storage. `out` and `ref` stay what they
        // were, the two halves of a pointer to a plain structure, to a GValue
        // or to a scalar; the star of the c:type is what says the callee writes
        // through the parameter, so a scalar without one keeps its value
        // projection.
        bool corrected = effective is not GirArrayRef
            && effective.IsPointer
            && (overridden == ArgumentDirection.In
                ? mapped.Kind is MarshalKind.Boxed or MarshalKind.OpaqueRecord or MarshalKind.MiniObject
                : mapped.Kind is MarshalKind.PlainStruct or MarshalKind.GValue
                    or MarshalKind.Blittable or MarshalKind.Boolean);

        if (!corrected)
        {
            _diagnostics.Warn(
                "GEN0017",
                $"The direction override of '{AnnotationKeyOf(callable)}#{parameter.Name}' is ignored: only a "
                + "pointer to a plain structure, to a GValue or to a scalar can be re-planned as an out or a ref "
                + "parameter, and only a pointer to a record can be re-planned as an in parameter.");
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
    /// <param name="reportFailure">
    /// <see langword="true"/> to report a shape this cannot plan. Only the
    /// overlay driven path does: an <c>&lt;array fixed-size&gt;</c> the gir
    /// spells itself is offered here and falls through silently when it is not
    /// the caller allocated out this projects, because the gir states a great
    /// many of them that no member of the surface reaches.
    /// </param>
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
        PlanningContext context,
        bool reportFailure = true)
    {
        if (size > 0
            && direction == ArgumentDirection.Out
            && parameter.Type.IsPointer
            && mapped.Kind == MarshalKind.Blittable
            && string.Equals(mapped.RawType, mapped.PublicType, StringComparison.Ordinal)
            && (context.StorageOwner ?? context.OwnerType) is { } owner)
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

        if (reportFailure)
        {
            _diagnostics.Warn(
                "GEN0017",
                $"The fixed array size of '{AnnotationKeyOf(callable)}#{parameter.Name}' is ignored: only an out "
                + "parameter of a blittable value, declared on a type that can carry the storage, is planned as a "
                + "caller allocated array.");
        }

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
    /// <param name="inbound">
    /// Whether the value travels from native code into managed code because a
    /// trampoline is handed it: a parameter of a signal or of a callback. Such
    /// an argument is passed <c>In</c> and usually transfers nothing, exactly
    /// as an argument this code passes to a call does, so the two are told
    /// apart by this flag alone.
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
        bool callerAllocates = false,
        bool booleanCallee = false,
        bool redirectedDestination = false,
        bool inbound = false)
    {
        // A pointer typed scalar cannot be projected: the value would be read
        // where C hands an address over, and a returned pointer would be
        // truncated to the width of the scalar. The refusal is made on the
        // return side alone for now, which is every return PlanScalar plans -
        // a method, a function, a constructor, a virtual method invoker and a
        // signal all reach it with `isReturn`, and a callback return is
        // refused where it is planned, because it reaches PlanScalar without
        // the flag. The in parameter side of the same shape is knowingly not
        // refused yet; IsPointerToScalar says why.
        if (isReturn && IsPointerToScalar(type, mapped))
        {
            return null;
        }

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

            case MarshalKind.GValue:
                return PlanGValue(type, name, direction, transfer, nullable, isReturn);

            case MarshalKind.GError:
                return PlanGError(name, direction, transfer, nullable);

            case MarshalKind.Date:
                return PlanDate(type, name, direction, transfer, nullable, isReturn, callerAllocates);

            // A GParamSpec is a fundamental type of its own rather than a
            // GObject, and the runtime wraps it by hand, so it reaches the
            // handle plan through its qualified name. Every other fundamental
            // - GObject.Closure and the GParamSpec subclasses among them - has
            // no wrapper to name and stays rejected.
            case MarshalKind.Fundamental when mapped.Symbol is { QualifiedName: ParamSpecType }:
            case MarshalKind.GObject:
            case MarshalKind.MiniObject:
            case MarshalKind.Boxed:
            case MarshalKind.OpaqueRecord:
                return PlanHandle(
                    mapped,
                    name,
                    direction,
                    transfer,
                    nullable,
                    isReturn,
                    callerAllocates,
                    booleanCallee,
                    redirectedDestination,
                    inbound);

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

            case MarshalKind.GList:
            case MarshalKind.GSList:
                return PlanListArgument(
                    type,
                    mapped,
                    name,
                    direction,
                    transfer,
                    nullable,
                    isReturn,
                    callerAllocates);

            default:
                return null;
        }
    }

    /// <summary>
    /// Plans a <c>GValue</c>, which crosses as a pointer into storage the
    /// caller owns: the runtime <c>Gst.GObject.Value</c> struct, passed by
    /// <c>in</c>, <c>ref</c> or <c>out</c>. Nothing is allocated for the call
    /// and nothing is disposed after it, so the argument takes no part in the
    /// three phase prologue of the materializing members.
    /// </summary>
    /// <param name="type">The gir type reference, whose C type tells const from writable.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="direction">How the argument is passed, overrides applied.</param>
    /// <param name="transfer">The ownership transfer.</param>
    /// <param name="nullable">Whether the gir marks the value nullable.</param>
    /// <param name="isReturn">Whether the value is the return value.</param>
    /// <returns>The plan, or <see langword="null"/> when the shape is not supported.</returns>
    /// <remarks>
    /// <para>
    /// The shapes, keyed on direction, transfer and the const-ness of the C
    /// type. A <c>const GValue*</c> in parameter is only read; it is guarded
    /// against the empty value, which the callee would <c>g_critical</c> on and
    /// ignore. A non-const <c>GValue*</c> in parameter is storage the callee
    /// writes under a contract of its own — <c>gst_value_set_fraction</c> wants
    /// an initialized fraction, <c>gst_value_fraction_multiply</c> a
    /// pre-initialized product — so it crosses as <c>ref</c> and is not
    /// guarded: which states are valid is the callee's to say. A <c>ref</c>
    /// direction also arrives from the overlays, for the out parameters whose
    /// callee reads the type of the destination before writing it
    /// (<c>gst_value_deserialize</c>, <c>gst_util_set_value_from_string</c>).
    /// </para>
    /// <para>
    /// An out parameter is bound when it is a single <c>GValue*</c> the callee
    /// fills: the member zeroes the caller's storage — the uninitialized state
    /// <c>g_value_init</c> expects to find — and the callee writes in place. A
    /// nullable or optional annotation is ignored there: storage is always
    /// passed, and a callee that declines leaves it empty, which disposes as a
    /// no-op. A <c>const GValue**</c> hands back a borrowed pointer instead
    /// (<c>gst_message_parse_property_notify</c>) and stays unsupported.
    /// </para>
    /// <para>
    /// Two in shapes are rejected on purpose, and a synthetic fixture asserts
    /// each so that a refactor cannot silently widen them. A callee that takes
    /// the contents over (<c>transfer-ownership="full"</c>, the
    /// <c>take_value</c> family) would leave the caller's struct owning what
    /// the callee now owns; binding that needs an emission that moves the
    /// contents out of the caller's value, which does not exist. And a nullable
    /// <c>GValue</c> cannot be expressed: a C# <c>in</c> struct has no null.
    /// </para>
    /// </remarks>
    private static ArgumentPlan? PlanGValue(
        GirTypeRef type,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        bool isReturn)
    {
        const string PublicType = "Gst.GObject.Value";
        const string RawType = "Gst.GObject.GValueNative*";

        if (isReturn)
        {
            // A returned GValue becomes a value of the caller's own: a
            // borrowed return is copied, an owned one is adopted — contents
            // moved, shell freed. NULL is the empty value either way, so the
            // public type is never nullable.
            if (transfer is not (GirTransfer.None or GirTransfer.Full))
            {
                return null;
            }

            return new ArgumentPlan
            {
                Kind = ArgumentKind.GValue,
                Name = name,
                PublicType = PublicType,
                RawType = NativeInt,
                Transfer = transfer,
            };
        }

        if (direction == ArgumentDirection.Out)
        {
            // Only a single GValue* the callee fills is storage this can
            // provide; a `const GValue**` hands back a borrowed pointer and
            // stays unsupported.
            if (type.CType is not { } cType
                || !cType.EndsWith('*')
                || cType.EndsWith("**", StringComparison.Ordinal))
            {
                return null;
            }

            return new ArgumentPlan
            {
                Kind = ArgumentKind.GValue,
                Name = name,
                PublicType = PublicType,
                RawType = RawType,
                Direction = ArgumentDirection.Out,
                Transfer = transfer,
            };
        }

        // The take_value family. Rejected on purpose; the synthetic fixture
        // that asserts this path is the only guard — no introspectable real
        // gir case survives the overlays — so do not let a refactor turn it
        // into a fall-through.
        if (transfer != GirTransfer.None)
        {
            return null;
        }

        // A C# `in` or `ref` struct cannot be null. Rejected on purpose and
        // asserted by a synthetic fixture as well.
        if (nullable)
        {
            return null;
        }

        bool isConst = type.CType?.Contains("const", StringComparison.Ordinal) ?? false;
        return new ArgumentPlan
        {
            Kind = ArgumentKind.GValue,
            Name = name,
            PublicType = PublicType,
            RawType = RawType,
            Direction = direction == ArgumentDirection.In && isConst
                ? ArgumentDirection.In
                : ArgumentDirection.Ref,
        };
    }

    /// <summary>
    /// Plans a <c>GError</c>, projected onto <c>Gst.GLib.GException</c>.
    /// </summary>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="direction">How the argument is passed, overrides applied.</param>
    /// <param name="transfer">The ownership transfer.</param>
    /// <param name="nullable">Whether the gir marks the value nullable.</param>
    /// <returns>The plan, or <see langword="null"/> when the shape is not supported.</returns>
    /// <remarks>
    /// <para>
    /// Exactly one shape is understood: an error the callee only borrows,
    /// travelling <c>in</c>. A parameter of that shape is built into a
    /// temporary <c>GError</c> that the member frees when the call returns,
    /// which is safe because every callee in the corpus either copies it
    /// (<c>gst_message_new_error</c> and its relatives, through
    /// <c>g_error_copy</c>) or only reads it
    /// (<c>gst_object_default_error</c>). A return value reaches this with
    /// <c>direction: In</c> as well - that is how <c>PlanReturn</c> and
    /// <c>PlanSignalReturn</c> call <c>PlanScalar</c> - and is read into a
    /// managed value the binding copies eagerly and never frees.
    /// </para>
    /// <para>
    /// An <c>out</c> or <c>ref</c> direction is refused, and that refusal is
    /// what keeps the explicit <c>GError**</c> direction out of the pipeline:
    /// <c>gst_message_parse_error</c> and its relatives stay rejected, and the
    /// hand written members of <c>Custom/Message.cs</c> keep the surface. A
    /// transfer other than <c>none</c> is refused for the same reason the
    /// scope exists: the temporary belongs to the member, and a callee that
    /// took it over would be freed twice.
    /// </para>
    /// </remarks>
    private static ArgumentPlan? PlanGError(
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable)
    {
        if (direction != ArgumentDirection.In || transfer != GirTransfer.None)
        {
            return null;
        }

        return new ArgumentPlan
        {
            Kind = ArgumentKind.GError,
            Name = name,
            PublicType = nullable ? "Gst.GLib.GException?" : "Gst.GLib.GException",
            RawType = NativeInt,
            Direction = ArgumentDirection.In,
            Transfer = transfer,
            IsNullable = nullable,
        };
    }

    /// <summary>
    /// Plans a <c>GDate</c>, projected onto <c>System.DateOnly</c>.
    /// </summary>
    /// <param name="type">The gir type reference.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="direction">How the argument is passed, overrides applied.</param>
    /// <param name="transfer">The ownership transfer.</param>
    /// <param name="nullable">Whether the gir marks the value nullable.</param>
    /// <param name="isReturn">Whether the value is the return value of the callable.</param>
    /// <param name="callerAllocates">Whether the caller provides the storage of an out parameter.</param>
    /// <returns>The plan, or <see langword="null"/> when the shape is not supported.</returns>
    /// <remarks>
    /// <para>
    /// Two shapes are understood, and they are the two the corpus has. A
    /// <c>const GDate*</c> the callee only reads travels <c>in</c> as a
    /// <c>System.DateOnly</c>, built into a temporary that the scope around the
    /// call frees. A <c>GDate**</c> the callee allocates travels <c>out</c> as a
    /// <c>System.DateOnly?</c>: the pointer the call wrote is read out and freed
    /// again, and the parameter is nullable because a call may answer
    /// <c>true</c> and leave no date — <c>gst_structure_get_date</c> and
    /// <c>ges_meta_container_get_date</c> both hand out whatever a generic value
    /// holds, and that may be <c>NULL</c>.
    /// </para>
    /// <para>
    /// Every other shape is refused so that it is reported rather than emitted
    /// wrong. A returned <c>GDate</c>, a <c>ref</c> one, a nullable <c>in</c>
    /// one — <c>System.DateOnly</c> is a value type with no null to pass — a
    /// consumed <c>in</c> one, a borrowed <c>out</c> one and a caller allocated
    /// one all come back as <see langword="null"/>. None of them exists in the
    /// corpus today.
    /// </para>
    /// </remarks>
    private static ArgumentPlan? PlanDate(
        GirTypeRef type,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        bool isReturn,
        bool callerAllocates)
    {
        if (isReturn || !type.IsPointer)
        {
            return null;
        }

        if (direction == ArgumentDirection.In)
        {
            if (transfer != GirTransfer.None || nullable)
            {
                return null;
            }

            return new ArgumentPlan
            {
                Kind = ArgumentKind.Date,
                Name = name,
                PublicType = "System.DateOnly",
                RawType = NativeInt,
                Direction = ArgumentDirection.In,
                Transfer = transfer,
            };
        }

        if (direction != ArgumentDirection.Out || callerAllocates || transfer != GirTransfer.Full)
        {
            return null;
        }

        return new ArgumentPlan
        {
            Kind = ArgumentKind.Date,
            Name = name,
            PublicType = "System.DateOnly?",
            RawType = NativeInt + "*",
            Direction = ArgumentDirection.Out,
            Transfer = transfer,
            IsNullable = true,
        };
    }

    /// <summary>
    /// Names the wrapper a pointer is projected onto and the flavour it is
    /// wrapped with, for the positions that need nothing but the type: a
    /// returned handle the caller does not take over, and the field of a record
    /// that holds one.
    /// </summary>
    /// <param name="mapped">The mapping of what the pointer points at.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="publicType">The C# type of the wrapper.</param>
    /// <param name="flavor">How a handle of it is wrapped.</param>
    /// <returns><see langword="true"/> when the pointee has a wrapper.</returns>
    /// <remarks>
    /// This is the head of <see cref="PlanHandle"/>, which the record emitter
    /// reaches without a planner: a field that holds a handle is projected the
    /// way a <c>transfer none</c> return of the same type is, and there is one
    /// place that decides what that type is. What is left out here is
    /// everything about a position that a field has none of - direction, caller
    /// allocated storage, and the transfer, which is always <c>none</c> for a
    /// field the structure keeps.
    /// </remarks>
    internal static bool TryProjectHandle(
        MappedType mapped,
        Overlays overlays,
        Classifier classifier,
        [NotNullWhen(true)] out string? publicType,
        out HandleFlavor flavor)
    {
        publicType = null;
        flavor = HandleFlavor.None;
        if (mapped.Symbol is not { } symbol || UnusableTypes.Contains(mapped.PublicType))
        {
            return false;
        }

        if (RuntimeTypes.TryGetValue(symbol.QualifiedName, out RuntimeHandle? runtimeType))
        {
            // A borrowed only wrapper carries its Handle and nothing else, so
            // there is no factory to adopt what the field holds with.
            if (runtimeType.BorrowedOnly)
            {
                return false;
            }

            publicType = runtimeType.PublicType;
            flavor = runtimeType.Flavor;
            return true;
        }

        if (mapped.Kind is not (MarshalKind.GObject or MarshalKind.Interface or MarshalKind.MiniObject
                or MarshalKind.Boxed or MarshalKind.OpaqueRecord)
            || !IsEmitted(symbol, overlays, classifier))
        {
            return false;
        }

        publicType = mapped.PublicType;
        flavor = mapped.Kind switch
        {
            MarshalKind.GObject or MarshalKind.Interface => HandleFlavor.GObject,
            MarshalKind.OpaqueRecord => HandleFlavor.Opaque,
            _ => HandleFlavor.Wrapper,
        };

        return true;
    }

    private ArgumentPlan? PlanHandle(
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        bool isReturn,
        bool callerAllocates,
        bool booleanCallee = false,
        bool redirectedDestination = false,
        bool inbound = false)
    {
        if (mapped.Symbol is not { } symbol || UnusableTypes.Contains(mapped.PublicType))
        {
            return null;
        }

        HandleFlavor flavor;
        string publicType;
        if (RuntimeTypes.TryGetValue(symbol.QualifiedName, out RuntimeHandle? runtimeType))
        {
            flavor = runtimeType.Flavor;
            publicType = runtimeType.PublicType;

            // A borrowed only wrapper has the Handle a call site reads and
            // none of the members the other positions name: a handle the
            // binding is handed - returned, written to an out parameter, or
            // received by a trampoline - is adopted through the typed
            // FromNative of the wrapper, and a transferred in parameter is
            // minted with BoxedCopy off its BoxedType. All of them would be
            // emitted as text naming a member that does not exist, which is a
            // compile error in the shipped tree rather than a skip, so every
            // position but the borrowed in argument is refused here.
            if (runtimeType.BorrowedOnly)
            {
                // An inbound position is refused through null rather than
                // through Reject, which is the route PlanSignalArgument and
                // PlanCallbackCore already take for everything they cannot
                // project: the signal is then skipped by TryPlanSignal under
                // its own reason and the callback takes its consumer with it,
                // while a Reject would file the rejection against whichever
                // callable happens to be in flight.
                if (inbound)
                {
                    return null;
                }

                if (isReturn
                    || direction != ArgumentDirection.In
                    || transfer is GirTransfer.Full or GirTransfer.Floating)
                {
                    return Reject(SkipReason.UnsupportedSignature);
                }
            }
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
            // the local it is handed. A boxed record whose own library can
            // allocate one is the case that has an answer: the binding calls
            // that constructor, which sizes and zeroes the storage and pairs it
            // with the registered boxed free the wrapper disposes through.
            // Everything else stays rejected.
            if (callerAllocates)
            {
                if (StorageFactoryOf(symbol) is not { } factory)
                {
                    return Reject(SkipReason.CallerAllocates);
                }

                return new ArgumentPlan
                {
                    Kind = ArgumentKind.CallerAllocatedBoxed,
                    Name = name,
                    PublicType = booleanCallee ? publicType + "?" : publicType,
                    RawType = NativeInt,
                    Direction = direction,
                    Transfer = transfer,
                    Flavor = flavor,
                    StorageFactory = factory,

                    // The gir marks most of these optional, which is the C
                    // caller's freedom to pass NULL. The binding has no such
                    // shape: it always provides the storage and always hands
                    // the value back.
                    IsNullable = booleanCallee,
                };
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

        // A callee that takes ownership of an in parameter is not handed the
        // wrapper's own reference — both of them would release it — but a value
        // minted for the call: a reference for a mini object or a GObject, a
        // copy for a boxed value. The wrapper is disposed when the member
        // returns, which is the consuming contract of the hand written members
        // in docs/ownership.md. An opaque record owns nothing to hand over, so
        // it stays rejected, and so does transfer="container", whose split
        // ownership no minting rule covers. A floating reference is passed as
        // it is, because every wrapper sinks it when it is created; a returned
        // handle is the other way round and the wrapper adopts it.
        if (!isReturn && transfer == GirTransfer.Full)
        {
            ConsumedFamily family = flavor switch
            {
                HandleFlavor.GObject => ConsumedFamily.GObject,
                HandleFlavor.Wrapper when mapped.Kind == MarshalKind.MiniObject => ConsumedFamily.MiniObject,
                HandleFlavor.Wrapper when mapped.Kind == MarshalKind.Boxed => ConsumedFamily.Boxed,
                _ => ConsumedFamily.None,
            };

            if (family == ConsumedFamily.None)
            {
                return null;
            }

            return new ArgumentPlan
            {
                Kind = ArgumentKind.ConsumedHandle,
                Name = name,
                PublicType = nullable ? publicType + "?" : publicType,
                RawType = NativeInt,
                Direction = direction,
                Transfer = transfer,
                Flavor = flavor,
                ConsumedFamily = family,
                IsNullable = nullable,
            };
        }

        if (!isReturn && transfer == GirTransfer.Container)
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
            IsRedirectedDestination = redirectedDestination,
        };
    }

    /// <summary>
    /// Returns the constructor the storage of a caller allocated out parameter
    /// is taken from, or <see langword="null"/> when the record has none.
    /// </summary>
    /// <param name="symbol">The record the callee fills.</param>
    /// <returns>The factory, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Only a boxed record qualifies, and only through a <c>&lt;constructor&gt;</c>
    /// that takes nothing, is named <c>_new</c> and hands the value over. That
    /// is the one allocation whose size the library itself decides and whose
    /// free is the registered boxed free the wrapper already disposes through.
    /// An opaque record has no boxed free to pair with, and a plain structure
    /// is spelled with the size of the C type in C# and needs none of this.
    /// </remarks>
    private BoxedStorageFactory? StorageFactoryOf(GirSymbol symbol)
    {
        if (_classifier.Classify(symbol.Declaration) != TypeKind.Boxed
            || symbol.Declaration is not GirTypeDeclaration declaration
            || ModuleMap.Find(symbol.Namespace.Name) is not { } module)
        {
            return null;
        }

        foreach (GirFunction constructor in declaration.Constructors)
        {
            if (constructor.Parameters.Count != 0
                || constructor.Throws
                || constructor.CIdentifier is not { } identifier
                || !identifier.EndsWith("_new", StringComparison.Ordinal)
                || constructor.ReturnValue.Transfer != GirTransfer.Full
                || _overlays.IsSkipped(identifier))
            {
                continue;
            }

            MappedType returned = _types.Map(constructor.ReturnValue.Type, symbol.Namespace);
            if (returned.Symbol?.QualifiedName != symbol.QualifiedName)
            {
                continue;
            }

            return new BoxedStorageFactory(
                identifier,
                NameMapper.EscapeIdentifier(NameMapper.ToPascalCase(identifier)),
                module.NativeLibrary);
        }

        return null;
    }

    /// <summary>
    /// Returns the C# type one element of a block of memory is read as, or
    /// <see langword="null"/> when the element has no projection a span or a
    /// managed array can carry.
    /// </summary>
    /// <param name="element">The mapping of the element.</param>
    /// <returns>The public element type.</returns>
    /// <remarks>
    /// <para>
    /// A blittable value whose raw and public spellings agree is the case that
    /// needs nothing: the memory the call points at already holds the type the
    /// span declares.
    /// </para>
    /// <para>
    /// A generated enumeration is the same memory read under a name. It is
    /// backed by the <c>int</c> or the <c>uint</c> its members fit into, which
    /// is the very reinterpretation a scalar enumeration argument already
    /// performs; anything wider is refused, so that an enumeration of another
    /// width could never be read out of a block sized for this one.
    /// </para>
    /// </remarks>
    private string? ArrayElementType(MappedType element)
    {
        if (element.Kind == MarshalKind.Blittable
            && string.Equals(element.RawType, element.PublicType, StringComparison.Ordinal))
        {
            return element.PublicType;
        }

        if (element.Kind is not (MarshalKind.Enum or MarshalKind.Flags)
            || element.RawType is not ("int" or "uint")
            || element.Symbol is not { } enumeration)
        {
            return null;
        }

        // The hand written enumerations come first, exactly as they do in
        // PlanScalar: their module emits nothing, so IsEmitted rejects them
        // although the runtime declares them.
        if (RuntimeEnums.TryGetValue(enumeration.QualifiedName, out string? runtimeEnum))
        {
            return runtimeEnum;
        }

        return IsEmitted(enumeration) ? element.PublicType : null;
    }

    private ArgumentPlan? PlanArrayArgument(
        GirParameter parameter,
        GirArrayRef array,
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        int index,
        PlanningContext context,
        int offset,
        bool callerAllocates,
        bool lengthIsOverridden)
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
            if (!array.IsZeroTerminated)
            {
                return null;
            }

            // A vector the callee both reads and replaces is neither shape: the
            // encode below owns what it built for the length of the call, and
            // reading a replacement back out of it would read the caller's own
            // allocation as the callee's answer. Nothing of the reference girs
            // reaches this — every `char***` there is spelled
            // zero-terminated="0" — so this is the rule that keeps a future
            // annotation from being planned as an out.
            if (direction == ArgumentDirection.Ref)
            {
                return null;
            }

            if (direction == ArgumentDirection.In)
            {
                // The vector and its strings live in the caller's scope, which
                // releases both when the call returns. A callee that took them
                // over would free memory that scope frees again, the reason the
                // span arm below states for the same shape.
                if (transfer is GirTransfer.Full or GirTransfer.Container)
                {
                    return null;
                }

                return new ArgumentPlan
                {
                    Source = parameter,
                    Kind = ArgumentKind.Strv,
                    Name = name,
                    PublicType = nullable ? "string[]?" : "string[]",
                    RawType = NativeInt + "*",
                    Direction = ArgumentDirection.In,
                    Transfer = transfer,
                    IsNullable = nullable,
                };
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

        if (ArrayElementType(element) is not { } elementType
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
                PublicType = (readOnly ? "System.ReadOnlySpan<" : "System.Span<") + elementType + ">",
                RawType = elementType + "*",
                Direction = ArgumentDirection.In,
                ElementType = elementType,
                LengthArgument = length + offset,
                LengthIsOverridden = lengthIsOverridden,
            };
        }

        return new ArgumentPlan
        {
            Source = parameter,
            Kind = ArgumentKind.ArrayOut,
            Name = name,
            PublicType = elementType + "[]?",
            RawType = NativeInt + "*",
            Direction = ArgumentDirection.Out,
            Transfer = transfer,
            ElementType = elementType,
            LengthArgument = length + offset,
            IsNullable = true,
        };
    }

    /// <summary>Plans a parameter the callee is handed a function through.</summary>
    /// <param name="callable">The callable being planned.</param>
    /// <param name="parameter">The callback parameter.</param>
    /// <param name="name">The C# name of the parameter.</param>
    /// <param name="nullable">Whether the C function accepts no function at all.</param>
    /// <param name="context">The module that is being emitted.</param>
    /// <returns>The plan, or <see langword="null"/> when the callback cannot be handed over.</returns>
    /// <remarks>
    /// <para>
    /// Every accepted scope needs a closure index, because a callback the
    /// binding cannot attach its state to is a function pointer with no
    /// delegate behind it. <c>notified</c> needs a destroy index as well, which
    /// is what releases the state again; <c>async</c> and <c>forever</c> must
    /// have none, because a trampoline that frees its own handle after the one
    /// invocation and a destroy notification that frees the same handle are
    /// mutually exclusive by construction. No site of the corpus carries both.
    /// </para>
    /// <para>
    /// <c>forever</c> keeps the handle for the life of the process: the library
    /// stores the pointer and offers nothing that releases it again, so the
    /// generated member leaks one handle per call and says so in its
    /// documentation.
    /// </para>
    /// </remarks>
    private ArgumentPlan? PlanCallbackArgument(
        GirCallable callable,
        GirParameter parameter,
        string name,
        bool nullable,
        PlanningContext context)
    {
        GirScope scope = ScopeOf(callable, parameter);
        if (scope is not (GirScope.Call or GirScope.Notified or GirScope.Async or GirScope.Forever)
            || parameter.ClosureIndex is null
            || (scope == GirScope.Notified && parameter.DestroyIndex is null)
            || (scope is GirScope.Async or GirScope.Forever && parameter.DestroyIndex is not null))
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

        // The trampoline is shared by every site of the callback type, so the
        // free after invoke epilogue cannot be decided per site. A type that is
        // asked to be self freeing at one site and not at another is refused
        // here, rather than emitted with an epilogue that would double free.
        // The claim of this callable is only queued: it is written to the plan
        // once the whole callable has been planned, so that a site the planner
        // rejects afterwards does not decide the epilogue of a type it never
        // binds. The queue is consulted alongside the plan, because one
        // callable can hand the same callback type over twice. A plan the
        // emitter drops after the planner has built it -- the probe that asks
        // whether a shadowing callable binds, and a name collision -- still
        // counts as a use, which can only make a type stricter than it has to
        // be, never laxer.
        bool selfFreeing = scope == GirScope.Async;
        if (selfFreeing ? IsUsedOutsideAsync(plan) : IsSelfFreeing(plan))
        {
            _diagnostics.Warn(
                "GEN0022",
                $"'{plan.Callback.CType ?? plan.Callback.Name}' is used at both an async and a non-async site; "
                + $"the {(selfFreeing ? "async" : "non-async")} use of "
                + $"'{callable.CIdentifier ?? callable.Name}' is skipped.");
            return null;
        }

        _pendingCallbacks.Add((plan, selfFreeing));
        return new ArgumentPlan
        {
            Source = parameter,
            Kind = ArgumentKind.Callback,
            Name = name,
            PublicType = nullable ? plan.DelegateType + "?" : plan.DelegateType,
            IsNullable = nullable,
            RawType = NativeInt,
            Scope = scope,
            DelegateType = plan.DelegateType,
            TrampolineType = plan.TrampolineType,
            Doc = parameter.Doc,
        };
    }

    /// <summary>
    /// Answers whether a callback type is already known to be self freeing,
    /// counting the claims of the callable that is being planned.
    /// </summary>
    /// <param name="plan">The callback type.</param>
    /// <returns><see langword="true"/> when an async site has claimed it.</returns>
    private bool IsSelfFreeing(CallbackPlan plan) =>
        plan.SelfFreeing || _pendingCallbacks.Any(pending => pending.Plan == plan && pending.SelfFreeing);

    /// <summary>
    /// Answers whether a callback type is already known to be used outside an
    /// async site, counting the claims of the callable that is being planned.
    /// </summary>
    /// <param name="plan">The callback type.</param>
    /// <returns><see langword="true"/> when a non-async site has claimed it.</returns>
    private bool IsUsedOutsideAsync(CallbackPlan plan) =>
        plan.UsedOutsideAsync || _pendingCallbacks.Any(pending => pending.Plan == plan && !pending.SelfFreeing);

    private ReturnPlan? PlanReturn(GirCallable callable, PlanningContext context, int offset)
    {
        GirReturnValue value = callable.ReturnValue;

        // The array facts of the return value are read once, here, exactly as
        // they are for a parameter.
        GirTypeRef effective = EffectiveArrayOf(callable) ?? value.Type;
        MappedType mapped = _types.Map(effective, context.Namespace);
        GirTransfer transfer = TransferOf(callable);
        bool nullable = NullableOf(callable);
        bool discarded = DiscardsReturn(callable);

        // Dropping a return the caller is handed the ownership of would leak it
        // on every call, so the correction only holds for one the caller
        // already has. The override is reported and ignored rather than obeyed.
        if (discarded && mapped.Kind != MarshalKind.Void && transfer == GirTransfer.Full)
        {
            _diagnostics.Warn(
                "GEN0019",
                $"'{callable.CIdentifier ?? callable.Name}' is marked 'discardReturn' but hands the ownership "
                + "of its return value to the caller; the override is ignored, because dropping the value "
                + "would leak it.");
            discarded = false;
        }

        if (mapped.Kind == MarshalKind.Void || discarded)
        {
            if (discarded && mapped.Kind == MarshalKind.Void)
            {
                _diagnostics.Warn(
                    "GEN0018",
                    $"'{callable.CIdentifier ?? callable.Name}' is marked 'discardReturn' and returns nothing; "
                    + "the override has no effect and is stale.");
            }

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

        if (effective is GirArrayRef array)
        {
            if (mapped.ElementType is not { } element)
            {
                return null;
            }

            // A returned block of a size the C declaration fixes carries no
            // count of its own, so the length of the managed array is that
            // size rather than the value of an argument.
            if (array.FixedSize is int returnedSize)
            {
                if (array.LengthParameterIndex is not null
                    || returnedSize <= 0
                    || ArrayElementType(element) is not { } sizedElement)
                {
                    return null;
                }

                return new ReturnPlan
                {
                    Kind = ArgumentKind.ArrayOut,
                    PublicType = sizedElement + "[]?",
                    RawType = NativeInt,
                    Transfer = transfer,
                    ElementType = sizedElement,
                    FixedLength = returnedSize,
                    IsNullable = true,
                    Doc = value.Doc,
                };
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

            if (ArrayElementType(element) is not { } elementType
                || array.LengthParameterIndex is not int length)
            {
                return null;
            }

            return new ReturnPlan
            {
                Kind = ArgumentKind.ArrayOut,
                PublicType = elementType + "[]?",
                RawType = NativeInt,
                Transfer = transfer,
                ElementType = elementType,
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
            effective,
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
        if (scalar.Kind == ArgumentKind.PlainStruct && effective.IsPointer)
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
    /// Only the return position is planned here. A <c>GList</c> parameter is
    /// built in managed code and handed over by
    /// <see cref="PlanListArgument"/>, which is the mirror of this method and
    /// carries the ownership rules of that direction. A <c>GSList</c> return is
    /// skipped: the only one in a bound module carries an element this
    /// projection would refuse anyway.
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
    /// Plans a <c>GList</c> or a <c>GSList</c> that a call is given, the mirror
    /// of <see cref="PlanListReturn"/>.
    /// </summary>
    /// <param name="type">The gir type reference, whose C type tells a list from its address.</param>
    /// <param name="mapped">Its mapping, whose element type carries the payload.</param>
    /// <param name="name">The C# name of the argument.</param>
    /// <param name="direction">How the argument is passed.</param>
    /// <param name="transfer">What the call takes over along with the list.</param>
    /// <param name="nullable">Whether the list may be null.</param>
    /// <param name="isReturn">Whether the value is the return value of the callable.</param>
    /// <param name="callerAllocates">Whether the caller provides the storage of an out parameter.</param>
    /// <returns>The plan, or <see langword="null"/> when the shape is not supported.</returns>
    /// <remarks>
    /// <para>
    /// There are exactly two shapes, and <paramref name="transfer"/> decides
    /// which one. A <em>borrowed</em> list (<c>none</c>) is built into a scope
    /// that owns the spine, and the UTF-8 copies of a list of strings, and
    /// releases both when the call returns; the callee reads the list while the
    /// call runs and copies whatever it keeps. A <em>consumed</em> list
    /// (<c>full</c>) is built with one value minted per element and handed
    /// over; the callee owns the spine and the minted values from the moment
    /// the call is made, including when it answers false, and nothing is
    /// released afterwards. <c>container</c>, the hybrid that would consume the
    /// spine and borrow the elements, has no introspectable case in the sixteen
    /// modules and is refused; a synthetic fixture is what keeps that refusal
    /// honest.
    /// </para>
    /// <para>
    /// Only the <c>in</c> direction is planned. An out or inout list comes back
    /// through the address of the caller's own variable, which is a different
    /// marshaller altogether, and a <c>GSList</c> return has to be refused here
    /// rather than fall through: <see cref="PlanReturn"/> intercepts a
    /// <c>GList</c> before this method is reached but leaves a <c>GSList</c> to
    /// the scalar switch. A <c>c:type</c> that ends in two stars is refused for
    /// the same family of reasons — <c>gst_iterator_new_list</c> keeps the
    /// address it is given and re-reads it on every resync, so the caller's
    /// list variable has to stay valid and mutable for the life of the
    /// iterator, which is not a value the callee reads once.
    /// </para>
    /// <para>
    /// The element decides the rest. A string is copied, a <c>GObject</c> is
    /// listed by its handle and only where the list is borrowed — a call that
    /// takes a list of GObjects over releases the references the binding would
    /// have to mint for it, which is the <c>*_list_free</c> family and is a
    /// no-op with a double release hazard — and a mini object is listed only
    /// where the list is consumed, because a reference minted per element is
    /// the only thing this plans for one. Everything else is refused, a boxed
    /// value and an opaque record included: neither shape has a per element
    /// mint story for them.
    /// </para>
    /// <para>
    /// The public type is spelled here rather than taken from
    /// <paramref name="mapped"/>. The type map projects a GLib container onto
    /// <c>IReadOnlyList&lt;T&gt;</c>, which is the shape of a list that comes
    /// back; a list that goes in is an <c>IEnumerable&lt;T&gt;</c>, so that a
    /// caller may hand over whatever sequence it already has.
    /// </para>
    /// </remarks>
    private ArgumentPlan? PlanListArgument(
        GirTypeRef type,
        MappedType mapped,
        string name,
        ArgumentDirection direction,
        GirTransfer transfer,
        bool nullable,
        bool isReturn,
        bool callerAllocates)
    {
        if (direction != ArgumentDirection.In || isReturn || callerAllocates)
        {
            return null;
        }

        if (type.CType?.EndsWith("**", StringComparison.Ordinal) ?? false)
        {
            return null;
        }

        if (transfer == GirTransfer.Container)
        {
            return null;
        }

        if (mapped.ElementType is not { } element)
        {
            return null;
        }

        ArgumentKind elementKind;
        HandleFlavor flavor = HandleFlavor.None;

        switch (element.Kind)
        {
            case MarshalKind.Utf8String when transfer is GirTransfer.None or GirTransfer.Full:
                elementKind = ArgumentKind.Utf8;
                break;

            case MarshalKind.GObject when transfer == GirTransfer.None:
            case MarshalKind.MiniObject when transfer == GirTransfer.Full:
                if (element.Symbol is not { } symbol
                    || !IsEmitted(symbol)
                    || UnusableTypes.Contains(element.PublicType))
                {
                    return null;
                }

                elementKind = ArgumentKind.Handle;
                flavor = element.Kind == MarshalKind.GObject ? HandleFlavor.GObject : HandleFlavor.Wrapper;
                break;

            default:
                return null;
        }

        string publicType = "System.Collections.Generic.IEnumerable<" + element.PublicType + ">";
        return new ArgumentPlan
        {
            Kind = ArgumentKind.ListIn,
            Name = name,
            PublicType = nullable ? publicType + "?" : publicType,
            RawType = NativeInt,
            Direction = direction,
            Transfer = transfer,
            IsNullable = nullable,
            Flavor = flavor,
            ElementType = element.PublicType,
            ElementKind = elementKind,
            IsSinglyLinked = mapped.Kind == MarshalKind.GSList,
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

    /// <summary>
    /// Re-projects a <c>GValue</c> argument of a callback onto the view that
    /// matches how the caller of the callback lets it be used.
    /// </summary>
    /// <param name="argument">The argument as the shared scalar projection planned it.</param>
    /// <returns>The projected argument, or <see langword="null"/> when the shape is not supported.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="PlanGValue"/> has already read the const-ness of the C type
    /// and answered with the direction the method surface would use: <c>in</c>
    /// for a <c>const GValue*</c>, which is only read, and <c>ref</c> for a
    /// writable <c>GValue*</c>, which the callee is invited to change in place.
    /// A callback receives its argument instead of passing one, so the direction
    /// becomes the choice of view — <c>Gst.GObject.ValueView</c> or
    /// <c>Gst.GObject.ValueRef</c> — and the argument is passed by value, the
    /// view being a pointer already.
    /// </para>
    /// <para>
    /// Nothing else is admitted. An <c>out</c> <c>GValue</c> would be storage
    /// the trampoline has to provide and a lifetime nothing states, and the
    /// transfers and the nullable in shapes are already refused where every
    /// other <c>GValue</c> refuses them.
    /// </para>
    /// </remarks>
    private static ArgumentPlan? BorrowedGValue(ArgumentPlan argument)
    {
        string? publicType = argument.Direction switch
        {
            ArgumentDirection.In => "Gst.GObject.ValueView",
            ArgumentDirection.Ref => "Gst.GObject.ValueRef",
            _ => null,
        };

        if (publicType is null)
        {
            return null;
        }

        return new ArgumentPlan
        {
            Source = argument.Source,
            Kind = ArgumentKind.BorrowedGValue,
            Name = argument.Name,
            PublicType = publicType,
            RawType = argument.RawType,
            Direction = ArgumentDirection.In,
            Doc = argument.Doc,
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

            // The same refusal, in front of the projection a callback argument
            // goes through. Nothing is recorded through Reject either: a
            // callable whose plan comes back null is ledgered as
            // UnsupportedSignature already, and TryPlanCallback caches the
            // refusal, so a field written here would only reach the first site
            // that hands the type over.
            if (IsPointerToHandlePointer(parameter.Type, mapped))
            {
                return null;
            }

            ArgumentPlan? argument = PlanScalar(
                parameter.Type,
                mapped,
                NameMapper.ParameterName(parameter.Name),
                ArgumentDirection.In,
                parameter.Transfer,
                NullableOf(callback, parameter),
                context,
                inbound: true);

            // A GValue a callback is handed points into storage that the
            // caller of the callback owns and keeps, which the owning
            // Gst.GObject.Value cannot wrap, so it is re-projected onto a view
            // whose lifetime the compiler bounds by the invocation. Everything
            // the projection does not cover - an out parameter, a transfer, a
            // nullable value - comes back as null from PlanGValue or from the
            // projection itself and takes the callback with it.
            if (argument is { Kind: ArgumentKind.GValue })
            {
                argument = BorrowedGValue(argument);
            }

            // A list argument is built and handed over on the method surface,
            // while a trampoline is handed one and would have to project it
            // into managed code, which is the return side shape and not this
            // one.
            //
            // A GError is excluded for a related reason: a trampoline that
            // receives a borrowed error needs a delegate contract - how long
            // the value it hands the delegate stays valid, and what a delegate
            // may do with it - that nothing states. Its only user in the
            // corpus is GstVideoConvertSampleCallback, whose entry point
            // gst_video_convert_sample_async is unsupported for reasons of its
            // own, so the exclusion costs nothing and keeps the kind from
            // widening the callback surface behind the scope it was added for.
            //
            // A GDate is excluded because the projection only exists in the two
            // directions a method uses: a trampoline would have to convert a
            // borrowed date into a managed value, which nothing writes and
            // nothing needs — no callback of the corpus takes one.
            if (argument is null
                || argument.Kind is ArgumentKind.Utf8Owned or ArgumentKind.Callback
                    or ArgumentKind.ListIn or ArgumentKind.GError or ArgumentKind.Date)
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
            // of it would free what the caller still uses. The consuming kind
            // is a contract for arguments this code passes in, not for ones a
            // trampoline receives, so it is rejected here as well.
            if (argument.Kind == ArgumentKind.ConsumedHandle
                || (argument.Kind == ArgumentKind.Handle && argument.Transfer == GirTransfer.Full))
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
            // The same refusal, in front of the value a trampoline hands back.
            // A callback return reaches PlanScalar without `isReturn` - the
            // flag steers the GValue, the GDate and the handle plans, and
            // setting it here would move projections this rule is not about -
            // so the test is made here instead.
            if (IsPointerToScalar(callback.ReturnValue.Type, returnMapped))
            {
                return null;
            }

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
    /// <param name="signalKey">
    /// The GObject spelling of the signal, <c>GES.Project::error-loading-asset</c>,
    /// which an overlay keys its corrections by.
    /// </param>
    /// <returns>The plan, or <see langword="null"/> when the argument is not supported.</returns>
    /// <remarks>
    /// Only <c>nullable</c> is honoured on a signal key. Every other field of
    /// an annotation override describes something a signal argument does not
    /// have - a direction, an array, a callback scope, a discardable return -
    /// so a key that carries one of those is read for its nullable flag and
    /// otherwise ignored. A key that matches nothing is reported as GEN0024,
    /// as it is for a callable.
    /// </remarks>
    private ArgumentPlan? PlanSignalArgument(GirParameter parameter, PlanningContext context, string signalKey)
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
        bool nullable = AnnotationOverrideFor(signalKey + "#" + parameter.Name)?.Nullable
            ?? parameter.IsNullable;

        // The refusal that PlanParameter applies to the in parameter of a
        // callable, hoisted in front of the projection a signal argument goes
        // through: a handler receives the pointer the emitter passes exactly
        // as a method passes one, so two stars are as wrong here as there.
        // Nothing is recorded through Reject, because TryPlanSignal has
        // already set the reason this would record - an argument it cannot
        // plan takes the signal out as UnsupportedSignature.
        if (IsPointerToHandlePointer(parameter.Type, mapped))
        {
            return null;
        }

        ArgumentPlan? argument = PlanScalar(
            parameter.Type,
            mapped,
            name,
            ArgumentDirection.In,
            parameter.Transfer,
            nullable,
            context,
            inbound: true);

        if (argument is null
            || argument.Kind is not (ArgumentKind.Value or ArgumentKind.Boolean or ArgumentKind.Enumeration
                or ArgumentKind.Wrapper or ArgumentKind.Pointer or ArgumentKind.Utf8 or ArgumentKind.Handle
                or ArgumentKind.GError))
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
    /// Plans the value a signal handler returns: a value that is blittable on
    /// its own, or a GObject the handler transfers ownership of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A handle return is supported when the handler transfers ownership of a
    /// GObject, because that is the shape whose emitter takes the reference:
    /// the generic marshal hands what the handler returned to
    /// <c>g_value_take_object</c>, so the trampoline mints a reference for the
    /// value it passes out and the wrapper the handler holds stays the
    /// handler's own. A transfer-none handle is excluded for the same reason
    /// read the other way round — the marshal would take a reference nobody
    /// minted — and a mini object or a boxed value is excluded because it needs
    /// a different minting function than <c>g_object_ref</c>.
    /// </para>
    /// <para>
    /// What the accumulator of a signal may do is therefore bounded rather than
    /// unknown: it sees an owned reference and either keeps it or releases it.
    /// Every other non blittable shape stays rejected, notably a container the
    /// handler would have to allocate — the accumulator of such a signal
    /// decides how the container is freed, and no annotation states it.
    /// </para>
    /// </remarks>
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
            context,
            isReturn: true);

        if (scalar is null)
        {
            return null;
        }

        bool supported = scalar.Kind switch
        {
            ArgumentKind.Value or ArgumentKind.Boolean or ArgumentKind.Enumeration
                or ArgumentKind.Wrapper or ArgumentKind.Pointer => true,
            ArgumentKind.Handle => value.Transfer == GirTransfer.Full
                && scalar.Flavor == HandleFlavor.GObject,

            // A string the handler transfers is copied into memory the
            // emitting library owns and frees - the accumulator of
            // GESProject::missing-uri g_frees it - so the trampoline hands out
            // a g_malloc'd copy. A borrowed string return stays rejected:
            // nobody would own it, and the emission has no way to say who
            // does.
            ArgumentKind.Utf8Owned => value.Transfer == GirTransfer.Full,
            _ => false,
        };

        if (!supported)
        {
            return null;
        }

        return new ReturnPlan
        {
            Kind = scalar.Kind,
            PublicType = scalar.PublicType,
            RawType = scalar.RawType,
            Transfer = scalar.Transfer,
            IsNullable = scalar.IsNullable,
            Flavor = scalar.Flavor,
            Doc = value.Doc,
        };
    }
}
