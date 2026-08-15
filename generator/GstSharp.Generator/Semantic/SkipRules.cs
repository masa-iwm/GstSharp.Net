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
    /// <returns>The reason, or <see cref="SkipReason.None"/>.</returns>
    internal SkipReason GetSkipReason(GirCallable callable)
    {
        if (_overlays.IsSkipped(callable.CIdentifier))
        {
            return SkipReason.OverlaySkip;
        }

        if (callable is GirCallback { IsFieldSlot: true })
        {
            return SkipReason.FieldSlotCallback;
        }

        if (callable.ShadowedBy is not null)
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
