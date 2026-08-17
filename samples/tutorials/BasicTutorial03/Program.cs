// Basic tutorial 3: dynamic pipelines — a source whose pads appear only once it
// knows what it is decoding, and an application that links them when they do.
//
// Ported from basic-tutorial-3.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/dynamic-pipelines.html
//
// Usage: BasicTutorial03 [<uri-or-file>] [--headless] [--native-path <directory>]
//                        [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * g_signal_connect (source, "pad-added", ...) with a CustomData pointer is
//     a typed .NET event here: Element.PadAdded, whose argument carries the new
//     pad. The state the C code passes through the user-data pointer is
//     captured by the lambda instead, which is also what keeps it alive — see
//     the note above the handler.
//
//   * The handler still runs on a streaming thread of uridecodebin, exactly as
//     it does in C. Nothing in this binding moves it to the application thread,
//     so what it does has to be safe there. An exception that leaves it does
//     not cross the native frame: the trampoline of the binding catches it and
//     reports it through Gst.Interop.ExceptionTrap, which is subscribed below
//     so that a failed link cannot pass for a link that never happened.
//
//   * gst_object_unref (sink_pad) is gone: a Pad is a GObject wrapper, interned
//     and shared, so the application does not release it. gst_caps_unref is
//     `using`, because a Caps is a mini object and its wrapper owns a
//     reference. The Structure read out of the caps is `using` as well and is a
//     copy rather than a window into them — see docs/ownership.md.
//
//   * The bus is polled in short slices rather than waited on forever.
//
//   * --headless is not part of the tutorial. It swaps the autoaudiosink for a
//     fakesink that does not wait for the clock, so the program can be run as a
//     gate on a machine with no sound card. An audio sink is the worst thing to
//     put in an unattended run: in an environment with no sound daemon it does
//     not fail, it waits.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return DynamicPipelines.Run(args);

internal static class DynamicPipelines
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// Builds the half-linked pipeline, plays it and links the rest when the
    /// source says what it found.
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
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"uri:         {options.Uri}");
            Console.WriteLine($"sink:        {options.Sink}");

            Element? source = ElementFactory.Make("uridecodebin", "source");
            Element? convert = ElementFactory.Make("audioconvert", "convert");
            Element? resample = ElementFactory.Make("audioresample", "resample");
            Element? sink = ElementFactory.Make(options.Sink, "sink");
            Pipeline? pipeline = Pipeline.New("test-pipeline");

            if (source is null || convert is null || resample is null || sink is null || pipeline is null)
            {
                Console.Error.WriteLine("BasicTutorial03: not all elements could be created.");
                return 1;
            }

            using (pipeline)
            {
                pipeline.AddMany(source, convert, resample, sink);

                // The source is deliberately not linked here: it has no source
                // pad yet. Everything behind it can be linked now, because
                // those pads are always there.
                if (!convert.Link(resample, sink))
                {
                    Console.Error.WriteLine("BasicTutorial03: the elements could not be linked.");
                    return 1;
                }

                Global.UtilSetObjectArg(source, "uri", options.Uri);

                if (options.Headless)
                {
                    Global.UtilSetObjectArg(sink, "sync", "false");
                }

                // What the C code passes as a user-data pointer is captured
                // here. The capture keeps `convert` reachable for as long as
                // the source holds the handler, and the source lets go of it
                // when the pipeline is torn down — which is why the pipeline is
                // disposed rather than dropped.
                EventHandler<Element.PadAddedSignalArgs> handler =
                    (sender, args) => OnPadAdded(convert, args.NewPad);

                source.PadAdded += handler;

                try
                {
                    return Play(pipeline, options.Timeout);
                }
                finally
                {
                    // The pipeline is already back at NULL by here, so the
                    // streaming thread that ran the handler is gone and taking
                    // the handler off cannot race with it.
                    source.PadAdded -= handler;
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial03: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Links a pad the source has just grown, if it carries raw audio and if
    /// nothing is linked yet.
    /// </summary>
    /// <param name="convert">The converter whose sink pad is the target.</param>
    /// <param name="newPad">The pad the source announced.</param>
    /// <remarks>
    /// This runs on a streaming thread of the source.
    /// </remarks>
    private static void OnPadAdded(Element convert, Pad newPad)
    {
        // Interned: this is the same wrapper every other lookup of that pad
        // hands out, and the application does not release it.
        Pad? sinkPad = convert.GetStaticPad("sink");

        if (sinkPad is null)
        {
            Console.Error.WriteLine("pad-added:   the converter has no sink pad.");
            return;
        }

        Console.WriteLine($"pad-added:   \"{newPad.Name}\" from \"{newPad.Parent?.Name ?? "?"}\"");

        // A media file with a video stream produces a second pad. The first one
        // that fits took the converter, and there is nothing to do with the
        // rest.
        if (sinkPad.IsLinked())
        {
            Console.WriteLine("             already linked, ignoring.");
            return;
        }

        // The caps of a pad that has just appeared say what it will carry. The
        // wrapper owns a reference of its own, so it is disposed here;
        // GetStructure hands out a copy of the structure, which is disposed as
        // well.
        using Caps? caps = newPad.GetCurrentCaps();

        if (caps is null || caps.GetSize() == 0)
        {
            Console.WriteLine("             no current caps, ignoring.");
            return;
        }

        using Structure structure = caps.GetStructure(0);
        string type = structure.GetName();

        if (!type.StartsWith("audio/x-raw", StringComparison.Ordinal))
        {
            Console.WriteLine($"             type is \"{type}\", which is not raw audio. Ignoring.");
            return;
        }

        PadLinkReturn result = newPad.Link(sinkPad);

        Console.WriteLine(result == PadLinkReturn.Ok
            ? $"             linked (type \"{type}\")."
            : $"             type is \"{type}\" but the link failed with {result}.");
    }

    /// <summary>
    /// Plays the pipeline and reads the bus until the run ends.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="timeout">How long the pipeline may take.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, TimeSpan timeout)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial03: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos | MessageType.StateChanged);

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
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    case MessageType.StateChanged:
                        // Every element posts its own transitions; only the
                        // ones of the pipeline describe the run as a whole.
                        if (message.Src is { } source && source.Handle == pipeline.Handle)
                        {
                            message.ParseStateChanged(out State old, out State current, out _);
                            Console.WriteLine($"state:       {old} -> {current}");
                        }

                        break;

                    default:
                        break;
                }
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial03: no end of stream within {timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Reports a failure that was caught on a callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    private static void OnCallbackFailure(Exception exception) =>
        Console.Error.WriteLine($"BasicTutorial03: the pad-added handler failed: {exception}");

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the media to play.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets a value indicating whether the run needs no sound card.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the factory of the sink to build.</summary>
        internal string Sink => Headless ? "fakesink" : "autoaudiosink";

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
