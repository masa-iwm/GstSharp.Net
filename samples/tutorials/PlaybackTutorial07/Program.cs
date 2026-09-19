// Playback tutorial 7: custom playbin sinks — giving playbin a bin of its own
// making, an equalizer in front of an audio sink, hidden behind a ghost pad so
// that playbin sees one element.
//
// Ported from playback-tutorial-7.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/custom-playbin-sinks.html
//
// Usage: PlaybackTutorial07 [<uri-or-file>] [--headless]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * gst_object_unref is gone. The elements, the pads and the bus are interned
//     GObject wrappers, so nothing here disposes them: the bin takes a reference
//     to what is added to it, ghost pad included, playbin takes one to the bin
//     when audio-sink is written, and disposing the pipeline is the one
//     sanctioned Dispose. That includes the static pad the ghost pad is built
//     on, which the C unrefs.
//
//   * audio-sink is object-valued, so the bin goes in through a GValue.
//     gst_util_set_object_arg deserializes a string into the type of the
//     property and GStreamer registers no deserializer for an object, which is
//     the same reason PlaybackTutorial06 writes vis-plugin that way.
//
//   * band1 and band2 are gdouble properties, and SetProperty takes the double
//     literals the C passes as (gdouble) -24.0.
//
//   * The C program blocks on gst_bus_timed_pop_filtered with
//     GST_CLOCK_TIME_NONE. Here the same call is made in 50 ms slices inside a
//     loop bounded by --timeout, which is the house style: no main loop, the
//     application owns its thread. This media is meant to end, so the bound
//     elapsing is a failure.
//
//   * equalizer-3bands lives in gst-plugins-good. It is the whole point of the
//     tutorial rather than one branch of it, so an installation without it ends
//     the run with a message, as the C original does.
//
//   * --headless is not part of the tutorial. The upstream media carries video
//     as well as audio, so it replaces playbin's video-sink with a fakesink and
//     ends the sink bin in a fakesink instead of autoaudiosink: the equalizer,
//     the converter and the ghost pad — everything the tutorial is about — are
//     built and run exactly as they are otherwise. Neither fakesink waits for
//     the clock, so an unattended run takes the time it takes to decode rather
//     than the running time of the media.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return CustomSinks.Run(args);

internal static class CustomSinks
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// Builds the sink bin, hands it to playbin and plays.
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

            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybackTutorial07: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                if (BuildSinkBin(options) is not Bin bin)
                {
                    return 1;
                }

                // The bin is never disposed. Bin.New hands back a wrapper that
                // has already sunk the floating reference and holds one of its
                // own, and playbin takes a reference of its own when audio-sink
                // is written — exactly as PlaybackTutorial06's vis-plugin does,
                // through a GValue for the same reason. See docs/ownership.md.
                using (Gst.GObject.Value value = Gst.GObject.Value.New(bin.NativeType))
                {
                    value.SetObject(bin);
                    pipeline.SetProperty("audio-sink", value);
                }

                if (options.Headless)
                {
                    // Only the video sink: the audio one is the bin above, and
                    // it already ends in a fakesink of its own.
                    if (ElementFactory.Make("fakesink", null) is not Element video)
                    {
                        Console.Error.WriteLine("PlaybackTutorial07: fakesink could not be created.");
                        return 1;
                    }

                    Global.UtilSetObjectArg(video, "sync", "false");

                    using Gst.GObject.Value value = Gst.GObject.Value.New(video.NativeType);
                    value.SetObject(video);
                    pipeline.SetProperty("video-sink", value);
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial07: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Builds <c>equalizer-3bands ! audioconvert ! sink</c> in a bin whose only
    /// pad is a ghost of the equalizer's.
    /// </summary>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>The bin, or <see langword="null"/> when it could not be built.</returns>
    private static Bin? BuildSinkBin(Options options)
    {
        Element? equalizer = ElementFactory.Make("equalizer-3bands", "equalizer");
        Element? convert = ElementFactory.Make("audioconvert", "convert");
        Element? sink = ElementFactory.Make(options.Headless ? "fakesink" : "autoaudiosink", "audio_sink");

        if (equalizer is null || convert is null || sink is null)
        {
            Console.Error.WriteLine("Not all elements could be created.");
            return null;
        }

        if (options.Headless)
        {
            // Nothing listens, so the sink has no reason to keep time.
            Global.UtilSetObjectArg(sink, "sync", "false");
        }

        if (Bin.New("audio_sink_bin") is not Bin bin)
        {
            Console.Error.WriteLine("PlaybackTutorial07: the sink bin could not be created.");
            return null;
        }

        if (!bin.AddMany(equalizer, convert, sink) || !equalizer.Link(convert, sink))
        {
            Console.Error.WriteLine("PlaybackTutorial07: the sink bin could not be linked.");
            return null;
        }

        // The bin has to look like one element to playbin, and a ghost pad is
        // how the sink pad of the first element becomes the sink pad of the bin.
        if (equalizer.GetStaticPad("sink") is not Pad pad)
        {
            Console.Error.WriteLine("PlaybackTutorial07: the equalizer has no sink pad.");
            return null;
        }

        if (GhostPad.New("sink", pad) is not Pad ghost)
        {
            Console.Error.WriteLine("PlaybackTutorial07: the ghost pad could not be created.");
            return null;
        }

        ghost.SetActive(true);

        if (!bin.AddPad(ghost))
        {
            Console.Error.WriteLine("PlaybackTutorial07: the ghost pad could not be added to the bin.");
            return null;
        }

        // Two bands pulled all the way down, which is what makes the effect
        // audible on a manual run.
        equalizer.SetProperty("band1", -24.0);
        equalizer.SetProperty("band2", -24.0);

        return bin;
    }

    /// <summary>
    /// Plays the pipeline until it ends, fails, or runs out of time.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial07: Unable to set the pipeline to the playing state.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine(
                        $"Error received from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"Debugging information: {debug ?? "none"}");
                    return 1;
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                return 0;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial07: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
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
