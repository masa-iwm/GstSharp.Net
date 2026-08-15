using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gst.Analyzers;

/// <summary>
/// GST0001: reports a local that owns a GstSharp.Net wrapper which is never
/// disposed and never handed to anybody else.
/// </summary>
/// <remarks>
/// <para>
/// Every wrapper GstSharp.Net returns owns a reference to the native object, so
/// dropping the last local without disposing it leaks that reference until the
/// finalizer runs, if it ever does. The rule looks at locals initialized from a
/// call or an object creation whose type derives from <c>Gst.MiniObject</c> or
/// <c>Gst.GObject.Boxed</c>.
/// </para>
/// <para>
/// The flow analysis is intentionally pragmatic. A local is reported only when
/// every one of its uses is understood and none of them releases the value or
/// lets it escape; a single unrecognized use silences the rule. Disposing on one
/// branch of an <c>if</c> counts as disposing everywhere, which is the same
/// over-suppression CA2000 accepts.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UndisposedWrapperAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The identifier of the rule.</summary>
    public const string DiagnosticId = "GST0001";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        title: "GstSharp.Net wrapper is never disposed",
        messageFormat: "'{0}' owns a '{1}' that is never released; dispose it or declare it with 'using'",
        category: OwnershipFacts.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Every wrapper GstSharp.Net hands to user code owns a reference to the native object and releases "
            + "it in Dispose. A local that holds such a wrapper without disposing it, and without passing it "
            + "on, leaks that reference.",
        helpLinkUri: OwnershipFacts.HelpLink(DiagnosticId));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? miniObject = context.Compilation.GetTypeByMetadataName(
            OwnershipFacts.MiniObjectMetadataName);
        INamedTypeSymbol? boxed = context.Compilation.GetTypeByMetadataName(
            OwnershipFacts.BoxedMetadataName);

        if (miniObject is null && boxed is null)
        {
            // The compilation does not use GstSharp.Net at all.
            return;
        }

        context.RegisterOperationBlockStartAction(blockStart =>
        {
            var tracker = new LocalLifetimeTracker();

            blockStart.RegisterOperationAction(
                operationContext => OnVariableDeclarator(operationContext, tracker, miniObject, boxed),
                OperationKind.VariableDeclarator);

            blockStart.RegisterOperationAction(
                operationContext => OnLocalReference(operationContext, tracker, miniObject, boxed),
                OperationKind.LocalReference);

            blockStart.RegisterOperationBlockEndAction(blockEnd => tracker.Report(blockEnd, Rule));
        });
    }

    private static void OnVariableDeclarator(
        OperationAnalysisContext context,
        LocalLifetimeTracker tracker,
        INamedTypeSymbol? miniObject,
        INamedTypeSymbol? boxed)
    {
        var declarator = (IVariableDeclaratorOperation)context.Operation;
        ILocalSymbol local = declarator.Symbol;

        if (!OwnershipFacts.DerivesFromAny(local.Type, miniObject, boxed))
        {
            return;
        }

        if (declarator.Initializer?.Value is not { } initializer)
        {
            return;
        }

        // Only a call or a "new" hands out a fresh reference. Anything else is
        // most likely a borrow of a wrapper somebody else owns.
        if (OwnershipFacts.Unwrap(initializer) is not (IInvocationOperation or IObjectCreationOperation))
        {
            return;
        }

        // "using var s = ..." and "using (var s = ...)" are already correct.
        if (OwnershipFacts.IsUsingDeclared(declarator))
        {
            return;
        }

        if (local.Locations.Length == 0)
        {
            return;
        }

        tracker.AddCandidate(
            local,
            local.Locations[0],
            OwnershipFacts.GetOwningFunction(declarator),
            local.Name,
            local.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
    }

    private static void OnLocalReference(
        OperationAnalysisContext context,
        LocalLifetimeTracker tracker,
        INamedTypeSymbol? miniObject,
        INamedTypeSymbol? boxed)
    {
        var reference = (ILocalReferenceOperation)context.Operation;

        if (!OwnershipFacts.DerivesFromAny(reference.Local.Type, miniObject, boxed))
        {
            return;
        }

        tracker.AddUse(
            reference.Local,
            OwnershipFacts.GetOwningFunction(reference),
            OwnershipFacts.Classify(reference));
    }
}
