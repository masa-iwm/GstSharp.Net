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
