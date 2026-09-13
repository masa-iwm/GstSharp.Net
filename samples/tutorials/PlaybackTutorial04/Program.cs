// Playback tutorial 4: progressive streaming — downloading a stream to a
// temporary file while it plays, and drawing what has arrived so far.
//
// Ported from playback-tutorial-4.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/progressive-streaming.html
//
// Usage: PlaybackTutorial04 [<uri-or-file>] [--headless]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop, no signal watch on the bus and no g_timeout_add_seconds.
//     The loop below pops the same messages with TimedPopFiltered, and the
//     one-second redraw is a Stopwatch checked inside that loop rather than a
//     timer source — with no main loop there is nothing for a GSource to be
//     attached to, and a poll loop already runs often enough to be a clock.
//
//   * On ERROR and on EOS the C program sets the pipeline to READY before
//     quitting its loop. This one returns straight away and the finally block
//     takes it all the way to NULL.
//
//   * GstPlayFlags is a GFlags type that the playback plugin registers at run
//     time, so no .gir declares it and this binding has no managed type for it.
//     The download flag is therefore written as the plain uint bit it is.
//
//   * The bar arithmetic of the C original is kept as it is written there,
//     including the fact that it rewrites `start` before dividing by
//     `stop - start` again. Two guards are added that C does not have: a range
//     whose ends are equal would divide by zero, and an index past the end of
//     the array would be an out-of-range write in C and an exception here.
//     Clamping is the smallest change that keeps the same picture on the
//     ranges the query really returns.
//
//   * GRAPH_LENGTH is 78 in the C source and is 78 here.
//
//   * deep-notify::temp-location is connected with the generic ConnectSignal
//     rather than with the generated DeepNotify event, because the generated
//     one is wired to the bare signal with no detail and would fire for every
//     property of every element in the pipeline. The handler is handed the
//     emitting child as a Gst.Object wrapper and the GParamSpec as a ParamSpec
//     wrapper, both borrowed for the length of the call, so the temp file name
//     is read off the child — not off the pipeline, which does not have that
//     property at all.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card.
//
//   * --timeout bounds the run and is a failure when it elapses: this pipeline
//     was supposed to reach the end of its stream.
//
//   * A file:// URI is not a download, so pointing this at a local path shows
//     no buffering and no temporary file. Serve the same file over http to see
//     both, which is what CI does.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return ProgressiveStreaming.Run(args);

internal static class ProgressiveStreaming
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>How wide the ASCII bar is, as GRAPH_LENGTH is upstream.</summary>
    private const int GraphLength = 78;

    /// <summary>
    /// The one bit of <c>GstPlayFlags</c> this tutorial names. The type is a
    /// GFlags the playback plugin registers when it is loaded, so it appears in
    /// no <c>.gir</c> file and this binding declares no managed type for it;
    /// the property is read and written as the <see cref="uint"/> it holds.
    /// </summary>
    private const uint PlayFlagDownload = 0x80;

    /// <summary>How often the bar is redrawn.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

    /// <summary>How much of the stream has arrived, as the last message said.</summary>
    private static int _bufferingLevel = 100;

    /// <summary>
    /// Plays a stream that is downloaded while it plays.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the stream ended, 1 on any error.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);

            GstSharp.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"uri:         {options.Uri}");

            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybackTutorial04: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                // Set the download flag. The read-modify-write is the C
                // original's: playbin's default already has other bits.
                uint flags = pipeline.GetProperty<uint>("flags");
                flags |= PlayFlagDownload;
                pipeline.SetProperty("flags", flags);

                // Uncomment this line to limit the amount of downloaded data:
                // pipeline.SetProperty("ring-buffer-max-size", 4000000UL);

                if (options.Headless && !Silence(pipeline))
                {
                    return 1;
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial04: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Gives playbin sinks that need neither a screen nor a sound card.
    /// </summary>
    /// <param name="playbin">The playbin to configure.</param>
    /// <returns><see langword="true"/> when both sinks were set.</returns>
    /// <remarks>
    /// gst_util_set_object_arg cannot do this: it deserializes a string into
    /// the type of the property, and GStreamer registers no deserializer for an
    /// object-valued one. A GValue that holds the element is the way.
    /// </remarks>
    private static bool Silence(Pipeline playbin)
    {
        foreach (string property in (string[])["video-sink", "audio-sink"])
        {
            if (ElementFactory.Make("fakesink", null) is not Element sink)
            {
                Console.Error.WriteLine("PlaybackTutorial04: fakesink could not be created.");
                return false;
            }

            // A sink that runs as fast as it is fed never lets the download get
            // ahead of the playback, which is the whole subject here.
            Global.UtilSetObjectArg(sink, "sync", "true");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the pipeline, answers its messages and redraws the bar.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the stream ended, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            // The handler runs on whichever thread the child posted the
            // notification from, which is a streaming thread rather than this
            // one. It only prints, so that is all it needs to be safe.
            _ = pipeline.ConnectSignal("deep-notify::temp-location", GotLocation);

            StateChangeReturn started = pipeline.SetState(State.Playing);

            if (started == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial04: Unable to set the pipeline to the playing state.");
                return 1;
            }

            // A live source has nothing to download and nothing to buffer.
            bool isLive = started == StateChangeReturn.NoPreroll;

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();
            TimeSpan nextRefresh = RefreshInterval;

            while (elapsed.Elapsed < options.Timeout)
            {
                using (Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos | MessageType.Buffering | MessageType.ClockLost))
                {
                    if (message is not null)
                    {
                        switch (message.Type)
                        {
                            case MessageType.Error:
                                (GException error, string? debug) = message.ParseError();
                                Console.WriteLine();
                                Console.WriteLine($"Error: {error.Message}");
                                Console.Error.WriteLine($"Debugging information: {debug ?? "none"}");
                                return 1;

                            case MessageType.Eos:
                                Console.WriteLine();
                                Console.WriteLine("End-Of-Stream reached.");
                                return 0;

                            case MessageType.Buffering:
                                if (isLive)
                                {
                                    // If the stream is live, we do not care
                                    // about buffering.
                                    break;
                                }

                                message.ParseBuffering(out _bufferingLevel);

                                // Wait until buffering is complete before
                                // start/resume playing.
                                pipeline.SetState(_bufferingLevel < 100 ? State.Paused : State.Playing);
                                break;

                            case MessageType.ClockLost:
                                // Get a new clock.
                                pipeline.SetState(State.Paused);
                                pipeline.SetState(State.Playing);
                                break;

                            default:
                                break;
                        }

                        continue;
                    }
                }

                GstSharp.DrainPendingReleases();

                if (elapsed.Elapsed >= nextRefresh)
                {
                    nextRefresh = elapsed.Elapsed + RefreshInterval;

                    if (!isLive)
                    {
                        RefreshUi(pipeline);
                    }
                }
            }

            Console.WriteLine();
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial04: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Prints the temporary file the emitting child chose.
    /// </summary>
    /// <param name="sender">The pipeline the notification travelled up to.</param>
    /// <param name="args">
    /// The emitting child and the specification of the property that changed.
    /// </param>
    /// <returns><see langword="null"/>: the signal returns nothing.</returns>
    /// <remarks>
    /// The property belongs to the child, not to the pipeline the handler is
    /// connected to — that is the point of a deep notification and the one
    /// thing easy to get wrong here.
    /// </remarks>
    private static object? GotLocation(Gst.GObject.Object sender, object?[] args)
    {
        if (args.Length > 0 && args[0] is Gst.GObject.Object propertyObject)
        {
            Console.WriteLine();
            Console.WriteLine($"Temporary file: {propertyObject.GetProperty<string>("temp-location")}");

            // Uncomment this line to keep the temporary file after the program
            // exits:
            // propertyObject.SetProperty("temp-remove", false);
        }

        return null;
    }

    /// <summary>
    /// Draws what has been downloaded and where the playback is inside it.
    /// </summary>
    /// <param name="pipeline">The pipeline to ask.</param>
    private static void RefreshUi(Pipeline pipeline)
    {
        using Query query = Query.NewBuffering(Format.Percent);

        if (!pipeline.Query(query))
        {
            return;
        }

        char[] graph = new char[GraphLength];
        Array.Fill(graph, ' ');

        uint ranges = query.GetNBufferingRanges();

        for (uint range = 0; range < ranges; range++)
        {
            if (!query.ParseNthBufferingRange(range, out long start, out long stop))
            {
                continue;
            }

            // The C original rewrites `start` and then divides by `stop - start`
            // a second time, so the second divisor is not the one the first
            // division used. That is kept, because the picture it draws on a
            // range that begins at zero — which is what a download produces —
            // is the picture the tutorial shows. The two guards are ours: a
            // range of no width would divide by zero, and an index past the end
            // of the array is an exception here rather than a silent overwrite.
            if (stop == start)
            {
                continue;
            }

            long width = stop - start;
            start = start * GraphLength / width;

            if (stop == start)
            {
                continue;
            }

            stop = stop * GraphLength / (stop - start);

            for (long i = Math.Max(start, 0); i < Math.Min(stop, GraphLength); i++)
            {
                graph[i] = '-';
            }
        }

        if (pipeline.QueryPosition(Format.Time, out long position) &&
            !ClockTime.FromNanoseconds((ulong)position).IsNone &&
            pipeline.QueryDuration(Format.Time, out long duration) &&
            !ClockTime.FromNanoseconds((ulong)duration).IsNone)
        {
            int at = (int)(GraphLength * (double)position / (duration + 1));
            graph[Math.Clamp(at, 0, GraphLength - 1)] = _bufferingLevel < 100 ? 'X' : '>';
        }

        Console.Write($"[{new string(graph)}]");

        Console.Write(_bufferingLevel < 100
            ? string.Create(CultureInfo.InvariantCulture, $" Buffering: {_bufferingLevel,3}%")
            : "                ");

        Console.Write('\r');
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the media to play.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets a value indicating whether the run needs no display.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets how long the run may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(120);

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
            bool media = false;

            for (int i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "--headless":
                        options.Headless = true;
                        break;

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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || media)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uri = Cli.ToUri(arguments[i]);
                        media = true;
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
    /// <returns>The URI to hand to the pipeline.</returns>
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
