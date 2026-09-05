// A timeline whose clip and whose source are managed types: the clip builds its
// own track element and the source answers the element behind it, both through
// overrides of the editing services' class struct slots. It is the smallest
// application that exercises the child contract of docs/subclassing.md §11.
//
// Usage: GesCustomSource [--timeout <seconds>] [--native-path <directory>]
//                        [--flavor msvc|mingw]
//
// It is headless and bounded: the source is a videotestsrc, the preview sink is
// a fakesink and the clip is half a second long, so the run ends at the end of
// stream on any machine that has the base plugins.
//
// Everything runs on this thread. The editing services assert the thread a
// timeline and its tracks were created on, so a Task.Run around any of this
// would abort the process rather than fail.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using GES;
using Gst;
using Gst.GLib;
using Gst.Interop;

return CustomSourceSample.Run(args);

internal static class CustomSourceSample
{
    /// <summary>How long the clip is.</summary>
    private static readonly ClockTime Length = ClockTime.FromMilliseconds(500);

    /// <summary>How long one poll of the bus waits.</summary>
    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

    /// <summary>
    /// Builds the timeline, plays it and reports what the bus said.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 on end of stream, 1 on any error or on the timeout.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);

            // Initialising through the module rather than through GstSharp is
            // what runs ges_init and puts the editing services into the type
            // registry before the two subclasses below register themselves.
            GstGES.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");
            Console.WriteLine($"clip:        {CustomSourceClip.GTypeName}");
            Console.WriteLine($"source:      {CustomVideoSource.GTypeName}");

            // A video-only timeline: the clip is asked for a video child and
            // for nothing else, which is what keeps the audio side of the
            // question - a track type the override answers null for - out of
            // this sample.
            using Timeline timeline = Timeline.New();
            using VideoTrack track = VideoTrack.New();

            if (!timeline.AddTrack(track))
            {
                Console.Error.WriteLine("GesCustomSource: the video track was refused.");
                return 1;
            }

            using Layer layer = timeline.AppendLayer();

            // The clip is extracted from an asset for its own type, which is
            // the same contract its child follows: a clip built with new has
            // no asset and the layer would remove it again.
            using CustomSourceClip clip = CustomSourceClip.New();

            clip.SupportedFormats = TrackType.Video;

            if (!clip.SetStart(ClockTime.Zero) || !clip.SetDuration(Length))
            {
                Console.Error.WriteLine("GesCustomSource: the clip refused its start or its duration.");
                return 1;
            }

            // This is what runs create_track_element, which runs create_source
            // for the child it answered.
            if (!layer.AddClip(clip))
            {
                Console.Error.WriteLine("GesCustomSource: the clip was refused by the layer.");
                return 1;
            }

            Report(clip);

            return Play(timeline, options.Timeout);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GesCustomSource: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Prints what the two overrides built, which is the contract this sample
    /// is about.
    /// </summary>
    /// <param name="clip">The clip that was added to the layer.</param>
    private static void Report(CustomSourceClip clip)
    {
        IReadOnlyList<TimelineElement> children = clip.GetChildren(false);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"children:    {children.Count}"));

        // The child is the very wrapper the override answered: the interning
        // is what makes the two the same object rather than two wrappers for
        // one instance.
        CustomVideoSource? child = clip.AnsweredChild;

        Console.WriteLine($"child:       {child?.Name ?? "none"}");
        Console.WriteLine($"interned:    {children.Count == 1 && ReferenceEquals(children[0], child)}");
        Console.WriteLine($"element:     {child?.BuiltElement ?? "none"}");
    }

    /// <summary>
    /// Plays the timeline through a preview pipeline and pumps its bus until it
    /// ends, fails or runs out of time.
    /// </summary>
    /// <param name="timeline">The timeline to play.</param>
    /// <param name="timeout">How long the run may take.</param>
    /// <returns>0 on end of stream, 1 on any error or on the timeout.</returns>
    private static int Play(Timeline timeline, TimeSpan timeout)
    {
        using GES.Pipeline pipeline = GES.Pipeline.New();

        try
        {
            if (!pipeline.SetTimeline(timeline))
            {
                Console.Error.WriteLine("GesCustomSource: the pipeline refused the timeline.");
                return 1;
            }

            // Headless: the preview goes nowhere. The sink is set before the
            // pipeline leaves NULL, which is the only window a preview sink
            // can be chosen in.
            using Element sink = ElementFactory.Make("fakesink", null)
                ?? throw new InvalidOperationException("fakesink is not installed.");

            sink.SetProperty("sync", false);
            pipeline.PreviewSetVideoSink(sink);

            // The bus wrapper is an interned GObject wrapper, shared with
            // every other lookup of the same bus, so it is not disposed here.
            Bus bus = pipeline.GetBus();

            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("GesCustomSource: the pipeline refused to go to PLAYING.");
                Drain(bus);
                return 1;
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    PollInterval,
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    continue;
                }

                if (message.Type == MessageType.Eos)
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                    return 0;
                }

                PrintError(message);
                return 1;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"GesCustomSource: no end of stream within {timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // Back to NULL before anything is released: a pipeline that is
            // still PLAYING when its last reference goes away leaves its
            // streaming threads running.
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>Prints an error message together with the element that posted it.</summary>
    /// <param name="message">The error message.</param>
    private static void PrintError(Message message)
    {
        (GException error, string? debug) = message.ParseError();

        Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"domain:      {error.Domain} ({error.Code})"));
        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
    }

    /// <summary>Prints whatever the bus already holds after a failed state change.</summary>
    /// <param name="bus">The bus of the pipeline.</param>
    private static void Drain(Bus bus)
    {
        while (bus.PopFiltered(MessageType.Error | MessageType.Warning) is Message message)
        {
            using (message)
            {
                if (message.Type == MessageType.Error)
                {
                    PrintError(message);
                    continue;
                }

                (GException warning, string? debug) = message.ParseWarning();
                Console.Error.WriteLine($"warning:     {message.SourceName ?? "?"}: {warning.Message}");
                Console.Error.WriteLine($"debug:       {debug ?? "none"}");
            }
        }
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets how long the run may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(10);

        /// <summary>Gets the options of the native loader.</summary>
        internal GstSharpOptions Native { get; } = new();

        /// <summary>Reads the command line.</summary>
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
                    case "--native-path":
                        options.Native.NativeSearchPath = ValueOf(arguments, ref i);
                        break;

                    case "--flavor":
                        options.Native.WindowsFlavor = ValueOf(arguments, ref i).ToUpperInvariant() switch
                        {
                            "MSVC" => GstFlavor.Msvc,
                            "MINGW" => GstFlavor.MinGW,
                            string other => throw new ArgumentException(
                                $"\"{other}\" is not a flavor. Use msvc or mingw.",
                                nameof(arguments)),
                        };
                        break;

                    case "--timeout":
                        options.Timeout = TimeSpan.FromSeconds(double.Parse(
                            ValueOf(arguments, ref i),
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

        /// <summary>Reads the value that follows an option.</summary>
        /// <param name="arguments">The arguments of the process.</param>
        /// <param name="index">The index of the option, advanced to its value.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException">The option has no value.</exception>
        private static string ValueOf(string[] arguments, ref int index)
        {
            if (index + 1 >= arguments.Length)
            {
                throw new ArgumentException($"\"{arguments[index]}\" needs a value.", nameof(arguments));
            }

            return arguments[++index];
        }
    }
}
