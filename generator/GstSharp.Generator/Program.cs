using System.Globalization;
using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator;

/// <summary>
/// Command line entry point of the binding generator.
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 1;
    private const int ExitDifferences = 1;
    private const int ExitFailed = 2;

    internal static int Main(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage(Console.Out);
            return args.Length == 0 ? ExitUsage : ExitSuccess;
        }

        if (!GeneratorOptions.TryParse(args, out GeneratorOptions? options, out string? error))
        {
            Console.Error.WriteLine(error);
            PrintUsage(Console.Error);
            return ExitUsage;
        }

        try
        {
            GenerationResult result = GenerationPipeline.Run(options.GirDirectory);
            ReportDiagnostics(result);
            if (HasErrors(result))
            {
                return ExitFailed;
            }

            return options.Verb == GeneratorVerb.Generate ? Generate(options, result) : Verify(options, result);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine(exception.Message);
            return ExitFailed;
        }
    }

    private static int Generate(GeneratorOptions options, GenerationResult result)
    {
        foreach (GeneratedFile file in result.Files)
        {
            CodeWriter.WriteFile(ToAbsolutePath(options.OutputDirectory, file.RelativePath), file.Content);
        }

        // A source that used to be generated and is not any more would stay in
        // the tree and keep compiling, which is how a binding that was renamed
        // ends up shipped twice. Writing the run is therefore not enough: the
        // directories it wrote into have to hold exactly what it wrote.
        foreach (string orphan in Orphans(options, result))
        {
            File.Delete(orphan);
            Console.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Deleted the orphan generated file '{Relative(options, orphan)}'."));
        }

        // The listing of what was left out belongs next to the gir files it was
        // derived from, not into the binding projects: it is review
        // documentation rather than source. It is committed, so that a member
        // that stops being generated shows up as a line of its diff.
        CodeWriter.WriteFile(SkipReportPath(options), result.SkipReport);

        Console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Generated {result.Files.Count} file(s) below '{options.OutputDirectory}'."));

        foreach (string line in result.Census.Report())
        {
            Console.Out.WriteLine(line);
        }

        return ExitSuccess;
    }

    private static int Verify(GeneratorOptions options, GenerationResult result)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "GstSharp.Generator", Path.GetRandomFileName());
        List<string> differences = [];
        try
        {
            foreach (GeneratedFile file in result.Files)
            {
                string scratchPath = ToAbsolutePath(scratch, file.RelativePath);
                CodeWriter.WriteFile(scratchPath, file.Content);

                string committedPath = ToAbsolutePath(options.OutputDirectory, file.RelativePath);
                if (!File.Exists(committedPath)
                    || !File.ReadAllBytes(committedPath).AsSpan().SequenceEqual(File.ReadAllBytes(scratchPath)))
                {
                    differences.Add(file.RelativePath);
                }
            }
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        if (!IsUpToDate(SkipReportPath(options), result.SkipReport))
        {
            differences.Add(GenerationPipeline.SkipReportFileName);
        }

        foreach (string orphan in Orphans(options, result))
        {
            differences.Add("orphan generated file: " + Relative(options, orphan));
        }

        if (differences.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{result.Files.Count} generated file(s) are up to date."));
            return ExitSuccess;
        }

        Console.Error.WriteLine("The committed sources differ from the generator output:");
        foreach (string difference in differences)
        {
            Console.Error.WriteLine("  " + difference);
        }

        return ExitDifferences;
    }

    private static string ToAbsolutePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Spells a written path the way the generated files are named.</summary>
    /// <param name="options">The options of the run.</param>
    /// <param name="path">An absolute path below the output directory.</param>
    /// <returns>The path relative to the output directory, with forward slashes.</returns>
    private static string Relative(GeneratorOptions options, string path) =>
        Path.GetRelativePath(options.OutputDirectory, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>
    /// Finds the committed sources of a generated directory that this run did
    /// not produce.
    /// </summary>
    /// <param name="options">The options of the run.</param>
    /// <param name="result">What the run produced.</param>
    /// <returns>The absolute paths of the orphans, ordered.</returns>
    /// <remarks>
    /// Only the directories the run emitted into are looked at, so a module
    /// that is not generated at all, and every hand written folder beside the
    /// generated ones, are left alone. The comparison ignores case, because a
    /// file whose name differs from the emitted one only in case is the same
    /// file on the file systems this runs on, and deleting the source that was
    /// just written would be worse than missing an orphan.
    /// </remarks>
    private static IReadOnlyList<string> Orphans(GeneratorOptions options, GenerationResult result)
    {
        HashSet<string> emitted = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
        foreach (GeneratedFile file in result.Files)
        {
            string path = Path.GetFullPath(ToAbsolutePath(options.OutputDirectory, file.RelativePath));
            emitted.Add(path);
            if (Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                directories.Add(directory);
            }
        }

        List<string> orphans = [];
        foreach (string directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (string path in Directory.EnumerateFiles(directory, "*.cs"))
            {
                if (!emitted.Contains(Path.GetFullPath(path)))
                {
                    orphans.Add(Path.GetFullPath(path));
                }
            }
        }

        orphans.Sort(StringComparer.Ordinal);
        return orphans;
    }

    private static string SkipReportPath(GeneratorOptions options) =>
        Path.Combine(options.GirDirectory, GenerationPipeline.SkipReportFileName);

    /// <summary>Tests whether a file already holds exactly the given text.</summary>
    /// <param name="path">The file to compare.</param>
    /// <param name="content">The expected content.</param>
    /// <returns><see langword="true"/> when the bytes agree.</returns>
    private static bool IsUpToDate(string path, string content)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string scratch = Path.Combine(Path.GetTempPath(), "GstSharp.Generator", Path.GetRandomFileName());
        try
        {
            CodeWriter.WriteFile(scratch, content);
            return File.ReadAllBytes(path).AsSpan().SequenceEqual(File.ReadAllBytes(scratch));
        }
        finally
        {
            if (File.Exists(scratch))
            {
                File.Delete(scratch);
            }
        }
    }

    private static bool HasErrors(GenerationResult result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReportDiagnostics(GenerationResult result)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics)
        {
            Console.Error.WriteLine(diagnostic.ToString());
        }
    }

    private static bool IsHelp(string arg) =>
        arg is "-h" or "--help" or "-?" or "help";

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: GstSharp.Generator <verb> [options]");
        writer.WriteLine();
        writer.WriteLine("Verbs:");
        writer.WriteLine("  generate   Emit C# bindings from the reference gir files.");
        writer.WriteLine("  verify     Regenerate into a temporary tree and fail when it differs.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine($"  --gir-dir <path>   Directory holding reference/ and overlays/ (default: '{GeneratorOptions.DefaultGirDirectory}').");
        writer.WriteLine($"  --out-dir <path>   Directory holding the binding projects (default: '{GeneratorOptions.DefaultOutputDirectory}').");
        writer.WriteLine("  -h, --help         Show this help text.");
    }
}
