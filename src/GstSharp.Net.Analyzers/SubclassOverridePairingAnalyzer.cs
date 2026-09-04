using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Gst.Analyzers;

/// <summary>
/// GST0003 and GST0004: reports a subclass whose <c>On&lt;X&gt;</c> overrides and
/// whose <c>&lt;X&gt;Override</c> slot declarations do not come in pairs.
/// </summary>
/// <remarks>
/// <para>
/// Declaring a slot in <c>DefineSubclass</c> and overriding the matching
/// <c>On&lt;X&gt;</c> method are two statements of the same fact. Only declared
/// slots are patched, so an override without a declaration is never called;
/// a declaration without an override costs a managed transition that chains up,
/// which changes what the base class does on the slots GStreamer reads for
/// presence — see <c>docs/subclassing.md</c>.
/// </para>
/// <para>
/// The pairing is matched on the name stem alone, which is the only link the
/// compiler can see: a <c>VfuncOverride</c> carries its class and offset in
/// values that resolve at run time. The rule therefore only looks at an
/// <c>overrides</c> argument written out at the call site as an array or a
/// collection expression of plain property references. Anything else — a local,
/// a helper call, a spread — silences both directions rather than guessing.
/// </para>
/// <para>
/// The overrides are looked for on the class holding the registration call and
/// on every class between it and the class the call was resolved on, so an
/// override that sits on an intermediate managed class still pairs.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SubclassOverridePairingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The identifier of the "overridden but not declared" rule.</summary>
    public const string OverrideWithoutDeclarationId = "GST0003";

    /// <summary>The identifier of the "declared but not overridden" rule.</summary>
    public const string DeclarationWithoutOverrideId = "GST0004";

    /// <summary>The name of the registration method the rule keys on.</summary>
    private const string DefineSubclassName = "DefineSubclass";

    /// <summary>The suffix every slot declaration property carries.</summary>
    private const string OverrideSuffix = "Override";

    /// <summary>The prefix every overridable vfunc method carries.</summary>
    private const string OnPrefix = "On";

    private static readonly DiagnosticDescriptor OverrideWithoutDeclarationRule = new(
        OverrideWithoutDeclarationId,
        title: "Overridden vfunc is not declared in DefineSubclass",
        messageFormat:
            "'{0}' overrides '{1}' but its DefineSubclass call does not declare '{2}'; "
            + "GStreamer never calls the override",
        category: OwnershipFacts.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "Only the slots a DefineSubclass call declares are patched into the class structure. An On<X> "
            + "override that no <X>Override declares is dead code: GStreamer keeps calling the implementation "
            + "of the base class.",
        helpLinkUri: OwnershipFacts.HelpLink(OverrideWithoutDeclarationId));

    private static readonly DiagnosticDescriptor DeclarationWithoutOverrideRule = new(
        DeclarationWithoutOverrideId,
        title: "Declared vfunc slot is not overridden",
        messageFormat:
            "'{0}' declares '{1}' but does not override '{2}'; the declaration only adds a managed "
            + "transition that chains up, and on a presence-sensitive slot it changes what the base class does",
        category: OwnershipFacts.Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description:
            "A declared slot is patched whether or not the subclass overrides the matching On<X> method. "
            + "GStreamer reads the presence of some slots to decide what a class does, so a declaration "
            + "whose implementation only chains up is not always harmless.",
        helpLinkUri: OwnershipFacts.HelpLink(DeclarationWithoutOverrideId));

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(OverrideWithoutDeclarationRule, DeclarationWithoutOverrideRule);

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        INamedTypeSymbol? vfuncOverride = context.Compilation.GetTypeByMetadataName(
            OwnershipFacts.VfuncOverrideMetadataName);

        if (vfuncOverride is null)
        {
            // The compilation does not use the GstSharp.Net subclassing surface.
            return;
        }

        context.RegisterOperationAction(
            operationContext => OnInvocation(operationContext, vfuncOverride),
            OperationKind.Invocation);
    }

    private static void OnInvocation(OperationAnalysisContext context, INamedTypeSymbol vfuncOverride)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol target = invocation.TargetMethod;

        if (target.Name != DefineSubclassName || target.Parameters.Length == 0)
        {
            return;
        }

        IParameterSymbol last = target.Parameters[target.Parameters.Length - 1];
        if (!last.IsParams
            || last.Type is not IArrayTypeSymbol array
            || !SymbolEqualityComparer.Default.Equals(array.ElementType, vfuncOverride))
        {
            return;
        }

        // The slots belong to the class the method was resolved on, and the rule
        // only speaks about a class that is authoring a subclass of it.
        INamedTypeSymbol? subclass = context.ContainingSymbol as INamedTypeSymbol
            ?? context.ContainingSymbol.ContainingType;

        if (subclass is null || !DerivesFrom(subclass, target.ContainingType))
        {
            return;
        }

        if (!TryGetDeclaredSlots(invocation, last, out List<IOperation> elements))
        {
            // The overrides are not written out at the call site.
            return;
        }

        ReportPairing(context, subclass, target.ContainingType, elements);
    }

    /// <summary>
    /// Tests whether <paramref name="type"/> is a proper derived type of
    /// <paramref name="baseType"/>.
    /// </summary>
    /// <param name="type">The candidate subclass.</param>
    /// <param name="baseType">The class the registration method belongs to.</param>
    /// <returns><see langword="true"/> when the base chain reaches it.</returns>
    private static bool DerivesFrom(INamedTypeSymbol type, INamedTypeSymbol? baseType)
    {
        if (baseType is null)
        {
            return false;
        }

        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Collects the elements of the <c>overrides</c> argument when every one of
    /// them is a plain reference to a slot declaration property.
    /// </summary>
    /// <param name="invocation">The registration call.</param>
    /// <param name="parameter">The <c>params</c> parameter of the call.</param>
    /// <param name="elements">The accepted element operations, in source order.</param>
    /// <returns>
    /// <see langword="false"/> when the argument is not written out at the call
    /// site, which silences both directions of the rule.
    /// </returns>
    private static bool TryGetDeclaredSlots(
        IInvocationOperation invocation,
        IParameterSymbol parameter,
        out List<IOperation> elements)
    {
        elements = new List<IOperation>();

        IOperation? value = null;
        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter))
            {
                value = OwnershipFacts.Unwrap(argument.Value);
                break;
            }
        }

        IEnumerable<IOperation>? candidates;
        switch (value)
        {
            // Both "new VfuncOverride[] { ... }" and the array the compiler
            // synthesizes for an expanded params call.
            case IArrayCreationOperation creation when creation.Initializer is not null:
                candidates = creation.Initializer.ElementValues;
                break;
            case ICollectionExpressionOperation collection:
                candidates = collection.Elements;
                break;
            default:
                return false;
        }

        foreach (IOperation candidate in candidates)
        {
            IOperation element = OwnershipFacts.Unwrap(candidate);
            if (element is not IPropertyReferenceOperation { Instance: null } property
                || StemOf(property.Property.Name, OverrideSuffix, prefix: false) is null)
            {
                return false;
            }

            elements.Add(element);
        }

        return true;
    }

    /// <summary>
    /// Reports every slot of the class that has a declaration without an
    /// override, or an override without a declaration.
    /// </summary>
    /// <param name="context">The analysis context to report through.</param>
    /// <param name="subclass">The class the registration call sits in.</param>
    /// <param name="baseType">The class the registration method belongs to.</param>
    /// <param name="elements">The declared slots, in source order.</param>
    private static void ReportPairing(
        OperationAnalysisContext context,
        INamedTypeSymbol subclass,
        INamedTypeSymbol? baseType,
        List<IOperation> elements)
    {
        var declared = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (IOperation element in elements)
        {
            var property = (IPropertyReferenceOperation)element;
            declared.Add(StemOf(property.Property.Name, OverrideSuffix, prefix: false)!);
        }

        var overridden = new HashSet<string>(System.StringComparer.Ordinal);

        // The merged symbol members, so that a partial declaration in another
        // file is seen, and every managed class between the registering one and
        // the wrapped base, so that an override placed on an intermediate class
        // still pairs. The walk stops at the base itself: its own "On" methods
        // are the virtuals being overridden, not overrides of a subclass.
        for (INamedTypeSymbol? current = subclass;
            current is not null && !SymbolEqualityComparer.Default.Equals(current, baseType);
            current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                if (member is not IMethodSymbol { IsOverride: true } method
                    || StemOf(method.Name, OnPrefix, prefix: true) is not { } stem)
                {
                    continue;
                }

                overridden.Add(stem);

                // An intermediate class can come from another assembly, whose
                // location the diagnostic cannot point at.
                if (!declared.Contains(stem)
                    && method.Locations.Length > 0
                    && method.Locations[0].IsInSource)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        OverrideWithoutDeclarationRule,
                        method.Locations[0],
                        subclass.Name,
                        method.Name,
                        stem + OverrideSuffix));
                }
            }
        }

        foreach (IOperation element in elements)
        {
            var property = (IPropertyReferenceOperation)element;
            string stem = StemOf(property.Property.Name, OverrideSuffix, prefix: false)!;

            if (!overridden.Contains(stem))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DeclarationWithoutOverrideRule,
                    element.Syntax.GetLocation(),
                    subclass.Name,
                    property.Property.Name,
                    OnPrefix + stem));
            }
        }
    }

    /// <summary>
    /// Strips the naming convention off <paramref name="name"/> and returns the
    /// vfunc stem it carries.
    /// </summary>
    /// <param name="name">The member name to look at.</param>
    /// <param name="affix">The prefix or suffix of the convention.</param>
    /// <param name="prefix"><see langword="true"/> to strip a prefix.</param>
    /// <returns>The stem, or <see langword="null"/> when the name does not fit.</returns>
    private static string? StemOf(string name, string affix, bool prefix)
    {
        if (name.Length <= affix.Length)
        {
            return null;
        }

        return prefix
            ? name.StartsWith(affix, System.StringComparison.Ordinal)
                ? name.Substring(affix.Length)
                : null
            : name.EndsWith(affix, System.StringComparison.Ordinal)
                ? name.Substring(0, name.Length - affix.Length)
                : null;
    }
}
