using System.Globalization;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// What a generator run emitted and what it left out, per module.
/// </summary>
/// <remarks>
/// The counts are printed by the <c>generate</c> verb and frozen by the census
/// tests, so that a change of the marshalling rules shows up as a number that
/// moved instead of as silently missing bindings.
/// </remarks>
internal sealed class EmissionCensus
{
    private readonly Overlays _overlays;

    private readonly SortedSet<string> _handBound = new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedDictionary<string, int>> _emitted =
        new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedDictionary<SkipReason, int>> _skipped = [];

    private readonly SortedDictionary<string, SortedDictionary<string, SortedSet<string>>> _skippedSymbols =
        new(StringComparer.Ordinal);

    /// <summary>The virtual methods left out of the surface, by module and slot.</summary>
    private readonly SortedDictionary<string, SortedDictionary<string, string>> _skippedVirtuals =
        new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedDictionary<string, string>> _droppedFields =
        new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedDictionary<string, string>> _exposedFields =
        new(StringComparer.Ordinal);

    private readonly SortedSet<string> _fieldSkips = new(StringComparer.Ordinal);

    private readonly SortedSet<string> _fieldAnnotations = new(StringComparer.Ordinal);

    /// <summary>
    /// The field annotation keys whose <c>name</c> is the one the field derives
    /// anyway, which corrects nothing.
    /// </summary>
    private readonly SortedSet<string> _redundantFieldNames = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="EmissionCensus"/> class.</summary>
    /// <param name="overlays">
    /// The overlays, read for the hand bound ledger. A census built without
    /// them reports every skip under the reason the rules produced.
    /// </param>
    internal EmissionCensus(Overlays? overlays = null) => _overlays = overlays ?? Overlays.Empty;

    /// <summary>
    /// Gets the hand bound identifiers the run actually saw skipped, so that
    /// the ones it never saw can be reported as stale.
    /// </summary>
    internal IReadOnlySet<string> HandBoundSymbols => _handBound;

    /// <summary>
    /// Gets the field skip keys the run matched against a field of an emitted
    /// record, so that the ones it never matched can be reported as stale.
    /// </summary>
    internal IReadOnlySet<string> FieldSkipKeys => _fieldSkips;

    /// <summary>
    /// Gets the field annotation keys the run applied to a field of an emitted
    /// record, so that the ones it never applied can be reported as stale.
    /// </summary>
    internal IReadOnlySet<string> FieldAnnotationKeys => _fieldAnnotations;

    /// <summary>Records one field annotation the run applied.</summary>
    /// <param name="key">The overlay key that matched, for the stale report.</param>
    internal void AnnotatedField(string key) => _fieldAnnotations.Add(key);

    /// <summary>
    /// Gets the field annotation keys that named the member the field already
    /// derives, so that a name which renames nothing can be reported.
    /// </summary>
    internal IReadOnlySet<string> RedundantFieldNames => _redundantFieldNames;

    /// <summary>Records one field annotation whose name is the derived one.</summary>
    /// <param name="key">The overlay key that named it, for the stale report.</param>
    internal void RedundantFieldName(string key) => _redundantFieldNames.Add(key);

    /// <summary>Counts one emitted member.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="category">What was emitted, for example <c>method</c>.</param>
    internal void Emitted(string module, string category)
    {
        if (!_emitted.TryGetValue(module, out SortedDictionary<string, int>? categories))
        {
            categories = new SortedDictionary<string, int>(StringComparer.Ordinal);
            _emitted.Add(module, categories);
        }

        categories[category] = categories.GetValueOrDefault(category) + 1;
    }

    /// <summary>Counts one callable that was not emitted.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="reason">Why it was skipped.</param>
    /// <param name="symbol">
    /// What was skipped: the <c>c:identifier</c> of a callable, or the GObject
    /// spelling of a property (<c>Gst.Element:name</c>) or of a signal
    /// (<c>Gst.Element::pad-added</c>).
    /// </param>
    internal void Skipped(string module, SkipReason reason, string symbol)
    {
        // A symbol the hand written surface already covers is reported as
        // such whatever kept it out of the emitters, because the reason it is
        // absent from the generated code says nothing about the bindings: the
        // call exists, and the remaining sections are the ones that measure a
        // real gap.
        //
        // MovedTo and ShadowedBy are the two exceptions, because neither says
        // the symbol is absent: both say it is emitted under another
        // declaration of the same gir. Counting one of them as hand bound
        // would let a ledger entry on a generated symbol look satisfied - the
        // gir declares a handful of functions twice, once at namespace scope
        // with a moved-to and once inside the record they belong to - and
        // GEN0023 would never see the entry it exists to report.
        if ((reason is not SkipReason.MovedTo and not SkipReason.ShadowedBy)
            && _overlays.IsHandBound(symbol))
        {
            reason = SkipReason.HandBound;
            _handBound.Add(symbol);
        }

        if (!_skipped.TryGetValue(module, out SortedDictionary<SkipReason, int>? reasons))
        {
            reasons = [];
            _skipped.Add(module, reasons);
        }

        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

        if (!_skippedSymbols.TryGetValue(module, out SortedDictionary<string, SortedSet<string>>? symbols))
        {
            symbols = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            _skippedSymbols.Add(module, symbols);
        }

        string key = reason.ToString();
        if (!symbols.TryGetValue(key, out SortedSet<string>? names))
        {
            names = new SortedSet<string>(StringComparer.Ordinal);
            symbols.Add(key, names);
        }

        names.Add(symbol);
    }

    /// <summary>Records one virtual method the surface leaves out.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="key">The slot, as <c>Gst.Element::pad_added</c>.</param>
    /// <param name="reason">
    /// Why it is absent: the prose of the <c>skipVirtuals</c> entry that named
    /// it, or <c>UnsupportedSignature</c> when the planner refused the shape.
    /// </param>
    /// <remarks>
    /// A slot is not a callable and has no <c>c:identifier</c>, so none of the
    /// sections above would ever show one. The ledger is what keeps the
    /// difference between a class struct that is fully bound and one whose
    /// unbound half nobody wrote down.
    /// </remarks>
    internal void SkippedVirtual(string module, string key, string reason)
    {
        if (!_skippedVirtuals.TryGetValue(module, out SortedDictionary<string, string>? slots))
        {
            slots = new SortedDictionary<string, string>(StringComparer.Ordinal);
            _skippedVirtuals.Add(module, slots);
        }

        slots[key] = reason;
    }

    /// <summary>Counts the virtual methods the run left out.</summary>
    /// <param name="module">The module to count, or <see langword="null"/> for the run.</param>
    /// <returns>The number of slots listed.</returns>
    internal int SkippedVirtualCount(string? module = null)
    {
        int total = 0;
        foreach ((string name, SortedDictionary<string, string> slots) in _skippedVirtuals)
        {
            if (module is null || string.Equals(name, module, StringComparison.Ordinal))
            {
                total += slots.Count;
            }
        }

        return total;
    }

    /// <summary>Counts one record field that carries no binding.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="field">
    /// What was dropped, spelled <c>Record.field</c> in the gir names of both.
    /// </param>
    /// <param name="reason">
    /// The shape that kept it out: <c>Pointer</c>, <c>EmbeddedStruct</c>,
    /// <c>Callback</c>, <c>Union</c>, <c>Private</c>,
    /// <c>InlineArray(pointer element)</c>, <c>InlineArray(struct element)</c>
    /// or <c>Other</c>.
    /// </param>
    /// <remarks>
    /// A field is not a callable, so none of the skip reasons describes one and
    /// none of the sections above would ever show it. Without this ledger a
    /// record whose fields carry API in C is reported as fully bound the moment
    /// its methods are, which is how the fixed size fields of
    /// <c>GstVideoInfo</c> went unnoticed for four milestones.
    /// </remarks>
    internal void DroppedField(string module, string field, string reason)
    {
        if (!_droppedFields.TryGetValue(module, out SortedDictionary<string, string>? fields))
        {
            fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            _droppedFields.Add(module, fields);
        }

        fields[field] = reason;
    }

    /// <summary>
    /// Records one field that the overlays say something else already hands
    /// out.
    /// </summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="field">The field, spelled <c>Record.field</c> in gir names.</param>
    /// <param name="key">The overlay key that matched, for the stale report.</param>
    /// <param name="reason">The member that answers it, or that it is hand written.</param>
    internal void ExposedField(string module, string field, string key, string reason)
    {
        _fieldSkips.Add(key);
        if (!_exposedFields.TryGetValue(module, out SortedDictionary<string, string>? fields))
        {
            fields = new SortedDictionary<string, string>(StringComparer.Ordinal);
            _exposedFields.Add(module, fields);
        }

        fields[field] = reason;
    }

    /// <summary>Reads back the number of record fields another member answers.</summary>
    /// <returns>The count over the whole run.</returns>
    internal int ExposedFieldCount()
    {
        int total = 0;
        foreach (SortedDictionary<string, string> fields in _exposedFields.Values)
        {
            total += fields.Count;
        }

        return total;
    }

    /// <summary>Reads back the number of record fields the run left unbound.</summary>
    /// <param name="module">The gir namespace of the module, or <see langword="null"/> for the whole run.</param>
    /// <returns>The count.</returns>
    internal int DroppedFieldCount(string? module = null)
    {
        if (module is null)
        {
            int total = 0;
            foreach (SortedDictionary<string, string> fields in _droppedFields.Values)
            {
                total += fields.Count;
            }

            return total;
        }

        return _droppedFields.TryGetValue(module, out SortedDictionary<string, string>? entries)
            ? entries.Count
            : 0;
    }

    /// <summary>Reads back the number of emitted members of one category.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="category">The category to read.</param>
    /// <returns>The count, or zero.</returns>
    internal int EmittedCount(string module, string category) =>
        _emitted.TryGetValue(module, out SortedDictionary<string, int>? categories)
            ? categories.GetValueOrDefault(category)
            : 0;

    /// <summary>Reads back the virtual method ledger of one module.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <returns>
    /// The slots the module left out, keyed by the <c>Namespace.Class::slot</c>
    /// the ledger names them with and carrying the reason each is absent; an
    /// empty map when the module left none out.
    /// </returns>
    /// <remarks>
    /// A count alone would not catch a slot that leaves the surface while
    /// another joins it, so the tests freeze the keys and their reasons rather
    /// than the length of the list.
    /// </remarks>
    internal IReadOnlyDictionary<string, string> SkippedVirtuals(string module) =>
        _skippedVirtuals.TryGetValue(module, out SortedDictionary<string, string>? slots)
            ? slots
            : new SortedDictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Reads back the number of callables skipped for one reason.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="reason">The reason to read.</param>
    /// <returns>The count, or zero.</returns>
    internal int SkippedCount(string module, SkipReason reason) =>
        _skipped.TryGetValue(module, out SortedDictionary<SkipReason, int>? reasons)
            ? reasons.GetValueOrDefault(reason)
            : 0;

    /// <summary>
    /// Renders the symbols the run left out as a Markdown document.
    /// </summary>
    /// <returns>The document, with LF line endings and a trailing newline.</returns>
    /// <remarks>
    /// The document is committed next to the gir files, so that a member that
    /// stops being generated shows up as a line that a review can see instead
    /// of as a binding that silently disappeared. The counts are the number of
    /// distinct symbols listed, which can be lower than the counts of the
    /// console census: the gir declares a handful of functions twice, once at
    /// namespace scope and once inside the record they belong to.
    /// </remarks>
    internal string SkipReport()
    {
        CodeWriter writer = new();
        writer.WriteLine("<!-- Generated by GstSharp.Generator. Do not edit. -->");
        writer.WriteLine();
        writer.WriteLine("# Skipped symbols");
        writer.WriteLine();
        writer.WriteLine("Every gir symbol the generator did not bind, grouped by module and by reason.");
        writer.WriteLine("The file is regenerated by `generate` and diffed by `verify`, so a binding that");
        writer.WriteLine("disappears shows up here as an added line.");

        foreach ((string module, SortedDictionary<string, SortedSet<string>> reasons) in _skippedSymbols)
        {
            writer.WriteLine();
            writer.WriteLine("## " + module);

            foreach ((string reason, SortedSet<string> symbols) in reasons)
            {
                writer.WriteLine();
                writer.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"### {reason} ({symbols.Count})"));
                writer.WriteLine();
                foreach (string symbol in symbols)
                {
                    writer.WriteLine("- `" + symbol + "`");
                }
            }
        }

        WriteVirtualLedger(writer);
        WriteFieldLedger(writer);
        return writer.ToSource();
    }

    /// <summary>Writes the virtual methods the run left out of the subclassing surface.</summary>
    /// <param name="writer">The target writer.</param>
    private void WriteVirtualLedger(CodeWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"## Virtuals ({SkippedVirtualCount()})"));
        writer.WriteLine();
        writer.WriteLine("The class struct slots of a subclassable class that carry no `OnX` member, with");
        writer.WriteLine("the reason. `UnsupportedSignature` is the planner refusing a shape and");
        writer.WriteLine("`OpaqueSlot` is a function pointer field the mirror lays out with no virtual");
        writer.WriteLine("method to pair it with; every other reason is the statement of an overlay");
        writer.WriteLine("entry. The mirror still lays every slot out, so what is listed here is the");
        writer.WriteLine("managed surface and not the ABI.");

        foreach ((string module, SortedDictionary<string, string> slots) in _skippedVirtuals)
        {
            writer.WriteLine();
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"### {module} ({slots.Count})"));
            writer.WriteLine();
            foreach ((string key, string reason) in slots)
            {
                writer.WriteLine("- `" + key + "` — " + reason);
            }
        }
    }

    /// <summary>
    /// Writes the record fields the run laid out but did not bind, grouped by
    /// module.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <remarks>
    /// The sections above list callables, properties and signals, all of which
    /// are named by a symbol the gir declares on its own. A field is named by
    /// the record it belongs to, it has no <c>c:identifier</c>, and nothing
    /// that measures the binding gap counted one until this section existed.
    /// Padding and the fields the gir marks <c>private</c> or
    /// <c>readable="0"</c> are left out: they carry no API in C either, the one
    /// exception being the members of a reserved ABI union, which stand for the
    /// single line the union itself used to occupy. A field a hand written
    /// member reads through stays listed, the same way a hand bound entry point
    /// stays on the skip list - what is measured is the generated surface -
    /// unless the overlays register it under <c>fieldSkips</c>, which is the
    /// statement that moves it into the section below.
    /// </remarks>
    private void WriteFieldLedger(CodeWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"## Fields ({DroppedFieldCount()})"));
        writer.WriteLine();
        writer.WriteLine("Public record fields that carry API in C and none in C#, with the shape that");
        writer.WriteLine("kept them out. A field is bound when a wrapper declares an accessor for it, or");
        writer.WriteLine("when a value projected structure declares it as a typed public field; one that");
        writer.WriteLine("is projected onto a machine address binds nothing that can be read without the");
        writer.WriteLine("interop layer and stays listed. A union the layout stops in front of is listed");
        writer.WriteLine("once, under its own name, because the record ends where it sits; a reserved ABI");
        writer.WriteLine("union the mirror lays out is listed member by member instead, under the name of");
        writer.WriteLine("the member alone, and a member the gir keeps to the C implementation is listed");
        writer.WriteLine("as `Private` rather than left out. A field a hand written member reads through");
        writer.WriteLine("stays listed as well - the ledger measures what the generator binds, the same");
        writer.WriteLine("convention the hand bound entry points above follow - unless the overlays");
        writer.WriteLine("register it under `fieldSkips`, which moves it into the section below. A field");
        writer.WriteLine("the overlays hold back under `fieldAnnotations` with `accessor: false` stays");
        writer.WriteLine("here under its own shape and is meant to: the entry says why the binding leaves");
        writer.WriteLine("it unbound, and this ledger is where that is counted.");

        foreach ((string module, SortedDictionary<string, string> fields) in _droppedFields)
        {
            writer.WriteLine();
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"### {module} ({fields.Count})"));
            writer.WriteLine();
            foreach ((string field, string reason) in fields)
            {
                writer.WriteLine("- `" + field + "` — " + reason);
            }
        }

        WriteExposedFields(writer);
    }

    /// <summary>
    /// Writes the record fields that carry API in C and are answered by
    /// something other than an accessor of their own.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <remarks>
    /// These are the entries of <c>fieldSkips</c> in the overlays. They are kept
    /// out of the ledger above, because a field a member of the same wrapper
    /// hands out is not a gap; they are listed all the same, with what answers
    /// them, so that the claim is a line a review can check rather than a
    /// silence.
    /// </remarks>
    private void WriteExposedFields(CodeWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"## Fields exposed elsewhere ({ExposedFieldCount()})"));
        writer.WriteLine();
        writer.WriteLine("Public record fields that another member of the binding answers, with the");
        writer.WriteLine("member that answers them. They are declared in `girs/overlays/fixups.json`");
        writer.WriteLine("under `fieldSkips` and are left out of the ledger above: what is measured");
        writer.WriteLine("there is what the bindings do not cover, and these are covered.");

        foreach ((string module, SortedDictionary<string, string> fields) in _exposedFields)
        {
            writer.WriteLine();
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"### {module} ({fields.Count})"));
            writer.WriteLine();
            foreach ((string field, string reason) in fields)
            {
                writer.WriteLine("- `" + field + "` — " + reason);
            }
        }
    }

    /// <summary>Renders the census as one line per module and category.</summary>
    /// <returns>The report lines, in a deterministic order.</returns>
    internal IReadOnlyList<string> Report()
    {
        List<string> lines = [];
        foreach ((string module, SortedDictionary<string, int> categories) in _emitted)
        {
            foreach ((string category, int count) in categories)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {module}: emitted {count} {category}(s)"));
            }
        }

        foreach ((string module, SortedDictionary<SkipReason, int> reasons) in _skipped)
        {
            foreach ((SkipReason reason, int count) in reasons)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {module}: skipped {count} callable(s), {reason}"));
            }
        }

        return lines;
    }
}
