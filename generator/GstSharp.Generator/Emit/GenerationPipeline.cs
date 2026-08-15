using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// The result of one generator run.
/// </summary>
/// <param name="Files">The generated files, ordered by relative path.</param>
/// <param name="Diagnostics">Everything the run reported.</param>
internal sealed record GenerationResult(IReadOnlyList<GeneratedFile> Files, IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Runs the whole generator: gir parsing, semantic analysis and emission.
/// </summary>
internal static class GenerationPipeline
{
    /// <summary>Name of the sub directory holding the gir files.</summary>
    internal const string ReferenceDirectoryName = "reference";

    /// <summary>Name of the sub directory holding the overlays.</summary>
    internal const string OverlayDirectoryName = "overlays";

    /// <summary>Parses, analyses and emits every generated module.</summary>
    /// <param name="girDirectory">Directory holding <c>reference/</c> and <c>overlays/</c>.</param>
    /// <param name="emitRecords">
    /// Whether the record types are emitted as well. They are off by default
    /// while the runtime layer they bind against is being written, so that the
    /// committed tree stays enumerations only.
    /// </param>
    /// <returns>The generated files and the diagnostics of the run.</returns>
    internal static GenerationResult Run(string girDirectory, bool emitRecords = false)
    {
        string referenceDirectory = Path.Combine(girDirectory, ReferenceDirectoryName);
        if (!Directory.Exists(referenceDirectory))
        {
            throw new DirectoryNotFoundException(
                FormattableString.Invariant($"The gir directory {referenceDirectory} does not exist."));
        }

        DiagnosticBag diagnostics = new();
        Repository repository = Repository.Load(referenceDirectory);
        Overlays overlays = Overlays.Load(Path.Combine(girDirectory, OverlayDirectoryName));
        NameMapper names = new(overlays);
        Classifier classifier = new(repository, diagnostics);

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
        EnumEmitter enumEmitter = new(names, overlays, diagnostics);
        RecordEmitter recordEmitter = new(repository, classifier, names, types, overlays, diagnostics);

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

            // M1 emits the Gst module only. Further emitters are appended here
            // and contribute additional files per module.
            if (!IsEmittedModule(module))
            {
                continue;
            }

            GeneratedFile? file = enumEmitter.Emit(module, ns);
            if (file is not null)
            {
                files.Add(file);
            }

            if (emitRecords)
            {
                files.AddRange(recordEmitter.Emit(module, ns));
            }
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return new GenerationResult(files, diagnostics.Items);
    }

    /// <summary>
    /// Gets a value indicating whether sources are emitted for the module. The
    /// remaining modules follow once their runtime support exists.
    /// </summary>
    /// <param name="module">The module to test.</param>
    /// <returns><see langword="true"/> when the module is emitted.</returns>
    private static bool IsEmittedModule(ModuleInfo module) =>
        string.Equals(module.GirNamespace, "Gst", StringComparison.Ordinal);
}
