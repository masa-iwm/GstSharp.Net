// A timeline whose clip and whose sources are managed types: the clip builds
// its own track elements, each source answers the element behind it, the clip
// takes over the split of its own children and the audio source watches every
// child property write reaching it - all through overrides of the editing
// services' class struct slots. It is the smallest application that exercises
// the child contract of docs/subclassing.md §11.
//
// Usage: GesCustomSource [--timeout <seconds>] [--native-path <directory>]
//                        [--flavor msvc|mingw]
//
// It is headless and bounded: the sources are a videotestsrc and an
// audiotestsrc, both preview sinks are fakesinks and the clip is half a second
// long, so the run ends at the end of stream on any machine that has the nle
// and ges plugins and the base and good plugin sets a video and an audio source
// bin are built from.
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

    /// <summary>The tone the audio source lets through, in hertz.</summary>
    private const double AcceptedTone = 880.0;

    /// <summary>The tone the audio source refuses, in hertz.</summary>
    private const double RefusedTone = 5000.0;

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
            Console.WriteLine($"video:       {CustomVideoSource.GTypeName}");
            Console.WriteLine($"audio:       {CustomAudioSource.GTypeName}");

            // One track of each type: the clip is asked for a child twice and
            // answers a managed source both times, so the timeline is made of
            // managed types on the audio side as well as on the video one.
            using Timeline timeline = Timeline.New();
            using VideoTrack videoTrack = VideoTrack.New();
            using AudioTrack audioTrack = AudioTrack.New();

            if (!timeline.AddTrack(videoTrack))
            {
                Console.Error.WriteLine("GesCustomSource: the video track was refused.");
                return 1;
            }

            if (!timeline.AddTrack(audioTrack))
            {
                Console.Error.WriteLine("GesCustomSource: the audio track was refused.");
                return 1;
            }

            using Layer layer = timeline.AppendLayer();

            // The clip is extracted from an asset for its own type, which is
            // the same contract its child follows: a clip built with new has
            // no asset and the layer would remove it again.
            using CustomSourceClip clip = CustomSourceClip.New();

            clip.SupportedFormats = TrackType.Video | TrackType.Audio;

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

            if (!Report(clip))
            {
                return 1;
            }

            // The children carry the child properties, so this is asked once
            // the clip is in a layer and before anything plays: the tone the
            // override lets through is the tone the run renders.
            if (!ShowChildProperties(clip))
            {
                return 1;
            }

            int played = Play(timeline, options.Timeout);

            if (played != 0)
            {
                return played;
            }

            // The split comes after the run: the pipeline is back at NULL and
            // released, so the pipeline no longer drives the timeline.
            return ShowUngroup(clip) ? 0 : 1;
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
    /// Prints what the overrides built, which is the contract this sample is
    /// about.
    /// </summary>
    /// <param name="clip">The clip that was added to the layer.</param>
    /// <returns>Whether the clip was given the two children it answered.</returns>
    private static bool Report(CustomSourceClip clip)
    {
        Console.WriteLine();
        Console.WriteLine("1. the clip builds its own children");

        IReadOnlyList<TimelineElement> children = clip.GetChildren(false);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"children:    {children.Count}"));

        // Each child is the very wrapper the override answered: the interning
        // is what makes the two the same object rather than two wrappers for
        // one instance.
        CustomVideoSource? video = clip.AnsweredChild;
        CustomAudioSource? audio = clip.AnsweredAudioChild;

        bool interned = children.Count == 2
            && children.Any(child => ReferenceEquals(child, video))
            && children.Any(child => ReferenceEquals(child, audio));

        Console.WriteLine($"video child: {video?.Name ?? "none"} ({video?.BuiltElement ?? "no element"})");
        Console.WriteLine($"audio child: {audio?.Name ?? "none"} ({audio?.BuiltElement ?? "no element"})");
        Console.WriteLine($"interned:    {interned}");

        if (!interned)
        {
            Console.Error.WriteLine(
                "GesCustomSource: the clip was not given the two children its override answered.");
        }

        return interned;
    }

    /// <summary>
    /// Writes a child property of the element the audio source is made of,
    /// through the slot the source took over.
    /// </summary>
    /// <param name="clip">The clip that was added to the layer.</param>
    /// <returns>Whether the writes were answered the way the source promises.</returns>
    private static bool ShowChildProperties(CustomSourceClip clip)
    {
        Console.WriteLine();
        Console.WriteLine("2. the audio source watches its child properties");

        CustomAudioSource? audio = clip.AnsweredAudioChild;

        if (audio is null)
        {
            Console.Error.WriteLine("GesCustomSource: there is no audio child to ask.");
            return false;
        }

        using (Gst.GObject.Value current = audio.GetChildProperty(CustomAudioSource.ToneProperty))
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"registered:  {CustomAudioSource.ToneProperty} of the inner element, at {current.GetDouble():F0} Hz"));
        }

        using Gst.GObject.Value wanted = Gst.GObject.Value.New(Gst.GObject.GType.Double);
        wanted.SetDouble(AcceptedTone);

        // The write goes through set_child_property_full, which is the only
        // caller of the set_child_property slot: the override sees it, lets it
        // through by chaining up, and the value lands on the audiotestsrc.
        if (!audio.SetChildPropertyFull(CustomAudioSource.ToneProperty, wanted))
        {
            Console.Error.WriteLine("GesCustomSource: the accepted tone was refused after all.");
            return false;
        }

        double written;
        using (Gst.GObject.Value readBack = audio.GetChildProperty(CustomAudioSource.ToneProperty))
        {
            written = readBack.GetDouble();
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"accepted:    {AcceptedTone:F0} Hz, read back as {written:F0} Hz"));

        if (Math.Abs(written - AcceptedTone) > 0.5)
        {
            Console.Error.WriteLine("GesCustomSource: the tone the source kept is not the one that was written.");
            return false;
        }

        using Gst.GObject.Value refused = Gst.GObject.Value.New(Gst.GObject.GType.Double);
        refused.SetDouble(RefusedTone);

        // A refusal that carries a reason reaches the caller as the GError the
        // override answered, which the full member raises.
        try
        {
            // A refusal without a reason is a legal answer of the slot as well,
            // so the answer is what tells a write from a silent refusal.
            if (audio.SetChildPropertyFull(CustomAudioSource.ToneProperty, refused))
            {
                Console.Error.WriteLine("GesCustomSource: the tone above the ceiling was written all the same.");
            }
            else
            {
                Console.Error.WriteLine(
                    "GesCustomSource: the tone above the ceiling was refused, but without the reason the override gave.");
            }

            return false;
        }
        catch (GException refusal)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"refused:     {RefusedTone:F0} Hz, domain {refusal.Domain} ({refusal.Code})"));
            Console.WriteLine($"reason:      {refusal.Message}");
        }

        // The same refusal through the plain setter, which carries no room for
        // a reason: it is told apart from an unknown name by a lookup and
        // raised as a refusal rather than as a missing property.
        try
        {
            audio.SetChildProperty(CustomAudioSource.ToneProperty, refused);
            Console.Error.WriteLine("GesCustomSource: the plain setter wrote what the full one refused.");
            return false;
        }
        catch (InvalidOperationException plain)
        {
            Console.WriteLine($"setter:      {plain.Message}");
        }

        double kept;
        using (Gst.GObject.Value afterRefusal = audio.GetChildProperty(CustomAudioSource.ToneProperty))
        {
            kept = afterRefusal.GetDouble();
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"still at:    {kept:F0} Hz after {audio.ObservedWrites.Count} writes the override saw"));

        if (Math.Abs(kept - AcceptedTone) > 0.5)
        {
            Console.Error.WriteLine("GesCustomSource: a refused write changed the tone anyway.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Splits the clip into one clip per track type, through the slot the clip
    /// took over.
    /// </summary>
    /// <param name="clip">The clip that was played.</param>
    /// <returns>Whether the split was answered the way the override reports it.</returns>
    private static bool ShowUngroup(CustomSourceClip clip)
    {
        Console.WriteLine();
        Console.WriteLine("4. the clip answers its own split");

        // The containers are owned by this caller. The clip is in the answer as
        // well, and it is disposed by the using of the caller above.
        IReadOnlyList<Container> parts = clip.Ungroup(recursive: false);

        try
        {
            Console.WriteLine($"override:    {clip.UngroupStory ?? "never ran"}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"containers:  {parts.Count}"));

            List<string> lines = [];

            foreach (Container part in parts)
            {
                IReadOnlyList<TimelineElement> children = part.GetChildren(false);
                string which = ReferenceEquals(part, clip) ? "the clip itself" : "a new clip";
                string carries = children.Count == 1 ? children[0].GetTrackTypes().ToString() : "no single track type";

                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {part.Name ?? "?"}: {which}, {children.Count} child, {carries}"));
            }

            // The answer is built by a walk of a hash table, so the order it
            // arrives in is the library's business: the lines are sorted here
            // to make the print a function of the set rather than of the walk.
            lines.Sort(StringComparer.Ordinal);
            lines.ForEach(Console.WriteLine);

            if (clip.UngroupStory is null || parts.Count != 2)
            {
                Console.Error.WriteLine(
                    "GesCustomSource: the split did not answer one container per track type.");
                return false;
            }

            return true;
        }
        finally
        {
            // Disposing releases the reference of this caller only - the layer
            // keeps its own and the clip stays in the timeline - but it also
            // retires the managed side of that clip: a disposed wrapper chains
            // up for ever (docs/subclassing.md §5). That is fine here because
            // the run is over; an application that goes on editing keeps the
            // wrapper for as long as it wants its overrides to answer.
            foreach (Container part in parts)
            {
                if (!ReferenceEquals(part, clip))
                {
                    part.Dispose();
                }
            }
        }
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
        Console.WriteLine();
        Console.WriteLine("3. the timeline renders audio and video to fakesinks");

        using GES.Pipeline pipeline = GES.Pipeline.New();

        try
        {
            if (!pipeline.SetTimeline(timeline))
            {
                Console.Error.WriteLine("GesCustomSource: the pipeline refused the timeline.");
                return 1;
            }

            // Headless: both previews go nowhere. A sink is set before the
            // pipeline leaves NULL, which is the only window a preview sink
            // can be chosen in, and each preview needs a sink of its own.
            using Element videoSink = ElementFactory.Make("fakesink", null)
                ?? throw new InvalidOperationException("fakesink is not installed.");
            using Element audioSink = ElementFactory.Make("fakesink", null)
                ?? throw new InvalidOperationException("fakesink is not installed.");

            videoSink.SetProperty("sync", false);
            audioSink.SetProperty("sync", false);
            pipeline.PreviewSetVideoSink(videoSink);
            pipeline.PreviewSetAudioSink(audioSink);

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
