// Playback tutorial 5: colour balance — reading the channels an element offers
// and moving contrast, brightness, hue and saturation while it plays.
//
// Ported from playback-tutorial-5.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/color-balance.html
//
// Usage: PlaybackTutorial05 [<uri-or-file>] [--headless] [--keys <string>]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop and no GIOChannel. The loop below polls
//     the bus with TimedPopFiltered and asks Console.KeyAvailable whether a key
//     is waiting, which is one program on every operating system rather than
//     the two the C original needs — g_io_channel_win32_new_fd on Windows and
//     g_io_channel_unix_new everywhere else.
//
//   * The pipeline is built with ElementFactory.Make rather than with
//     gst_parse_launch, because --headless has to hand playbin two sink objects
//     and a parsed description can only carry strings. The element and the URI
//     are the same ones.
//
//   * The C keyboard handler reads a whole line and looks at its first
//     character. This one reads single characters, because that is what an
//     unattended --keys run can feed and what Console.KeyAvailable gives. The
//     keys themselves are the tutorial's: 'C'/'c' contrast, 'B'/'b'
//     brightness, 'H'/'h' hue, 'S'/'s' saturation — upper case increases, lower
//     case decreases — and 'Q'/'q' quits.
//
//   * 'q' is the tutorial's own key and quits with 0 here as it does there.
//     What the port adds is every other way out: the C watches no bus at all
//     and so handles neither an error nor the end of the stream, while this
//     one polls for both and exits 1 on an error and 0 on EOS, and it bounds
//     the whole run with --timeout so that an unattended run finishes.
//
//   * --keys is sample scaffolding, identical in mechanism to
//     BasicTutorial13's: the characters are fed to the same handler the
//     keyboard feeds, one every half second, starting once the pipeline has
//     reported PLAYING. A scripted run also checks what it did: the value the
//     channel is expected to take is computed before the key is applied and
//     compared with the value the element reports afterwards, and a run where
//     one of them did not match exits 1. So do the two other ways a script can
//     move nothing: a scripted key that names a channel the element does not
//     list, and an end of stream that arrives while keys are still unfed.
//     Without all that a headless run would print numbers nobody reads.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card. The
//     colour balance is still real: with no colour balance element in the sink,
//     playsink inserts a videobalance of its own ahead of the sink, and its own
//     four proxy channels answer and remember the values either way.
//
//   * channel->label, ->min_value and ->max_value are instance fields of a
//     GstColorBalanceChannel with no accessor in C. They are hand bound here as
//     Gst.Video.ColorBalanceChannel.Label, .MinValue and .MaxValue; Label is
//     nullable, because the C leaves it NULL until an implementer sets one, so
//     a channel with no label is skipped rather than matched.
//
//   * The channel list is read once and held. gst_color_balance_set_value
//     matches the channel it is given against the element's own channel
//     objects, so the objects the element listed are the ones that have to be
//     handed back to it. The channels are interned wrappers that hold their
//     own reference; none of them is the caller's to dispose.
//
//   * GstVideo.Initialize rather than GstSharp.Initialize: it is a call into
//     the GstVideo assembly, which is what makes sure that its module
//     initialiser has run and that a GstColorBalanceChannel is wrapped as one.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;
using Gst.Video;

return ColorBalance.Run(args);

internal static class ColorBalance
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>How long a scripted run waits between two keys.</summary>
    private static readonly TimeSpan KeyInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Plays the media and moves its colour balance while it runs.
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

            GstVideo.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"uri:         {options.Uri}");
            Console.WriteLine("USAGE: Press one of the following keys:");
            Console.WriteLine(" 'C' to increase contrast, 'c' to decrease contrast");
            Console.WriteLine(" 'B' to increase brightness, 'b' to decrease brightness");
            Console.WriteLine(" 'H' to increase hue, 'h' to decrease hue");
            Console.WriteLine(" 'S' to increase saturation, 's' to decrease saturation");
            Console.WriteLine(" 'Q' to quit");

            if (ElementFactory.Make("playbin", "playbin") is not Element playbin)
            {
                Console.Error.WriteLine("PlaybackTutorial05: Not all elements could be created.");
                return 1;
            }

            using (playbin)
            {
                playbin.SetProperty("uri", options.Uri);

                if (options.Headless && !Silence(playbin))
                {
                    return 1;
                }

                if (playbin.As<IColorBalance>() is not IColorBalance balance)
                {
                    Console.Error.WriteLine("PlaybackTutorial05: playbin offers no colour balance.");
                    return 1;
                }

                return Play(playbin, balance, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial05: {exception}");
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
                Console.Error.WriteLine("PlaybackTutorial05: fakesink could not be created.");
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
    /// <param name="balance">The colour balance of that same pipeline.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as asked, 1 on any error.</returns>
    private static int Play(Element playbin, IColorBalance balance, Options options)
    {
        try
        {
            // The channels are read once and held: the element matches the
            // channel it is handed against its own objects, so a channel from
            // any other list is refused. They are interned wrappers that hold
            // their own reference; not the caller's to dispose.
            IReadOnlyList<ColorBalanceChannel> channels = balance.ListChannels();

            if (channels.Count == 0)
            {
                Console.Error.WriteLine("PlaybackTutorial05: the pipeline offers no colour balance channel.");
                return 1;
            }

            if (playbin.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial05: Unable to set the pipeline to the playing state.");
                return 1;
            }

            PrintCurrentValues(balance, channels);

            if (playbin.GetBus() is not Bus bus)
            {
                Console.Error.WriteLine("PlaybackTutorial05: the pipeline has no bus.");
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

                            if (keys.Pending)
                            {
                                // A script the media outlasted applied only
                                // some of its keys, which is not the run the
                                // caller asked for.
                                Console.Error.WriteLine(
                                    "PlaybackTutorial05: the media ended with keys of --keys still unfed.");
                                return 1;
                            }

                            return 0;
                        }

                        if (message.Src is { } source && source.Handle == playbin.Handle)
                        {
                            message.ParseStateChanged(out _, out State current, out _);

                            if (current == State.Playing)
                            {
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

                if (NameOf(key) is string channelName
                    && !UpdateColorChannel(
                        channelName,
                        char.IsAsciiLetterUpper(key),
                        balance,
                        channels,
                        options.Script is not null))
                {
                    return 1;
                }

                PrintCurrentValues(balance, channels);
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial05: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            playbin.SetState(State.Null);
        }
    }

    /// <summary>
    /// Says which channel a key is about.
    /// </summary>
    /// <param name="key">The key that was pressed or scripted.</param>
    /// <returns>The label to look for, or <see langword="null"/>.</returns>
    private static string? NameOf(char key) => char.ToUpperInvariant(key) switch
    {
        'C' => "CONTRAST",
        'B' => "BRIGHTNESS",
        'H' => "HUE",
        'S' => "SATURATION",
        _ => null,
    };

    /// <summary>
    /// Moves one channel of the colour balance by a tenth of its range, which
    /// is the whole of the tutorial.
    /// </summary>
    /// <param name="channelName">The label to look for.</param>
    /// <param name="increase">Whether the value goes up rather than down.</param>
    /// <param name="balance">The colour balance to write to.</param>
    /// <param name="channels">The channels that balance listed.</param>
    /// <param name="scripted">Whether the key came from <c>--keys</c>.</param>
    /// <returns>
    /// <see langword="false"/> when the element did not take the value that was
    /// written, or when a scripted key names a channel the element does not
    /// list, which is what makes an unattended run a gate rather than a print.
    /// </returns>
    private static bool UpdateColorChannel(
        string channelName,
        bool increase,
        IColorBalance balance,
        IReadOnlyList<ColorBalanceChannel> channels,
        bool scripted)
    {
        // The C matches with g_strrstr, a substring search, because an element
        // is free to give its channels longer names than the four the tutorial
        // knows. A channel whose label was never set is skipped: the field is
        // NULL until an implementer writes one.
        ColorBalanceChannel? channel = null;

        foreach (ColorBalanceChannel candidate in channels)
        {
            if (candidate.Label is { } label && label.Contains(channelName, StringComparison.Ordinal))
            {
                channel = candidate;
                break;
            }
        }

        if (channel is null)
        {
            // A person who pressed a key the element has no channel for is left
            // alone, as upstream leaves them. A script is not: a key that moves
            // nothing is the failure this sample is run unattended to catch.
            if (scripted)
            {
                Console.Error.WriteLine(
                    $"PlaybackTutorial05: no channel of this element is labelled {channelName}.");
                return false;
            }

            return true;
        }

        double step = 0.1 * (channel.MaxValue - channel.MinValue);
        int value = balance.GetValue(channel);

        if (increase)
        {
            value = (int)(value + step);

            if (value > channel.MaxValue)
            {
                value = channel.MaxValue;
            }
        }
        else
        {
            value = (int)(value - step);

            if (value < channel.MinValue)
            {
                value = channel.MinValue;
            }
        }

        balance.SetValue(channel, value);

        int written = balance.GetValue(channel);

        if (written != value)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial05: {channelName} was set to {value} and reads back as {written}."));
            return false;
        }

        return true;
    }

    /// <summary>
    /// Prints the current value of every channel, as the percentage of its own
    /// range the C original prints.
    /// </summary>
    /// <param name="balance">The colour balance to read.</param>
    /// <param name="channels">The channels that balance listed.</param>
    private static void PrintCurrentValues(
        IColorBalance balance,
        IReadOnlyList<ColorBalanceChannel> channels)
    {
        foreach (ColorBalanceChannel channel in channels)
        {
            int value = balance.GetValue(channel);
            int range = channel.MaxValue - channel.MinValue;
            int percent = range == 0 ? 0 : (100 * (value - channel.MinValue)) / range;

            Console.Write(string.Create(
                CultureInfo.InvariantCulture,
                $"{channel.Label ?? "?"}: {percent,3}% "));
        }

        Console.WriteLine();
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
        /// Gets a value indicating whether a script has keys left to feed. It
        /// is false for the interactive path, which has no end of its own.
        /// </summary>
        internal bool Pending => _script is not null && _index < _script.Length;

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
        /// The channels answer from the moment the sink is built, so a key
        /// would work earlier than this; waiting for PLAYING is what makes a
        /// scripted run look like the manual one the tutorial describes, where
        /// the picture is on screen while the values move.
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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || positional >= 1)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uri = Cli.ToUri(arguments[i]);
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
