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
    /// <para>
    /// <c>out</c> and <c>ref</c> only correct a parameter the gir spells as a
    /// bare pointer to a plain structure — which the planner would otherwise
    /// pass as a copy the callee writes into and the caller never sees — or to
    /// a <c>GValue</c>, whose projection is a pointer into the caller's own
    /// storage in every direction.
    /// </para>
    /// <para>
    /// <c>in</c> additionally corrects a pointer to a <em>record</em> that the
    /// gir calls a caller allocated out and the C function really reads and
    /// updates in place, which is the annotation
    /// <c>gst_sdp_media_set_media_from_caps</c> carries. The redirect clears
    /// <c>callerAllocates</c> on its own, so an entry that states it as well
    /// only says out loud what the planner already does.
    /// </para>
    /// <para>
    /// Everything else is left alone and reported, because a direction is not a
    /// marshalling this can invent: an out handle, an array or a string needs a
    /// projection of its own.
    /// </para>
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

    /// <summary>
    /// Gets or sets the corrected <c>scope</c> of a callback parameter:
    /// <c>call</c>, <c>notified</c>, <c>async</c> or <c>forever</c>.
    /// </summary>
    /// <remarks>
    /// The scope is the lifetime of the managed state behind the callback, and
    /// a gir that states the wrong one is a use after free rather than a
    /// missing binding: the <c>GstCollectPads</c> setters annotate their
    /// function <c>(scope call)</c> and the library keeps the pointer for the
    /// life of the object. The correction states what the C implementation
    /// does with the function it is handed, which no other annotation carries.
    /// </remarks>
    public string? Scope { get; set; }

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

    /// <summary>
    /// Gets or sets the message of an <c>[Obsolete]</c> attribute the emitted
    /// member carries.
    /// </summary>
    /// <remarks>
    /// It is keyed on the bare <c>c:identifier</c> of the callable, because it
    /// describes the member rather than any one argument of it. What it exists
    /// for is a member that shipped in a shape the binding cannot correct: the
    /// promise of a stable series keeps the published signature alive, so the
    /// working shape is written by hand beside it and the generated one is
    /// marked here. The attribute is a warning and never an error - the member
    /// still compiles, which is the whole point of keeping it - and the gir
    /// deprecation, where a callable carries one, is what the message replaces:
    /// two attributes on one member do not compile.
    /// </remarks>
    public string? Obsolete { get; set; }
}

/// <summary>
/// A correction of an <c>&lt;array&gt;</c> the gir already spells, whose
/// element count or element type the annotation gets wrong.
/// </summary>
/// <remarks>
/// <para>
/// Every field corrects an attribute of the <c>&lt;array&gt;</c> element and
/// nothing else: this never promotes a bare pointer into an array, because the
/// decision that a pointer is one is exactly the decision a binding must not
/// invent. An entry on a parameter the gir does not spell as an array is
/// reported as GEN0020 and ignored.
/// </para>
/// <para>
/// <c>length</c> and <c>fixedSize</c> are mutually exclusive in GIR, so an
/// entry that states one clears the other: an array whose length the overlays
/// name is not also of a size fixed by the C declaration, and the other way
/// round.
/// </para>
/// </remarks>
internal sealed class ArrayOverride
{
    /// <summary>Gets or sets the index of the parameter carrying the element count.</summary>
    public int? Length { get; set; }

    /// <summary>Gets or sets the number of elements the C declaration sizes the array at.</summary>
    public int? FixedSize { get; set; }

    /// <summary>Gets or sets the corrected <c>zero-terminated</c> flag.</summary>
    public bool? ZeroTerminated { get; set; }

    /// <summary>Gets or sets the gir name of the element type.</summary>
    public string? ElementType { get; set; }
}

/// <summary>
/// A record field the ledger must not count as a missing binding, because
/// something else already hands the same value out.
/// </summary>
/// <remarks>
/// <para>
/// A field is not a callable, so no skip reason describes one and the hand
/// bound list cannot name one either: the field ledger of the skip report is
/// where a field that carries API in C and none in C# is counted. An entry here
/// says the second half is not true after all, and moves the field into a
/// section of its own instead of leaving it among the ones nothing reads.
/// </para>
/// <para>
/// It also keeps the generator from emitting an accessor for the field, which
/// is what makes it the answer to a name a hand written member already carries:
/// two declarations of one name in a partial class do not compile, and the hand
/// written one is the one that shipped.
/// </para>
/// <para>
/// Exactly one of the two has to be stated, and the check is exclusive: an
/// entry that states neither says nothing about the field, and one that states
/// both says two different things about who answers it. Either way the ledger
/// would go quiet on the strength of a claim nothing can check, so neither is
/// applied and both are reported as stale.
/// </para>
/// </remarks>
internal sealed class FieldSkip
{
    /// <summary>
    /// Gets or sets the generated member that answers the same value, for
    /// example <c>GetFlowReturn</c> for <c>GstPadProbeInfo.flow_ret</c>.
    /// </summary>
    public string? ExposedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a hand written member under
    /// <c>src/&lt;Project&gt;/Custom/</c> answers the field.
    /// </summary>
    public bool? HandBound { get; set; }

    /// <summary>Gets what the ledger reports the field under.</summary>
    internal string Reason =>
        ExposedBy is { Length: > 0 } member ? member : "hand written";

    /// <summary>
    /// Gets a value indicating whether the entry states exactly one of the two
    /// halves, which is the only shape that says who answers the field.
    /// </summary>
    internal bool IsStated => (ExposedBy is { Length: > 0 }) ^ (HandBound == true);
}

/// <summary>
/// A correction of a record field that no gir annotation carries.
/// </summary>
/// <remarks>
/// <para>
/// A gir never states the nullability of a <c>&lt;field&gt;</c>: the attribute
/// exists on parameters and on return values only. A field that is projected
/// onto a reference - a string, or the wrapper of what a pointer points at - is
/// therefore nullable by default, because that is what the corpus says about
/// every one of them, and a non nullable one is a claim about the C
/// implementation that has to be made by hand.
/// </para>
/// <para>
/// <c>nullable: false</c> is one of the two corrections an entry may state. The
/// other is <c>accessor: false</c>, which holds a field back from the accessors
/// altogether: the pointer stays on the mirror and the field stays on the
/// ledger under the shape that keeps it there. The reason belongs in the
/// <c>$comment</c> of the entry rather than in a key of its own, because it
/// differs per field: a pointer the library replaces or clears under whoever is
/// reading it, where the reference a <c>transfer none</c> projection takes is
/// taken after the read and can therefore be taken too late; an accessor whose
/// name a member that shipped already carries, where the accessor is the one
/// that yields; or a field a wave deliberately left for the next one.
/// </para>
/// <para>
/// Exactly one of the two has to be stated, and the check is exclusive: the
/// default is already the other answer to <c>nullable</c>, so an entry that
/// states nothing or that states <c>nullable: true</c> corrects nothing, and
/// one that states both says two things about a field only one of which can
/// be acted on. Each entry carries a <c>$comment</c> with the C file and line
/// the claim rests on, which is ignored here and read by the reviewer.
/// </para>
/// </remarks>
internal sealed class FieldAnnotation
{
    /// <summary>
    /// Gets or sets a value indicating whether the field may hold the null
    /// pointer. Only <see langword="false"/> is applied.
    /// </summary>
    public bool? Nullable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the field is projected onto an
    /// accessor at all. Only <see langword="false"/> is applied.
    /// </summary>
    public bool? Accessor { get; set; }

    /// <summary>
    /// Gets a value indicating whether the entry states exactly one of the two
    /// corrections, which is the only shape that changes what is emitted.
    /// </summary>
    internal bool IsStated => (Nullable == false) ^ (Accessor == false);

    /// <summary>
    /// Gets a value indicating whether the field is held back from the
    /// accessors.
    /// </summary>
    internal bool SuppressesAccessor => IsStated && Accessor == false;
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
/// <item><description><c>skip</c>: <c>c:identifier</c> of a callable, the
/// qualified gir name of a type (<c>Gst.Foo</c>), or the GObject spelling of a
/// property (<c>Gst.Element:name</c>) whose C implementation no annotation can
/// describe.</description></item>
/// <item><description><c>handBound</c>: <c>c:identifier</c> of a callable, or
/// the GObject spelling of a signal (<c>Gst.Element::pad-added</c>) or property
/// (<c>Gst.Element:name</c>) as the census reports it, whose managed surface is
/// hand written. It changes nothing about what is generated; it annotates the
/// ledger, so that a symbol the bindings do cover is reported under
/// <see cref="SkipReason.HandBound"/> instead of counting as a missing
/// binding.</description></item>
/// <item><description><c>rename</c>: qualified gir name of a type
/// (<c>Gst.MessageType</c>), of an enumeration member
/// (<c>Gst.MessageType.state_changed</c>) or a <c>c:identifier</c>.</description></item>
/// <item><description><c>annotationOverrides</c>: <c>c:identifier</c> of a
/// callable, or the <c>c:type</c> of a callback, which has no identifier of its
/// own; optionally suffixed with <c>#parameter-name</c> or
/// <c>#return</c>. Besides the annotations the gir spells, an entry may state
/// the <c>direction</c> of a pointer to a plain structure, of a pointer to a
/// record the C function works on in place, and the <c>fixedArraySize</c> of a
/// caller allocated out array, all of which are facts about the C
/// implementation that no gir annotation carries; it may also state
/// <c>discardReturn</c> on <c>#return</c> to drop a return value the caller
/// already holds, and the <c>scope</c> of a callback parameter whose gir
/// annotation does not describe how long the library keeps the function it is
/// handed. On the bare identifier, with no suffix, it may state
/// <c>obsolete</c>, the message of an <c>[Obsolete]</c> attribute the emitted
/// member carries, which is how a member that shipped in a shape the binding
/// cannot correct without breaking the series is marked while the corrected
/// shape is written by hand beside it. A signal argument is addressed by the
/// GObject spelling of its signal instead,
/// <c>GES.Project::error-loading-asset#error</c>, the key
/// <c>rename</c> uses for the event of the same signal; only <c>nullable</c>
/// is read there, because a signal argument has no direction, no array, no
/// callback scope and no discardable return.</description></item>
/// <item><description><c>arrayOverrides</c>: keyed like
/// <c>annotationOverrides</c> and applied to a parameter or a return value the
/// gir already spells as an <c>&lt;array&gt;</c>. It corrects the
/// <c>length</c> index, the <c>fixedSize</c>, the <c>zeroTerminated</c> flag
/// or the <c>elementType</c> of that array, which is how a C function that
/// counts its elements off another argument gets a span rather than staying
/// unbound. It never turns a bare pointer into an array.</description></item>
/// <item><description><c>fieldSkips</c>: the <c>c:type</c> of a record and the
/// gir name of one of its fields (<c>GstPadProbeInfo.flow_ret</c>), naming the
/// member that answers the same value. A field of a reserved ABI union is
/// addressed by the field alone, the same way its accessor is named. It keeps
/// the field off the ledger of unbound fields and keeps the generator from
/// emitting an accessor for it; a key that matches no field is reported as
/// stale.</description></item>
/// <item><description><c>fieldAnnotations</c>: keyed like <c>fieldSkips</c>
/// and stating what no gir annotation carries about a record field. Today
/// that is <c>nullable</c>, read only as <c>false</c>, which says the field
/// never holds the null pointer and emits the accessor of it non nullable, and
/// <c>accessor</c>, read only as <c>false</c>, which holds the field back from
/// the accessors and leaves it on the ledger. Exactly one of the two has to be
/// stated; an entry that states neither, that states the default, or that
/// states both is reported as stale.</description></item>
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
    private readonly HashSet<string> _handBound;
    private readonly HashSet<string> _forceOpaque;
    private readonly Dictionary<string, string> _rename;
    private readonly Dictionary<string, AnnotationOverride> _annotations;
    private readonly Dictionary<string, ArrayOverride> _arrayOverrides;
    private readonly Dictionary<string, PlatformSupport> _platforms;
    private readonly Dictionary<string, string> _returnTypes;
    private readonly Dictionary<string, FieldSkip> _fieldSkips;
    private readonly Dictionary<string, FieldAnnotation> _fieldAnnotations;

    private Overlays(
        HashSet<string> skip,
        HashSet<string> handBound,
        HashSet<string> forceOpaque,
        Dictionary<string, string> rename,
        Dictionary<string, AnnotationOverride> annotations,
        Dictionary<string, ArrayOverride> arrayOverrides,
        Dictionary<string, PlatformSupport> platforms,
        Dictionary<string, string> returnTypes,
        Dictionary<string, FieldSkip> fieldSkips,
        Dictionary<string, FieldAnnotation> fieldAnnotations)
    {
        _skip = skip;
        _handBound = handBound;
        _forceOpaque = forceOpaque;
        _rename = rename;
        _annotations = annotations;
        _arrayOverrides = arrayOverrides;
        _platforms = platforms;
        _returnTypes = returnTypes;
        _fieldSkips = fieldSkips;
        _fieldAnnotations = fieldAnnotations;
    }

    /// <summary>Gets an overlay set without any correction.</summary>
    internal static Overlays Empty { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, AnnotationOverride>(StringComparer.Ordinal),
        new Dictionary<string, ArrayOverride>(StringComparer.Ordinal),
        new Dictionary<string, PlatformSupport>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, FieldSkip>(StringComparer.Ordinal),
        new Dictionary<string, FieldAnnotation>(StringComparer.Ordinal));

    /// <summary>Gets the skipped identifiers, ordered for reporting.</summary>
    internal IReadOnlyCollection<string> SkippedIdentifiers => _skip;

    /// <summary>
    /// Gets the identifiers whose managed surface is hand written, so that a
    /// run can report the ones it never saw skipped.
    /// </summary>
    internal IReadOnlyCollection<string> HandBoundIdentifiers => _handBound;

    /// <summary>
    /// The qualified gir names of the records kept behind a pointer instead of
    /// being projected as value types.
    /// </summary>
    internal IReadOnlyCollection<string> OpaqueRecords => _forceOpaque;

    /// <summary>
    /// Gets the keys of every declared annotation correction, so that a run
    /// can report the ones no callable, parameter or signal argument matched.
    /// </summary>
    internal IReadOnlyCollection<string> AnnotationOverrideKeys => _annotations.Keys;

    /// <summary>
    /// Gets the keys of every declared array correction, so that a run can
    /// report the ones no array matched.
    /// </summary>
    internal IReadOnlyCollection<string> ArrayOverrideKeys => _arrayOverrides.Keys;

    /// <summary>
    /// Gets the keys of every declared field skip, so that a run can report the
    /// ones no field of an emitted record matched.
    /// </summary>
    internal IReadOnlyCollection<string> FieldSkipKeys => _fieldSkips.Keys;

    /// <summary>
    /// Gets the keys of every declared field annotation, so that a run can
    /// report the ones no field of an emitted record matched.
    /// </summary>
    internal IReadOnlyCollection<string> FieldAnnotationKeys => _fieldAnnotations.Keys;

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

        HashSet<string> handBound = new(StringComparer.Ordinal);
        foreach (string identifier in fixups.HandBound ?? [])
        {
            handBound.Add(identifier);
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

        Dictionary<string, ArrayOverride> arrayOverrides = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ArrayOverride> entry in fixups.ArrayOverrides ?? [])
        {
            arrayOverrides[entry.Key] = entry.Value;
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

        Dictionary<string, FieldSkip> fieldSkips = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, FieldSkip> entry in fixups.FieldSkips ?? [])
        {
            fieldSkips[entry.Key] = entry.Value;
        }

        Dictionary<string, FieldAnnotation> fieldAnnotations = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, FieldAnnotation> entry in fixups.FieldAnnotations ?? [])
        {
            fieldAnnotations[entry.Key] = entry.Value;
        }

        return new Overlays(
            skip,
            handBound,
            forceOpaque,
            rename,
            annotations,
            arrayOverrides,
            symbols,
            returnTypes,
            fieldSkips,
            fieldAnnotations);
    }

    /// <summary>Tests whether a symbol is skipped by the overlays.</summary>
    /// <param name="key">A <c>c:identifier</c> or a qualified gir name.</param>
    /// <returns><see langword="true"/> when the symbol must not be generated.</returns>
    internal bool IsSkipped(string? key) => key is not null && _skip.Contains(key);

    /// <summary>
    /// Tests whether the managed surface of a symbol is hand written.
    /// </summary>
    /// <param name="key">A <c>c:identifier</c>.</param>
    /// <returns><see langword="true"/> when the symbol is listed as hand bound.</returns>
    /// <remarks>
    /// This says nothing about whether the symbol is generated: it is the
    /// annotation the skip report groups by, so that a call the bindings cover
    /// by hand is not counted among the ones they do not cover at all.
    /// </remarks>
    internal bool IsHandBound(string? key) => key is not null && _handBound.Contains(key);

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

    /// <summary>Looks up the correction of an array.</summary>
    /// <param name="key">A <c>c:identifier</c> suffixed with <c>#parameter</c> or <c>#return</c>.</param>
    /// <returns>The correction, or <see langword="null"/>.</returns>
    internal ArrayOverride? GetArrayOverride(string key) =>
        _arrayOverrides.TryGetValue(key, out ArrayOverride? value) ? value : null;

    /// <summary>Looks up the member that answers a record field.</summary>
    /// <param name="key">
    /// The <c>c:type</c> of the record and the gir name of the field, for
    /// example <c>GstPadProbeInfo.flow_ret</c>.
    /// </param>
    /// <returns>The entry, or <see langword="null"/> when there is none.</returns>
    internal FieldSkip? GetFieldSkip(string key) =>
        _fieldSkips.TryGetValue(key, out FieldSkip? value) ? value : null;

    /// <summary>Looks up the correction of a record field.</summary>
    /// <param name="key">
    /// The <c>c:type</c> of the record and the gir name of the field, for
    /// example <c>GstRTSPUrl.host</c>.
    /// </param>
    /// <returns>The entry, or <see langword="null"/> when there is none.</returns>
    internal FieldAnnotation? GetFieldAnnotation(string key) =>
        _fieldAnnotations.TryGetValue(key, out FieldAnnotation? value) ? value : null;

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

        public List<string>? HandBound { get; set; }

        public List<string>? ForceOpaque { get; set; }

        public Dictionary<string, string>? Rename { get; set; }

        public Dictionary<string, AnnotationOverride>? AnnotationOverrides { get; set; }

        public Dictionary<string, ArrayOverride>? ArrayOverrides { get; set; }

        public Dictionary<string, string>? ReturnTypeOverrides { get; set; }

        public Dictionary<string, FieldSkip>? FieldSkips { get; set; }

        public Dictionary<string, FieldAnnotation>? FieldAnnotations { get; set; }
    }

    private sealed class PlatformSymbolsFile
    {
        public Dictionary<string, PlatformSupport>? Symbols { get; set; }
    }
}
