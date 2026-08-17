// Basic tutorial 13: playback speed — seeking with a rate, playing backwards,
// and stepping one frame at a time.
//
// Ported from basic-tutorial-13.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/playback-speed.html
//
// Usage: BasicTutorial13 [<uri-or-file>] [--headless] [--keys <string>]
//                        [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop and no GIOChannel. The C program is the only one in the
//     basic series that reads the keyboard, and it does so with the one part of
//     GLib that is written twice — g_io_channel_win32_new_fd on Windows,
//     g_io_channel_unix_new everywhere else. .NET has one console for every
//     operating system, so this is a single cross-platform program: the loop
//     that polls the bus also asks Console.KeyAvailable whether a key is
//     waiting. That removes the last per-OS #ifdef of the tutorial series.
//
//   * --keys is the sample-only option that makes the tutorial runnable
//     unattended, the way GstLaunch --interrupt-after does for a Ctrl+C path:
//     the characters of the string are fed to the same handler the keyboard
//     feeds, one every half second, starting once the pipeline has reported
//     PLAYING — a rate change needs a position, and a pipeline that has not
//     prerolled has none. It is what lets a machine with no terminal exercise
//     the rate and step events.
//
//   * gst_element_send_event takes the event over, and so does SendEvent here:
//     after the call the wrapper owns nothing. That is why every send below
//     builds an event of its own — an event cannot be sent twice. See
//     docs/ownership.md, "Calls that consume their argument".
//
//   * g_object_get (pipeline, "video-sink", &sink) hands out a reference that
//     the C program unrefs at the end. Here the sink comes out of a GValue and
//     is an interned GObject wrapper, so releasing the value is all there is to
//     do; the wrapper stays valid because playbin holds the sink.
//
//   * The events go to the sink rather than to the pipeline, which is the trick
//     of the C original and is kept: a step event only means something to a
//     sink, and sending the rate change to the same place keeps the two
//     consistent.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card. They
//     synchronize to the clock, because a rate is a statement about time.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return PlaybackSpeed.Run(args);

internal static class PlaybackSpeed
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>How long a scripted run waits between two keys.</summary>
    private static readonly TimeSpan KeyInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Plays the media and changes its speed on command.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the run ended as asked, 1 on any error.</returns>
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

            Console.WriteLine("USAGE: choose one of the following, then press enter:");
            Console.WriteLine(" 'P' to toggle between PAUSED and PLAYING");
            Console.WriteLine(" 'S' to double the speed, 's' to halve it");
            Console.WriteLine(" 'D' to play in the other direction");
            Console.WriteLine(" 'N' to step one frame (best while PAUSED)");
            Console.WriteLine(" 'Q' to quit");
            Console.WriteLine();
            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"uri:         {options.Uri}");

            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("BasicTutorial13: the description did not produce a pipeline.");
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
            Console.Error.WriteLine($"BasicTutorial13: {exception}");
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
                Console.Error.WriteLine("BasicTutorial13: fakesink could not be created.");
                return false;
            }

            // A rate is a statement about time, so the sink has to keep time.
            // A fakesink is the one sink that does not do that by default.
            Global.UtilSetObjectArg(sink, "sync", "true");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the pipeline and acts on the keys that arrive while it runs.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as asked, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        State target = State.Playing;
        double rate = 1;

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial13: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Keys keys = Keys.For(options.Script);
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using (Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos | MessageType.StateChanged))
                {
                    if (message is not null)
                    {
                        if (message.Type == MessageType.Error)
                        {
                            (GException error, string? debug) = message.ParseError();
                            Console.Error.WriteLine(
                                $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                            Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                            return 1;
                        }

                        if (message.Type == MessageType.StateChanged)
                        {
                            // The C program never looks at these. This one does,
                            // for one reason: it is how a scripted run knows the
                            // pipeline is far enough along to be told anything.
                            if (message.Src is { } source && source.Handle == pipeline.Handle)
                            {
                                message.ParseStateChanged(out _, out State current, out _);

                                if (current == State.Playing)
                                {
                                    keys.Start(elapsed.Elapsed);
                                }
                            }

                            continue;
                        }

                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;
                    }
                }

                GstSharp.DrainPendingReleases();

                if (keys.Next(elapsed.Elapsed) is not char key)
                {
                    continue;
                }

                switch (char.ToUpperInvariant(key))
                {
                    case 'P':
                        target = target == State.Playing ? State.Paused : State.Playing;
                        pipeline.SetState(target);
                        Console.WriteLine($"state:       {target}");
                        break;

                    case 'S':
                        // The capital doubles and the small one halves, which
                        // is the one place the C program looks at the case of
                        // the key rather than at the key.
                        rate *= char.IsUpper(key) ? 2 : 0.5;
                        Seek(pipeline, rate);
                        break;

                    case 'D':
                        rate *= -1;
                        Seek(pipeline, rate);
                        break;

                    case 'N':
                        Step(pipeline, rate);
                        break;

                    case 'Q':
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"quit:        after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    default:
                        break;
                }
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial13: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Seeks to where the stream already is, at a new rate.
    /// </summary>
    /// <param name="pipeline">The pipeline that is playing.</param>
    /// <param name="rate">The rate to play at, negative to play backwards.</param>
    private static void Seek(Pipeline pipeline, double rate)
    {
        if (!pipeline.QueryPosition(Format.Time, out long position))
        {
            Console.Error.WriteLine("BasicTutorial13: the current position could not be queried.");
            return;
        }

        if (SinkOf(pipeline) is not Element sink)
        {
            return;
        }

        // Forwards, the segment runs from where the stream is to the end;
        // backwards, it runs from the beginning to where the stream is, and the
        // negative rate is what makes it be played from the far end.
        using Event seek = rate > 0
            ? Event.NewSeek(
                rate,
                Format.Time,
                SeekFlags.Flush | SeekFlags.Accurate,
                SeekType.Set,
                position,
                SeekType.End,
                0)
            : Event.NewSeek(
                rate,
                Format.Time,
                SeekFlags.Flush | SeekFlags.Accurate,
                SeekType.Set,
                0,
                SeekType.Set,
                position);

        // SendEvent consumes the event. The `using` stays correct because
        // Dispose is idempotent, and it is what releases the event on the paths
        // that return before the send.
        sink.SendEvent(seek);

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rate:        {rate:0.####}"));
    }

    /// <summary>
    /// Advances the stream by exactly one buffer.
    /// </summary>
    /// <param name="pipeline">The pipeline that is playing.</param>
    /// <param name="rate">The rate the step is played at.</param>
    private static void Step(Pipeline pipeline, double rate)
    {
        if (SinkOf(pipeline) is not Element sink)
        {
            return;
        }

        // A step event is counted in buffers rather than in time, and it is one
        // of the two events that only a sink understands. flush=true throws
        // away what is already queued so that the step is seen at once;
        // intermediate=false makes this a step of its own rather than part of a
        // series.
        using Event step = Event.NewStep(Format.Buffers, 1, Math.Abs(rate), true, false);

        sink.SendEvent(step);

        Console.WriteLine("step:        one frame");
    }

    /// <summary>
    /// Reads the sink playbin plays the video through.
    /// </summary>
    /// <param name="playbin">The playbin to ask.</param>
    /// <returns>The sink, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// The value owns a reference while it lives, and the wrapper it hands out
    /// is the interned one, kept alive by playbin itself.
    /// </remarks>
    private static Element? SinkOf(Pipeline playbin)
    {
        using Gst.GObject.Value value = playbin.GetProperty("video-sink");

        if (value.GetObject() is Element sink)
        {
            return sink;
        }

        Console.Error.WriteLine("BasicTutorial13: playbin has no video sink to send the event to.");
        return null;
    }

    /// <summary>
    /// Where the keys come from: the console when a person is watching, the
    /// <c>--keys</c> string when nobody is.
    /// </summary>
    private sealed class Keys
    {
        private readonly string? _script;
        private int _index;
        private bool _started;
        private TimeSpan _due;

        private Keys(string? script) => _script = script;

        /// <summary>
        /// Chooses the source of the keys.
        /// </summary>
        /// <param name="script">The <c>--keys</c> string, or <see langword="null"/>.</param>
        /// <returns>The reader to ask.</returns>
        internal static Keys For(string? script)
        {
            if (script is null && Console.IsInputRedirected)
            {
                Console.WriteLine("keys:        stdin is not a terminal and no --keys was given; just playing.");
            }

            return new Keys(script);
        }

        /// <summary>
        /// Says that the pipeline has reached PLAYING, which is when a person
        /// would start pressing keys and when the first one may be fed.
        /// </summary>
        /// <param name="elapsed">How long the run has been going.</param>
        /// <remarks>
        /// A rate change needs a position, and a pipeline that has not prerolled
        /// has none. Waiting for PLAYING is what keeps a scripted run from
        /// pressing its first key into a pipeline that is not there yet — and
        /// waiting for the state rather than for a duration is what keeps it
        /// working on a machine that is slower or faster than this one.
        /// </remarks>
        internal void Start(TimeSpan elapsed)
        {
            if (!_started)
            {
                _started = true;
                _due = elapsed + KeyInterval;
            }
        }

        /// <summary>
        /// Reads the next key, if there is one to read yet.
        /// </summary>
        /// <param name="elapsed">How long the run has been going.</param>
        /// <returns>The key, or <see langword="null"/>.</returns>
        internal char? Next(TimeSpan elapsed)
        {
            if (_script is null)
            {
                // KeyAvailable throws when stdin is a pipe rather than a
                // console, which is exactly the case --keys is there for. A
                // person is not made to wait for PLAYING: quitting a pipeline
                // that never got there has to stay possible.
                return !Console.IsInputRedirected && Console.KeyAvailable
                    ? Console.ReadKey(true).KeyChar
                    : null;
            }

            if (!_started || _index >= _script.Length || elapsed < _due)
            {
                return null;
            }

            _due = elapsed + KeyInterval;
            return _script[_index++];
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

        /// <summary>Gets the keys to play back, or <see langword="null"/>.</summary>
        internal string? Script { get; private set; }

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

                    case "--keys":
                        options.Script = Cli.ValueOf(arguments, ref i);
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
