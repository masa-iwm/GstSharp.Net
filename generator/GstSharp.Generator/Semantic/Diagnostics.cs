using System.Globalization;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// Severity of a generator diagnostic.
/// </summary>
internal enum DiagnosticSeverity
{
    /// <summary>The generator worked around the problem.</summary>
    Warning,

    /// <summary>The generator cannot produce correct output.</summary>
    Error,
}

/// <summary>
/// A single generator diagnostic.
/// </summary>
/// <param name="Severity">How bad the finding is.</param>
/// <param name="Code">A stable identifier, for example <c>GEN0001</c>.</param>
/// <param name="Message">The human readable message.</param>
internal readonly record struct Diagnostic(DiagnosticSeverity Severity, string Code, string Message)
{
    /// <summary>Formats the diagnostic for the console.</summary>
    /// <returns>A one line rendering.</returns>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{(Severity == DiagnosticSeverity.Error ? "error" : "warning")} {Code}: {Message}");
}

/// <summary>
/// Collects diagnostics, suppressing exact duplicates so that a single broken
/// type reference does not produce thousands of identical warnings.
/// </summary>
internal sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = [];
    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <summary>Gets the collected diagnostics, in the order they were reported.</summary>
    internal IReadOnlyList<Diagnostic> Items => _items;

    /// <summary>Gets a value indicating whether at least one error was reported.</summary>
    internal bool HasErrors
    {
        get
        {
            foreach (Diagnostic item in _items)
            {
                if (item.Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Reports a warning.</summary>
    /// <param name="code">A stable identifier.</param>
    /// <param name="message">The human readable message.</param>
    internal void Warn(string code, string message) =>
        Add(new Diagnostic(DiagnosticSeverity.Warning, code, message));

    /// <summary>Reports an error.</summary>
    /// <param name="code">A stable identifier.</param>
    /// <param name="message">The human readable message.</param>
    internal void Error(string code, string message) =>
        Add(new Diagnostic(DiagnosticSeverity.Error, code, message));

    private void Add(Diagnostic diagnostic)
    {
        if (_seen.Add(diagnostic.Code + "|" + diagnostic.Message))
        {
            _items.Add(diagnostic);
        }
    }
}
