// Basic tutorial 12: streaming — what a pipeline does while it waits for a
// network to catch up, and what a live source changes about that.
//
// Ported from basic-tutorial-12.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/streaming.html
//
// Usage: BasicTutorial12 [<uri-or-file>] [--headless]
//                        [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop and no signal watch on the bus. The C program calls
//     gst_bus_add_signal_watch and connects to "message"; here the loop below
//     pops the same messages with TimedPopFiltered, which is the shape every
//     tutorial in this directory uses and needs no main loop to pump it.
//
//   * On ERROR and on EOS the C program sets the pipeline to READY before
//     quitting its loop. This one returns straight away and the finally block
//     takes it all the way to NULL, which is what disposing a pipeline needs
//     anyway — READY was only there so that the main loop could still be
//     running when the state change happened.
//
//   * A trailing newline is printed at the end. The buffering line ends in a
//     carriage return, exactly as upstream, so without it the last percentage
//     would sit unterminated on the log of an unattended run.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card.
//
//   * --timeout bounds the run. A pipeline that was supposed to end and did
//     not is a failure here, which is what lets CI use the exit code as a gate;
//     the C program simply waits forever.
//
//   * The error path prints the debug string on a second line. The C throws it
//     away after freeing it, which loses the one part of the message that says
//     which element and which file the failure came from.
//
//   * A file:// URI never buffers, so pointing this at a local path shows the
//     live/no-preroll and clock-lost paths but not the buffering one. Serve the
//     same file over http to see BUFFERING messages, which is what CI does.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return Streaming.Run(args);

internal static class Streaming
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// Plays a stream and follows it through its buffering.
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
                Console.Error.WriteLine("BasicTutorial12: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                if (options.Headless && !Silence(pipeline))
                {
                    return 1;
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial12: {exception}");
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
                Console.Error.WriteLine("BasicTutorial12: fakesink could not be created.");
                return false;
            }

            // Buffering is a statement about time, so the sink has to keep
            // time: a sink that runs as fast as it is fed never underruns and
            // never asks anything to buffer.
            Global.UtilSetObjectArg(sink, "sync", "true");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the pipeline and answers the messages a stream posts.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the stream ended, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            StateChangeReturn started = pipeline.SetState(State.Playing);

            if (started == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial12: Unable to set the pipeline to the playing state.");
                return 1;
            }

            // A live source has nothing to preroll from and nothing to buffer:
            // the data arrives when it arrives. That is the one thing this
            // program has to know before it starts answering BUFFERING.
            bool isLive = started == StateChangeReturn.NoPreroll;

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos | MessageType.Buffering | MessageType.ClockLost);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

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
                            // A live source has no buffer to fill: the data
                            // arrives when it arrives, and pausing would only
                            // drop it.
                            break;
                        }

                        message.ParseBuffering(out int percent);
                        Console.Write(string.Create(CultureInfo.InvariantCulture, $"Buffering ({percent,3}%)\r"));

                        // Pausing while the buffer fills is the whole of the
                        // answer: playing on with nothing to play is what a
                        // stall looks like.
                        pipeline.SetState(percent < 100 ? State.Paused : State.Playing);
                        break;

                    case MessageType.ClockLost:
                        // The clock a sink was providing has gone. A round trip
                        // through PAUSED is what makes the pipeline choose
                        // another one.
                        pipeline.SetState(State.Paused);
                        pipeline.SetState(State.Playing);
                        break;

                    default:
                        break;
                }
            }

            Console.WriteLine();
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial12: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
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
