namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// A <c>&lt;field&gt;</c> of a record, class or union.
/// </summary>
internal sealed class GirField : GirNode
{
    /// <summary>Gets a value indicating whether the field is writable.</summary>
    internal bool IsWritable { get; init; }

    /// <summary>Gets a value indicating whether the field is readable. Defaults to <see langword="true"/>.</summary>
    internal bool IsReadable { get; init; } = true;

    /// <summary>Gets a value indicating whether the field is private.</summary>
    internal bool IsPrivate { get; init; }

    /// <summary>Gets the bit width of a bitfield member, if any.</summary>
    internal int? Bits { get; init; }

    /// <summary>Gets the field type, unless the field is an inline callback.</summary>
    internal GirTypeRef? Type { get; init; }

    /// <summary>Gets the inline callback declaration, for vfunc slots.</summary>
    internal GirCallback? Callback { get; init; }
}

/// <summary>
/// A <c>&lt;property&gt;</c> of a class or interface.
/// </summary>
internal sealed class GirProperty : GirNode
{
    /// <summary>Gets a value indicating whether the property is writable.</summary>
    internal bool IsWritable { get; init; }

    /// <summary>Gets a value indicating whether the property is readable. Defaults to <see langword="true"/>.</summary>
    internal bool IsReadable { get; init; } = true;

    /// <summary>Gets a value indicating whether the property is construct-only.</summary>
    internal bool IsConstructOnly { get; init; }

    /// <summary>Gets a value indicating whether the property can be set at construction time.</summary>
    internal bool IsConstruct { get; init; }

    /// <summary>Gets the name of the C getter, if any.</summary>
    internal string? Getter { get; init; }

    /// <summary>Gets the name of the C setter, if any.</summary>
    internal string? Setter { get; init; }

    /// <summary>Gets the <c>default-value</c> attribute, if any.</summary>
    internal string? DefaultValue { get; init; }

    /// <summary>Gets the ownership transfer mode.</summary>
    internal GirTransfer Transfer { get; init; }

    /// <summary>Gets the property type.</summary>
    internal GirTypeRef Type { get; init; } = new GirTypeRef();
}
