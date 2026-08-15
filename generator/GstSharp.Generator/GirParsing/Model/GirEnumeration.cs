namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// A <c>&lt;member&gt;</c> of an enumeration or bitfield.
/// </summary>
internal sealed class GirEnumMember : GirNode
{
    /// <summary>Gets the verbatim <c>value</c> attribute.</summary>
    internal string Value { get; init; } = string.Empty;

    /// <summary>Gets the <c>c:identifier</c> attribute.</summary>
    internal string? CIdentifier { get; init; }

    /// <summary>Gets the <c>glib:nick</c> attribute, if any.</summary>
    internal string? Nick { get; init; }

    /// <summary>Gets the <c>glib:name</c> attribute, if any.</summary>
    internal string? GlibName { get; init; }
}

/// <summary>
/// An <c>&lt;enumeration&gt;</c> or a <c>&lt;bitfield&gt;</c>.
/// </summary>
internal sealed class GirEnumeration : GirNode
{
    /// <summary>Gets a value indicating whether this was a <c>&lt;bitfield&gt;</c>.</summary>
    internal bool IsBitfield { get; init; }

    /// <summary>Gets the <c>c:type</c> attribute.</summary>
    internal string? CType { get; init; }

    /// <summary>Gets the <c>glib:type-name</c> attribute, if any.</summary>
    internal string? GlibTypeName { get; init; }

    /// <summary>Gets the <c>glib:get-type</c> attribute, if any.</summary>
    internal string? GlibGetType { get; init; }

    /// <summary>Gets the <c>glib:error-domain</c> attribute, if any.</summary>
    internal string? ErrorDomain { get; init; }

    /// <summary>Gets the members, in gir document order.</summary>
    internal IReadOnlyList<GirEnumMember> Members { get; init; } = [];

    /// <summary>Gets the static functions declared inside the element.</summary>
    internal IReadOnlyList<GirFunction> Functions { get; init; } = [];
}
