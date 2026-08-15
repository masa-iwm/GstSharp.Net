using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Gst.Analyzers;

/// <summary>
/// What a single reference to a local does to the value the local holds.
/// </summary>
internal enum ReferenceUse
{
    /// <summary>
    /// The reference neither releases the value nor lets it escape, for example
    /// reading a property of it or comparing it against <c>null</c>.
    /// </summary>
    Neutral,

    /// <summary>
    /// The reference releases the value: a <c>Dispose()</c> call, or a
    /// <c>using</c> statement over the local.
    /// </summary>
    Disposed,

    /// <summary>
    /// The value may leave the scope of the analysis, so nothing can be said
    /// about who releases it. Every use that is not understood ends up here, so
    /// that the analysis errs towards silence.
    /// </summary>
    Escaped,
}

/// <summary>
/// The shared, deliberately pragmatic facts both ownership rules are built on.
/// </summary>
/// <remarks>
/// The analysis is a CA2000-style approximation, not a dataflow solver: it
/// classifies every syntactic use of a local and stays quiet as soon as one use
/// is not fully understood. False negatives are the accepted cost of never
/// warning about a wrapper that somebody else owns.
/// </remarks>
internal static class OwnershipFacts
{
    /// <summary>The category both rules are reported under.</summary>
    internal const string Category = "GstSharp.Reliability";

    /// <summary>The metadata name of the mini object base class.</summary>
    internal const string MiniObjectMetadataName = "Gst.MiniObject";

    /// <summary>The metadata name of the boxed value base class.</summary>
    internal const string BoxedMetadataName = "Gst.GObject.Boxed";

    /// <summary>The unqualified name of a buffer mapping scope.</summary>
    internal const string MapScopeName = "MapScope";

    /// <summary>The root namespace a mapping scope has to live in.</summary>
    internal const string GstNamespace = "Gst";

    /// <summary>Builds the documentation link of a rule.</summary>
    /// <param name="ruleId">The identifier of the rule.</param>
    /// <returns>The absolute URL of the rule documentation.</returns>
    internal static string HelpLink(string ruleId) =>
        "https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/analyzers.md#" + ruleId.ToLowerInvariant();

    /// <summary>
    /// Tests whether <paramref name="type"/> is, or derives from, one of the
    /// given base types.
    /// </summary>
    /// <param name="type">The type to test, may be null.</param>
    /// <param name="first">The first base type, may be null when it is absent from the compilation.</param>
    /// <param name="second">The second base type, may be null when it is absent from the compilation.</param>
    /// <returns><see langword="true"/> when the base chain reaches one of them.</returns>
    internal static bool DerivesFromAny(ITypeSymbol? type, INamedTypeSymbol? first, INamedTypeSymbol? second)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, first)
                || SymbolEqualityComparer.Default.Equals(current, second))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether <paramref name="type"/> is a <c>MapScope</c> nested in a
    /// type of the <c>Gst</c> namespace tree.
    /// </summary>
    /// <param name="type">The type to test, may be null.</param>
    /// <returns><see langword="true"/> for a mapping scope.</returns>
    /// <remarks>
    /// The match is on metadata names only. The analyzer must not reference the
    /// product assemblies, and every module may grow its own nested scope type.
    /// </remarks>
    internal static bool IsMapScope(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol named || named.Name != MapScopeName)
        {
            return false;
        }

        INamedTypeSymbol? container = named.ContainingType;
        if (container is null)
        {
            return false;
        }

        while (container.ContainingType is not null)
        {
            container = container.ContainingType;
        }

        for (INamespaceSymbol? ns = container.ContainingNamespace; ns is not null; ns = ns.ContainingNamespace)
        {
            if (ns.ContainingNamespace is { IsGlobalNamespace: true })
            {
                return ns.Name == GstNamespace;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether the local of <paramref name="declarator"/> is declared by a
    /// <c>using</c> declaration or a <c>using</c> statement.
    /// </summary>
    /// <param name="declarator">The declarator to inspect.</param>
    /// <returns><see langword="true"/> when the compiler disposes the local.</returns>
    internal static bool IsUsingDeclared(IVariableDeclaratorOperation declarator)
    {
        IOperation? current = declarator.Parent;
        if (current is IVariableDeclarationOperation)
        {
            current = current.Parent;
        }

        if (current is IVariableDeclarationGroupOperation)
        {
            current = current.Parent;
        }

        return current is IUsingDeclarationOperation or IUsingOperation;
    }

    /// <summary>
    /// Returns the lambda or local function a piece of code belongs to.
    /// </summary>
    /// <param name="operation">The operation to look at.</param>
    /// <returns>
    /// The innermost enclosing lambda or local function, or <see langword="null"/>
    /// when the operation belongs to the body of the analyzed member itself.
    /// </returns>
    internal static IOperation? GetOwningFunction(IOperation operation)
    {
        for (IOperation? current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Strips the conversions and parentheses around <paramref name="operation"/>
    /// and returns the value they were built from.
    /// </summary>
    /// <param name="operation">The operation to unwrap.</param>
    /// <returns>The innermost value.</returns>
    internal static IOperation Unwrap(IOperation operation)
    {
        IOperation current = operation;
        while (true)
        {
            switch (current)
            {
                case IParenthesizedOperation parenthesized:
                    current = parenthesized.Operand;
                    break;
                case IConversionOperation conversion:
                    current = conversion.Operand;
                    break;
                default:
                    return current;
            }
        }
    }

    /// <summary>
    /// Classifies one reference to a local.
    /// </summary>
    /// <param name="reference">The reference to classify.</param>
    /// <returns>What the reference does to the value.</returns>
    /// <remarks>
    /// Everything that is not recognized is reported as
    /// <see cref="ReferenceUse.Escaped"/>, which silences the rule.
    /// </remarks>
    internal static ReferenceUse Classify(ILocalReferenceOperation reference)
    {
        IOperation node = reference;
        IOperation? parent = reference.Parent;
        while (parent is IParenthesizedOperation or IConversionOperation)
        {
            node = parent;
            parent = parent.Parent;
        }

        switch (parent)
        {
            // x.Dispose(), or any other instance call on the value.
            case IInvocationOperation invocation when ReferenceEquals(invocation.Instance, node):
                return IsDisposeCall(invocation.TargetMethod) ? ReferenceUse.Disposed : ReferenceUse.Neutral;

            // x?.Dispose(), or any other conditional member access.
            case IConditionalAccessOperation conditional when ReferenceEquals(conditional.Operation, node):
                return conditional.WhenNotNull is IInvocationOperation guarded
                    && IsDisposeCall(guarded.TargetMethod)
                        ? ReferenceUse.Disposed
                        : ReferenceUse.Neutral;

            // using (x) { }
            case IUsingOperation @using when ReferenceEquals(@using.Resources, node):
                return ReferenceUse.Disposed;

            // x.Property, x.Field.
            case IMemberReferenceOperation member when ReferenceEquals(member.Instance, node):
                return ReferenceUse.Neutral;

            // x == null, !x, x is Sample.
            case IBinaryOperation:
            case IUnaryOperation:
            case IIsTypeOperation:
                return ReferenceUse.Neutral;

            // x is null stays neutral, x is Sample s aliases the value away.
            case IIsPatternOperation pattern when ReferenceEquals(pattern.Value, node):
                return DeclaresVariable(pattern.Pattern) ? ReferenceUse.Escaped : ReferenceUse.Neutral;

            // Arguments, returns, assignments, tuples, collection initializers,
            // switch subjects and everything else this rule does not model.
            default:
                return ReferenceUse.Escaped;
        }
    }

    /// <summary>Tests whether a method is a parameterless <c>Dispose</c>.</summary>
    /// <param name="method">The method that is called.</param>
    /// <returns><see langword="true"/> for <c>Dispose()</c>.</returns>
    internal static bool IsDisposeCall(IMethodSymbol method) =>
        method.Name == nameof(IDisposable.Dispose)
        && method.Parameters.Length == 0
        && method.Arity == 0;

    /// <summary>
    /// Tests whether a pattern binds the matched value to a new variable.
    /// </summary>
    /// <param name="pattern">The pattern to walk.</param>
    /// <returns><see langword="true"/> when a designation is present.</returns>
    private static bool DeclaresVariable(IOperation pattern)
    {
        switch (pattern)
        {
            case IDeclarationPatternOperation declaration when declaration.DeclaredSymbol is not null:
            case IRecursivePatternOperation recursive when recursive.DeclaredSymbol is not null:
                return true;
        }

        foreach (IOperation child in pattern.ChildOperations)
        {
            if (DeclaresVariable(child))
            {
                return true;
            }
        }

        return false;
    }
}
