// Playback tutorial 2: subtitle management — adding an external subtitle file
// to a media file, and choosing which text stream is shown.
//
// Ported from playback-tutorial-2.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/subtitle-management.html
//
// Usage: PlaybackTutorial02 [<uri-or-file> [<subtitle-uri-or-file>]] [--headless]
//                           [--keys <string>] [--native-path <directory>]
//                           [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop, no GstBusFunc watch and no GIOChannel. The loop below polls
//     the bus with TimedPopFiltered and asks Console.KeyAvailable whether a key
//     is waiting, which is one program on every operating system rather than
//     the two the C original needs — g_io_channel_win32_new_fd on Windows and
//     g_io_channel_unix_new everywhere else.
//
//   * GstPlayFlags is a GFlags type that the playback plugin registers at run
//     time, so no .gir declares it and this binding has no managed type for it.
//     The flags are therefore written as the plain uint the property holds,
//     with the bits spelled out below — the same thing samples/GstPlay does.
//
//   * The C keyboard handler reads a whole line and runs it through
//     g_ascii_strtoull, so anything that is not a number reads as stream 0 and
//     an index of more than one digit can be typed. This one reads single
//     characters, because that is what an unattended --keys run can feed and
//     what Console.KeyAvailable gives: a digit selects that stream, 'q' quits,
//     and anything else is ignored rather than read as a zero.
//
//   * 'q' quits with 0. The C program has no way out other than EOS, an error
//     or Ctrl+C, which would leave an unattended run nothing to gate on.
//
//   * --keys is sample scaffolding, identical in mechanism to
//     BasicTutorial13's: the characters are fed to the same handler the
//     keyboard feeds, one every half second, starting once the pipeline has
//     reported PLAYING — before that there are no streams to choose between.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card. It
//     does not switch the text off: subtitleoverlay and textoverlay are built
//     and render into the fakesink exactly as they would into a real one, which
//     is what makes a headless run still prove that the text stream was
//     selected and decoded. What is lost is only the picture.
//
//   * A subtitle file of under 128 bytes is not recognised by GStreamer 1.24:
//     its subparse typefinder peeks exactly that many and gives up on a shorter
//     file, playbin turns the suburi failure into a warning, and the run plays
//     with no text and reports 0 text streams. 1.28 falls back to the length of
//     the file. Worth knowing before blaming a tiny hand-written test .srt.
//
//   * The tag lists the get-*-tags action signals return are transfer full.
//     EmitSignal<TagList> hands back a wrapper that owns that reference, so
//     each one is disposed here where the C program calls gst_tag_list_free.
//
//   * GST_TAG_AUDIO_CODEC and its siblings are plain #defines, which no .gir
//     carries, so the tag names are written as the literals they expand to.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return SubtitleManagement.Run(args);

internal static class SubtitleManagement
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.ogv";

    /// <summary>The subtitles of the upstream tutorial.</summary>
    private const string DefaultSubtitleUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer_gr.srt";

    /// <summary>
    /// The bits of <c>GstPlayFlags</c> this tutorial names. The type is a
    /// GFlags the playback plugin registers when it is loaded, so it appears in
    /// no <c>.gir</c> file and this binding declares no managed type for it;
    /// the property is read and written as the <see cref="uint"/> it holds.
    /// </summary>
    private const uint PlayFlagVideo = 0x1;

    /// <inheritdoc cref="PlayFlagVideo"/>
    private const uint PlayFlagAudio = 0x2;

    /// <inheritdoc cref="PlayFlagVideo"/>
    private const uint PlayFlagText = 0x4;

    /// <summary>How long a scripted run waits between two keys.</summary>
    private static readonly TimeSpan KeyInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How many text streams the media turned out to have.</summary>
    private static int _textStreams;

    /// <summary>
    /// Plays the media with its subtitles and says what is inside it.
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

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"uri:         {options.Uri}");
            Console.WriteLine($"suburi:      {options.SubtitleUri}");

            if (ElementFactory.Make("playbin", "playbin") is not Element playbin)
            {
                Console.Error.WriteLine("PlaybackTutorial02: Not all elements could be created.");
                return 1;
            }

            using (playbin)
            {
                playbin.SetProperty("uri", options.Uri);

                // The subtitles are a second URI of their own, and the font is
                // a Pango description string rather than anything GStreamer
                // parses itself.
                playbin.SetProperty("suburi", options.SubtitleUri);
                playbin.SetProperty("subtitle-font-desc", "Sans, 18");

                // Show audio, video and subtitles. The read-modify-write is the
                // C original's: playbin's default already has more bits than
                // these three, and the tutorial only means to add them.
                uint flags = playbin.GetProperty<uint>("flags");
                flags |= PlayFlagVideo | PlayFlagAudio | PlayFlagText;
                playbin.SetProperty("flags", flags);

                if (options.Headless && !Silence(playbin))
                {
                    return 1;
                }

                return Play(playbin, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial02: {exception}");
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
    private static bool Silence(Element playbin)
    {
        foreach (string property in (string[])["video-sink", "audio-sink"])
        {
            if (ElementFactory.Make("fakesink", null) is not Element sink)
            {
                Console.Error.WriteLine("PlaybackTutorial02: fakesink could not be created.");
                return false;
            }

            // A fakesink is the one sink that does not keep time by default,
            // and a run that does not keep time is not a playback run.
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
    /// <param name="playbin">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as asked, 1 on any error.</returns>
    private static int Play(Element playbin, Options options)
    {
        try
        {
            if (playbin.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial02: Unable to set the pipeline to the playing state.");
                return 1;
            }

            if (playbin.GetBus() is not Bus bus)
            {
                Console.Error.WriteLine("PlaybackTutorial02: the pipeline has no bus.");
                return 1;
            }

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
                                $"Error received from element {message.SourceName ?? "?"}: {error.Message}");
                            Console.Error.WriteLine($"Debugging information: {debug ?? "none"}");
                            return 1;
                        }

                        if (message.Type == MessageType.Eos)
                        {
                            Console.WriteLine("End-Of-Stream reached.");
                            return 0;
                        }

                        if (message.Src is { } source && source.Handle == playbin.Handle)
                        {
                            message.ParseStateChanged(out _, out State current, out _);

                            if (current == State.Playing)
                            {
                                AnalyzeStreams(playbin);
                                keys.Start(elapsed.Elapsed);
                            }
                        }

                        continue;
                    }
                }

                GstSharp.DrainPendingReleases();

                if (keys.Next(elapsed.Elapsed) is not char key)
                {
                    continue;
                }

                if (key is 'q' or 'Q')
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"quit:        after {elapsed.Elapsed.TotalSeconds:F2} s"));
                    return 0;
                }

                if (char.IsAsciiDigit(key))
                {
                    Select(playbin, key - '0');
                }
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial02: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            playbin.SetState(State.Null);
        }
    }

    /// <summary>
    /// Chooses which subtitle stream is shown, which is what the keyboard of
    /// the C original does.
    /// </summary>
    /// <param name="playbin">The pipeline to tell.</param>
    /// <param name="index">The subtitle stream that was asked for.</param>
    private static void Select(Element playbin, int index)
    {
        if (index < 0 || index >= _textStreams)
        {
            Console.Error.WriteLine("Index out of bounds");
            return;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Setting current subtitle stream to {index}"));
        playbin.SetProperty("current-text", index);
    }

    /// <summary>
    /// Extracts some metadata from the streams and prints it on the screen.
    /// </summary>
    /// <param name="playbin">The pipeline to ask.</param>
    private static void AnalyzeStreams(Element playbin)
    {
        int videoStreams = playbin.GetProperty<int>("n-video");
        int audioStreams = playbin.GetProperty<int>("n-audio");
        int textStreams = playbin.GetProperty<int>("n-text");

        _textStreams = textStreams;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{videoStreams} video stream(s), {audioStreams} audio stream(s), {textStreams} text stream(s)"));

        Console.WriteLine();
        for (int i = 0; i < videoStreams; i++)
        {
            // The action signal returns a transfer-full GstTagList, which is a
            // mini object, so what comes back is a wrapper that owns that
            // reference: disposing it is the gst_tag_list_free of the C.
            using Gst.TagList? tags = playbin.EmitSignal<Gst.TagList>("get-video-tags", i);

            if (tags is not null)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"video stream {i}:"));
                _ = tags.GetString("video-codec", out string? codec);
                Console.WriteLine($"  codec: {codec ?? "unknown"}");
            }
        }

        Console.WriteLine();
        for (int i = 0; i < audioStreams; i++)
        {
            using Gst.TagList? tags = playbin.EmitSignal<Gst.TagList>("get-audio-tags", i);

            if (tags is not null)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"audio stream {i}:"));

                if (tags.GetString("audio-codec", out string? codec))
                {
                    Console.WriteLine($"  codec: {codec}");
                }

                if (tags.GetString("language-code", out string? language))
                {
                    Console.WriteLine($"  language: {language}");
                }

                if (tags.GetUint("bitrate", out uint bitrate))
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  bitrate: {bitrate}"));
                }
            }
        }

        Console.WriteLine();
        for (int i = 0; i < textStreams; i++)
        {
            // This loop prints its header before asking, which is where the two
            // tutorials differ: a subtitle stream with no tags at all is still
            // a subtitle stream worth listing, so it says so instead of
            // vanishing from the list.
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"subtitle stream {i}:"));

            using Gst.TagList? tags = playbin.EmitSignal<Gst.TagList>("get-text-tags", i);

            if (tags is null)
            {
                Console.WriteLine("  no tags found");
                continue;
            }

            if (tags.GetString("language-code", out string? language))
            {
                Console.WriteLine($"  language: {language}");
            }
        }

        int currentVideo = playbin.GetProperty<int>("current-video");
        int currentAudio = playbin.GetProperty<int>("current-audio");
        int currentText = playbin.GetProperty<int>("current-text");

        Console.WriteLine();
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Currently playing video stream {currentVideo}, audio stream {currentAudio} and subtitle stream {currentText}"));
        Console.WriteLine("Type any number and hit ENTER to select a different subtitle stream");
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
        /// Choosing a stream needs the streams to be known, and they are known
        /// once the pipeline has prerolled. Waiting for the state rather than
        /// for a duration is what keeps a scripted run working on a machine
        /// that is slower or faster than this one.
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
                // console, which is exactly the case --keys is there for.
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

        /// <summary>Gets the subtitles to render over it.</summary>
        internal string SubtitleUri { get; private set; } = DefaultSubtitleUri;

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
            int positional = 0;

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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || positional >= 2)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        if (positional == 0)
                        {
                            options.Uri = Cli.ToUri(arguments[i]);
                        }
                        else
                        {
                            options.SubtitleUri = Cli.ToUri(arguments[i]);
                        }

                        positional++;
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
