using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gst.Analyzers;

/// <summary>
/// GST0002: reports a buffer mapping whose scope is never disposed, so that the
/// buffer is never unmapped.
/// </summary>
/// <remarks>
/// <para>
/// <c>Gst.Buffer.Map</c> and its relatives return a nested <c>MapScope</c> that
/// holds the mapping until it is disposed. The scope is a <c>ref struct</c>, so
/// it cannot be stored in a field, captured by a lambda or awaited across: the
/// only ways it can leave the enclosing statement are a local, an argument or a
/// return, which keeps this rule much simpler than GST0001.
/// </para>
/// <para>
/// The rule reports a scope that is discarded outright and a scope that is put
/// into a local which is neither declared with <c>using</c> nor disposed. Any
/// argument pass counts as consumption, including a by-value one, because the
/// callee may well be the code that disposes the mapping. A scope that is only
/// used as a temporary, as in <c>buffer.Map(flags).Size</c>, is a known false
/// negative.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnmappedMapScopeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The identifier of the rule.</summary>
    public const string DiagnosticId = "GST0002";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "Buffer mapping is never released",
        messageFormat: "The mapping returned by '{0}' is never released; declare it with 'using' or call 'Dispose'",
        category: OwnershipFacts.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A map scope keeps the memory of a buffer mapped into the address space of the process until it "
            + "is disposed. A scope that is discarded, or stored in a local that is never disposed, leaves the "
            + "buffer mapped for good.",
        helpLinkUri: OwnershipFacts.HelpLink(DiagnosticId));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockStartAction(blockStart =>
        {
            var tracker = new LocalLifetimeTracker();

            blockStart.RegisterOperationAction(
                operationContext => OnInvocation(operationContext, tracker),
                OperationKind.Invocation);

            blockStart.RegisterOperationAction(
                operationContext => OnLocalReference(operationContext, tracker),
                OperationKind.LocalReference);

            blockStart.RegisterOperationBlockEndAction(blockEnd => tracker.Report(blockEnd, Rule));
        });
    }

    private static void OnInvocation(OperationAnalysisContext context, LocalLifetimeTracker tracker)
    {
        var invocation = (IInvocationOperation)context.Operation;

        if (!OwnershipFacts.IsMapScope(invocation.TargetMethod.ReturnType))
        {
            return;
        }

        IOperation node = invocation;
        IOperation? parent = invocation.Parent;
        while (parent is IParenthesizedOperation or IConversionOperation)
        {
            node = parent;
            parent = parent.Parent;
        }

        string name = invocation.TargetMethod.ContainingType is { } containingType
            ? containingType.Name + "." + invocation.TargetMethod.Name
            : invocation.TargetMethod.Name;

        switch (parent)
        {
            // buffer.Map(flags);
            case IExpressionStatementOperation:
            // _ = buffer.Map(flags);
            case ISimpleAssignmentOperation { Target: IDiscardOperation }:
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), name));
                break;

            // var map = buffer.Map(flags);
            case IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator }
                when ReferenceEquals(GetInitializerValue(declarator), node):
                if (!OwnershipFacts.IsUsingDeclared(declarator))
                {
                    tracker.AddCandidate(
                        declarator.Symbol,
                        invocation.Syntax.GetLocation(),
                        OwnershipFacts.GetOwningFunction(declarator),
                        name);
                }

                break;

            // Arguments, returns and everything else are treated as consumption.
            default:
                break;
        }
    }

    private static void OnLocalReference(OperationAnalysisContext context, LocalLifetimeTracker tracker)
    {
        var reference = (ILocalReferenceOperation)context.Operation;

        if (!OwnershipFacts.IsMapScope(reference.Local.Type))
        {
            return;
        }

        tracker.AddUse(
            reference.Local,
            OwnershipFacts.GetOwningFunction(reference),
            OwnershipFacts.Classify(reference));
    }

    private static IOperation? GetInitializerValue(IVariableDeclaratorOperation declarator) =>
        declarator.Initializer?.Value;
}
