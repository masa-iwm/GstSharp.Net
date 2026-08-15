namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// Shared shape of the gir elements that declare a C type with members.
/// </summary>
internal abstract class GirTypeDeclaration : GirNode
{
    /// <summary>Gets the <c>c:type</c> attribute, if any.</summary>
    internal string? CType { get; init; }

    /// <summary>Gets the <c>c:symbol-prefix</c> attribute, if any.</summary>
    internal string? CSymbolPrefix { get; init; }

    /// <summary>Gets the <c>glib:type-name</c> attribute, if any.</summary>
    internal string? GlibTypeName { get; init; }

    /// <summary>Gets the <c>glib:get-type</c> attribute, if any.</summary>
    internal string? GlibGetType { get; init; }

    /// <summary>Gets the fields, in gir document order.</summary>
    internal IReadOnlyList<GirField> Fields { get; init; } = [];

    /// <summary>Gets the constructors.</summary>
    internal IReadOnlyList<GirFunction> Constructors { get; init; } = [];

    /// <summary>Gets the instance methods.</summary>
    internal IReadOnlyList<GirFunction> Methods { get; init; } = [];

    /// <summary>Gets the static functions.</summary>
    internal IReadOnlyList<GirFunction> Functions { get; init; } = [];

    /// <summary>Gets the virtual methods.</summary>
    internal IReadOnlyList<GirVirtualMethod> VirtualMethods { get; init; } = [];

    /// <summary>Gets the signals.</summary>
    internal IReadOnlyList<GirSignal> Signals { get; init; } = [];

    /// <summary>Gets the properties.</summary>
    internal IReadOnlyList<GirProperty> Properties { get; init; } = [];

    /// <summary>Gets the anonymous unions declared inside the element.</summary>
    internal IReadOnlyList<GirUnion> Unions { get; init; } = [];
}

/// <summary>
/// A <c>&lt;class&gt;</c>.
/// </summary>
internal sealed class GirClass : GirTypeDeclaration
{
    /// <summary>Gets the <c>parent</c> attribute, if any.</summary>
    internal string? Parent { get; init; }

    /// <summary>Gets a value indicating whether the class is abstract.</summary>
    internal bool IsAbstract { get; init; }

    /// <summary>Gets the <c>glib:type-struct</c> attribute, if any.</summary>
    internal string? GlibTypeStruct { get; init; }

    /// <summary>Gets a value indicating whether <c>glib:fundamental="1"</c> was set.</summary>
    internal bool IsFundamental { get; init; }

    /// <summary>Gets the names of the implemented interfaces.</summary>
    internal IReadOnlyList<string> Implements { get; init; } = [];
}

/// <summary>
/// A <c>&lt;record&gt;</c>.
/// </summary>
internal sealed class GirRecord : GirTypeDeclaration
{
    /// <summary>Gets a value indicating whether <c>disguised="1"</c> was set.</summary>
    internal bool IsDisguised { get; init; }

    /// <summary>Gets a value indicating whether <c>opaque="1"</c> was set.</summary>
    internal bool IsOpaque { get; init; }

    /// <summary>Gets a value indicating whether <c>pointer="1"</c> was set.</summary>
    internal bool IsPointer { get; init; }

    /// <summary>Gets the <c>glib:is-gtype-struct-for</c> attribute, if any.</summary>
    internal string? GlibIsGTypeStructFor { get; init; }
}

/// <summary>
/// An <c>&lt;interface&gt;</c>.
/// </summary>
internal sealed class GirInterface : GirTypeDeclaration
{
    /// <summary>Gets the <c>glib:type-struct</c> attribute, if any.</summary>
    internal string? GlibTypeStruct { get; init; }

    /// <summary>Gets the names of the prerequisite types.</summary>
    internal IReadOnlyList<string> Prerequisites { get; init; } = [];
}

/// <summary>
/// A <c>&lt;union&gt;</c>.
/// </summary>
internal sealed class GirUnion : GirTypeDeclaration
{
    /// <summary>Gets the records declared inside the union.</summary>
    internal IReadOnlyList<GirRecord> Records { get; init; } = [];
}
