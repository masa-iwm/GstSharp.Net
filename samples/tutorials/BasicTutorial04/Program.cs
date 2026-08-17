// Basic tutorial 4: time management — where the stream is, how long it is,
// whether it can be seeked, and how to seek it.
//
// Ported from basic-tutorial-4.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/time-management.html
//
// Usage: BasicTutorial04 [<uri-or-file>] [--headless] [--seek-at <seconds>]
//                        [--seek-to <seconds>] [--native-path <directory>]
//                        [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * The structure of the C program survives intact, because it already is the
//     shape this binding teaches: a bus polled with a 100 ms timeout, and the
//     work of the application done in the slices where no message arrived. What
//     C writes as a GMainLoop in the later tutorials is written like this one
//     throughout the ports in this directory.
//
//   * GST_TIME_FORMAT / GST_TIME_ARGS are a macro pair with no equivalent here.
//     Gst.ClockTime is a value type over the same guint64 and its ToString
//     prints HH:MM:SS.mmm, so a position is wrapped in one and printed.
//     GST_CLOCK_TIME_IS_VALID is ClockTime.IsNone, reversed.
//
//   * gst_element_query_duration answers in a plain long, as it does in C, and
//     -1 is what it writes when the duration is not known yet. That is exactly
//     the GST_CLOCK_TIME_NONE the C program starts its duration out as.
//
//   * The GstQuery of the seeking test is a mini object, so `using` replaces
//     gst_query_unref, and nothing unrefs the playbin: it is disposed once, as
//     the pipeline this code built.
//
//   * --headless, --seek-at and --seek-to are not part of the tutorial.
//     --headless replaces the sinks playbin would pick with fakesinks, so the
//     program runs where there is no display and no sound card; those fakesinks
//     are told to sync, because the whole tutorial is about time and a pipeline
//     that runs as fast as it can passes every threshold in one slice. The two
//     thresholds default to the 10 s and 30 s of the C original and are options
//     so that a short local file can be used instead of the 52 s trailer.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return TimeManagement.Run(args);

internal static class TimeManagement
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// Plays the media and seeks it once the position passes the threshold.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
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

            // The C program keeps a GstElement*. This asks for a Pipeline,
            // which playbin is: no wrapper is registered for GstPlayBin itself,
            // so the registry builds the closest ancestor it knows, and that is
            // Gst.Pipeline. Being a pipeline is what gives it a bus of its own.
            if (ElementFactory.Make("playbin", "playbin") is not Pipeline playbin)
            {
                Console.Error.WriteLine("BasicTutorial04: playbin could not be created.");
                return 1;
            }

            using (playbin)
            {
                Global.UtilSetObjectArg(playbin, "uri", options.Uri);

                if (options.Headless && !Silence(playbin))
                {
                    return 1;
                }

                return Play(playbin, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial04: {exception}");
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
    /// <para>
    /// This is the one thing in the tutorials that <c>gst_util_set_object_arg</c>
    /// cannot do. That call deserializes a string into the type of the property,
    /// and GStreamer registers no deserializer for an object-valued one — the
    /// element property of a playbin is set with a GValue that holds the
    /// element, which is what this does.
    /// </para>
    /// <para>
    /// <c>sync</c> is written explicitly, because a fakesink is the one sink
    /// that does not synchronize by default. This whole tutorial is about time,
    /// and a pipeline that runs as fast as it can reaches the end of a ten
    /// second file in half a second, passing every threshold in one slice.
    /// </para>
    /// </remarks>
    private static bool Silence(Pipeline playbin)
    {
        foreach (string property in (string[])["video-sink", "audio-sink"])
        {
            if (ElementFactory.Make("fakesink", null) is not Element sink)
            {
                Console.Error.WriteLine("BasicTutorial04: fakesink could not be created.");
                return false;
            }

            // A fakesink does not synchronize to the clock unless it is told
            // to, and a tutorial about time needs a pipeline that runs at the
            // speed of its clock.
            Global.UtilSetObjectArg(sink, "sync", "true");

            // The value holds a reference of its own while it lives, and
            // playbin takes one when the property is written; the wrapper of
            // the sink keeps the one it was born with. Disposing the value
            // gives its reference back.
            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the pipeline, reports where it is and seeks once.
    /// </summary>
    /// <param name="playbin">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline playbin, Options options)
    {
        State state = State.Null;
        bool seekEnabled = false;
        bool seekDone = false;

        // -1 is what gst_element_query_duration writes when it does not know
        // yet, and what the C program starts out with.
        long duration = -1;
        Progress progress = new();

        try
        {
            if (playbin.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial04: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = playbin.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos | MessageType.StateChanged | MessageType.DurationChanged);

                if (message is not null)
                {
                    switch (message.Type)
                    {
                        case MessageType.Error:
                            (GException error, string? debug) = message.ParseError();
                            Console.Error.WriteLine(
                                $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                            Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                            return 1;

                        case MessageType.Eos:
                            progress.End();
                            Console.WriteLine(string.Create(
                                CultureInfo.InvariantCulture,
                                $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));

                            // The gate of the sample rather than a rule of
                            // GStreamer: a stream that said it can be seeked
                            // and then ended without the seek having happened
                            // means the threshold was never reached, so nothing
                            // of what this tutorial demonstrates ran.
                            if (seekEnabled && !seekDone)
                            {
                                Console.Error.WriteLine(string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"BasicTutorial04: the stream ended before {options.SeekAt.TotalSeconds:F0} s, so the seek never ran. Lower --seek-at."));
                                return 1;
                            }

                            return 0;

                        case MessageType.DurationChanged:
                            // The duration changed, so what was measured before
                            // is worthless.
                            duration = -1;
                            break;

                        case MessageType.StateChanged:
                            if (message.Src is { } source && source.Handle == playbin.Handle)
                            {
                                message.ParseStateChanged(out State old, out state, out _);
                                progress.End();
                                Console.WriteLine($"state:       {old} -> {state}");

                                if (state == State.Playing)
                                {
                                    seekEnabled = AskWhetherSeekable(playbin);
                                }
                            }

                            break;

                        default:
                            break;
                    }

                    continue;
                }

                // No message in this slice: the timeout expired, which is where
                // the C program does its own work as well.
                GstSharp.DrainPendingReleases();

                if (state != State.Playing)
                {
                    continue;
                }

                if (!playbin.QueryPosition(Format.Time, out long position))
                {
                    Console.Error.WriteLine("BasicTutorial04: the position could not be queried.");
                    continue;
                }

                if (duration < 0 && !playbin.QueryDuration(Format.Time, out duration))
                {
                    Console.Error.WriteLine("BasicTutorial04: the duration could not be queried.");
                }

                progress.Report(position, duration);

                if (seekEnabled && !seekDone && position > (long)options.SeekAt.TotalNanoseconds)
                {
                    progress.End();
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"seek:        past {options.SeekAt.TotalSeconds:F0} s, seeking to {options.SeekTo.TotalSeconds:F0} s"));

                    // FLUSH throws away everything that is already in the
                    // pipeline so that the new position is heard at once;
                    // KEY_UNIT lets the demuxer land on the nearest key frame
                    // instead of decoding up to the exact one asked for.
                    playbin.SeekSimple(
                        Format.Time,
                        SeekFlags.Flush | SeekFlags.KeyUnit,
                        (long)options.SeekTo.TotalNanoseconds);

                    seekDone = true;
                }
            }

            progress.End();
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial04: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            playbin.SetState(State.Null);
        }
    }

    /// <summary>
    /// Asks the pipeline whether the stream it plays can be seeked, and over
    /// what range.
    /// </summary>
    /// <param name="playbin">The pipeline to ask.</param>
    /// <returns><see langword="true"/> when seeking is possible.</returns>
    private static bool AskWhetherSeekable(Pipeline playbin)
    {
        // A query is a mini object like a message or a buffer: it is created,
        // handed to the element, read back and released.
        using Query query = Query.NewSeeking(Format.Time);

        if (!playbin.Query(query))
        {
            Console.Error.WriteLine("BasicTutorial04: the seeking query failed.");
            return false;
        }

        query.ParseSeeking(out _, out bool seekable, out long start, out long end);

        Console.WriteLine(seekable
            ? $"seeking:     enabled, from {Time(start)} to {Time(end)}"
            : "seeking:     disabled for this stream");

        return seekable;
    }

    /// <summary>
    /// The position line, which the C program keeps rewriting with a carriage
    /// return. That is worth keeping on a console and worth not doing in a log
    /// file, where it would produce one unreadable line, so a redirected run
    /// gets one line per second of media instead.
    /// </summary>
    private sealed class Progress
    {
        private long _printed = -1;
        private bool _pending;

        /// <summary>
        /// Prints where the stream is, at most once per second of media.
        /// </summary>
        /// <param name="position">Where the stream is, in nanoseconds.</param>
        /// <param name="duration">How long it is, in nanoseconds, or -1.</param>
        internal void Report(long position, long duration)
        {
            long second = position / (long)ClockTime.NanosecondsPerSecond;

            if (second == _printed)
            {
                return;
            }

            _printed = second;
            string line = $"position:    {Time(position)} / {Time(duration)}";

            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(line);
                return;
            }

            Console.Write($"\r{line}");
            _pending = true;
        }

        /// <summary>
        /// Ends the line that is being rewritten, so that the next message
        /// starts on one of its own.
        /// </summary>
        internal void End()
        {
            if (_pending)
            {
                Console.WriteLine();
                _pending = false;
            }
        }
    }

    /// <summary>
    /// Formats a time the way GST_TIME_FORMAT does.
    /// </summary>
    /// <param name="nanoseconds">The time, or a negative number when unknown.</param>
    /// <returns>The formatted time.</returns>
    private static string Time(long nanoseconds) =>
        nanoseconds < 0 ? ClockTime.None.ToString() : ClockTime.FromNanoseconds((ulong)nanoseconds).ToString();

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the media to play.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets a value indicating whether the run needs no display.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the position at which the stream is seeked.</summary>
        internal TimeSpan SeekAt { get; private set; } = TimeSpan.FromSeconds(10);

        /// <summary>Gets the position that is seeked to.</summary>
        internal TimeSpan SeekTo { get; private set; } = TimeSpan.FromSeconds(30);

        /// <summary>Gets how long the pipeline may take.</summary>
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

                    case "--seek-at":
                        options.SeekAt = Cli.SecondsOf(arguments, ref i);
                        break;

                    case "--seek-to":
                        options.SeekTo = Cli.SecondsOf(arguments, ref i);
                        break;

                    case "--native-path":
                        options.Native.NativeSearchPath = Cli.ValueOf(arguments, ref i);
                        break;

                    case "--flavor":
                        options.Native.WindowsFlavor = Cli.FlavorOf(Cli.ValueOf(arguments, ref i));
                        break;

                    case "--timeout":
                        options.Timeout = Cli.SecondsOf(arguments, ref i);
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
    /// Reads a number of seconds that follows an option.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <param name="index">The index of the option, advanced to its value.</param>
    /// <returns>The duration.</returns>
    internal static TimeSpan SecondsOf(string[] arguments, ref int index) =>
        TimeSpan.FromSeconds(double.Parse(ValueOf(arguments, ref index), CultureInfo.InvariantCulture));

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
