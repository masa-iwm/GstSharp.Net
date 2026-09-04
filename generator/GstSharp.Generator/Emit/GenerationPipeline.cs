using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// The result of one generator run.
/// </summary>
/// <param name="Files">The generated files, ordered by relative path.</param>
/// <param name="Diagnostics">Everything the run reported.</param>
/// <param name="Census">What the run emitted and what it left out.</param>
internal sealed record GenerationResult(
    IReadOnlyList<GeneratedFile> Files,
    IReadOnlyList<Diagnostic> Diagnostics,
    EmissionCensus Census)
{
    /// <summary>Gets the Markdown listing of every symbol the run left out.</summary>
    internal string SkipReport => Census.SkipReport();
}

/// <summary>
/// Runs the whole generator: gir parsing, semantic analysis and emission.
/// </summary>
internal static class GenerationPipeline
{
    /// <summary>Name of the sub directory holding the gir files.</summary>
    internal const string ReferenceDirectoryName = "reference";

    /// <summary>Name of the sub directory holding the overlays.</summary>
    internal const string OverlayDirectoryName = "overlays";

    /// <summary>
    /// Name of the listing of the symbols a run left out, written next to the
    /// gir files it was derived from.
    /// </summary>
    internal const string SkipReportFileName = "skip-report.md";

    /// <summary>Parses, analyses and emits every generated module.</summary>
    /// <param name="girDirectory">Directory holding <c>reference/</c> and <c>overlays/</c>.</param>
    /// <returns>The generated files and the diagnostics of the run.</returns>
    internal static GenerationResult Run(string girDirectory)
    {
        string referenceDirectory = Path.Combine(girDirectory, ReferenceDirectoryName);
        if (!Directory.Exists(referenceDirectory))
        {
            throw new DirectoryNotFoundException(
                FormattableString.Invariant($"The gir directory {referenceDirectory} does not exist."));
        }

        Repository repository = Repository.Load(referenceDirectory);
        Overlays overlays = Overlays.Load(Path.Combine(girDirectory, OverlayDirectoryName));
        return Execute(repository, overlays);
    }

    /// <summary>
    /// Emits every generated module of an already loaded repository. The tests
    /// use this with a repository that was parsed from a string.
    /// </summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="overlays">The corrections to apply.</param>
    /// <returns>The generated files and the diagnostics of the run.</returns>
    internal static GenerationResult Execute(Repository repository, Overlays overlays)
    {
        DiagnosticBag diagnostics = new();
        NameMapper names = new(overlays, diagnostics);
        Classifier classifier = new(repository, overlays, diagnostics);

        // Classify everything once so that the diagnostics of a run do not
        // depend on which emitters happen to touch which type.
        foreach (GirNamespace ns in repository.Namespaces)
        {
            foreach (GirRecord record in ns.Records)
            {
                classifier.Classify(record);
            }
        }

        // The class structs are paired with their virtual methods before any
        // emitter plans a callable: the pairing is what stamps a virtual method
        // with the key an annotation correction addresses it by, so a model
        // built later would leave those corrections unapplied and, worse,
        // reported stale.
        Dictionary<string, HashSet<string>> emittedVirtuals = new(StringComparer.Ordinal);
        SubclassModel subclasses = SubclassModel.Build(repository, overlays, diagnostics);
        ReportStaleVirtualKeys(subclasses, overlays, diagnostics);

        TypeMap types = new(repository, classifier, names, diagnostics);
        SkipRules skipRules = new(overlays);
        EmissionCensus census = new(overlays);
        EnumEmitter enumEmitter = new(names, overlays, diagnostics);
        Dictionary<string, List<string>> inherited = new(StringComparer.Ordinal);

        // The array corrections are consumed by a planner that is built per
        // module, and reported once for the whole run, so the record of what
        // was applied belongs here. The overlays themselves stay immutable:
        // Overlays.Empty is shared by every fixture of the test suite.
        HashSet<string> consumedArrayOverrides = new(StringComparer.Ordinal);
        HashSet<string> consumedAnnotationOverrides = new(StringComparer.Ordinal);

        List<GeneratedFile> files = [];
        foreach (ModuleInfo module in ModuleMap.Modules)
        {
            if (!module.IsGenerated)
            {
                continue;
            }

            GirNamespace? ns = repository.FindNamespace(module.GirNamespace);
            if (ns is null)
            {
                diagnostics.Warn(
                    "GEN0005",
                    $"No gir file was found for the '{module.GirNamespace}' namespace; the module is skipped.");
                continue;
            }

            files.AddRange(EmitModule(
                new ModuleEmitters(
                    repository,
                    classifier,
                    names,
                    types,
                    overlays,
                    skipRules,
                    census,
                    diagnostics,
                    enumEmitter,
                    inherited,
                    consumedArrayOverrides,
                    consumedAnnotationOverrides,
                    subclasses,
                    emittedVirtuals),
                module,
                ns));
        }

        // An array correction that matched nothing is a statement about a gir
        // that has moved on. Reporting it is what keeps the overlays from
        // accumulating entries that describe a symbol which no longer exists,
        // or a parameter that is no array.
        List<string> stale = [];
        foreach (string key in overlays.ArrayOverrideKeys)
        {
            if (!consumedArrayOverrides.Contains(key))
            {
                stale.Add(key);
            }
        }

        stale.Sort(StringComparer.Ordinal);
        foreach (string key in stale)
        {
            diagnostics.Warn(
                "GEN0020",
                $"The array override '{key}' matched no array parameter or return value; the entry is stale.");
        }

        // An annotation correction is read wherever the planner asks for one,
        // so a key that was never read matched no callable, no parameter and
        // no signal argument of this run. Silently ignoring it is what lets
        // the overlays describe a gir that has moved on, which the corrections
        // of an array are already protected from.
        List<string> staleAnnotations = [];
        foreach (string key in overlays.AnnotationOverrideKeys)
        {
            if (!consumedAnnotationOverrides.Contains(key))
            {
                staleAnnotations.Add(key);
            }
        }

        staleAnnotations.Sort(StringComparer.Ordinal);
        foreach (string key in staleAnnotations)
        {
            diagnostics.Warn(
                "GEN0024",
                $"The annotation override '{key}' matched no callable, parameter or signal argument; "
                + "the entry is stale.");
        }

        // A field skip the run never matched names a field that no longer
        // exists, one of a record that is not emitted, or a misspelling; an
        // entry that states neither an exposing member nor that the field is
        // hand written says nothing about it at all, and one that states both
        // says two different things about who answers it. Any of the three
        // would leave the ledger quiet about a field on the strength of a claim
        // nothing checks, which is what the report exists to prevent.
        List<string> staleFields = [];
        foreach (string key in overlays.FieldSkipKeys)
        {
            if (!census.FieldSkipKeys.Contains(key) || overlays.GetFieldSkip(key) is not { IsStated: true })
            {
                staleFields.Add(key);
            }
        }

        staleFields.Sort(StringComparer.Ordinal);
        foreach (string key in staleFields)
        {
            diagnostics.Warn(
                "GEN0025",
                $"The field skip '{key}' matched no field of an emitted record, or states neither "
                + "'exposedBy' nor 'handBound', or states both; the entry is stale.");
        }

        // A field annotation the run never applied names a field that no longer
        // exists, one of a record that is not emitted, or a misspelling; an
        // entry that states nothing, or that states the default the emitters
        // already use, corrects nothing. Either way the overlays would carry a
        // claim about the C implementation that nothing acts on, which reads as
        // a decision that was taken when none was.
        List<(string Key, string Fault)> staleAnnotatedFields = [];
        foreach (string key in overlays.FieldAnnotationKeys)
        {
            // The shape of the entry is read first, so that one that cannot be
            // acted on at all is reported for what is wrong with it rather than
            // for the run never having applied it, which is only the
            // consequence.
            if (overlays.GetFieldAnnotation(key)?.ShapeFault is { } fault)
            {
                staleAnnotatedFields.Add((key, fault));
            }
            else if (census.RedundantFieldNames.Contains(key))
            {
                staleAnnotatedFields.Add((key, "names the member the field derives anyway"));
            }
            else if (!census.FieldAnnotationKeys.Contains(key))
            {
                staleAnnotatedFields.Add((key, "was applied to no field of an emitted record"));
            }
        }

        staleAnnotatedFields.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        foreach ((string key, string fault) in staleAnnotatedFields)
        {
            diagnostics.Warn("GEN0026", $"The field annotation '{key}' {fault}; the entry is stale.");
        }

        // A hand bound entry the run never saw skipped names a symbol that is
        // generated after all, or one that no longer exists, or a misspelling.
        // Any of the three makes the ledger claim something about the bindings
        // that is not true, which is exactly what the annotation exists to
        // prevent.
        List<string> unseen = [];
        foreach (string identifier in overlays.HandBoundIdentifiers)
        {
            if (!census.HandBoundSymbols.Contains(identifier))
            {
                unseen.Add(identifier);
            }
        }

        unseen.Sort(StringComparer.Ordinal);
        foreach (string identifier in unseen)
        {
            diagnostics.Warn(
                "GEN0023",
                $"The hand bound entry '{identifier}' was not skipped by this run; the entry is stale.");
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return new GenerationResult(files, diagnostics.Items, census);
    }

    /// <summary>Emits one module.</summary>
    /// <param name="shared">The analysis that every module shares.</param>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">Its gir namespace.</param>
    /// <returns>The files of the module.</returns>
    private static IReadOnlyList<GeneratedFile> EmitModule(ModuleEmitters shared, ModuleInfo module, GirNamespace ns)
    {
        // The planner is built per module, because it collects the callbacks
        // that the members of this module hand to native code.
        MarshalPlanner planner = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.SkipRules,
            shared.Diagnostics,
            shared.ConsumedArrayOverrides,
            shared.ConsumedAnnotationOverrides);

        SurfaceBuilder surfaces = new(
            planner,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.Census,
            shared.Diagnostics);
        List<RegistryEntry> registry = [];
        RecordEmitter recordEmitter = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            shared.Types,
            shared.Overlays,
            shared.SkipRules,
            shared.Diagnostics,
            surfaces,
            shared.Census,
            registry);

        ClassEmitter classEmitter = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            surfaces,
            shared.Overlays,
            shared.Census,
            shared.Diagnostics,
            registry,
            shared.Inherited);

        List<InterfaceRegistryEntry> interfaceRegistry = [];
        InterfaceEmitter interfaceEmitter = new(
            shared.Names,
            surfaces,
            shared.Overlays,
            shared.Census,
            interfaceRegistry);

        CallbackEmitter callbackEmitter = new(planner, shared.Census);
        RegistryEmitter registryEmitter = new();

        List<GeneratedFile> files = [];
        if (shared.Enums.Emit(module, ns) is { } enumFile)
        {
            files.Add(enumFile);
        }

        ClassStructEmitter classStructEmitter = new(shared.Repository, shared.Census, shared.Diagnostics);
        files.AddRange(classStructEmitter.Emit(module, ns, shared.Subclasses));

        VfuncEmitter vfuncEmitter = new(
            planner,
            shared.Census,
            shared.Overlays,
            shared.Diagnostics,
            shared.EmittedVirtuals);
        files.AddRange(vfuncEmitter.Emit(module, ns, shared.Subclasses));
        files.AddRange(recordEmitter.Emit(module, ns));
        files.AddRange(classEmitter.Emit(module, ns));
        files.AddRange(interfaceEmitter.Emit(module, ns));
        files.AddRange(classEmitter.EmitEnumFunctions(module, ns));

        if (classEmitter.EmitGlobal(module, ns) is { } globalFile)
        {
            files.Add(globalFile);
        }

        // A callback type whose only consumers are hand bound is reached by
        // nothing the emitters planned, and would leave the module although
        // the bindings do hand it out. The ledger names those consumers, so
        // they claim their callback types here.
        planner.PlanHandBoundCallbacks(module, ns);

        // Every emitter has run, so the set of reachable callbacks is complete.
        if (callbackEmitter.Emit(module, ns) is { } callbackFile)
        {
            files.Add(callbackFile);
        }

        // The events of the module share one holder of the connected handlers,
        // which is only worth emitting when the module has events at all.
        if (shared.Census.EmittedCount(module.GirNamespace, "signal") > 0)
        {
            files.Add(SignalEmitter.EmitConnections(module, ns));
        }

        files.Add(registryEmitter.Emit(module, ns, registry, interfaceRegistry));
        return files;
    }

    /// <summary>
    /// The analysis and the emitters that every module of a run shares.
    /// </summary>
    /// <param name="Repository">The loaded gir repository.</param>
    /// <param name="Classifier">The type classifier.</param>
    /// <param name="Names">The name mapper.</param>
    /// <param name="Types">The type map.</param>
    /// <param name="Overlays">The overlay configuration.</param>
    /// <param name="SkipRules">The skip rules.</param>
    /// <param name="Census">The census of the run.</param>
    /// <param name="Diagnostics">The diagnostic sink.</param>
    /// <param name="Enums">The enumeration emitter.</param>
    /// <param name="Inherited">
    /// The members of every class the run has emitted so far, keyed by
    /// qualified gir name and shared by every module.
    /// </param>
    /// <param name="ConsumedArrayOverrides">
    /// The keys of the array corrections the run has applied, shared by every
    /// module so that the stale ones can be reported once.
    /// </param>
    /// <param name="ConsumedAnnotationOverrides">
    /// The keys of the annotation corrections the run has read, shared for the
    /// same reason.
    /// </param>
    private sealed record ModuleEmitters(
        Repository Repository,
        Classifier Classifier,
        NameMapper Names,
        TypeMap Types,
        Overlays Overlays,
        SkipRules SkipRules,
        EmissionCensus Census,
        DiagnosticBag Diagnostics,
        EnumEmitter Enums,
        Dictionary<string, List<string>> Inherited,
        HashSet<string> ConsumedArrayOverrides,
        HashSet<string> ConsumedAnnotationOverrides,
        SubclassModel Subclasses,
        Dictionary<string, HashSet<string>> EmittedVirtuals);

    /// <summary>
    /// Reports the overlay entries about virtual methods that name no slot of
    /// a subclassable class.
    /// </summary>
    /// <param name="subclasses">The class structs of the run.</param>
    /// <param name="overlays">The corrections to check.</param>
    /// <param name="diagnostics">Where a stale entry is reported.</param>
    /// <remarks>
    /// A misspelled key is worse here than elsewhere: a <c>vfuncDefaults</c>
    /// entry that lands nowhere silently turns a slot with a documented default
    /// into one whose chain-up throws, and a <c>skipVirtuals</c> entry that
    /// lands nowhere silently emits a slot the ledger claims was left out.
    /// </remarks>
    private static void ReportStaleVirtualKeys(
        SubclassModel subclasses,
        Overlays overlays,
        DiagnosticBag diagnostics)
    {
        Report(
            "GEN0029",
            overlays.SkippedVirtualKeys,
            subclasses.VirtualMethodKeys,
            "The skipped virtual method '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0030",
            overlays.VfuncDefaultKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method default '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0037",
            overlays.VfuncDocNoteKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method note '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0036",
            overlays.VfuncNonNullReturnKeys,
            subclasses.VirtualMethodKeys,
            "The non null return '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0038",
            overlays.VfuncFailureValueKeys,
            subclasses.VirtualMethodKeys,
            "The virtual method failure value '{0}' names no slot of a subclassable class; the entry is stale.");
        Report(
            "GEN0039",
            overlays.VfuncSpanKeys,
            subclasses.VirtualMethodParameterKeys,
            "The read only block '{0}' names no parameter of a slot of a subclassable class; "
            + "the entry is stale.");
        Report(
            "GEN0031",
            overlays.VfuncIdentityBufferKeys,
            subclasses.VirtualMethodParameterKeys,
            "The identity buffer '{0}' names no parameter of a slot of a subclassable class; "
            + "the entry is stale.");

        void Report(string code, IReadOnlyCollection<string> keys, IReadOnlySet<string> known, string message)
        {
            List<string> stale = [];
            foreach (string key in keys)
            {
                if (!known.Contains(key))
                {
                    stale.Add(key);
                }
            }

            stale.Sort(StringComparer.Ordinal);
            foreach (string key in stale)
            {
                diagnostics.Warn(code, string.Format(System.Globalization.CultureInfo.InvariantCulture, message, key));
            }
        }
    }
}
