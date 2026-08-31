// Basic tutorial 9: media information gathering — GstDiscoverer asked what is
// inside a URI, asynchronously, with the answer arriving as a signal.
//
// Ported from basic-tutorial-9.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/media-information-gathering.html
//
// Usage: BasicTutorial09 [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>] [<file-or-uri>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop. The C program builds one, runs it, and quits it from the
//     "finished" handler. Here the emission is kept on the thread that started
//     the run: a GMainContext of this thread's own is pushed as the thread
//     default before Start(), which is where gst_discoverer_start takes it —
//     it attaches the bus watch and the timeout source to whatever context is
//     thread default at that moment, and both signals are delivered on it —
//     and the loop below iterates that context until "finished" arrives or the
//     bound elapses. That is the house style, the same "the application owns
//     its thread" the other tutorials keep by polling a bus, and it is what
//     docs/ownership.md argues for under "Applications without a main loop".
//
//   * The two g_signal_connect calls are the Discovered and Finished events.
//     The GError of "discovered" is a borrowed argument registered
//     G_SIGNAL_TYPE_STATIC_SCOPE — the discoverer frees it when the emission
//     returns — so the binding copies it inside the trampoline and the
//     GException the handler is handed outlives the call.
//
//   * gst_discoverer_new reports its failure through a GError, which the
//     binding raises rather than returns: the "Error creating discoverer
//     instance" branch of the C program is the catch at the bottom of Run.
//
//   * gst_object_unref and gst_discoverer_stream_info_unref are gone. Every
//     information object here is an interned GObject wrapper, and the lists the
//     binding hands back are already the walked GList of the C program, so
//     gst_discoverer_stream_info_list_free has no counterpart either. The
//     discoverer itself is the one thing this program owns, and `using` is what
//     releases it.
//
//   * A local path is accepted where the tutorial wants a URI, through
//     gst_filename_to_uri, so that the program can be pointed at a file that
//     was just made. The default is the same trailer the upstream page uses,
//     which needs a network.
//
//   * The exit code says whether the URI was understood. The C program prints
//     "This URI cannot be played" and returns 0 all the same, which leaves an
//     automated run nothing to gate on; a result other than OK is exit code 1
//     here, which is the rule the rest of this directory follows. --timeout
//     bounds the wait for the signal and is not part of the tutorial; the five
//     second timeout of the discoverer itself is, and is the C program's.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;
using Gst.Pbutils;

// System.Diagnostics, which Stopwatch lives in, has a TagList of its own.
using TagList = Gst.TagList;

return MediaInformation.Run(args);

internal static class MediaInformation
{
    /// <summary>The media the upstream page uses when none is named.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// How long the discoverer gives one URI, which is the C program's
    /// <c>5 * GST_SECOND</c>.
    /// </summary>
    private static readonly ClockTime Patience = ClockTime.FromSeconds(5);

    /// <summary>
    /// Discovers one URI and prints what came back.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the URI was understood, 1 otherwise.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);

            // GstPbutils.Initialize rather than GstSharp.Initialize: it is a
            // call into GstSharp.Net.Pbutils, and only that runs the module
            // initialiser which puts GstDiscoverer and the information classes
            // into the type registry. Without it the casts in the topology walk
            // below would find nothing to cast to.
            GstPbutils.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"Discovering '{options.Uri}'");

            // The context has to be the thread default before Start() is
            // called, which is where gst_discoverer_start reads it. Pushing it
            // here, around the construction as well, is what keeps that true
            // however the code below is rearranged.
            using MainContext context = MainContext.New();
            context.PushThreadDefault();

            try
            {
                return Discover(context, options);
            }
            finally
            {
                context.PopThreadDefault();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial09: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Runs one asynchronous discovery and iterates the context until it is
    /// answered.
    /// </summary>
    /// <param name="context">The context the signals are delivered on.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the URI was understood, 1 otherwise.</returns>
    private static int Discover(MainContext context, Options options)
    {
        using Discoverer discoverer = Discoverer.New(Patience);

        int exitCode = 1;
        bool finished = false;

        // on_discovered_cb of the C program. It runs on the context this thread
        // pushed, so nothing here crosses a thread boundary.
        void OnDiscovered(object? sender, Discoverer.DiscoveredSignalArgs args) =>
            exitCode = Report(args.Info, args.Error);

        // on_finished_cb, which quits the main loop there and ends the loop
        // below here.
        void OnFinished(object? sender, EventArgs args)
        {
            Console.WriteLine("Finished discovering");
            finished = true;
        }

        discoverer.Discovered += OnDiscovered;
        discoverer.Finished += OnFinished;

        try
        {
            discoverer.Start();

            if (!discoverer.DiscoverUriAsync(options.Uri))
            {
                Console.Error.WriteLine($"Failed to start discovering URI '{options.Uri}'");
                return 1;
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (!finished && elapsed.Elapsed < options.Timeout)
            {
                // The one call the C program's g_main_loop_run makes for it.
                // False rather than true so that the bound above is checked
                // even when nothing is pending.
                if (!context.Iteration(mayBlock: false))
                {
                    Thread.Sleep(5);
                }
            }

            discoverer.Stop();

            if (finished)
            {
                return exitCode;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial09: nothing was discovered within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // A handler must not be disconnected while the emission is still
            // inside it, and the discoverer is stopped by the time this runs.
            discoverer.Finished -= OnFinished;
            discoverer.Discovered -= OnDiscovered;
        }
    }

    /// <summary>
    /// Prints one discovery, the way <c>on_discovered_cb</c> does.
    /// </summary>
    /// <param name="info">What the discoverer found.</param>
    /// <param name="error">The error it failed with, when it did.</param>
    /// <returns>0 when the URI was understood, 1 otherwise.</returns>
    private static int Report(DiscovererInfo info, GException? error)
    {
        string uri = info.GetUri();
        DiscovererResult result = info.GetResult();

        switch (result)
        {
            case DiscovererResult.UriInvalid:
                Console.WriteLine($"Invalid URI '{uri}'");
                break;

            case DiscovererResult.Error:
                Console.WriteLine($"Discoverer error: {error?.Message ?? "none"}");
                break;

            case DiscovererResult.Timeout:
                Console.WriteLine("Timeout");
                break;

            case DiscovererResult.Busy:
                Console.WriteLine("Busy");
                break;

            case DiscovererResult.MissingPlugins:
                // The structure names the elements that would have been needed,
                // which is what an installer is handed.
                //
                // gst_discoverer_info_get_misc has been deprecated since 1.4 in
                // favour of gst_discoverer_info_get_missing_elements_installer
                // _details, and the tutorial calls it anyway. Printing what the
                // tutorial prints is the point of the port, so the deprecation
                // is acknowledged rather than worked around.
#pragma warning disable CS0618
                using (Structure? misc = info.GetMisc())
#pragma warning restore CS0618
                {
                    Console.WriteLine($"Missing plugins: {misc?.ToString() ?? "none"}");
                }

                break;

            case DiscovererResult.Ok:
                Console.WriteLine($"Discovered '{uri}'");
                break;

            default:
                Console.WriteLine($"Unknown result {result} for '{uri}'");
                break;
        }

        if (result != DiscovererResult.Ok)
        {
            Console.Error.WriteLine("This URI cannot be played");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"Duration: {FormatClockTime(info.GetDuration())}");

        // gst_discoverer_info_get_tags is deprecated since 1.20 in favour of the
        // per-stream and per-container calls, and it is what the tutorial
        // prints: the merged list of everything the file said, which no other
        // call answers. Same reasoning as the misc block above.
#pragma warning disable CS0618
        using (TagList? tags = info.GetTags())
#pragma warning restore CS0618
        {
            if (tags is not null)
            {
                Console.WriteLine("Tags:");
                PrintTags(tags, depth: 1);
            }
        }

        Console.WriteLine($"Seekable: {(info.GetSeekable() ? "yes" : "no")}");
        Console.WriteLine();

        if (info.GetStreamInfo() is not DiscovererStreamInfo stream)
        {
            return 0;
        }

        Console.WriteLine("Stream information:");
        PrintTopology(stream, depth: 1);
        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// Prints a stream and everything under it, the way <c>print_topology</c>
    /// does.
    /// </summary>
    /// <param name="info">The stream to print.</param>
    /// <param name="depth">The indentation of the block.</param>
    private static void PrintTopology(DiscovererStreamInfo info, int depth)
    {
        PrintStream(info, depth);

        // A stream that has a next one is a link in a chain: the demuxer sits
        // between the container and the elementary streams, and the C program
        // walks the chain before it recurses into a container.
        if (info.GetNext() is DiscovererStreamInfo next)
        {
            PrintTopology(next, depth + 1);
            return;
        }

        if (info is not DiscovererContainerInfo container)
        {
            return;
        }

        foreach (DiscovererStreamInfo stream in container.GetStreams())
        {
            PrintTopology(stream, depth + 1);
        }
    }

    /// <summary>
    /// Prints one stream, the way <c>print_stream_info</c> does.
    /// </summary>
    /// <param name="info">The stream to print.</param>
    /// <param name="depth">The indentation of the line.</param>
    private static void PrintStream(DiscovererStreamInfo info, int depth)
    {
        string description = string.Empty;

        using (Caps? caps = info.GetCaps())
        {
            if (caps is not null)
            {
                // Fixed caps have a name a person can read; anything else is
                // printed as the caps themselves. This is the one place the
                // tutorial reaches into the base utils library, which is what
                // the project references it for.
                description = (caps.IsFixed()
                    ? PbutilsGlobal.PbUtilsGetCodecDescription(caps)
                    : caps.ToString())
                    ?? string.Empty;
            }
        }

        Console.WriteLine($"{Indent(depth)}{info.GetStreamTypeNick()}: {description}");

        using TagList? tags = info.GetTags();

        if (tags is null)
        {
            return;
        }

        Console.WriteLine($"{Indent(depth + 1)}Tags:");
        PrintTags(tags, depth + 2);
    }

    /// <summary>
    /// Prints every tag of a list, the way <c>print_tag_foreach</c> does.
    /// </summary>
    /// <param name="tags">The tags to print.</param>
    /// <param name="depth">The indentation of each line.</param>
    private static void PrintTags(TagList tags, int depth) =>
        tags.Foreach((list, tag) =>
        {
            // gst_tag_list_copy_value, which merges the values a tag was given
            // more than once into one.
            using Gst.GObject.Value value = list.CopyValue(tag);

            if (value.IsEmpty)
            {
                return;
            }

            string text = value.Type == Gst.GObject.GType.String
                ? value.GetString() ?? string.Empty
                : Global.ValueSerialize(value) ?? string.Empty;

            Console.WriteLine($"{Indent(depth)}{Global.TagGetNick(tag)}: {text}");
        });

    /// <summary>
    /// The indentation of one level, which the C program writes as
    /// <c>g_print ("%*s...", 2 * depth, " ")</c>.
    /// </summary>
    /// <param name="depth">How deep the line sits.</param>
    /// <returns>The spaces to put in front of it.</returns>
    /// <remarks>
    /// <c>%*s</c> right-aligns its argument in the width, and the argument is a
    /// single space, so the answer is two spaces per level — and one space, not
    /// none, at depth zero.
    /// </remarks>
    private static string Indent(int depth) => new(' ', Math.Max(2 * depth, 1));

    /// <summary>
    /// Formats a time the way <c>GST_TIME_FORMAT</c> does.
    /// </summary>
    /// <param name="time">The time to format.</param>
    /// <returns>The formatted time.</returns>
    /// <remarks>
    /// <see cref="ClockTime.ToString"/> stops at milliseconds and pads the
    /// hours, and the upstream page shows the format of GStreamer, so it is
    /// written out here: unpadded hours and nine digits of nanoseconds.
    /// </remarks>
    private static string FormatClockTime(ClockTime time)
    {
        if (time.Nanoseconds == ClockTime.NoneValue)
        {
            return "99:99:99.999999999";
        }

        ulong hours = time.Nanoseconds / (ClockTime.NanosecondsPerSecond * 3600);
        ulong minutes = time.Nanoseconds / (ClockTime.NanosecondsPerSecond * 60) % 60;
        ulong seconds = time.Nanoseconds / ClockTime.NanosecondsPerSecond % 60;
        ulong nanoseconds = time.Nanoseconds % ClockTime.NanosecondsPerSecond;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{nanoseconds:000000000}");
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the media to describe.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets how long the run may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(60);

        /// <summary>Gets the options of the native loader.</summary>
        internal GstSharpOptions Native { get; } = new();

        /// <summary>
        /// Reads the command line.
        /// </summary>
        /// <param name="arguments">The arguments of the process.</param>
        /// <returns>The parsed options.</returns>
        /// <exception cref="ArgumentException">An argument is unknown or incomplete.</exception>
        internal static Options Parse(string[] arguments)
        {
            Options options = new();
            bool uriSeen = false;

            for (int i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "--native-path":
                        options.Native.NativeSearchPath = Cli.ValueOf(arguments, ref i);
                        break;

                    case "--flavor":
                        options.Native.WindowsFlavor = Cli.FlavorOf(Cli.ValueOf(arguments, ref i));
                        break;

                    case "--timeout":
                        options.Timeout = TimeSpan.FromSeconds(double.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture));
                        break;

                    default:
                        if (uriSeen || arguments[i].StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uri = Cli.ToUri(arguments[i]);
                        uriSeen = true;
                        break;
                }
            }

            return options;
        }
    }
}

/// <summary>
/// The two or three lines of command line handling every tutorial in this
/// directory needs. Each tutorial is a self-contained program, so this is
/// repeated per project rather than shared: the upstream tutorials are read one
/// file at a time and so are these.
/// </summary>
internal static class Cli
{
    /// <summary>
    /// Turns a command line argument into a URI, so that a local file can be
    /// passed where the tutorial expects a URI.
    /// </summary>
    /// <param name="value">A URI, or the path of a local file.</param>
    /// <returns>The URI to hand to the discoverer.</returns>
    internal static string ToUri(string value) =>
        value.Contains("://", StringComparison.Ordinal)
            ? value
            : Global.FilenameToUri(Path.GetFullPath(value))
                ?? throw new ArgumentException($"\"{value}\" is neither a URI nor a path.", nameof(value));

    /// <summary>
    /// Reads the flavor of a Windows installation.
    /// </summary>
    /// <param name="value">The value that followed <c>--flavor</c>.</param>
    /// <returns>The flavor to pin.</returns>
    /// <exception cref="ArgumentException">The value is not a flavor.</exception>
    internal static GstFlavor FlavorOf(string value) => value.ToUpperInvariant() switch
    {
        "MSVC" => GstFlavor.Msvc,
        "MINGW" => GstFlavor.MinGW,
        _ => throw new ArgumentException($"\"{value}\" is not a flavor. Use msvc or mingw.", nameof(value)),
    };

    /// <summary>
    /// Reads the value that follows an option.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <param name="index">The index of the option, advanced to its value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The option has no value.</exception>
    internal static string ValueOf(string[] arguments, ref int index)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"\"{arguments[index]}\" needs a value.", nameof(arguments));
        }

        return arguments[++index];
    }
}
