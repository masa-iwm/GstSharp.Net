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

        TypeMap types = new(repository, classifier, names, diagnostics);
        SkipRules skipRules = new(overlays);
        EmissionCensus census = new();
        EnumEmitter enumEmitter = new(names, overlays, diagnostics);
        Dictionary<string, List<string>> inherited = new(StringComparer.Ordinal);

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
                    inherited),
                module,
                ns));
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
            shared.Diagnostics);

        SurfaceBuilder surfaces = new(planner, shared.Names, shared.Types, shared.Census, shared.Diagnostics);
        List<RegistryEntry> registry = [];
        RecordEmitter recordEmitter = new(
            shared.Repository,
            shared.Classifier,
            shared.Names,
            shared.Types,
            shared.Overlays,
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

        InterfaceEmitter interfaceEmitter = new(shared.Names, surfaces, shared.Overlays, shared.Census);
        CallbackEmitter callbackEmitter = new(planner, shared.Census);
        RegistryEmitter registryEmitter = new();

        List<GeneratedFile> files = [];
        if (shared.Enums.Emit(module, ns) is { } enumFile)
        {
            files.Add(enumFile);
        }

        files.AddRange(recordEmitter.Emit(module, ns));
        files.AddRange(classEmitter.Emit(module, ns));
        files.AddRange(interfaceEmitter.Emit(module, ns));
        files.AddRange(classEmitter.EmitEnumFunctions(module, ns));

        if (classEmitter.EmitGlobal(module, ns) is { } globalFile)
        {
            files.Add(globalFile);
        }

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

        files.Add(registryEmitter.Emit(module, ns, registry));
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
        Dictionary<string, List<string>> Inherited);
}
