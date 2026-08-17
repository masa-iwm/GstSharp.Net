using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.GObject;

/// <summary>
/// The run of <c>gst-typefind-1.0</c>: one <c>filesrc ! typefind ! fakesink</c>
/// pipeline per file, and the line that says what the file turned out to be.
/// </summary>
internal sealed class TypeFinder
{
    /// <summary>Every file was looked at.</summary>
    private const int ExitNoError = 0;

    /// <summary>The command line was wrong, or a type was not found under <c>--fail-on-unknown</c>.</summary>
    private const int ExitError = 1;

    private readonly Options _options;
    private bool _anyUnknown;

    /// <summary>Initialises a run.</summary>
    /// <param name="options">The command line of the process.</param>
    private TypeFinder(Options options) => _options = options;

    /// <summary>
    /// Reads the command line, initialises GStreamer and identifies every file.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 1 when the command line was unusable or
    /// carried no file at all, and 0 otherwise — including for a file whose
    /// type was not found, unless <c>--fail-on-unknown</c> was given.
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
            // tool prints here — on standard output, through g_print, where
            // gst-launch uses gst_printerr for the same sentence.
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
            Console.WriteLine($"Please give one or more filenames to {Options.ProgramName}");
            Console.WriteLine();
            return ExitError;
        }

        try
        {
            return new TypeFinder(options).FindAll();
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
    /// Identifies everything the command line named.
    /// </summary>
    /// <returns>The exit code.</returns>
    private int FindAll()
    {
        foreach (string file in _options.Files)
        {
            Find(file);
        }

        return _anyUnknown && _options.FailOnUnknown ? ExitError : ExitNoError;
    }

    /// <summary>
    /// Identifies one file, or everything below one directory.
    /// </summary>
    /// <param name="path">The file or directory to look at.</param>
    private void Find(string path)
    {
        // g_dir_open succeeds on a directory, and the C tool then walks its
        // entries and recurses into each one. Anything else is a file.
        if (Directory.Exists(path))
        {
            Recurse(path);
            return;
        }

        Identify(path);

        // No main loop runs here, so this thread drains the queue of pending
        // releases itself, and once per file is the natural place. See
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
    /// enumeration throw halfway through a line.
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
            // g_dir_open returns NULL here as well, and the C tool then hands
            // the directory to filesrc, which reports it as a failed file.
            Console.Error.WriteLine($"{path} - FAILED: {error.Message}");
            _anyUnknown = true;
            return;
        }

        foreach (string entry in entries)
        {
            Find(entry);
        }
    }

    /// <summary>
    /// Runs one file through <c>filesrc ! typefind ! fakesink</c> and reports
    /// what came out.
    /// </summary>
    /// <param name="path">The file to identify.</param>
    private void Identify(string path)
    {
        Element? source = ElementFactory.Make("filesrc", "source");
        Element? typefind = ElementFactory.Make("typefind", "typefind");
        Element? sink = ElementFactory.Make("fakesink", "fakesink");

        if (source is null || typefind is null || sink is null)
        {
            // The C tool asserts here and takes the process down with it.
            Console.Error.WriteLine(
                $"{path} - FAILED: filesrc, typefind and fakesink have to be installed");
            _anyUnknown = true;
            return;
        }

        // The pipeline is built by this code and released by it, which is the
        // one sanctioned Dispose of a GObject wrapper; the three elements
        // belong to it once they are added and are not disposed here. See
        // docs/ownership.md.
        using Pipeline pipeline = Pipeline.New("pipeline");

        if (!pipeline.AddMany(source, typefind, sink) || !source.Link(typefind, sink))
        {
            Console.Error.WriteLine($"{path} - FAILED: the pipeline could not be built");
            _anyUnknown = true;
            return;
        }

        using (Value location = Value.New(GType.String))
        {
            location.SetString(path);
            source.SetProperty("location", location);
        }

        // The reason this sample exists. typefind is a plugin element that no
        // .gir file describes, so "have-type" has no generated event and is
        // reached by name through the dynamic machinery instead. What the
        // emission can and cannot hand over is written out in Program.cs.
        HaveTypeWatch watch = new();
        ulong handler = typefind.ConnectSignal("have-type", watch.OnHaveType);

        try
        {
            // typefind only commits to PAUSED when it actually finds a type;
            // otherwise the state change fails.
            pipeline.SetState(State.Paused);

            // Wait until the state change either completes or fails.
            StateChangeReturn result = pipeline.GetState(out State _, out State _, ClockTime.None);

            switch (result)
            {
                case StateChangeReturn.Failure:
                    ReportFailure(pipeline, path);
                    break;

                case StateChangeReturn.Success:
                    Report(watch, path);
                    break;

                default:
                    // A blocking get_state on this pipeline answers SUCCESS or
                    // FAILURE, and the C tool has a g_assert_not_reached() here
                    // rather than a branch. A sample says what happened.
                    Console.Error.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{path} - FAILED: the state change answered {result}"));
                    _anyUnknown = true;
                    break;
            }
        }
        finally
        {
            // NULL before the handler comes off: the handler runs on a
            // streaming thread, and that thread is gone once the pipeline is
            // stopped. See docs/ownership.md.
            pipeline.SetState(State.Null);
            typefind.RemoveHandler(handler);

            // The copy the emission left behind on a run that never reached
            // Report — a failed state change, for example.
            watch.TakeCaps()?.Dispose();
        }
    }

    /// <summary>
    /// Prints the type of a file that prerolled.
    /// </summary>
    /// <param name="typefind">The element that found the type.</param>
    /// <param name="watch">What the emission left behind.</param>
    /// <param name="path">The file that was identified.</param>
    /// <remarks>
    /// The C handler keeps <c>gst_caps_copy</c> of the caps the emission
    /// carried and prints those, and that is what happens here:
    /// <see cref="HaveTypeWatch"/> takes <see cref="Caps.Copy"/> of the wrapper
    /// the emission lends it. <see cref="Caps.ToString"/> is
    /// <c>gst_caps_to_string</c>, so the line is the C tool's line.
    /// </remarks>
    private void Report(HaveTypeWatch watch, string path)
    {
        // Whether the emission left caps behind is what tells a file that was
        // identified from one that was not, which is the C tool's "if (caps)".
        using Caps? caps = watch.TakeCaps();

        if (caps is not null)
        {
            Console.WriteLine($"{path} - {caps}");
            return;
        }

        if (watch.Fired)
        {
            // The signal fired and carried nothing, which the declaration of
            // have-type does not allow. That is not a case the C tool has, so
            // it is reported rather than dressed up as one that it does.
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{path} - have-type fired with probability {watch.Probability}, " +
                $"but the emission carried no caps"));
        }

        Console.WriteLine($"{path} - No type found");
        _anyUnknown = true;
    }

    /// <summary>
    /// Prints why a file could not be identified.
    /// </summary>
    /// <param name="pipeline">The pipeline that refused to preroll.</param>
    /// <param name="path">The file that was being identified.</param>
    private void ReportFailure(Pipeline pipeline, string path)
    {
        // The bus wrapper is interned and shared with every other lookup of the
        // same bus, so it is not disposed here. PopFiltered returns whatever
        // the bus already holds, which is what the C tool's gst_bus_poll with a
        // zero timeout amounts to, without the main loop that call runs.
        Bus bus = pipeline.GetBus();

        using Message? message = bus.PopFiltered(MessageType.Error);

        if (message is null)
        {
            Console.Error.WriteLine($"{path} - FAILED: unknown error");
        }
        else
        {
            (GException error, string? _) = message.ParseError();
            Console.Error.WriteLine($"{path} - FAILED: {error.Message}");
        }

        _anyUnknown = true;
    }

    /// <summary>
    /// Prints the version of the tool and of the library it loaded.
    /// </summary>
    /// <remarks>
    /// The C tool prints a third line, the origin of the package, which is a
    /// constant of its build and has no run time equivalent here.
    /// </remarks>
    private static void PrintVersion()
    {
        Gst.Version version = GstSharp.NativeVersion;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Options.ProgramName} version {version.Major}.{version.Minor}.{version.Micro}"));
        Console.WriteLine(version.Description);
    }

    /// <summary>
    /// What one <c>have-type</c> emission left behind.
    /// </summary>
    /// <remarks>
    /// The signal is emitted from the streaming thread of <c>typefind</c>,
    /// while the thread that started the pipeline is blocked in
    /// <c>get_state</c>, so both fields are written there and read here.
    /// </remarks>
    private sealed class HaveTypeWatch
    {
        private int _fired;
        private uint _probability;
        private Caps? _caps;

        /// <summary>Gets a value indicating whether the signal was emitted.</summary>
        internal bool Fired => Volatile.Read(ref _fired) != 0;

        /// <summary>Gets how sure <c>typefind</c> was, out of 100.</summary>
        internal uint Probability => Volatile.Read(ref _probability);

        /// <summary>
        /// Hands the copy of the caps over to the caller, who disposes it.
        /// </summary>
        /// <returns>The caps, or <see langword="null"/> when none arrived.</returns>
        internal Caps? TakeCaps() => Interlocked.Exchange(ref _caps, null);

        /// <summary>
        /// Records one emission.
        /// </summary>
        /// <param name="sender">The <c>typefind</c> element.</param>
        /// <param name="args">
        /// The arguments the signal declares: the probability as a
        /// <see cref="uint"/> and the caps as a <see cref="Caps"/>, which the
        /// emission lends to the handler and takes back when it returns.
        /// </param>
        /// <returns>
        /// <see langword="null"/>: <c>have-type</c> returns nothing.
        /// </returns>
        internal object? OnHaveType(Gst.GObject.Object sender, object?[] args)
        {
            _ = sender;

            if (args.Length > 0 && args[0] is uint probability)
            {
                Volatile.Write(ref _probability, probability);
            }

            if (args.Length > 1 && args[1] is Caps caps)
            {
                // gst_caps_copy, for the reason the C handler calls it: the
                // argument belongs to the emission and is released when this
                // returns. A previous copy would be replaced, which typefind
                // never does, so the old one is released rather than dropped.
                Interlocked.Exchange(ref _caps, caps.Copy())?.Dispose();
            }

            Volatile.Write(ref _fired, 1);
            return null;
        }
    }
}
