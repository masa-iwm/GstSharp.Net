// Basic tutorial 7: multithreading and pad availability — one source split by a
// tee into an audio branch and a visualization branch, each with a queue in
// front of it so that the two run on threads of their own.
//
// Ported from basic-tutorial-7.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/multithreading-and-pad-availability.html
//
// Usage: BasicTutorial07 [--headless] [--buffers <count>]
//                        [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * g_object_set (audio_source, "freq", 215.0f, NULL) is variadic and typed,
//     and "freq" is a plugin property no generated member stands for. Gst
//     .Global.UtilSetObjectArg is the answer the binding gives — it is
//     gst_util_set_object_arg, the same string-to-property conversion
//     gst-launch-1.0 performs — so the value is written as the string
//     "215.0". The same call sets the two enumerations of wavescope through
//     their nicknames: the C original writes the integers 0 and 1, and "none"
//     and "lines" are the nicknames of those very values.
//
//   * A tee has no source pad until one is asked for, which is the whole point
//     of the tutorial. RequestPadSimple is gst_element_request_pad_simple, and
//     what comes back has to be given back: ReleaseRequestPad in a finally
//     block is the gst_element_release_request_pad of the C original. The
//     gst_object_unref that follows it there has no counterpart here, because
//     a pad is an interned GObject wrapper that stands for the whole process.
//
//   * wavescope lives in gst-plugins-bad. Where it is not installed the
//     visualization branch is left out and the program says so, exactly as
//     BasicTutorial08 does: an element is a plugin that may or may not be
//     there, and a program that discovers that at run time is more useful than
//     one that dies. What is left is still the lesson — a tee with a request
//     pad feeding a queue — with one branch instead of two.
//
//   * The bus is polled in short slices rather than waited on forever, which is
//     the house style: no main loop, the application owns its thread. See
//     docs/ownership.md, "Applications without a main loop". The C original
//     blocks in gst_bus_timed_pop_filtered with GST_CLOCK_TIME_NONE, which on
//     an endless audiotestsrc means until it is killed.
//
//   * gst_object_unref is gone. The elements, the bus and the pads are interned
//     GObject wrappers, so nothing here disposes them; the pipeline owns the
//     elements that were added to it, and disposing the pipeline is the one
//     sanctioned Dispose.
//
//   * --headless and --buffers are not part of the tutorial. --headless swaps
//     both automatic sinks for fakesinks that do not wait for the clock, and
//     --buffers says how many buffers audiotestsrc produces before the stream
//     ends. Either one bounds the run on its own: --headless has to, because
//     an unattended run has to finish, and giving --buffers explicitly asks
//     for it, so that a run which does open its windows can be bounded too. A
//     manual run with neither is the C original: two windows' worth of nothing
//     in particular, playing until the bound of --timeout elapses — and that
//     one ends with exit code 0, because the source was never given an end.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return Multithreading.Run(args);

internal static class Multithreading
{
    /// <summary>
    /// The frequency the test source is tuned to, as the string
    /// <c>gst_util_set_object_arg</c> parses into the float the C program
    /// passes.
    /// </summary>
    private const string Frequency = "215.0";

    /// <summary>
    /// Builds the pipeline, asks the tee for its pads, runs it and reports what
    /// the bus said.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the run ended as it should, 1 on any error.</returns>
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
            Console.WriteLine($"sinks:       {options.AudioSink} / {options.VideoSink}");

            Element? audioSource = ElementFactory.Make("audiotestsrc", "audio_source");
            Element? tee = ElementFactory.Make("tee", "tee");
            Element? audioQueue = ElementFactory.Make("queue", "audio_queue");
            Element? audioConvert = ElementFactory.Make("audioconvert", "audio_convert");
            Element? audioResample = ElementFactory.Make("audioresample", "audio_resample");
            Element? audioSink = ElementFactory.Make(options.AudioSink, "audio_sink");
            Pipeline? pipeline = Pipeline.New("test-pipeline");

            if (audioSource is null || tee is null || audioQueue is null || audioConvert is null ||
                audioResample is null || audioSink is null || pipeline is null)
            {
                Console.Error.WriteLine("BasicTutorial07: not all elements could be created.");
                return 1;
            }

            using (pipeline)
            {
                Global.UtilSetObjectArg(audioSource, "freq", Frequency);

                if (options.Bounded)
                {
                    // Nothing about the tutorial, everything about being able
                    // to run it unattended: an endless source has no end of
                    // stream to wait for. --headless implies this because an
                    // unattended run has to finish, and --buffers on its own
                    // asks for it, so that a bound can also be put on a run
                    // that does open windows.
                    Global.UtilSetObjectArg(
                        audioSource,
                        "num-buffers",
                        options.Buffers.ToString(CultureInfo.InvariantCulture));
                }

                if (options.Headless)
                {
                    // A sink that waits for the clock would make the run last
                    // as long as the audio does.
                    Global.UtilSetObjectArg(audioSink, "sync", "false");
                }

                pipeline.AddMany(audioSource, tee, audioQueue, audioConvert, audioResample, audioSink);

                // Everything with "Always" pads links itself; the tee is what
                // has to be asked, and that is done below.
                if (!audioSource.Link(tee) ||
                    !audioQueue.Link(audioConvert, audioResample, audioSink))
                {
                    Console.Error.WriteLine("BasicTutorial07: the elements could not be linked.");
                    return 1;
                }

                List<Pad> requested = [];

                try
                {
                    if (!RequestBranch(tee, audioQueue, "audio", requested) ||
                        !AddVisualization(pipeline, tee, options, requested))
                    {
                        return 1;
                    }

                    return Play(pipeline, options);
                }
                finally
                {
                    // gst_element_release_request_pad. A request pad outlives
                    // the link that used it, so giving it back is the caller's
                    // job whatever happened above.
                    foreach (Pad pad in requested)
                    {
                        tee.ReleaseRequestPad(pad);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial07: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Adds the visualization branch of the tee, when the plugin that draws it
    /// is installed.
    /// </summary>
    /// <param name="pipeline">The pipeline the branch is added to.</param>
    /// <param name="tee">The tee the branch hangs off.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <param name="requested">The request pads to release afterwards.</param>
    /// <returns><see langword="false"/> when the branch is wanted but could not be built.</returns>
    private static bool AddVisualization(Pipeline pipeline, Element tee, Options options, List<Pad> requested)
    {
        // wavescope is in gst-plugins-bad, which plenty of installations do not
        // have. That is not an error: it is one branch of two, and the tee is
        // still a tee with one.
        if (ElementFactory.Make("wavescope", "visual") is not Element visual)
        {
            Console.WriteLine("visual:      wavescope is not installed, running without the video branch.");
            return true;
        }

        Element? videoQueue = ElementFactory.Make("queue", "video_queue");
        Element? videoConvert = ElementFactory.Make("videoconvert", "video_convert");
        Element? videoSink = ElementFactory.Make(options.VideoSink, "video_sink");

        if (videoQueue is null || videoConvert is null || videoSink is null)
        {
            Console.Error.WriteLine("BasicTutorial07: the video branch could not be created.");
            return false;
        }

        // The C program writes the integers 0 and 1. These are the nicknames of
        // those two values, which is how a string-shaped API sets an
        // enumeration that no header on this side describes.
        Global.UtilSetObjectArg(visual, "shader", "none");
        Global.UtilSetObjectArg(visual, "style", "lines");

        if (options.Headless)
        {
            Global.UtilSetObjectArg(videoSink, "sync", "false");
        }

        pipeline.AddMany(videoQueue, visual, videoConvert, videoSink);

        if (!videoQueue.Link(visual, videoConvert, videoSink))
        {
            Console.Error.WriteLine("BasicTutorial07: the video branch could not be linked.");
            return false;
        }

        return RequestBranch(tee, videoQueue, "video", requested);
    }

    /// <summary>
    /// Asks the tee for a source pad and links it to the head of a branch.
    /// </summary>
    /// <param name="tee">The tee to ask.</param>
    /// <param name="head">The element the branch starts with.</param>
    /// <param name="label">What the branch is called, for the log line.</param>
    /// <param name="requested">The request pads to release afterwards.</param>
    /// <returns><see langword="true"/> when the branch is linked.</returns>
    private static bool RequestBranch(Element tee, Element head, string label, List<Pad> requested)
    {
        // A tee has no source pad until one is asked for: "src_%u" is the name
        // of the template, and the pad that comes back is called src_0, src_1
        // and so on. The sink pad of a queue is always there, which is the
        // difference the tutorial is about.
        Pad? teePad = tee.RequestPadSimple("src_%u");
        Pad? queuePad = head.GetStaticPad("sink");

        if (teePad is null || queuePad is null)
        {
            Console.Error.WriteLine($"BasicTutorial07: the {label} branch has no pad to link.");
            return false;
        }

        requested.Add(teePad);
        Console.WriteLine($"tee:         obtained request pad {teePad.Name} for the {label} branch.");

        PadLinkReturn result = teePad.Link(queuePad);

        if (result == PadLinkReturn.Ok)
        {
            return true;
        }

        Console.Error.WriteLine($"BasicTutorial07: the {label} branch could not be linked ({result}).");
        return false;
    }

    /// <summary>
    /// Plays the pipeline and reads the bus until the run ends.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as it should, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial07: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                switch (message.Type)
                {
                    case MessageType.Error:
                        (GException error, string? debug) = message.ParseError();
                        Console.Error.WriteLine(
                            $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                        return 1;

                    case MessageType.Eos:
                        // Every branch of the tee has to have seen the end for
                        // the pipeline to post one, which is the multithreading
                        // half of the lesson arriving as a message.
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    default:
                        // Unreachable: only those two types were asked for.
                        Console.Error.WriteLine($"BasicTutorial07: unexpected {message.Type} message.");
                        return 1;
                }
            }

            if (options.Bounded)
            {
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"BasicTutorial07: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
                return 1;
            }

            // An endless audiotestsrc never posts one, so running out of time
            // is how a manual run ends. The C original ends the same way, with
            // the window closed instead of a stopwatch.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"stop:        after {options.Timeout.TotalSeconds:F0} s, the source has no end of its own."));
            return 0;
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
        private bool _buffersGiven;

        /// <summary>Gets a value indicating whether the run needs no display and no sound card.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the factory of the audio sink to build.</summary>
        internal string AudioSink => Headless ? "fakesink" : "autoaudiosink";

        /// <summary>Gets the factory of the video sink to build.</summary>
        internal string VideoSink => Headless ? "fakesink" : "autovideosink";

        /// <summary>Gets how many buffers the source produces when it has to end.</summary>
        internal int Buffers { get; private set; } = 400;

        /// <summary>
        /// Gets a value indicating whether the source is given an end of its
        /// own, which <c>--headless</c> needs and <c>--buffers</c> asks for.
        /// </summary>
        internal bool Bounded => Headless || _buffersGiven;

        /// <summary>Gets how long the pipeline may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

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

            for (int i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "--headless":
                        options.Headless = true;
                        break;

                    case "--buffers":
                        options.Buffers = int.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture);
                        options._buffersGiven = true;
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
                        throw new ArgumentException(
                            $"\"{arguments[i]}\" is not a known argument.",
                            nameof(arguments));
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
