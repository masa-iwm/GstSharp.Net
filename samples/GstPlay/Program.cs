// A port of gst-play-1.0's user experience onto Gst.Play.Play.
//
// The C tool (gst-plugins-base/tools/gst-play.c) drives playbin directly and
// re-implements the state machine of a player on top of it. This sample plays
// the same playlist with the same keys, but the state machine is the one
// libgstplay already has: the module's Play object owns the pipeline, runs it
// on a thread of its own and reports everything as a message on the API bus of
// GetMessageBus.
//
// Usage: GstPlay [<uri-or-file> ...] [--volume <0..1>] [--audiosink <factory>]
//                [--videosink <factory>] [--visualization <name>]
//                [--list-visualizations] [--shuffle] [--duration <seconds>]
//                [--interactive]
//
// Where this port differs from the C tool, and why:
//
//   * The bus is polled with gst_bus_timed_pop_filtered rather than watched
//     from a GLib main loop, which is the house style of the binding: a .NET
//     application owns its thread. That is also why PlaySignalAdapter is not
//     used here - the asynchronous adapter only fires while a GLib loop is
//     running, and the synchronous one takes the whole bus over.
//
//   * Headless is the default. Nothing reads the keyboard unless --interactive
//     is given, and --duration bounds an otherwise endless run, so that the
//     sample can be an exit code gate in CI. The C tool always reads the
//     terminal.
//
//   * --audiosink and --videosink are the mechanism of the C tool: the sink is
//     made with gst_element_factory_make and written to the "audio-sink" or
//     "video-sink" property of the playbin that GetPipeline answers, before the
//     first Start. The module's PlayVideoOverlayVideoRenderer is not used - it
//     is for an application that has a window handle to embed the video in.
//
//   * The keys are the ones of the C tool with two pairs moved, so that a
//     terminal which reports no arrow keys can still reach both: + and - change
//     the volume (arrows up and down in the C tool), , and . change the
//     playback rate (- and + in the C tool), and 0 resets the rate to 1.0
//     (seek to the beginning in the C tool). d changes the playback direction,
//     as it does in the C tool. Press k for the list.
//
//   * --shuffle and the "d" direction key are ported. Trick modes (the "t" key)
//     and the plugin installer of the C tool remain out of scope: GstPlay
//     builds the flags of its own seek - FLUSH, ACCURATE from the config, and
//     TRICKMODE whenever the rate is not 1.0 - and offers no way to add
//     KEY_UNITS or NO_AUDIO, so the mode switch of the C tool cannot be
//     expressed through the module without bypassing GstPlay's seek state
//     machine.
//
//   * Only members that exist on GStreamer 1.24, the floor of this binding, are
//     called. Six of them - the three index based track setters, the stream
//     index read, and the duration and buffering parses - are deprecated since
//     1.26 and are therefore called under a pragma, one block at a time.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GObject;
using Gst.Play;

return Player.Run(args);

/// <summary>
/// The sample: it reads the command line, builds one play for the whole
/// playlist and pumps its API bus.
/// </summary>
internal static class Player
{
    /// <summary>How long one poll of the API bus waits.</summary>
    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

    /// <summary>
    /// How often the play reports its position, in milliseconds. The default is
    /// 100 ms, which is ten lines of output a second; a second is enough to see
    /// that the position moves and keeps an unattended log readable.
    /// </summary>
    private const uint PositionUpdateInterval = 1000;

    /// <summary>
    /// Plays the playlist and reports what the API bus said.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>
    /// 0 when every item was played, 1 when any of them failed, 2 for a command
    /// line that could not be read.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);

            if (options.Help)
            {
                PrintUsage(Console.Out);
                return 0;
            }

            // Initialising through the module rather than through GstSharp is
            // what puts the objects of GstPlay into the type registry
            // deterministically.
            GstPlay.Initialize();

            if (options.ListVisualizations)
            {
                ListVisualizations();
                return 0;
            }

            if (options.Uris.Count == 0)
            {
                PrintUsage(Console.Error);
                return 2;
            }

            if (options.Shuffle)
            {
                Shuffle(options.Uris);
            }

            // The play is this sample's own object and is disposed here. Its
            // message bus is not: that wrapper is interned and belongs to the
            // play, so it is left to the collector. See docs/ownership.md.
            using Play play = new();
            Bus bus = play.GetMessageBus();

            Configure(play, options);

            return new Session(play, bus, options).Run();
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"GstPlay: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GstPlay: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>Prints how the sample is called.</summary>
    /// <param name="writer">Where the text goes.</param>
    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage: GstPlay [<uri-or-file> ...] [--volume <0..1>]");
        writer.WriteLine("               [--audiosink <factory>] [--videosink <factory>]");
        writer.WriteLine("               [--visualization <name>] [--list-visualizations]");
        writer.WriteLine("               [--shuffle] [--duration <seconds>] [--interactive]");
    }

    /// <summary>
    /// Shuffles the playlist in place, the way the shuffle_uris of the C tool
    /// does: one Fisher-Yates pass before anything is played.
    /// </summary>
    /// <param name="uris">The playlist to reorder.</param>
    private static void Shuffle(List<string> uris)
    {
        if (uris.Count < 2)
        {
            return;
        }

        for (int i = uris.Count - 1; i >= 1; i--)
        {
            // The upper bound is exclusive, as it is in g_random_int_range.
            int j = Random.Shared.Next(0, i + 1);
            (uris[i], uris[j]) = (uris[j], uris[i]);
        }
    }

    /// <summary>
    /// Prints the visualizations the installed plugins offer, by the name
    /// <c>--visualization</c> takes.
    /// </summary>
    private static void ListVisualizations()
    {
        IReadOnlyList<PlayVisualization> visualizations = Play.GetVisualizations();

        try
        {
            if (visualizations.Count == 0)
            {
                Console.WriteLine("No plugin registers a visualization.");
                return;
            }

            foreach (PlayVisualization visualization in visualizations)
            {
                Console.WriteLine($"{visualization.Name,-16} {visualization.Description}");
            }
        }
        finally
        {
            // The list is the caller's, and each descriptor in it is a copy of
            // its own.
            foreach (PlayVisualization visualization in visualizations)
            {
                visualization.Dispose();
            }
        }
    }

    /// <summary>
    /// Applies everything the command line asked for, before the first
    /// <see cref="Play.Start"/>.
    /// </summary>
    /// <param name="play">The play to configure.</param>
    /// <param name="options">What the command line asked for.</param>
    private static void Configure(Play play, Options options)
    {
        // Only a stopped play accepts a configuration, which is why this runs
        // before anything is started.
        using (Structure config = play.GetConfig())
        {
            Play.ConfigSetPositionUpdateInterval(config, PositionUpdateInterval);

            if (!play.SetConfig(config))
            {
                Console.Error.WriteLine("GstPlay: the play refused its configuration.");
            }
        }

        if (options.AudioSink is not null || options.VideoSink is not null)
        {
            // gst_play_get_pipeline answers the playbin the play drives, which
            // is where gst-play-1.0 writes its two sink properties as well. The
            // wrapper is the interned one of an object the play owns, so it is
            // not disposed here; see docs/ownership.md.
            Element pipeline = play.GetPipeline();

            SetSink(pipeline, "audio-sink", options.AudioSink);
            SetSink(pipeline, "video-sink", options.VideoSink);
        }

        if (options.Volume is { } volume)
        {
            play.Volume = volume;
        }

        if (options.Visualization is { } visualization)
        {
            if (!play.SetVisualization(visualization))
            {
                throw new ArgumentException(
                    $"\"{visualization}\" is not a visualization. Try --list-visualizations.",
                    nameof(options));
            }

            play.SetVisualizationEnabled(true);
        }
    }

    /// <summary>
    /// Writes one sink property of the playbin, when the command line named a
    /// factory for it.
    /// </summary>
    /// <param name="pipeline">The playbin of the play.</param>
    /// <param name="property">The name of the property to write.</param>
    /// <param name="factory">The factory to make the sink with, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentException">The registry has no such factory.</exception>
    private static void SetSink(Element pipeline, string property, string? factory)
    {
        if (factory is null)
        {
            return;
        }

        // The playbin references the sink itself, so the wrapper here is only
        // needed until the property has been written.
        using Element sink = ElementFactory.Make(factory, null)
            ?? throw new ArgumentException($"\"{factory}\" is not an element factory.", nameof(factory));

        pipeline.SetProperty(property, sink);
        Console.WriteLine($"{property}:  {factory}");
    }

    /// <summary>
    /// The three kinds of track the play can select.
    /// </summary>
    private enum TrackKind
    {
        /// <summary>The audio tracks.</summary>
        Audio,

        /// <summary>The video tracks.</summary>
        Video,

        /// <summary>The subtitle tracks.</summary>
        Subtitle,
    }

    /// <summary>
    /// One run of the sample: the playlist, the play that plays it, and the
    /// loop that reads its API bus.
    /// </summary>
    private sealed class Session
    {
        /// <summary>How far the left and right keys seek.</summary>
        private static readonly TimeSpan ShortSeek = TimeSpan.FromSeconds(10);

        /// <summary>How far the up and down keys seek.</summary>
        private static readonly TimeSpan LongSeek = TimeSpan.FromSeconds(60);

        /// <summary>Into how many steps the volume keys divide the range.</summary>
        private const double VolumeSteps = 20.0;

        private readonly Play _play;
        private readonly Bus _bus;
        private readonly Options _options;
        private readonly Stopwatch _elapsed = new();
        private int _index;
        private bool _failed;
        private bool _paused;
        private bool _mediaInfoPrinted;

        /// <summary>Initialises one run.</summary>
        /// <param name="play">The play to drive.</param>
        /// <param name="bus">The API bus of that play.</param>
        /// <param name="options">What the command line asked for.</param>
        internal Session(Play play, Bus bus, Options options)
        {
            _play = play;
            _bus = bus;
            _options = options;
        }

        /// <summary>
        /// Plays the playlist from its first item and pumps the API bus until
        /// the list ends, the run runs out of time, or a key says to stop.
        /// </summary>
        /// <returns>0 when every item was played, 1 when any of them failed.</returns>
        internal int Run()
        {
            if (_options.Interactive)
            {
                PrintKeyboardHelp();
            }

            _elapsed.Start();
            StartCurrent();

            while (true)
            {
                if (_options.Duration > TimeSpan.Zero && _elapsed.Elapsed >= _options.Duration)
                {
                    Console.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"stopping:    after the {_options.Duration.TotalSeconds:F0} s of --duration"));
                    break;
                }

                using (Message? message = _bus.TimedPopFiltered(PollInterval, MessageType.Application))
                {
                    if (message is not null && Play.IsPlayMessage(message) && !Handle(message))
                    {
                        break;
                    }
                }

                if (!ReadKey())
                {
                    break;
                }
            }

            // The loop is over before the play is disposed, which is what the
            // module asks of a caller that polls the bus itself.
            _play.Stop();
            return _failed ? 1 : 0;
        }

        /// <summary>
        /// Acts on one message of the API bus.
        /// </summary>
        /// <param name="message">The message that was popped.</param>
        /// <returns><see langword="false"/> when the run is over.</returns>
        private bool Handle(Message message)
        {
            PlayMessageExtensions.ParseType(message, out PlayMessage kind);

            switch (kind)
            {
                case PlayMessage.UriLoaded:
                    // gst_play_message_parse_uri_loaded arrived in 1.26; the
                    // URI that was loaded is the one the play holds.
                    Console.WriteLine($"loaded:      {_play.Uri}");
                    break;

                case PlayMessage.PositionUpdated:
                    PlayMessageExtensions.ParsePositionUpdated(message, out ClockTime position);
                    Console.WriteLine($"position:    {position} / {_play.Duration}");
                    break;

                case PlayMessage.DurationChanged:
                    Console.WriteLine($"duration:    {ParseDuration(message)}");
                    break;

                case PlayMessage.StateChanged:
                    PlayMessageExtensions.ParseStateChanged(message, out PlayState state);
                    _paused = state == PlayState.Paused;
                    Console.WriteLine($"state:       {PlayStateExtensions.GetName(state)}");
                    break;

                case PlayMessage.Buffering:
                    PrintBuffering(message);
                    break;

                case PlayMessage.VideoDimensionsChanged:
                    PrintVideoDimensions(message);
                    break;

                case PlayMessage.MediaInfoUpdated:
                    PrintMediaInfo(message);
                    break;

                case PlayMessage.VolumeChanged:
                    PrintVolume(message);
                    break;

                case PlayMessage.MuteChanged:
                    PlayMessageExtensions.ParseMutedChanged(message, out bool muted);
                    Console.WriteLine($"mute:        {(muted ? "on" : "off")}");
                    break;

                case PlayMessage.SeekDone:
                    // gst_play_message_parse_seek_done arrived in 1.26; where
                    // the seek landed is the position of the play.
                    Console.WriteLine($"seek done:   {_play.Position}");
                    break;

                case PlayMessage.EndOfStream:
                    Console.WriteLine("eos:         the end of the current item");
                    return Advance(1);

                case PlayMessage.Warning:
                    ReportIssue(message, warning: true);
                    break;

                case PlayMessage.Error:
                    ReportIssue(message, warning: false);

                    // gst-play-1.0 moves on to the next item after an error.
                    // The failure is remembered all the same, so a run whose
                    // only item could not be played exits non zero.
                    _failed = true;
                    return Advance(1);

                default:
                    break;
            }

            return true;
        }

        /// <summary>
        /// Moves to another item of the playlist and starts it.
        /// </summary>
        /// <param name="delta">1 for the next item, -1 for the previous one.</param>
        /// <returns><see langword="false"/> when the playlist is over.</returns>
        private bool Advance(int delta)
        {
            _play.Stop();

            int next = _index + delta;

            if (next >= _options.Uris.Count)
            {
                Console.WriteLine("Reached end of play list.");
                return false;
            }

            // Going back from the first item replays it, as the C tool does.
            _index = Math.Max(next, 0);
            StartCurrent();
            return true;
        }

        /// <summary>Starts the item the playlist is on.</summary>
        private void StartCurrent()
        {
            string uri = _options.Uris[_index];

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"playing:     [{_index + 1}/{_options.Uris.Count}] {uri}"));

            _mediaInfoPrinted = false;
            _paused = false;
            _play.Uri = uri;
            _play.Start();
        }

        /// <summary>Prints how full the buffer of the play is.</summary>
        /// <param name="message">The buffering message.</param>
        private static void PrintBuffering(Message message)
        {
            uint percent = ParseBuffering(message);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"buffering:   {percent} %"));
        }

        /// <summary>Prints the size of the video that is being played.</summary>
        /// <param name="message">The video dimensions message.</param>
        private static void PrintVideoDimensions(Message message)
        {
            PlayMessageExtensions.ParseVideoDimensionsChanged(message, out uint width, out uint height);

            // The play reports 0x0 when the media has no video at all, or has
            // not decided on a size yet; neither is worth a line.
            if (width != 0 && height != 0)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"video:       {width}x{height}"));
            }
        }

        /// <summary>Prints the volume the play reports.</summary>
        /// <param name="message">The volume message.</param>
        private static void PrintVolume(Message message)
        {
            PlayMessageExtensions.ParseVolumeChanged(message, out double volume);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"volume:      {volume * 100:F0} %"));
        }

        /// <summary>
        /// Prints the streams and the tags of the media the play loaded, once
        /// per item: the play reports the same snapshot again for every stream
        /// it finishes inspecting.
        /// </summary>
        /// <param name="message">The media info message.</param>
        private void PrintMediaInfo(Message message)
        {
            PlayMessageExtensions.ParseMediaInfoUpdated(message, out PlayMediaInfo? info);

            using (info)
            {
                if (info is null || _mediaInfoPrinted)
                {
                    return;
                }

                _mediaInfoPrinted = true;

                Console.WriteLine($"media:       {info.GetUri()}");
                Console.WriteLine($"  container: {info.GetContainerFormat() ?? "unknown"}");
                Console.WriteLine($"  title:     {info.GetTitle() ?? "unknown"}");
                // The snapshot is taken as the streams are inspected, which is
                // usually before the duration is known; the duration message
                // reports it when it arrives.
                ClockTime duration = info.GetDuration();
                if (!duration.IsNone)
                {
                    Console.WriteLine($"  duration:  {duration}");
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  seekable:  {info.IsSeekable()}, live {info.IsLive()}"));

                foreach (PlayAudioInfo audio in info.GetAudioStreams())
                {
                    PrintAudioStream(audio);
                }

                foreach (PlayVideoInfo video in info.GetVideoStreams())
                {
                    PrintVideoStream(video);
                }

                foreach (PlaySubtitleInfo subtitle in info.GetSubtitleStreams())
                {
                    PrintSubtitleStream(subtitle);
                }

                using Gst.TagList? tags = info.GetTags();
                tags?.Foreach(PrintTag);
            }
        }

        /// <summary>Prints one line about an audio stream.</summary>
        /// <param name="audio">The stream to describe.</param>
        private static void PrintAudioStream(PlayAudioInfo audio)
        {
            string codec = audio.GetCodec() ?? "unknown";
            string language = audio.GetLanguage() ?? "unknown";

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  audio {IndexOf(audio)}:   {codec}, {audio.GetChannels()} ch, {audio.GetSampleRate()} Hz, {audio.GetBitrate()} bps, language {language}"));
        }

        /// <summary>Prints one line about a video stream.</summary>
        /// <param name="video">The stream to describe.</param>
        private static void PrintVideoStream(PlayVideoInfo video)
        {
            string codec = video.GetCodec() ?? "unknown";
            video.GetFramerate(out int fpsN, out int fpsD);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  video {IndexOf(video)}:   {codec}, {video.GetWidth()}x{video.GetHeight()}, {fpsN}/{fpsD} fps, {video.GetBitrate()} bps"));
        }

        /// <summary>Prints one line about a subtitle stream.</summary>
        /// <param name="subtitle">The stream to describe.</param>
        private static void PrintSubtitleStream(PlaySubtitleInfo subtitle)
        {
            string codec = subtitle.GetCodec() ?? "unknown";
            string language = subtitle.GetLanguage() ?? "unknown";

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  text {IndexOf(subtitle)}:    {codec}, language {language}"));
        }

        /// <summary>
        /// Prints one tag of the media, the way the discoverer sample does.
        /// </summary>
        /// <param name="tags">The list the tag belongs to.</param>
        /// <param name="tag">The name of the tag.</param>
        private static void PrintTag(Gst.TagList tags, string tag)
        {
            using Value value = tags.CopyValue(tag);

            if (value.IsEmpty)
            {
                return;
            }

            string text = value.Type == GType.String
                ? value.GetString() ?? string.Empty
                : Global.ValueSerialize(value) ?? string.Empty;

            Console.WriteLine($"  tag:       {Global.TagGetNick(tag)}: {text}");
        }

        /// <summary>Prints an error or a warning of the API bus.</summary>
        /// <param name="message">The message to read.</param>
        /// <param name="warning">Whether it is a warning rather than an error.</param>
        private static void ReportIssue(Message message, bool warning)
        {
            Gst.GLib.GException issue;
            Structure? details;

            if (warning)
            {
                PlayMessageExtensions.ParseWarning(message, out issue, out details);
            }
            else
            {
                PlayMessageExtensions.ParseError(message, out issue, out details);
            }

            using (details)
            {
                string label = warning ? "warning:" : "error:";
                Console.Error.WriteLine($"{label,-12} {issue.Message}");
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"domain:      {issue.Domain} ({issue.Code})"));

                // GStreamer 1.24 attaches no details to an issue that came with
                // none; 1.26 and later always attach the URI.
                Console.Error.WriteLine($"details:     {details?.ToString() ?? "none"}");
            }
        }

        /// <summary>
        /// Reads one key, when the run is interactive and a person is at the
        /// terminal.
        /// </summary>
        /// <returns><see langword="false"/> when the key says to stop.</returns>
        private bool ReadKey()
        {
            // KeyAvailable throws when stdin is a pipe rather than a console,
            // which is what an unattended run has.
            if (!_options.Interactive || Console.IsInputRedirected || !Console.KeyAvailable)
            {
                return true;
            }

            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.RightArrow:
                    RelativeSeek(ShortSeek);
                    return true;

                case ConsoleKey.LeftArrow:
                    RelativeSeek(-ShortSeek);
                    return true;

                case ConsoleKey.UpArrow:
                    RelativeSeek(LongSeek);
                    return true;

                case ConsoleKey.DownArrow:
                    RelativeSeek(-LongSeek);
                    return true;

                case ConsoleKey.Escape:
                    return false;

                default:
                    return Act(key.KeyChar);
            }
        }

        /// <summary>
        /// Acts on one printable key.
        /// </summary>
        /// <param name="key">The character that was typed.</param>
        /// <returns><see langword="false"/> when the key says to stop.</returns>
        private bool Act(char key)
        {
            switch (key)
            {
                case ' ':
                    TogglePaused();
                    break;

                case 'q':
                case 'Q':
                    return false;

                case 'n':
                case '>':
                    return Advance(1);

                case 'b':
                case '<':
                    return Advance(-1);

                case '+':
                    RelativeVolume(1.0 / VolumeSteps);
                    break;

                case '-':
                    RelativeVolume(-1.0 / VolumeSteps);
                    break;

                case 'm':
                case 'M':
                    _play.Mute = !_play.Mute;
                    break;

                case 'a':
                case 'A':
                    CycleTrack(TrackKind.Audio);
                    break;

                case 'v':
                case 'V':
                    CycleTrack(TrackKind.Video);
                    break;

                case 's':
                case 'S':
                    CycleTrack(TrackKind.Subtitle);
                    break;

                case '.':
                    RelativeRate(faster: true);
                    break;

                case ',':
                    RelativeRate(faster: false);
                    break;

                case '0':
                    SetRate(1.0);
                    break;

                case 'd':
                    // play_set_relative_playback_rate (play, 0.0, TRUE) of the
                    // C tool: the magnitude stays, the direction flips. The
                    // module turns a rate below zero into a reverse seek of its
                    // own, so nothing else is needed here.
                    SetRate(-_play.Rate);
                    break;

                case 'k':
                case 'K':
                    PrintKeyboardHelp();
                    break;

                default:
                    break;
            }

            return true;
        }

        /// <summary>Pauses a running play and resumes a paused one.</summary>
        private void TogglePaused()
        {
            if (_paused)
            {
                _play.Start();
            }
            else
            {
                _play.Pause();
            }
        }

        /// <summary>
        /// Seeks by a step from where the play is now, without leaving the
        /// media.
        /// </summary>
        /// <param name="step">How far to seek, backwards when negative.</param>
        private void RelativeSeek(TimeSpan step)
        {
            ClockTime position = _play.Position;

            if (position.IsNone)
            {
                Console.WriteLine("seek:        the play has no position yet.");
                return;
            }

            // A ClockTime is unsigned, so the arithmetic is done signed and
            // clamped back into the media before it is handed over.
            long target = (long)position.Nanoseconds + (step.Ticks * 100);
            target = Math.Max(target, 0);

            ClockTime duration = _play.Duration;
            if (!duration.IsNone && duration.Nanoseconds != 0 && (ulong)target > duration.Nanoseconds)
            {
                target = (long)duration.Nanoseconds;
            }

            _play.Seek(ClockTime.FromNanoseconds((ulong)target));
        }

        /// <summary>
        /// Raises or lowers the volume by one step, within the range the
        /// <c>--volume</c> option takes.
        /// </summary>
        /// <param name="step">How much to change it by.</param>
        private void RelativeVolume(double step)
        {
            double volume = Math.Round((_play.Volume + step) * VolumeSteps) / VolumeSteps;
            _play.Volume = Math.Clamp(volume, 0.0, 1.0);
        }

        /// <summary>
        /// Changes the playback rate by one step, in the steps of the C tool: a
        /// tenth up to twice the speed, a half up to four times, one beyond
        /// that, and a flip of the direction rather than a stop when the step
        /// would cross zero.
        /// </summary>
        /// <param name="faster">Whether the rate goes up.</param>
        private void RelativeRate(bool faster)
        {
            double rate = _play.Rate;
            double magnitude = Math.Abs(rate);

            if (faster)
            {
                if (rate is > -0.2 and < 0.0)
                {
                    SetRate(-rate);
                    return;
                }

                SetRate(rate + (magnitude < 2.0 ? 0.1 : magnitude < 4.0 ? 0.5 : 1.0));
                return;
            }

            if (rate is > 0.0 and < 0.2)
            {
                SetRate(-rate);
                return;
            }

            SetRate(rate - (magnitude <= 2.0 ? 0.1 : magnitude <= 4.0 ? 0.5 : 1.0));
        }

        /// <summary>Sets the playback rate and says what it became.</summary>
        /// <param name="rate">The rate to play at.</param>
        private void SetRate(double rate)
        {
            // A rate of zero is not a pause but a refusal in the C library.
            if (Math.Abs(rate) < 0.01)
            {
                return;
            }

            _play.Rate = rate;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"rate:        {rate:F2}"));
        }

        /// <summary>
        /// Selects the track after the current one of a kind, wrapping around at
        /// the end of the list.
        /// </summary>
        /// <param name="kind">Which kind of track to cycle.</param>
        private void CycleTrack(TrackKind kind)
        {
            using PlayMediaInfo? info = _play.MediaInfo;

            if (info is null)
            {
                Console.WriteLine("track:       the play has no media info yet.");
                return;
            }

            int count = (int)(kind switch
            {
                TrackKind.Audio => info.GetNumberOfAudioStreams(),
                TrackKind.Video => info.GetNumberOfVideoStreams(),
                _ => info.GetNumberOfSubtitleStreams(),
            });

            if (count == 0)
            {
                Console.WriteLine($"track:       the media has no {kind} track.");
                return;
            }

            int next = (CurrentTrack(kind) + 1) % count;
            bool selected = SelectTrack(kind, next);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"track:       {kind} {next} of {count}, {(selected ? "selected" : "refused")}"));
        }

        /// <summary>
        /// Answers the index of the track of a kind that is playing, or -1 when
        /// none is.
        /// </summary>
        /// <param name="kind">Which kind of track to look at.</param>
        /// <returns>The index, or -1.</returns>
        private int CurrentTrack(TrackKind kind)
        {
            if (kind == TrackKind.Audio)
            {
                using PlayAudioInfo? audio = _play.CurrentAudioTrack;
                return audio is null ? -1 : IndexOf(audio);
            }

            if (kind == TrackKind.Video)
            {
                using PlayVideoInfo? video = _play.CurrentVideoTrack;
                return video is null ? -1 : IndexOf(video);
            }

            using PlaySubtitleInfo? subtitle = _play.CurrentSubtitleTrack;
            return subtitle is null ? -1 : IndexOf(subtitle);
        }

        /// <summary>Prints the keys the interactive mode reads.</summary>
        private static void PrintKeyboardHelp()
        {
            Console.WriteLine("Interactive mode - keyboard controls:");
            Console.WriteLine("  space        pause/unpause");
            Console.WriteLine("  q or ESC     quit");
            Console.WriteLine("  n or >       play next");
            Console.WriteLine("  b or <       play previous");
            Console.WriteLine("  right/left   seek 10 seconds forward/backward");
            Console.WriteLine("  up/down      seek 60 seconds forward/backward");
            Console.WriteLine("  + / -        volume up/down");
            Console.WriteLine("  m            toggle audio mute on/off");
            Console.WriteLine("  . / ,        increase/decrease the playback rate");
            Console.WriteLine("  0            reset the playback rate");
            Console.WriteLine("  d            change the playback direction");
            Console.WriteLine("  a / v / s    change to the next audio/video/subtitle track");
            Console.WriteLine("  k            show these keyboard shortcuts");
        }

#pragma warning disable CS0618 // The index based API is the only one available on the 1.24 floor.

        /// <summary>Selects one track of a kind by its index.</summary>
        /// <param name="kind">Which kind of track to select.</param>
        /// <param name="index">The index of the track within its kind.</param>
        /// <returns><see langword="true"/> when the play accepted it.</returns>
        private bool SelectTrack(TrackKind kind, int index) => kind switch
        {
            TrackKind.Audio => _play.SetAudioTrack(index),
            TrackKind.Video => _play.SetVideoTrack(index),
            _ => _play.SetSubtitleTrack(index),
        };

        /// <summary>Answers the index of a stream within its kind.</summary>
        /// <param name="stream">The stream to look at.</param>
        /// <returns>The index.</returns>
        private static int IndexOf(PlayStreamInfo stream) => stream.GetIndex();

#pragma warning restore CS0618

#pragma warning disable CS0618 // These two parses are the only ones available on the 1.24 floor.

        /// <summary>Reads the duration out of a duration message.</summary>
        /// <param name="message">The message to read.</param>
        /// <returns>The duration of the media.</returns>
        private static ClockTime ParseDuration(Message message)
        {
            PlayMessageExtensions.ParseDurationUpdated(message, out ClockTime duration);
            return duration;
        }

        /// <summary>Reads the percentage out of a buffering message.</summary>
        /// <param name="message">The message to read.</param>
        /// <returns>How full the buffer is.</returns>
        private static uint ParseBuffering(Message message)
        {
            PlayMessageExtensions.ParseBufferingPercent(message, out uint percent);
            return percent;
        }

#pragma warning restore CS0618
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the playlist, in the order it was given.</summary>
        internal List<string> Uris { get; } = [];

        /// <summary>Gets the volume to start at, or <see langword="null"/>.</summary>
        internal double? Volume { get; private set; }

        /// <summary>Gets the audio sink factory, or <see langword="null"/>.</summary>
        internal string? AudioSink { get; private set; }

        /// <summary>Gets the video sink factory, or <see langword="null"/>.</summary>
        internal string? VideoSink { get; private set; }

        /// <summary>Gets the visualization to enable, or <see langword="null"/>.</summary>
        internal string? Visualization { get; private set; }

        /// <summary>Gets whether the visualizations are to be listed.</summary>
        internal bool ListVisualizations { get; private set; }

        /// <summary>Gets whether the keyboard is read.</summary>
        internal bool Interactive { get; private set; }

        /// <summary>Gets whether the playlist is shuffled before playback.</summary>
        internal bool Shuffle { get; private set; }

        /// <summary>Gets how long the run may take, or zero for no bound.</summary>
        internal TimeSpan Duration { get; private set; }

        /// <summary>Gets whether the usage was asked for.</summary>
        internal bool Help { get; private set; }

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
                    case "--volume":
                        options.Volume = Math.Clamp(NumberOf(arguments, ref i), 0.0, 1.0);
                        break;

                    case "--audiosink":
                        options.AudioSink = ValueOf(arguments, ref i);
                        break;

                    case "--videosink":
                        options.VideoSink = ValueOf(arguments, ref i);
                        break;

                    case "--visualization":
                        options.Visualization = ValueOf(arguments, ref i);
                        break;

                    case "--list-visualizations":
                        options.ListVisualizations = true;
                        break;

                    case "--interactive":
                        options.Interactive = true;
                        break;

                    case "--shuffle":
                        options.Shuffle = true;
                        break;

                    case "--duration":
                        options.Duration = TimeSpan.FromSeconds(Math.Max(NumberOf(arguments, ref i), 0.0));
                        break;

                    case "--help":
                    case "-h":
                        options.Help = true;
                        break;

                    default:
                        if (arguments[i].StartsWith('-'))
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uris.Add(ToUri(arguments[i]));
                        break;
                }
            }

            return options;
        }

        /// <summary>
        /// Turns a command line argument into a URI, so that a local file can be
        /// passed where the play expects one.
        /// </summary>
        /// <param name="value">A URI, or the path of a local file.</param>
        /// <returns>The URI to hand to the play.</returns>
        private static string ToUri(string value) =>
            value.Contains("://", StringComparison.Ordinal)
                ? value

                // gst_filename_to_uri, which escapes the path the way GStreamer
                // itself does. A Windows path with a drive letter comes back as
                // file:///C:/... from it.
                : Global.FilenameToUri(Path.GetFullPath(value))
                    ?? throw new ArgumentException($"\"{value}\" is neither a URI nor a path.", nameof(value));

        /// <summary>
        /// Reads the number that follows an option.
        /// </summary>
        /// <param name="arguments">The arguments of the process.</param>
        /// <param name="index">The index of the option, advanced to its value.</param>
        /// <returns>The number.</returns>
        /// <exception cref="ArgumentException">The option has no value, or none that is a number.</exception>
        private static double NumberOf(string[] arguments, ref int index)
        {
            string option = arguments[index];
            string value = ValueOf(arguments, ref index);

            // A number that cannot be read is a command line error, which the
            // caller reports as such rather than as a failure of the play.
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number
                : throw new ArgumentException($"\"{option}\" needs a number, not \"{value}\".", nameof(arguments));
        }

        /// <summary>
        /// Reads the value that follows an option.
        /// </summary>
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
