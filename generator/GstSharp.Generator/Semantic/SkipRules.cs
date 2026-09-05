using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// Why a callable is not generated.
/// </summary>
internal enum SkipReason
{
    /// <summary>The callable is generated.</summary>
    None,

    /// <summary>Listed in the <c>skip</c> array of <c>fixups.json</c>.</summary>
    OverlaySkip,

    /// <summary>
    /// Listed in the <c>handBound</c> array of <c>fixups.json</c>: the managed
    /// surface of the symbol exists, hand written, in a <c>Custom/</c> folder
    /// or in the runtime. The symbol still is not generated - something else,
    /// usually the <c>skip</c> array or the shape of its signature, is what
    /// keeps it out - and this reason states that its absence from the
    /// bindings is not a gap. It takes precedence over every other reason in
    /// the report, so that the remaining sections measure what is really
    /// unbound - except over <see cref="MovedTo"/> and
    /// <see cref="ShadowedBy"/>, which do not say the callable is absent but
    /// that it is emitted under another declaration of the same gir.
    /// </summary>
    HandBound,

    /// <summary>Superseded by a <c>shadows</c> variant that is generated instead.</summary>
    ShadowedBy,

    /// <summary>Declared with <c>moved-to</c>; the target is generated instead.</summary>
    MovedTo,

    /// <summary>Takes a C variable argument list.</summary>
    VarArgs,

    /// <summary>Declared <c>introspectable="0"</c>.</summary>
    NotIntrospectable,

    /// <summary>A vtable slot, that is a callback declared as a field.</summary>
    FieldSlotCallback,

    /// <summary>Has no <c>c:identifier</c>, so it cannot be bound.</summary>
    NoCIdentifier,

    /// <summary>
    /// Uses a type or an annotation that the marshalling of this milestone does
    /// not cover, for example a <c>GList</c>, a <c>GValue</c> or a callback
    /// without user data.
    /// </summary>
    UnsupportedSignature,

    /// <summary>
    /// The C# name of the member is already taken by another member of the same
    /// type, or by a member of its base class that cannot be overridden.
    /// </summary>
    NameCollision,

    /// <summary>
    /// Has a <c>caller-allocates</c> out parameter whose storage the generator
    /// cannot provide. The callee writes into memory the caller supplies, and
    /// only a plain struct is spelled in C# with the size of the C type: a
    /// record that is bound behind a handle is one pointer wide, so the call
    /// would write past the end of the local it is given.
    /// </summary>
    CallerAllocates,

    /// <summary>
    /// Releases or references the instance: the wrapper already owns exactly
    /// one reference and releases it when it is disposed, so a second release
    /// path can only corrupt the reference count.
    /// </summary>
    LifetimePrimitive,

    /// <summary>
    /// Consumes the instance and hands a replacement back, as
    /// <c>gst_caps_make_writable</c> does. Binding it needs reference counting
    /// semantics that the generator does not have yet.
    /// </summary>
    InstanceTransferFull,

    /// <summary>
    /// An action signal. It is a call API that GObject exposes through the
    /// signal machinery, not a notification, and the methods it stands for are
    /// already bound.
    /// </summary>
    ActionSignal,

    /// <summary>
    /// A property whose value is a mini object or a boxed record. Reading one
    /// builds a wrapper that owns a reference and has to be disposed, and a
    /// property is read as a value rather than acquired as a resource, so
    /// every evaluation of it leaks. The getter stays as a method, where the
    /// name says that something is produced.
    /// </summary>
    OwningProperty,

    /// <summary>
    /// A class struct field that holds a function pointer the managed surface
    /// does not project, so the mirror lays it out as an opaque
    /// <see langword="nint"/> and nothing overrides it. A field carrying a
    /// named callback typedef with no <c>&lt;virtual-method&gt;</c> beside it
    /// falls to this, so does an inline <c>&lt;callback&gt;</c> field with no
    /// virtual method of the same name - the abstract sentinel
    /// <c>GESVideoSourceClass::create_source</c> is one - and so does every
    /// callback field inside a class struct <c>&lt;union&gt;</c>, because a
    /// union member is laid out to keep the size right and is never given a
    /// slot. The row states that the C slot exists and carries no <c>OnX</c>
    /// member, which is what the ledger says about a slot the planner refused.
    /// </summary>
    OpaqueSlot,
}

/// <summary>
/// Decides which callables reach the emitters.
/// </summary>
/// <remarks>
/// <c>&lt;function-macro&gt;</c> elements never reach this class: the gir reader
/// does not parse them, because a C macro has no callable ABI.
/// </remarks>
internal sealed class SkipRules
{
    private readonly Overlays _overlays;

    /// <summary>Initializes a new instance of the <see cref="SkipRules"/> class.</summary>
    /// <param name="overlays">The overlay configuration holding the skip list.</param>
    internal SkipRules(Overlays overlays) => _overlays = overlays;

    /// <summary>
    /// Returns the gir name a callable is emitted under. A callable that
    /// <c>shadows</c> another one takes the shadowed, cleaner name: the gir
    /// pair <c>gst_bus_add_watch</c> (varargs free but not introspectable) and
    /// <c>gst_bus_add_watch_full</c> collapses onto <c>add_watch</c>.
    /// </summary>
    /// <param name="callable">The callable to name.</param>
    /// <returns>The gir name to derive the C# name from.</returns>
    internal static string EffectiveGirName(GirCallable callable) => callable.Shadows ?? callable.Name;

    /// <summary>Computes why a callable is skipped.</summary>
    /// <param name="callable">The callable to inspect.</param>
    /// <param name="ignoreShadowedBy">
    /// <see langword="true"/> to keep going when the callable is shadowed by
    /// another one. The caller passes this once it has established that the
    /// shadowing callable cannot be bound: the clean name is free again, and
    /// the shadowed declaration is the only chance of binding the function at
    /// all. Every other rule still applies, so a shadowed callable that is
    /// <c>introspectable="0"</c> stays out.
    /// </param>
    /// <returns>The reason, or <see cref="SkipReason.None"/>.</returns>
    internal SkipReason GetSkipReason(GirCallable callable, bool ignoreShadowedBy = false)
    {
        if (_overlays.IsSkipped(callable.CIdentifier))
        {
            return SkipReason.OverlaySkip;
        }

        if (callable is GirCallback { IsFieldSlot: true })
        {
            return SkipReason.FieldSlotCallback;
        }

        if (callable.ShadowedBy is not null && !ignoreShadowedBy)
        {
            return SkipReason.ShadowedBy;
        }

        if (callable.MovedTo is not null)
        {
            return SkipReason.MovedTo;
        }

        if (callable.HasVarArgs)
        {
            return SkipReason.VarArgs;
        }

        if (!callable.IsIntrospectable)
        {
            return SkipReason.NotIntrospectable;
        }

        // Signals, virtual methods and callbacks are bound without a C entry
        // point; everything else needs one.
        if (callable.CIdentifier is null
            && callable.Kind is GirCallableKind.Function or GirCallableKind.Method or GirCallableKind.Constructor)
        {
            return SkipReason.NoCIdentifier;
        }

        return SkipReason.None;
    }

    /// <summary>Tests whether a callable is skipped.</summary>
    /// <param name="callable">The callable to inspect.</param>
    /// <returns><see langword="true"/> when nothing is emitted for it.</returns>
    internal bool ShouldSkip(GirCallable callable) => GetSkipReason(callable) != SkipReason.None;
}
