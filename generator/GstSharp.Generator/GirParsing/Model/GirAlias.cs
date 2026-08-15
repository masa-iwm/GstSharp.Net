namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// An <c>&lt;alias&gt;</c>.
/// </summary>
internal sealed class GirAlias : GirNode
{
    /// <summary>Gets the <c>c:type</c> attribute.</summary>
    internal string? CType { get; init; }

    /// <summary>Gets the aliased type.</summary>
    internal GirTypeRef Target { get; init; } = new GirTypeRef();
}

/// <summary>
/// A <c>&lt;constant&gt;</c>.
/// </summary>
internal sealed class GirConstant : GirNode
{
    /// <summary>Gets the verbatim <c>value</c> attribute.</summary>
    internal string Value { get; init; } = string.Empty;

    /// <summary>Gets the <c>c:type</c> attribute.</summary>
    internal string? CType { get; init; }

    /// <summary>Gets the constant type.</summary>
    internal GirTypeRef Type { get; init; } = new GirTypeRef();
}
