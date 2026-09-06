using System.Diagnostics.CodeAnalysis;

namespace GstSharp.Generator;

/// <summary>
/// Verbs understood by the generator command line.
/// </summary>
internal enum GeneratorVerb
{
    /// <summary>Emit the bindings into the output directory.</summary>
    Generate,

    /// <summary>Regenerate and fail when the result differs from the committed tree.</summary>
    Verify,
}

/// <summary>
/// Parsed command line arguments.
/// </summary>
internal sealed class GeneratorOptions
{
    internal const string DefaultGirDirectory = "girs";
    internal const string DefaultOutputDirectory = "src";

    private GeneratorOptions(
        GeneratorVerb verb,
        string girDirectory,
        string outputDirectory,
        string reportDirectory)
    {
        Verb = verb;
        GirDirectory = girDirectory;
        OutputDirectory = outputDirectory;
        ReportDirectory = reportDirectory;
    }

    /// <summary>Gets the requested verb.</summary>
    internal GeneratorVerb Verb { get; }

    /// <summary>Gets the directory that holds <c>reference/</c> and <c>overlays/</c>.</summary>
    internal string GirDirectory { get; }

    /// <summary>Gets the directory that holds the binding projects.</summary>
    internal string OutputDirectory { get; }

    /// <summary>Gets the directory the skip report is written to and read from.</summary>
    /// <remarks>
    /// It defaults to <see cref="GirDirectory"/>, where the committed report
    /// lives. A run into a scratch tree passes it beside <c>--out-dir</c> so
    /// that the dry run leaves the committed report alone.
    /// </remarks>
    internal string ReportDirectory { get; }

    /// <summary>
    /// Parses <paramref name="args"/> into a <see cref="GeneratorOptions"/> instance.
    /// </summary>
    internal static bool TryParse(
        string[] args,
        [NotNullWhen(true)] out GeneratorOptions? options,
        [NotNullWhen(false)] out string? error)
    {
        options = null;
        error = null;

        GeneratorVerb verb;
        switch (args[0])
        {
            case "generate":
                verb = GeneratorVerb.Generate;
                break;
            case "verify":
                verb = GeneratorVerb.Verify;
                break;
            default:
                error = $"Unknown verb '{args[0]}'.";
                return false;
        }

        string girDirectory = DefaultGirDirectory;
        string outputDirectory = DefaultOutputDirectory;

        // Left unset until the loop is over: the default is the gir directory
        // the run ends up with, not the one it started with.
        string? reportDirectory = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--gir-dir":
                    if (!TryTakeValue(args, ref i, out girDirectory, out error))
                    {
                        return false;
                    }

                    break;
                case "--out-dir":
                    if (!TryTakeValue(args, ref i, out outputDirectory, out error))
                    {
                        return false;
                    }

                    break;
                case "--report-dir":
                    if (!TryTakeValue(args, ref i, out string report, out error))
                    {
                        return false;
                    }

                    reportDirectory = report;
                    break;
                default:
                    error = $"Unknown option '{args[i]}'.";
                    return false;
            }
        }

        options = new GeneratorOptions(verb, girDirectory, outputDirectory, reportDirectory ?? girDirectory);
        return true;
    }

    private static bool TryTakeValue(
        string[] args,
        ref int index,
        out string value,
        [NotNullWhen(false)] out string? error)
    {
        string name = args[index];
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"Option '{name}' requires a value.";
            return false;
        }

        index++;
        value = args[index];
        error = null;
        return true;
    }
}
