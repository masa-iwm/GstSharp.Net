namespace GstSharp.Generator.GirParsing.Model;

/// <summary>
/// An <c>&lt;include&gt;</c> of a repository.
/// </summary>
/// <param name="Name">The included namespace name.</param>
/// <param name="Version">The included namespace version.</param>
internal readonly record struct GirInclude(string Name, string Version);

/// <summary>
/// A <c>&lt;namespace&gt;</c> with its top level members.
/// </summary>
internal sealed class GirNamespace
{
    /// <summary>Gets the namespace name, for example <c>Gst</c>.</summary>
    internal string Name { get; init; } = string.Empty;

    /// <summary>Gets the namespace version, for example <c>1.0</c>.</summary>
    internal string Version { get; init; } = string.Empty;

    /// <summary>Gets the <c>shared-library</c> attribute.</summary>
    internal string? SharedLibrary { get; init; }

    /// <summary>Gets the <c>c:identifier-prefixes</c> entries.</summary>
    internal IReadOnlyList<string> IdentifierPrefixes { get; init; } = [];

    /// <summary>Gets the <c>c:symbol-prefixes</c> entries.</summary>
    internal IReadOnlyList<string> SymbolPrefixes { get; init; } = [];

    /// <summary>Gets the aliases.</summary>
    internal IReadOnlyList<GirAlias> Aliases { get; init; } = [];

    /// <summary>Gets the classes.</summary>
    internal IReadOnlyList<GirClass> Classes { get; init; } = [];

    /// <summary>Gets the records.</summary>
    internal IReadOnlyList<GirRecord> Records { get; init; } = [];

    /// <summary>Gets the interfaces.</summary>
    internal IReadOnlyList<GirInterface> Interfaces { get; init; } = [];

    /// <summary>Gets the unions.</summary>
    internal IReadOnlyList<GirUnion> Unions { get; init; } = [];

    /// <summary>Gets the enumerations, that is <c>&lt;enumeration&gt;</c> without <c>&lt;bitfield&gt;</c>.</summary>
    internal IReadOnlyList<GirEnumeration> Enumerations { get; init; } = [];

    /// <summary>Gets the bitfields.</summary>
    internal IReadOnlyList<GirEnumeration> Bitfields { get; init; } = [];

    /// <summary>Gets the namespace level callbacks.</summary>
    internal IReadOnlyList<GirCallback> Callbacks { get; init; } = [];

    /// <summary>Gets the constants.</summary>
    internal IReadOnlyList<GirConstant> Constants { get; init; } = [];

    /// <summary>Gets the namespace level functions.</summary>
    internal IReadOnlyList<GirFunction> Functions { get; init; } = [];

    /// <summary>Gets every enumeration and bitfield, enumerations first.</summary>
    internal IEnumerable<GirEnumeration> AllEnumerations => Enumerations.Concat(Bitfields);
}

/// <summary>
/// A parsed gir file.
/// </summary>
internal sealed class GirRepository
{
    /// <summary>Gets the path the repository was read from.</summary>
    internal string FilePath { get; init; } = string.Empty;

    /// <summary>Gets the gir schema version of the <c>&lt;repository&gt;</c> element.</summary>
    internal string? SchemaVersion { get; init; }

    /// <summary>Gets the declared includes.</summary>
    internal IReadOnlyList<GirInclude> Includes { get; init; } = [];

    /// <summary>Gets the <c>&lt;package&gt;</c> names.</summary>
    internal IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>Gets the <c>&lt;c:include&gt;</c> header names.</summary>
    internal IReadOnlyList<string> CIncludes { get; init; } = [];

    /// <summary>Gets the namespaces, which in practice is exactly one.</summary>
    internal IReadOnlyList<GirNamespace> Namespaces { get; init; } = [];
}
