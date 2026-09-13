using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Gst;

/// <summary>
/// The run of <c>gst-stats-1.0</c>: one log is read, folded into the tables of
/// gst-stats.c and printed as the report the C tool prints.
/// </summary>
internal static class StatsRunner
{
    /// <summary>The log was read.</summary>
    private const int ExitNoError = 0;

    /// <summary>The command line was wrong, or the log could not be read.</summary>
    private const int ExitError = 1;

    /// <summary>
    /// Reads the command line, initialises GStreamer and prints the report.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 1 when the command line named no file or
    /// more than one, and 0 otherwise -- with the two additions this port makes,
    /// a file that cannot be read and <c>--fail-if-empty</c>.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The tool turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        Options options;

        try
        {
            options = Options.Parse(arguments);
        }
        catch (OptionException error)
        {
            // The wording of the option parser of GLib, which is what the C tool
            // prints here, on standard output.
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        if (options.Help)
        {
            Console.WriteLine(Options.Usage);
            return ExitNoError;
        }

        try
        {
            // The --gst-* arguments travel with the options and reach
            // gst_init_check from here.
            GstSharp.Initialize(options.Native);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        // gst_tools_print_version() prints and leaves with 0, in this place.
        if (options.Version)
        {
            PrintVersion();
            return ExitNoError;
        }

        if (options.Files.Count == 0)
        {
            Console.WriteLine($"Please give one filename to {Options.ProgramName}");
            Console.WriteLine();
            return ExitError;
        }

        if (options.Files.Count > 1)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Please give exactly one filename to {Options.ProgramName} ({options.Files.Count} given)."));
            Console.WriteLine();
            return ExitError;
        }

        try
        {
            return Analyze(options);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{Options.ProgramName}: {error}");
            return ExitError;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Prints the version banner of <c>gst_tools_print_version</c>, without the
    /// origin of the package, which is a compile time constant of the C tool.
    /// </summary>
    private static void PrintVersion()
    {
        Gst.Version version = GstSharp.NativeVersion;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Options.ProgramName} version {version.Major}.{version.Minor}.{version.Micro}"));
        Console.WriteLine(Gst.Global.VersionString());
    }

    /// <summary>
    /// Reads the one log the command line named and prints the report.
    /// </summary>
    /// <param name="options">The command line of the process.</param>
    /// <returns>The exit code.</returns>
    private static int Analyze(Options options)
    {
        string filename = options.Files[0];
        StatsCollector collector;

        try
        {
            collector = new StatsCollector(options.TracerRegexp);
        }
        catch (ArgumentException error)
        {
            // g_regex_new fails the same way, and the C tool then prints the
            // empty report; this says what is wrong with the expression and
            // leaves with 1.
            Console.Error.WriteLine(
                $"{Options.ProgramName}: could not compile the tracer regular expression: {error.Message}");
            return ExitError;
        }

        try
        {
            collector.Collect(filename);
        }
        catch (Exception error) when (error is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or ArgumentException)
        {
            // The C tool ignores a failed fopen and prints the empty report.
            Console.Error.WriteLine($"{Options.ProgramName}: could not read {filename}: {error.Message}");
            return ExitError;
        }

        // The report is written through one writer that ends every line with a
        // single newline, which is what the C tool writes on every platform and
        // what the byte for byte comparison against it needs.
        using (StreamWriter writer = new(Console.OpenStandardOutput(), new UTF8Encoding(false)))
        {
            writer.NewLine = "\n";
            new StatsPrinter(collector, writer).Print();
            writer.Flush();
        }

        int skipped = collector.Foreign + collector.Unknown + collector.Unparsable;

        if (skipped > 0)
        {
            // GST_WARNING in the C tool, which is silent unless the reader turned
            // GStreamer debugging on. Standard error keeps the report diffable.
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Options.ProgramName}: skipped {collector.Foreign} foreign, {collector.Unknown} unknown, {collector.Unparsable} unparsable log entries"));
        }

        return collector.Handled == 0 && options.FailIfEmpty ? ExitError : ExitNoError;
    }
}
