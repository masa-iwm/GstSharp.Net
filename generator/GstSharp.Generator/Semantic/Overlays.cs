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

    /// <summary>Gets or sets the index of the parameter carrying the array length.</summary>
    public int? ArrayLength { get; set; }

    /// <summary>Gets or sets the corrected <c>zero-terminated</c> flag of an array.</summary>
    public bool? ZeroTerminated { get; set; }
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
/// <item><description><c>annotationOverrides</c>: <c>c:identifier</c>, optionally
/// suffixed with <c>#parameter-name</c> or <c>#return</c>.</description></item>
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
    private readonly Dictionary<string, string> _rename;
    private readonly Dictionary<string, AnnotationOverride> _annotations;
    private readonly Dictionary<string, PlatformSupport> _platforms;

    private Overlays(
        HashSet<string> skip,
        Dictionary<string, string> rename,
        Dictionary<string, AnnotationOverride> annotations,
        Dictionary<string, PlatformSupport> platforms)
    {
        _skip = skip;
        _rename = rename;
        _annotations = annotations;
        _platforms = platforms;
    }

    /// <summary>Gets an overlay set without any correction.</summary>
    internal static Overlays Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, AnnotationOverride>(StringComparer.Ordinal),
        new Dictionary<string, PlatformSupport>(StringComparer.Ordinal));

    /// <summary>Gets the skipped identifiers, ordered for reporting.</summary>
    internal IReadOnlyCollection<string> SkippedIdentifiers => _skip;

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

        return new Overlays(skip, rename, annotations, symbols);
    }

    /// <summary>Tests whether a symbol is skipped by the overlays.</summary>
    /// <param name="key">A <c>c:identifier</c> or a qualified gir name.</param>
    /// <returns><see langword="true"/> when the symbol must not be generated.</returns>
    internal bool IsSkipped(string? key) => key is not null && _skip.Contains(key);

    /// <summary>Looks up a name override.</summary>
    /// <param name="key">A qualified gir name or a <c>c:identifier</c>.</param>
    /// <param name="name">The overriding C# name.</param>
    /// <returns><see langword="true"/> when an override exists.</returns>
    internal bool TryGetRename(string key, [NotNullWhen(true)] out string? name) =>
        _rename.TryGetValue(key, out name);

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

        public Dictionary<string, string>? Rename { get; set; }

        public Dictionary<string, AnnotationOverride>? AnnotationOverrides { get; set; }
    }

    private sealed class PlatformSymbolsFile
    {
        public Dictionary<string, PlatformSupport>? Symbols { get; set; }
    }
}
