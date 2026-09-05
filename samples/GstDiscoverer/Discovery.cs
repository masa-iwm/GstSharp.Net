using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.GObject;
using Gst.Pbutils;

/// <summary>
/// The run of <c>gst-discoverer-1.0</c>: one synchronous
/// <see cref="Discoverer.DiscoverUri"/> per URI, and the report the C tool
/// writes about what came back.
/// </summary>
internal sealed partial class Discovery
{
    /// <summary>Every URI was looked at.</summary>
    private const int ExitNoError = 0;

    /// <summary>
    /// The command line was wrong, or a URI failed under
    /// <c>--fail-on-error</c>.
    /// </summary>
    private const int ExitError = 1;

    /// <summary>
    /// No URI was given. The C tool calls <c>exit(-1)</c> here, which is what a
    /// shell reports as 255 and what Windows reports as -1; returning the same
    /// number from <c>Main</c> reproduces both.
    /// </summary>
    private const int ExitUsage = -1;

    private readonly Options _options;
    private readonly Discoverer _discoverer;
    private bool _anyFailed;

    /// <summary>Initialises a run.</summary>
    /// <param name="options">The command line of the process.</param>
    /// <param name="discoverer">The discoverer every URI goes through.</param>
    private Discovery(Options options, Discoverer discoverer)
    {
        _options = options;
        _discoverer = discoverer;
    }

    /// <summary>
    /// Reads the command line, initialises GStreamer and discovers every URI.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 1 when the command line was unusable, -1
    /// when it carried no URI at all, and 0 otherwise — including for a URI
    /// that could not be discovered, unless <c>--fail-on-error</c> was given.
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
            // The wording of the option parser of GLib, which is what the C
            // tool prints here, on standard output.
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
            // GstPbutils.Initialize rather than GstSharp.Initialize: it is a
            // call into the Pbutils assembly, so its type registry is filled
            // before the first DiscovererInfo is wrapped. See
            // docs/ownership.md, "The GType registry".
            GstPbutils.Initialize(options.Native);
        }
        catch (Exception error)
        {
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        if (options.Uris.Count == 0)
        {
            // The C tool prints argv[0] here, which is the full path of the
            // executable rather than a program name.
            Console.WriteLine($"usage: {Options.ProgramName} <uris>");
            return ExitUsage;
        }

        if (options.PrintCacheDir)
        {
            // After the usage check and before any discovery, which is where
            // the C tool has it: the option parser took the option out of the
            // command line, so a run that asks for nothing else still has to
            // name a URI to get this far, and none of the URIs is looked at.
            Console.WriteLine();
            Console.WriteLine("GstDiscoverer cache directory:");
            Console.WriteLine();
            Console.WriteLine($"    {Path.Combine(UserDirectories.CacheDir, "gstreamer-1.0", "discoverer")}");
            Console.WriteLine();
            return ExitNoError;
        }

        try
        {
            return Discover(options);
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
    /// Builds the discoverer and runs every URI through it.
    /// </summary>
    /// <param name="options">The command line of the process.</param>
    /// <returns>The exit code.</returns>
    private static int Discover(Options options)
    {
        Discoverer discoverer;

        try
        {
            discoverer = Discoverer.New(ClockTime.FromSeconds(options.TimeoutSeconds));
        }
        catch (GException error)
        {
            Console.WriteLine($"Error initializing: {error.Message}");
            return ExitError;
        }

        // The discoverer is built here and finished with here, and it owns a
        // uridecodebin pipeline of its own, so this is the sanctioned Dispose
        // of a GObject wrapper. The DiscovererInfo objects below are the other
        // case and are deliberately not disposed: they are interned wrappers
        // that the collector takes and the runtime releases, which is what
        // DrainPendingReleases below is for. See docs/ownership.md.
        using (discoverer)
        {
            using (Value cache = Value.New(GType.Boolean))
            {
                cache.SetBoolean(options.UseCache);
                discoverer.SetProperty("use-cache", cache);
            }

            Discovery run = new(options, discoverer);

            foreach (string uri in options.Uris)
            {
                run.ProcessFile(uri);
            }

            return run._anyFailed && options.FailOnError ? ExitError : ExitNoError;
        }
    }

    /// <summary>
    /// Discovers one URI or file, or everything below one directory.
    /// </summary>
    /// <param name="path">The URI, file or directory to look at.</param>
    private void ProcessFile(string path)
    {
        string uri;

        if (Gst.Uri.IsValid(path))
        {
            uri = path;
        }
        else
        {
            // g_dir_open succeeds on a directory, and the C tool then walks its
            // entries and recurses into each one. A Windows path is never a
            // URI here: a protocol has to be at least two characters long, so
            // the C: of a drive letter is not one.
            if (Directory.Exists(path))
            {
                Recurse(path);
                return;
            }

            // gst_filename_to_uri, which is g_filename_to_uri with the two
            // steps the C tool performs around it: a relative path is resolved
            // against the working directory first. It canonicalises the result
            // as well, which the C tool does not, so a path written with "./"
            // or "../" segments comes out shorter here and identical
            // everywhere else.
            string? converted;

            try
            {
                converted = Global.FilenameToUri(path);
            }
            catch (GException error)
            {
                // g_warning in the C tool, which is standard error with a
                // GLib prefix around the same sentence.
                Console.Error.WriteLine($"Couldn't convert filename to URI: {error.Message}");
                _anyFailed = true;
                return;
            }

            if (converted is null)
            {
                Console.Error.WriteLine($"Couldn't convert filename to URI: {path}");
                _anyFailed = true;
                return;
            }

            uri = converted;
        }

        Analyze(uri);

        // No main loop runs here, so this thread drains the queue of pending
        // releases itself, and once per URI is the natural place. See
        // docs/ownership.md.
        GstSharp.DrainPendingReleases();
    }

    /// <summary>
    /// Walks the entries of a directory, which is the recursion of the C tool.
    /// </summary>
    /// <param name="path">The directory to walk.</param>
    /// <remarks>
    /// The order is the order the file system reports, as
    /// <c>g_dir_read_name</c> reports it, and the entries are read in one go so
    /// that a directory that changes while it is walked cannot make the
    /// enumeration throw halfway through a report.
    /// </remarks>
    private void Recurse(string path)
    {
        string[] entries;

        try
        {
            entries = Directory.GetFileSystemEntries(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't read directory {path}: {error.Message}");
            _anyFailed = true;
            return;
        }

        foreach (string entry in entries)
        {
            ProcessFile(entry);
        }
    }

    /// <summary>
    /// Discovers one URI and prints the report.
    /// </summary>
    /// <param name="uri">The URI to discover.</param>
    /// <remarks>
    /// <para>
    /// <c>gst_discoverer_discover_uri</c> returns an information object
    /// <b>and</b> fills its <c>GError</c> when the run saw an error on its bus,
    /// which is what <see cref="Discoverer.TryDiscoverUri"/> hands out
    /// together: the report of a failed discovery is read here exactly as the
    /// C tool reads it, and <c>print_info</c> is given the error to print
    /// under the result line.
    /// </para>
    /// <para>
    /// The <c>Could not discover URI</c> branch of <c>print_info</c> stays out
    /// of reach in practice: the only way the C call answers nothing at all is
    /// a second discovery while one is running, and this tool discovers one URI
    /// at a time. It is written out all the same, because a call that can
    /// answer nothing is a call whose nothing has to be printed.
    /// </para>
    /// </remarks>
    private void Analyze(string uri)
    {
        Console.WriteLine($"Analyzing {uri}");

        DiscovererInfo? info = _discoverer.TryDiscoverUri(uri, out GException? error);

        if (info is null)
        {
            Console.WriteLine("Could not discover URI");
            Console.WriteLine($" {error?.Message}");
            Console.WriteLine();
            _anyFailed = true;
            return;
        }

        PrintInfo(info, error);

        if (info.GetResult() != DiscovererResult.Ok)
        {
            _anyFailed = true;
        }
    }

    /// <summary>
    /// Formats a time the way <c>GST_TIME_FORMAT</c> does.
    /// </summary>
    /// <param name="value">The time in nanoseconds, as the C signed type.</param>
    /// <returns>The formatted time.</returns>
    /// <remarks>
    /// <see cref="ClockTime.ToString"/> stops at milliseconds and pads the
    /// hours, and every line of this tool is compared against the output of the
    /// C one, so the format of GStreamer is written out here: unpadded hours and
    /// nine digits of nanoseconds, and the nines that stand for a time that is
    /// not set.
    /// </remarks>
    private static string FormatClockTime(long value)
    {
        ulong time = unchecked((ulong)value);

        if (time == ClockTime.NoneValue)
        {
            return "99:99:99.999999999";
        }

        ulong hours = time / (ClockTime.NanosecondsPerSecond * 3600);
        ulong minutes = time / (ClockTime.NanosecondsPerSecond * 60) % 60;
        ulong seconds = time / ClockTime.NanosecondsPerSecond % 60;
        ulong nanoseconds = time % ClockTime.NanosecondsPerSecond;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{nanoseconds:000000000}");
    }
}
