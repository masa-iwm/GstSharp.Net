namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// The gir element a callable was parsed from.
/// </summary>
internal enum GirCallableKind
{
    /// <summary><c>&lt;function&gt;</c>.</summary>
    Function,

    /// <summary><c>&lt;method&gt;</c>.</summary>
    Method,

    /// <summary><c>&lt;constructor&gt;</c>.</summary>
    Constructor,

    /// <summary><c>&lt;virtual-method&gt;</c>.</summary>
    VirtualMethod,

    /// <summary><c>&lt;callback&gt;</c>.</summary>
    Callback,

    /// <summary><c>&lt;glib:signal&gt;</c>.</summary>
    Signal,
}

/// <summary>
/// A <c>&lt;parameter&gt;</c>.
/// </summary>
internal sealed class GirParameter : GirNode
{
    /// <summary>Gets the parameter direction.</summary>
    internal GirDirection Direction { get; init; }

    /// <summary>Gets the ownership transfer mode.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>
    /// Gets a value indicating whether the parameter accepts <c>NULL</c>. Falls
    /// back to the legacy <c>allow-none</c> attribute.
    /// </summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets a value indicating whether the raw <c>allow-none</c> attribute was set.</summary>
    internal bool IsAllowNone { get; init; }

    /// <summary>Gets a value indicating whether an out parameter may be omitted.</summary>
    internal bool IsOptional { get; init; }

    /// <summary>Gets a value indicating whether the caller allocates the out storage.</summary>
    internal bool IsCallerAllocates { get; init; }

    /// <summary>Gets a value indicating whether the parameter is annotated <c>skip="1"</c>.</summary>
    internal bool IsSkip { get; init; }

    /// <summary>Gets the index of the user data parameter of a callback, if any.</summary>
    internal int? ClosureIndex { get; init; }

    /// <summary>Gets the index of the destroy notify parameter of a callback, if any.</summary>
    internal int? DestroyIndex { get; init; }

    /// <summary>Gets the callback lifetime.</summary>
    internal GirScope Scope { get; init; }

    /// <summary>Gets a value indicating whether this is the <c>&lt;varargs/&gt;</c> parameter.</summary>
    internal bool IsVarArgs { get; init; }

    /// <summary>Gets the parameter type.</summary>
    internal GirTypeRef Type { get; init; } = new GirTypeRef();
}

/// <summary>
/// An <c>&lt;instance-parameter&gt;</c>.
/// </summary>
internal sealed class GirInstanceParameter : GirNode
{
    /// <summary>Gets the ownership transfer mode.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets a value indicating whether the instance may be <c>NULL</c>.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets the instance type.</summary>
    internal GirTypeRef Type { get; init; } = new GirTypeRef();
}

/// <summary>
/// A <c>&lt;return-value&gt;</c>.
/// </summary>
internal sealed class GirReturnValue : GirNode
{
    /// <summary>Gets the ownership transfer mode.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets a value indicating whether the return value may be <c>NULL</c>.</summary>
    internal bool IsNullable { get; init; }

    /// <summary>Gets a value indicating whether the return value is annotated <c>skip="1"</c>.</summary>
    internal bool IsSkip { get; init; }

    /// <summary>Gets the returned type.</summary>
    internal GirTypeRef Type { get; init; } = new GirTypeRef();
}

/// <summary>
/// Anything with a parameter list and a return value.
/// </summary>
internal abstract class GirCallable : GirNode
{
    /// <summary>Gets the gir element this callable was parsed from.</summary>
    internal abstract GirCallableKind Kind { get; }

    /// <summary>Gets the <c>c:identifier</c> attribute, if any.</summary>
    internal string? CIdentifier { get; init; }

    /// <summary>Gets the <c>moved-to</c> attribute, if any.</summary>
    internal string? MovedTo { get; init; }

    /// <summary>Gets the <c>shadows</c> attribute, if any.</summary>
    internal string? Shadows { get; init; }

    /// <summary>Gets the <c>shadowed-by</c> attribute, if any.</summary>
    internal string? ShadowedBy { get; init; }

    /// <summary>Gets a value indicating whether the callable takes a <c>GError**</c>.</summary>
    internal bool Throws { get; init; }

    /// <summary>Gets the instance parameter, if any.</summary>
    internal GirInstanceParameter? InstanceParameter { get; init; }

    /// <summary>Gets the parameters, excluding the instance parameter.</summary>
    internal IReadOnlyList<GirParameter> Parameters { get; init; } = [];

    /// <summary>Gets the return value.</summary>
    internal GirReturnValue ReturnValue { get; init; } = new GirReturnValue();

    /// <summary>Gets a value indicating whether any parameter is <c>&lt;varargs/&gt;</c>.</summary>
    internal bool HasVarArgs
    {
        get
        {
            for (int i = 0; i < Parameters.Count; i++)
            {
                if (Parameters[i].IsVarArgs)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// A <c>&lt;function&gt;</c>, <c>&lt;method&gt;</c> or <c>&lt;constructor&gt;</c>.
/// </summary>
internal sealed class GirFunction : GirCallable
{
    /// <inheritdoc/>
    internal override GirCallableKind Kind => FunctionKind;

    /// <summary>Gets the concrete element kind.</summary>
    internal GirCallableKind FunctionKind { get; init; } = GirCallableKind.Function;

    /// <summary>Gets the <c>glib:get-property</c> attribute, if any.</summary>
    internal string? GetsProperty { get; init; }

    /// <summary>Gets the <c>glib:set-property</c> attribute, if any.</summary>
    internal string? SetsProperty { get; init; }
}

/// <summary>
/// A <c>&lt;virtual-method&gt;</c>.
/// </summary>
internal sealed class GirVirtualMethod : GirCallable
{
    /// <inheritdoc/>
    internal override GirCallableKind Kind => GirCallableKind.VirtualMethod;

    /// <summary>Gets the name of the method that invokes this vfunc, if any.</summary>
    internal string? Invoker { get; init; }

    /// <summary>
    /// Gets or sets the key the overlays address this slot by, which is the
    /// qualified name of the declaring class and the gir name of the slot, as
    /// in <c>Gst.Element::request_new_pad</c>.
    /// </summary>
    /// <remarks>
    /// A <c>&lt;virtual-method&gt;</c> carries no <c>c:identifier</c>, so it
    /// has nothing an annotation correction could name until the class it was
    /// read from is known. The semantic layer knows that and writes the key
    /// here once, in the same pass that pairs the slot with the field of the
    /// class struct; the planner then reads it the way it reads the identifier
    /// of a function. The spelling is the one a signal argument already uses,
    /// <c>Ns.Type::member</c>, with <c>#parameter</c> or <c>#return</c>
    /// appended for the member of the slot an entry corrects.
    /// </remarks>
    internal string? OverlayKey { get; set; }
}

/// <summary>
/// A <c>&lt;callback&gt;</c>, either at namespace scope or as a record field.
/// </summary>
internal sealed class GirCallback : GirCallable
{
    /// <inheritdoc/>
    internal override GirCallableKind Kind => GirCallableKind.Callback;

    /// <summary>Gets the <c>c:type</c> attribute, if any.</summary>
    internal string? CType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the callback is a vfunc slot, that is a
    /// callback declared inline as a field of a record or class.
    /// </summary>
    internal bool IsFieldSlot { get; init; }
}

/// <summary>
/// A <c>&lt;glib:signal&gt;</c>.
/// </summary>
internal sealed class GirSignal : GirCallable
{
    /// <inheritdoc/>
    internal override GirCallableKind Kind => GirCallableKind.Signal;

    /// <summary>Gets the <c>when</c> attribute (<c>first</c>, <c>last</c>, <c>cleanup</c>).</summary>
    internal string? When { get; init; }

    /// <summary>Gets a value indicating whether the signal is detailed.</summary>
    internal bool IsDetailed { get; init; }

    /// <summary>Gets a value indicating whether the signal is an action signal.</summary>
    internal bool IsAction { get; init; }

    /// <summary>Gets a value indicating whether the signal is annotated <c>no-recurse</c>.</summary>
    internal bool NoRecurse { get; init; }

    /// <summary>Gets a value indicating whether the signal is annotated <c>no-hooks</c>.</summary>
    internal bool NoHooks { get; init; }
}
