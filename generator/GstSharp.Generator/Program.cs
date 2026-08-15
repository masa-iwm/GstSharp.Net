using System.Globalization;

namespace GstSharp.Generator;

/// <summary>
/// Command line entry point of the binding generator.
/// </summary>
internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsage = 1;
    private const int ExitNotImplemented = 2;

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

        // The pipeline (GirParsing -> Semantic -> Planning -> Emit) is added in M1.
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"'{options.Verb}' is not implemented yet (gir dir: '{options.GirDirectory}', out dir: '{options.OutputDirectory}')."));
        return ExitNotImplemented;
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
