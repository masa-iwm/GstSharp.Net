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
    private readonly SortedDictionary<string, SortedDictionary<string, int>> _emitted =
        new(StringComparer.Ordinal);

    private readonly SortedDictionary<string, SortedDictionary<SkipReason, int>> _skipped = [];

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
    internal void Skipped(string module, SkipReason reason)
    {
        if (!_skipped.TryGetValue(module, out SortedDictionary<SkipReason, int>? reasons))
        {
            reasons = [];
            _skipped.Add(module, reasons);
        }

        reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
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
