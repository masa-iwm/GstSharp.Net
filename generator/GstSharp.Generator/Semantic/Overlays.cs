using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// A corrected annotation for a callable, a parameter or a return value.
/// </summary>
internal sealed class AnnotationOverride
{
    /// <summary>Gets or sets the corrected <c>transfer-ownership</c> value.</summary>
    public string? Transfer { get; set; }

    /// <summary>Gets or sets the corrected nullability.</summary>
    public bool? Nullable { get; set; }

    /// <summary>Gets or sets the corrected optionality of an out parameter.</summary>
    public bool? Optional { get; set; }

    /// <summary>Gets or sets the corrected <c>caller-allocates</c> flag.</summary>
    public bool? CallerAllocates { get; set; }

    /// <summary>
    /// Gets or sets how the parameter is passed in C#: <c>in</c>, <c>out</c> or
    /// <c>ref</c>.
    /// </summary>
    /// <remarks>
    /// It only corrects a parameter the gir spells as a bare pointer to a plain
    /// structure — which the planner would otherwise pass as a copy the callee
    /// writes into and the caller never sees — or to a <c>GValue</c>, whose
    /// projection is a pointer into the caller's own storage in every
    /// direction. Everything else is left alone and reported, because a
    /// direction is not a marshalling this can invent: an out handle, an array
    /// or a string needs a projection of its own.
    /// </remarks>
    public string? Direction { get; set; }

    /// <summary>
    /// Gets or sets the number of elements a caller allocated out array holds,
    /// for a parameter the gir spells as a pointer to a single value.
    /// </summary>
    /// <remarks>
    /// The C function writes that many elements into storage the caller
    /// provides, which the gir does not say: <c>gst_video_info_align_full</c>
    /// fills four <c>gsize</c> values through a parameter its gir declares as
    /// one <c>gsize*</c>. The size is a fact about the C implementation and
    /// belongs in the overlays for that reason.
    /// </remarks>
    public int? FixedArraySize { get; set; }

    /// <summary>Gets or sets the index of the parameter carrying the array length.</summary>
    public int? ArrayLength { get; set; }

    /// <summary>Gets or sets the corrected <c>zero-terminated</c> flag of an array.</summary>
    public bool? ZeroTerminated { get; set; }

    /// <summary>
    /// Gets or sets whether the return value is dropped, so that the member is
    /// emitted as if the C function returned nothing.
    /// </summary>
    /// <remarks>
    /// It is keyed on <c>#return</c> and states a fact about the C
    /// implementation that no gir annotation carries: the value handed back is
    /// something the caller already holds. <c>gst_value_list_init</c> returns
    /// the very pointer it was given, and binding that return would deep copy a
    /// freshly initialized list into a second owner for nothing.
    /// </remarks>
    public bool? DiscardReturn { get; set; }
}

/// <summary>
/// Per platform availability of a native symbol.
/// </summary>
internal sealed class PlatformSupport
{
    /// <summary>Gets or sets the platforms the symbol exists on.</summary>
    public List<string>? Supported { get; set; }

    /// <summary>Gets or sets the platforms the symbol is missing on.</summary>
    public List<string>? Unsupported { get; set; }
}

/// <summary>
/// The hand maintained corrections applied on top of the reference gir files.
/// </summary>
/// <remarks>
/// <para>
/// <c>fixups.json</c> uses these key formats:
/// </para>
/// <list type="bullet">
/// <item><description><c>skip</c>: <c>c:identifier</c> of a callable, or the
/// qualified gir name of a type (<c>Gst.Foo</c>).</description></item>
/// <item><description><c>rename</c>: qualified gir name of a type
/// (<c>Gst.MessageType</c>), of an enumeration member
/// (<c>Gst.MessageType.state_changed</c>) or a <c>c:identifier</c>.</description></item>
/// <item><description><c>annotationOverrides</c>: <c>c:identifier</c> of a
/// callable, or the <c>c:type</c> of a callback, which has no identifier of its
/// own; optionally suffixed with <c>#parameter-name</c> or
/// <c>#return</c>. Besides the annotations the gir spells, an entry may state
/// the <c>direction</c> of a pointer to a plain structure and the
/// <c>fixedArraySize</c> of a caller allocated out array, both of which are
/// facts about the C implementation that no gir annotation
/// carries, and <c>discardReturn</c> on <c>#return</c> to drop a return value
/// the caller already holds.</description></item>
/// <item><description><c>forceOpaque</c>: qualified gir name of a record
/// (<c>Gst.DebugCategory</c>) that must be wrapped behind a pointer rather
/// than copied by value.</description></item>
/// <item><description><c>returnTypeOverrides</c>: <c>c:identifier</c> mapped
/// onto the C# type the member returns. It only narrows a returned handle onto
/// the type that declares the member, which is what turns
/// <c>gst_pipeline_new</c> from a factory of <c>Gst.Element</c> into one of
/// <c>Gst.Pipeline</c>.</description></item>
/// </list>
/// </remarks>
internal sealed class Overlays
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly HashSet<string> _skip;
    private readonly HashSet<string> _forceOpaque;
    private readonly Dictionary<string, string> _rename;
    private readonly Dictionary<string, AnnotationOverride> _annotations;
    private readonly Dictionary<string, PlatformSupport> _platforms;
    private readonly Dictionary<string, string> _returnTypes;

    private Overlays(
        HashSet<string> skip,
        HashSet<string> forceOpaque,
        Dictionary<string, string> rename,
        Dictionary<string, AnnotationOverride> annotations,
        Dictionary<string, PlatformSupport> platforms,
        Dictionary<string, string> returnTypes)
    {
        _skip = skip;
        _forceOpaque = forceOpaque;
        _rename = rename;
        _annotations = annotations;
        _platforms = platforms;
        _returnTypes = returnTypes;
    }

    /// <summary>Gets an overlay set without any correction.</summary>
    internal static Overlays Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, AnnotationOverride>(StringComparer.Ordinal),
        new Dictionary<string, PlatformSupport>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Gets the skipped identifiers, ordered for reporting.</summary>
    internal IReadOnlyCollection<string> SkippedIdentifiers => _skip;

    /// <summary>
    /// The qualified gir names of the records kept behind a pointer instead of
    /// being projected as value types.
    /// </summary>
    internal IReadOnlyCollection<string> OpaqueRecords => _forceOpaque;

    /// <summary>
    /// Loads <c>fixups.json</c> and <c>platform-symbols.json</c> from an overlay
    /// directory. Missing files are treated as empty.
    /// </summary>
    /// <param name="overlayDirectory">Directory holding the overlay files.</param>
    /// <returns>The loaded overlays.</returns>
    internal static Overlays Load(string overlayDirectory)
    {
        FixupsFile fixups = ReadJson<FixupsFile>(Path.Combine(overlayDirectory, "fixups.json")) ?? new FixupsFile();
        PlatformSymbolsFile platforms =
            ReadJson<PlatformSymbolsFile>(Path.Combine(overlayDirectory, "platform-symbols.json"))
            ?? new PlatformSymbolsFile();

        HashSet<string> skip = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.Skip ?? [])
        {
            skip.Add(identifier);
        }

        HashSet<string> forceOpaque = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.ForceOpaque ?? [])
        {
            forceOpaque.Add(identifier);
        }

        Dictionary<string, string> rename = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.Rename ?? [])
        {
            rename[entry.Key] = entry.Value;
        }

        Dictionary<string, AnnotationOverride> annotations = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, AnnotationOverride> entry in fixups.AnnotationOverrides ?? [])
        {
            annotations[entry.Key] = entry.Value;
        }

        Dictionary<string, PlatformSupport> symbols = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, PlatformSupport> entry in platforms.Symbols ?? [])
        {
            symbols[entry.Key] = entry.Value;
        }

        Dictionary<string, string> returnTypes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in fixups.ReturnTypeOverrides ?? [])
        {
            returnTypes[entry.Key] = entry.Value;
        }

        return new Overlays(skip, forceOpaque, rename, annotations, symbols, returnTypes);
    }

    /// <summary>Tests whether a symbol is skipped by the overlays.</summary>
    /// <param name="key">A <c>c:identifier</c> or a qualified gir name.</param>
    /// <returns><see langword="true"/> when the symbol must not be generated.</returns>
    internal bool IsSkipped(string? key) => key is not null && _skip.Contains(key);

    /// <summary>
    /// Tests whether a record must be wrapped behind a pointer instead of being
    /// projected as a value type.
    /// </summary>
    /// <param name="qualifiedName">The qualified gir name of the record.</param>
    /// <returns><see langword="true"/> when the record is forced opaque.</returns>
    internal bool IsForcedOpaque(string qualifiedName) => _forceOpaque.Contains(qualifiedName);

    /// <summary>Looks up a name override.</summary>
    /// <param name="key">A qualified gir name or a <c>c:identifier</c>.</param>
    /// <param name="name">The overriding C# name.</param>
    /// <returns><see langword="true"/> when an override exists.</returns>
    internal bool TryGetRename(string key, [NotNullWhen(true)] out string? name) =>
        _rename.TryGetValue(key, out name);

    /// <summary>Looks up the C# type a member returns instead of the mapped one.</summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the callable.</param>
    /// <param name="type">The overriding C# type.</param>
    /// <returns><see langword="true"/> when an override exists.</returns>
    internal bool TryGetReturnTypeOverride(string cIdentifier, [NotNullWhen(true)] out string? type) =>
        _returnTypes.TryGetValue(cIdentifier, out type);

    /// <summary>Looks up an annotation correction.</summary>
    /// <param name="key">A <c>c:identifier</c>, optionally suffixed with <c>#parameter</c>.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    internal AnnotationOverride? GetAnnotationOverride(string key) =>
        _annotations.TryGetValue(key, out AnnotationOverride? value) ? value : null;

    /// <summary>Looks up the platform availability of a native symbol.</summary>
    /// <param name="cIdentifier">The <c>c:identifier</c> of the symbol.</param>
    /// <returns>The availability, or <see langword="null"/> when the symbol is portable.</returns>
    internal PlatformSupport? GetPlatformSupport(string cIdentifier) =>
        _platforms.TryGetValue(cIdentifier, out PlatformSupport? value) ? value : null;

    private static T? ReadJson<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<T>(stream, SerializerOptions);
    }

    private sealed class FixupsFile
    {
        public List<string>? Skip { get; set; }

        public List<string>? ForceOpaque { get; set; }

        public Dictionary<string, string>? Rename { get; set; }

        public Dictionary<string, AnnotationOverride>? AnnotationOverrides { get; set; }

        public Dictionary<string, string>? ReturnTypeOverrides { get; set; }
    }

    private sealed class PlatformSymbolsFile
    {
        public Dictionary<string, PlatformSupport>? Symbols { get; set; }
    }
}
