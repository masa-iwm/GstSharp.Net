using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Gst.Analyzers;

/// <summary>
/// Collects the candidates and the uses of one operation block and decides at
/// the end of the block which candidates are never released.
/// </summary>
/// <remarks>
/// Operation actions of a single block may run concurrently, so everything the
/// actions write to is a concurrent collection and the decision is taken once,
/// in the block end action.
/// </remarks>
internal sealed class LocalLifetimeTracker
{
    private readonly ConcurrentDictionary<ILocalSymbol, Candidate> _candidates =
        new(SymbolEqualityComparer.Default);

    private readonly ConcurrentBag<Use> _uses = new();

    /// <summary>
    /// Records a local that owns a value which somebody has to release.
    /// </summary>
    /// <param name="local">The local holding the value.</param>
    /// <param name="location">Where the diagnostic goes.</param>
    /// <param name="owningFunction">The lambda or local function the local belongs to.</param>
    /// <param name="messageArguments">The arguments of the message format.</param>
    internal void AddCandidate(
        ILocalSymbol local,
        Location location,
        IOperation? owningFunction,
        params object?[] messageArguments)
    {
        _candidates.TryAdd(local, new Candidate(location, owningFunction, messageArguments));
    }

    /// <summary>Records what one reference to a local does to its value.</summary>
    /// <param name="local">The referenced local.</param>
    /// <param name="owningFunction">The lambda or local function the reference sits in.</param>
    /// <param name="use">The classification of the reference.</param>
    internal void AddUse(ILocalSymbol local, IOperation? owningFunction, ReferenceUse use)
    {
        _uses.Add(new Use(local, owningFunction, use));
    }

    /// <summary>
    /// Reports every candidate that is neither released nor allowed to escape.
    /// </summary>
    /// <param name="context">The block end context to report into.</param>
    /// <param name="rule">The descriptor to report.</param>
    internal void Report(OperationBlockAnalysisContext context, DiagnosticDescriptor rule)
    {
        if (_candidates.IsEmpty)
        {
            return;
        }

        var disposed = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var escaped = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);

        foreach (Use use in _uses)
        {
            if (!_candidates.TryGetValue(use.Local, out Candidate candidate))
            {
                continue;
            }

            // A use from a different lambda or local function means the value was
            // captured, and the capture may outlive the block. Stay silent.
            if (!ReferenceEquals(use.OwningFunction, candidate.OwningFunction))
            {
                escaped.Add(use.Local);
                continue;
            }

            switch (use.Kind)
            {
                case ReferenceUse.Disposed:
                    disposed.Add(use.Local);
                    break;
                case ReferenceUse.Escaped:
                    escaped.Add(use.Local);
                    break;
            }
        }

        foreach (KeyValuePair<ILocalSymbol, Candidate> entry in _candidates)
        {
            if (disposed.Contains(entry.Key) || escaped.Contains(entry.Key))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                rule,
                entry.Value.Location,
                entry.Value.MessageArguments));
        }
    }

    private readonly struct Candidate
    {
        internal Candidate(Location location, IOperation? owningFunction, object?[] messageArguments)
        {
            Location = location;
            OwningFunction = owningFunction;
            MessageArguments = messageArguments;
        }

        internal Location Location { get; }

        internal IOperation? OwningFunction { get; }

        internal object?[] MessageArguments { get; }
    }

    private readonly struct Use
    {
        internal Use(ILocalSymbol local, IOperation? owningFunction, ReferenceUse kind)
        {
            Local = local;
            OwningFunction = owningFunction;
            Kind = kind;
        }

        internal ILocalSymbol Local { get; }

        internal IOperation? OwningFunction { get; }

        internal ReferenceUse Kind { get; }
    }
}
