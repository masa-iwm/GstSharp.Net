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

    /// <summary>Reads back the number of emitted members of one category.</summary>
    /// <param name="module">The gir namespace of the module.</param>
    /// <param name="category">The category to read.</param>
    /// <returns>The count, or zero.</returns>
    internal int EmittedCount(string module, string category) =>
        _emitted.TryGetValue(module, out SortedDictionary<string, int>? categories)
            ? categories.GetValueOrDefault(category)
            : 0;

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

        return writer.ToSource();
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
