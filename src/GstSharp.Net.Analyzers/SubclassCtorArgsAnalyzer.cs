using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gst.Analyzers;

/// <summary>
/// GST0005: reports an implementation of
/// <c>IManagedSubclass&lt;TSelf&gt;.CreateWrapper</c> that never looks at the
/// <c>SubclassCtorArgs</c> it was handed.
/// </summary>
/// <remarks>
/// <para>
/// The factory exists to adopt an instance GStreamer created: the arguments
/// carry the handle and how ownership is transferred, and the wrapper only
/// takes charge of that instance when the constructor is reached through them.
/// A body that ignores the parameter builds a wrapper around something else —
/// a fresh instance, or nothing at all — so the fabrication either fails or
/// hands out a wrapper of the wrong handle.
/// </para>
/// <para>
/// The rule is deliberately syntactic: any reference to the parameter, whether
/// it is passed to the constructor, forwarded to a helper or only read from,
/// silences it. Tracking what the body then does with the value is left to the
/// reader.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubclassCtorArgsAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The identifier of the "arguments are ignored" rule.</summary>
    public const string UnusedCtorArgsId = "GST0005";

    private static readonly DiagnosticDescriptor UnusedCtorArgsRule = new(
        UnusedCtorArgsId,
        title: "CreateWrapper ignores its SubclassCtorArgs",
        messageFormat:
            "'{0}.{1}' never uses '{2}'; a wrapper built without the arguments does not adopt the "
            + "instance GStreamer created",
        category: OwnershipFacts.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "The factory of IManagedSubclass<TSelf> is called with the instance native code just created. "
            + "Only a wrapper constructed from those arguments adopts that instance; one built any other "
            + "way wraps a different handle, or none.",
        helpLinkUri: OwnershipFacts.HelpLink(UnusedCtorArgsId));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(UnusedCtorArgsRule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? managedSubclass = context.Compilation.GetTypeByMetadataName(
            OwnershipFacts.ManagedSubclassMetadataName);
        INamedTypeSymbol? ctorArgs = context.Compilation.GetTypeByMetadataName(
            OwnershipFacts.SubclassCtorArgsMetadataName);

        if (managedSubclass is null || ctorArgs is null)
        {
            // The compilation does not use the GstSharp.Net subclassing surface.
            return;
        }

        context.RegisterOperationBlockAction(
            blockContext => OnOperationBlock(blockContext, managedSubclass, ctorArgs));
    }

    private static void OnOperationBlock(
        OperationBlockAnalysisContext context,
        INamedTypeSymbol managedSubclass,
        INamedTypeSymbol ctorArgs)
    {
        // The name is not part of the filter: an explicit implementation carries
        // the qualified name of the interface member instead.
        if (context.OwningSymbol is not IMethodSymbol { IsStatic: true, Parameters.Length: 1 } method)
        {
            return;
        }

        IParameterSymbol args = method.Parameters[0];
        if (!SymbolEqualityComparer.Default.Equals(args.Type, ctorArgs)
            || !IsFactoryImplementation(method, managedSubclass))
        {
            return;
        }

        foreach (IOperation block in context.OperationBlocks)
        {
            if (ReferencesParameter(block, args))
            {
                return;
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(
            UnusedCtorArgsRule,
            method.Locations.Length > 0 ? method.Locations[0] : Location.None,
            method.ContainingType?.Name,
            method.Name,
            args.Name));
    }

    /// <summary>
    /// Tests whether <paramref name="method"/> is what the containing type
    /// contributes for the factory of <c>IManagedSubclass&lt;TSelf&gt;</c>.
    /// </summary>
    /// <param name="method">The candidate factory.</param>
    /// <param name="managedSubclass">The unbound interface definition.</param>
    /// <returns>
    /// <see langword="true"/> when the interface is implemented by the
    /// containing type and this method is the implementation of its factory,
    /// whether it was written implicitly or as an explicit implementation.
    /// </returns>
    private static bool IsFactoryImplementation(IMethodSymbol method, INamedTypeSymbol managedSubclass)
    {
        INamedTypeSymbol? containing = method.ContainingType;
        if (containing is null)
        {
            return false;
        }

        foreach (INamedTypeSymbol candidate in containing.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, managedSubclass))
            {
                continue;
            }

            foreach (ISymbol member in candidate.GetMembers(OwnershipFacts.CreateWrapperName))
            {
                ISymbol? implementation = containing.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(implementation, method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Walks an operation tree looking for a reference to a parameter.
    /// </summary>
    /// <param name="operation">The root of the tree.</param>
    /// <param name="parameter">The parameter to look for.</param>
    /// <returns><see langword="true"/> when the tree reads the parameter.</returns>
    private static bool ReferencesParameter(IOperation operation, IParameterSymbol parameter)
    {
        if (operation is IParameterReferenceOperation reference
            && SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
        {
            return true;
        }

        foreach (IOperation child in operation.ChildOperations)
        {
            if (ReferencesParameter(child, parameter))
            {
                return true;
            }
        }

        return false;
    }
}
