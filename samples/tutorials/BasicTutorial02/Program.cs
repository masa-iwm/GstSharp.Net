// Basic tutorial 2: GStreamer concepts — elements, a pipeline to hold them, a
// link between them, a property, and a bus message read properly.
//
// Ported from basic-tutorial-2.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/concepts.html
//
// Usage: BasicTutorial02 [--headless] [--native-path <directory>]
//                        [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * g_object_set (source, "pattern", 0, NULL) is variadic and typed; the
//     binding has no variadic call, and "pattern" is an enumeration a plugin
//     registered, so no generated property stands for it. Gst.Global
//     .UtilSetObjectArg is the answer the binding gives: it is
//     gst_util_set_object_arg, the same string-to-property conversion
//     gst-launch-1.0 performs, so "smpte" — the nickname of pattern 0 — sets it
//     without the application having to name a GType.
//
//   * The bus is polled in short slices rather than waited on forever, which is
//     the house style: no main loop, the application owns its thread. See
//     docs/ownership.md, "Applications without a main loop".
//
//   * gst_object_unref is gone. The elements and the bus are interned GObject
//     wrappers — one wrapper per native object for the whole process — so
//     nothing here disposes them; the pipeline owns the elements that were
//     added to it, and disposing the pipeline is the one sanctioned Dispose.
//     The message is a mini object, so `using` releases it.
//
//   * --headless is not part of the tutorial. It swaps the autovideosink for a
//     fakesink and bounds videotestsrc, so that the program can be run as a
//     gate on a machine with no display: the C original opens a window and
//     plays until it is closed, which nothing headless can do. Everything
//     between those two elements stays the same.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return Concepts.Run(args);

internal static class Concepts
{
    /// <summary>How many frames the source produces when it has to end.</summary>
    private const string HeadlessBuffers = "300";

    /// <summary>
    /// Builds the pipeline element by element, runs it and reports what the bus
    /// said.
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
            Console.WriteLine($"sink:        {options.Sink}");

            // gst_element_factory_make. The name is what the element is called
            // inside its pipeline; passing null lets GStreamer invent one.
            Element? source = ElementFactory.Make("videotestsrc", "source");
            Element? sink = ElementFactory.Make(options.Sink, "sink");

            // gst_pipeline_new. A pipeline is a bin that owns a clock and a
            // bus, which is what makes it the thing that can be played.
            Pipeline? pipeline = Pipeline.New("test-pipeline");

            if (source is null || sink is null || pipeline is null)
            {
                Console.Error.WriteLine("BasicTutorial02: not all elements could be created.");
                return 1;
            }

            using (pipeline)
            {
                // gst_bin_add_many. The pipeline takes the elements over: they
                // are its children now and go away with it, which is why
                // nothing below unrefs them.
                pipeline.AddMany(source, sink);

                // gst_element_link. It fails when the two pads cannot agree on
                // a format, and here it cannot: videotestsrc offers raw video
                // and every video sink takes it.
                if (!source.Link(sink))
                {
                    Console.Error.WriteLine("BasicTutorial02: the elements could not be linked.");
                    return 1;
                }

                // The C tutorial writes the integer 0. "smpte" is the nickname
                // of that same value, and going through the nickname is what
                // lets a string-shaped API set an enumeration no header on this
                // side describes.
                Global.UtilSetObjectArg(source, "pattern", "smpte");

                if (options.Headless)
                {
                    // Nothing about the tutorial, everything about being able
                    // to run it unattended: an endless source has no end of
                    // stream to wait for.
                    Global.UtilSetObjectArg(source, "num-buffers", HeadlessBuffers);
                    Global.UtilSetObjectArg(sink, "sync", "false");
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial02: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
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
            // Unlike gst_element_set_state in the first tutorial, this one is
            // checked: an element that cannot reach PLAYING says so here.
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial02: the pipeline refused to go to PLAYING.");
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
                        // This is what the first tutorial promised: the error
                        // with the element that posted it and the debugging
                        // string beside it.
                        (GException error, string? debug) = message.ParseError();
                        Console.Error.WriteLine(
                            $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                        return 1;

                    case MessageType.Eos:
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    default:
                        // Unreachable: only those two types were asked for.
                        Console.Error.WriteLine($"BasicTutorial02: unexpected {message.Type} message.");
                        return 1;
                }
            }

            if (options.Headless)
            {
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"BasicTutorial02: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
                return 1;
            }

            // An endless videotestsrc never posts one, so running out of time
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
        /// <summary>Gets a value indicating whether the run needs no display.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the factory of the sink to build.</summary>
        internal string Sink => Headless ? "fakesink" : "autovideosink";

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
