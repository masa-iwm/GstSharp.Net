using System.Globalization;
using System.Text;
using GES;
using Gst;
using Gst.Interop;

/// <summary>
/// The command line could not be read.
/// </summary>
internal sealed class OptionException : Exception
{
    /// <summary>Initialises the error without a message.</summary>
    internal OptionException()
    {
    }

    /// <summary>Initialises the error.</summary>
    /// <param name="message">What is wrong with the command line.</param>
    internal OptionException(string message)
        : base(message)
    {
    }

    /// <summary>Initialises the error.</summary>
    /// <param name="message">What is wrong with the command line.</param>
    /// <param name="innerException">What went wrong underneath.</param>
    internal OptionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The command line of the sample: the options of <c>ges-launch-1.0</c> that
/// this port carries, the timeline description that follows them, and the
/// options the samples of this repository share.
/// </summary>
internal sealed class Options
{
    /// <summary>The characters that need no quoting inside a description token.</summary>
    /// <remarks>
    /// This is <c>ASCII_IS_STRING</c> of <c>utils.c:30-32</c>. The test is
    /// deliberately ASCII only: anything else is what makes a token be wrapped
    /// in quotes so that the structure parser of the library keeps it in one
    /// piece.
    /// </remarks>
    /// <param name="character">The character to classify.</param>
    /// <returns><see langword="true"/> when it may stand unquoted.</returns>
    private static bool IsStringCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '_' or '-' or '+' or '/' or ':' or '.';

    /// <summary>Gets the description tokens that follow the options.</summary>
    private List<string> Tokens { get; } = [];

    /// <summary>Gets the project file to load, the <c>-l</c> option.</summary>
    internal string? LoadPath { get; private set; }

    /// <summary>Gets the file to save the project to, the <c>-s</c> option.</summary>
    internal string? SavePath { get; private set; }

    /// <summary>Gets the file to save the project to before exiting.</summary>
    internal string? SaveOnlyPath { get; private set; }

    /// <summary>Gets a value indicating whether to list the transition types.</summary>
    internal bool ListTransitions { get; private set; }

    /// <summary>Gets a value indicating whether the synopsis was asked for.</summary>
    internal bool Help { get; private set; }

    /// <summary>Gets where the timeline is rendered to, the <c>-o</c> option.</summary>
    internal string? OutputUri { get; private set; }

    /// <summary>Gets the serialized encoding profile, the <c>-f</c> option.</summary>
    internal string? Format { get; private set; }

    /// <summary>Gets the name of a profile the project carries, the <c>-e</c> option.</summary>
    internal string? EncodingProfile { get; private set; }

    /// <summary>
    /// Gets the muxer profile the profile tree is re-parented into, the
    /// <c>--container-profile</c> option.
    /// </summary>
    internal string? ContainerProfile { get; private set; }

    /// <summary>
    /// Gets the name of the clip the encoding profile is read out of, the
    /// <c>--profile-from</c> option.
    /// </summary>
    internal string? ProfileFrom { get; private set; }

    /// <summary>Gets a value indicating whether tags are forwarded to the output.</summary>
    internal bool ForwardTags { get; private set; }

    /// <summary>Gets a value indicating whether rendering avoids reencoding.</summary>
    internal bool SmartRendering { get; private set; }

    /// <summary>Gets the description of the video sink of a preview.</summary>
    internal string? VideoSink { get; private set; }

    /// <summary>Gets the description of the audio sink of a preview.</summary>
    internal string? AudioSink { get; private set; }

    /// <summary>Gets a value indicating whether the preview goes nowhere.</summary>
    internal bool Mute { get; private set; }

    /// <summary>Gets a value indicating whether the layers are mixed together.</summary>
    internal bool DisableMixing { get; private set; }

    /// <summary>Gets the track types the timeline keeps, the <c>-t</c> option.</summary>
    internal TrackType TrackTypes { get; private set; } = TrackType.Audio | TrackType.Video;

    /// <summary>Gets the restriction caps of the video track.</summary>
    internal string? VideoCaps { get; private set; }

    /// <summary>Gets the restriction caps of the audio track.</summary>
    internal string? AudioCaps { get; private set; }

    /// <summary>Gets a value indicating whether the keyboard was asked for.</summary>
    internal bool Interactive { get; private set; } = true;

    /// <summary>Gets a value indicating whether the end of stream ends the run.</summary>
    internal bool IgnoreEos { get; private set; }

    /// <summary>Gets how long the whole run may take, zero for no bound.</summary>
    internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the options of the native loader.</summary>
    internal GstSharpOptions Native { get; } = new();

    /// <summary>
    /// Gets the <c>ges:</c> description the tokens of the command line spell,
    /// or <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// This is <c>sanitize_timeline_description</c> of <c>utils.c:92-157</c>:
    /// every token is escaped, the whole is prefixed with the scheme the
    /// library dispatches on, and the tracks the user asked for with
    /// <c>-t</c>, <c>--video-caps</c> and <c>--audio-caps</c> are synthesized
    /// as <c>+track</c> keywords unless the description adds tracks itself.
    /// The audio track is prepended after the video one, so it comes first,
    /// which is the order the C tool builds as well.
    /// </remarks>
    internal string? TimelineDescription
    {
        get
        {
            if (Tokens.Count == 0)
            {
                return null;
            }

            StringBuilder description = new(" ");
            string? previous = null;
            bool addsTracks = false;

            foreach (string token in Tokens)
            {
                description.Append(' ').Append(Sanitize(token, previous));
                addsTracks |= string.Equals(token, "+track", StringComparison.Ordinal);
                previous = token;
            }

            if (addsTracks)
            {
                return "ges:" + description.ToString();
            }

            if ((TrackTypes & TrackType.Video) != 0)
            {
                description.Insert(0, TrackDefinition("video", VideoCaps));
            }

            if ((TrackTypes & TrackType.Audio) != 0)
            {
                description.Insert(0, TrackDefinition("audio", AudioCaps));
            }

            return description.Insert(0, "ges:").ToString();
        }
    }

    /// <summary>Reads the command line.</summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="OptionException">An argument is unknown or incomplete.</exception>
    internal static Options Parse(string[] arguments)
    {
        Options options = new();

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            switch (argument)
            {
                case "-l":
                case "--load":
                    options.LoadPath = ValueOf(arguments, ref i);
                    break;

                case "-s":
                case "--save":
                    options.SavePath = ValueOf(arguments, ref i);
                    break;

                case "--save-only":
                    options.SaveOnlyPath = ValueOf(arguments, ref i);
                    break;

                case "--list-transitions":
                    options.ListTransitions = true;
                    break;

                case "-o":
                case "--outputuri":
                    options.OutputUri = ValueOf(arguments, ref i);
                    break;

                case "-f":
                case "--format":
                    options.Format = ValueOf(arguments, ref i);
                    break;

                case "-e":
                case "--encoding-profile":
                    options.EncodingProfile = ValueOf(arguments, ref i);
                    break;

                case "--container-profile":
                    options.ContainerProfile = ValueOf(arguments, ref i);
                    break;

                case "--profile-from":
                    options.ProfileFrom = ValueOf(arguments, ref i);
                    break;

                case "--forward-tags":
                    options.ForwardTags = true;
                    break;

                case "--smart-rendering":
                    options.SmartRendering = true;
                    break;

                case "-v":
                case "--videosink":
                    options.VideoSink = ValueOf(arguments, ref i);
                    break;

                case "-a":
                case "--audiosink":
                    options.AudioSink = ValueOf(arguments, ref i);
                    break;

                case "-m":
                case "--mute":
                    options.Mute = true;
                    break;

                case "--disable-mixing":
                    options.DisableMixing = true;
                    break;

                case "-t":
                case "--track-types":
                    options.TrackTypes = ParseTrackTypes(ValueOf(arguments, ref i));
                    break;

                case "--video-caps":
                    options.VideoCaps = ValueOf(arguments, ref i);
                    break;

                case "--audio-caps":
                    options.AudioCaps = ValueOf(arguments, ref i);
                    break;

                case "--no-interactive":
                    options.Interactive = false;
                    break;

                case "--ignore-eos":
                    options.IgnoreEos = true;
                    break;

                case "--timeout":
                    options.Timeout = ParseTimeout(ValueOf(arguments, ref i));
                    break;

                case "--native-path":
                    options.Native.NativeSearchPath = ValueOf(arguments, ref i);
                    break;

                case "--flavor":
                    options.Native.WindowsFlavor = ValueOf(arguments, ref i).ToUpperInvariant() switch
                    {
                        "MSVC" => GstFlavor.Msvc,
                        "MINGW" => GstFlavor.MinGW,
                        string other => throw new OptionException(
                            $"\"{other}\" is not a flavor. Use msvc or mingw."),
                    };
                    break;

                case "-h":
                case "--help":
                    options.Help = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        throw new OptionException($"Unknown option {argument}");
                    }

                    options.Tokens.Add(argument);
                    break;
            }
        }

        return options;
    }

    /// <summary>Prints the synopsis of the sample.</summary>
    internal static void PrintUsage()
    {
        Console.WriteLine("Usage: GesLaunch [OPTION...] [+clip <uri> [<property>=<value>...] ...]");
        Console.WriteLine();
        Console.WriteLine("Plays or renders a timeline. The description that follows the options is");
        Console.WriteLine("parsed by the editing services themselves; see the ges-launch-1.0 manual");
        Console.WriteLine("page for the keywords +clip, +test-clip, +effect, +title, +track,");
        Console.WriteLine("+keyframes and set-<property>, and their arguments.");
        Console.WriteLine();
        Console.WriteLine("Project options:");
        Console.WriteLine("  -l, --load <path>          Load the project from a file.");
        Console.WriteLine("  -s, --save <path>          Save the project to a file, +r for its own uri.");
        Console.WriteLine("      --save-only <path>     Save the project and exit without playing it.");
        Console.WriteLine();
        Console.WriteLine("Informative options:");
        Console.WriteLine("      --list-transitions     List the transition types and exit.");
        Console.WriteLine("  -h, --help                 Print this synopsis and exit.");
        Console.WriteLine();
        Console.WriteLine("Rendering options:");
        Console.WriteLine("  -o, --outputuri <uri>      Render the timeline into <uri> instead of playing it.");
        Console.WriteLine("  -f, --format <profile>     The serialized encoding profile to render with.");
        Console.WriteLine("  -e, --encoding-profile <n> The name of a profile the loaded project carries.");
        Console.WriteLine("      --profile-from <name>  Build the profile and the tracks from the named clip.");
        Console.WriteLine("      --container-profile <p> Re-parent the profile tree into this muxer profile.");
        Console.WriteLine("      --forward-tags         Forward the tags of the input files to the output.");
        Console.WriteLine("      --smart-rendering      Avoid reencoding; implies --disable-mixing. Without");
        Console.WriteLine("                             --format the profile comes from the discoverer");
        Console.WriteLine("                             information of the clips of the timeline.");
        Console.WriteLine();
        Console.WriteLine("Playback options:");
        Console.WriteLine("  -v, --videosink <desc>     The video sink of the preview.");
        Console.WriteLine("  -a, --audiosink <desc>     The audio sink of the preview.");
        Console.WriteLine("  -m, --mute                 Send the preview to fake sinks.");
        Console.WriteLine();
        Console.WriteLine("Timeline options:");
        Console.WriteLine("      --disable-mixing       Do not mix the layers together.");
        Console.WriteLine("  -t, --track-types <types>  audio, video or audio+video. Default audio+video.");
        Console.WriteLine("      --video-caps <caps>    The restriction caps of the video track.");
        Console.WriteLine("      --audio-caps <caps>    The restriction caps of the audio track.");
        Console.WriteLine("      --no-interactive       Do not read the keyboard.");
        Console.WriteLine("      --ignore-eos           Keep running after the end of stream.");
        Console.WriteLine();
        Console.WriteLine("Sample options:");
        Console.WriteLine("      --timeout <seconds>    Give up after <seconds>. Default 30, 0 for none.");
        Console.WriteLine("      --native-path <dir>    Where to load the native GStreamer from.");
        Console.WriteLine("      --flavor msvc|mingw    Which Windows build of GStreamer to load.");
    }

    /// <summary>Reads the value that follows an option.</summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <param name="index">The index of the option, advanced to its value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="OptionException">The option has no value.</exception>
    private static string ValueOf(string[] arguments, ref int index)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new OptionException($"\"{arguments[index]}\" needs a value.");
        }

        return arguments[++index];
    }

    /// <summary>Reads the value of the <c>--timeout</c> option.</summary>
    /// <param name="value">What was written on the command line.</param>
    /// <returns>How long the run may take.</returns>
    /// <exception cref="OptionException">The value is not a number of seconds.</exception>
    private static TimeSpan ParseTimeout(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            || double.IsNaN(seconds)
            || seconds < 0.0)
        {
            throw new OptionException($"\"{value}\" is not a number of seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Reads the value of the <c>-t</c> option.</summary>
    /// <param name="value">What was written on the command line.</param>
    /// <returns>The track types it names.</returns>
    /// <exception cref="OptionException">A name is not a track type.</exception>
    /// <remarks>
    /// The C tool hands the string to <c>gst_value_deserialize</c> against the
    /// flags type of <c>GESTrackType</c> (<c>utils.c:158-174</c>), which needs
    /// the type to be registered already. That deserializer is
    /// <c>gst_value_gflags_str_to_flags</c> (<c>gstvalue.c:4575-4622</c>): it
    /// separates the nicks with <c>+</c> and <c>/</c>, and a nick reached
    /// through <c>/</c> names a flag that is deliberately *not* set - it only
    /// goes into the mask, which deserializing a plain flags value throws
    /// away. Both separators are read here for that reason. The <c>|</c> and
    /// the <c>,</c> are not the deserializer's; this sample accepted them
    /// before the <c>/</c> was added and keeps doing so.
    /// </remarks>
    private static TrackType ParseTrackTypes(string value)
    {
        TrackType types = 0;

        // A leading separator belongs to the term after it; anything else
        // starts as if it had been written with a "+".
        char separator = '+';

        foreach (string term in Split(value))
        {
            if (term.Length == 1 && (term[0] == '+' || term[0] == '/'))
            {
                separator = term[0];
                continue;
            }

            TrackType type = term.Trim() switch
            {
                "audio" => TrackType.Audio,
                "video" => TrackType.Video,
                "text" => TrackType.Text,
                "custom" => TrackType.Custom,
                "unknown" => TrackType.Unknown,
                string other => throw new OptionException($"\"{other}\" is not a track type."),
            };

            if (separator == '+')
            {
                types |= type;
            }

            separator = '+';
        }

        return types;

        // Yields the nicks and the "+" and "/" between them, so that the
        // caller can tell which of the two introduced a nick.
        static IEnumerable<string> Split(string value)
        {
            int start = 0;

            for (int i = 0; i <= value.Length; i++)
            {
                bool end = i == value.Length;

                if (!end && value[i] != '+' && value[i] != '/' && value[i] != '|' && value[i] != ',')
                {
                    continue;
                }

                if (i > start)
                {
                    yield return value[start..i];
                }

                if (!end && (value[i] == '+' || value[i] == '/'))
                {
                    yield return value[i].ToString();
                }

                start = i + 1;
            }
        }
    }

    /// <summary>Spells the <c>+track</c> keyword of a synthesized track.</summary>
    /// <param name="type">The nick of the track type.</param>
    /// <param name="caps">The restriction caps, if any were asked for.</param>
    /// <returns>The keyword and its arguments, with the spacing of the C tool.</returns>
    private static string TrackDefinition(string type, string? caps) =>
        caps is null
            ? $" +track {type} "
            : $" +track {type}  restrictions=[{caps}] ";

    /// <summary>
    /// Escapes one token of the description so that the structure parser of
    /// the library reads it as one value.
    /// </summary>
    /// <param name="argument">The token as it was written.</param>
    /// <param name="previous">The token before it, or <see langword="null"/>.</param>
    /// <returns>The token, quoted when it has to be.</returns>
    /// <remarks>
    /// This is <c>_sanitize_argument</c> of <c>utils.c:35-88</c>. A token that
    /// is a property assignment - one that neither starts a keyword nor follows
    /// one - keeps its first <c>=</c> outside the quotes, so that
    /// <c>name=a b</c> becomes <c>name="a b"</c> and not <c>"name=a b"</c>.
    /// </remarks>
    private static string Sanitize(string argument, string? previous)
    {
        bool expectEqual = !(argument.StartsWith('+')
            || argument.StartsWith("set-", StringComparison.Ordinal)
            || previous is null
            || previous.StartsWith('+')
            || previous.StartsWith("set-", StringComparison.Ordinal));

        bool needWrap = false;
        int firstEqual = -1;

        for (int i = 0; i < argument.Length; i++)
        {
            char character = argument[i];

            if (expectEqual && firstEqual < 0 && character == '=')
            {
                firstEqual = i;
            }
            else if (!IsStringCharacter(character))
            {
                needWrap = true;
                break;
            }
        }

        if (!needWrap)
        {
            return argument;
        }

        int wrapStart = firstEqual < 0 ? 0 : firstEqual + 1;
        StringBuilder wrapped = new(argument.Length + 2);

        wrapped.Append(argument, 0, wrapStart).Append('"');

        for (int i = wrapStart; i < argument.Length; i++)
        {
            char character = argument[i];

            if (character is '"' or '\\')
            {
                wrapped.Append('\\');
            }

            wrapped.Append(character);
        }

        return wrapped.Append('"').ToString();
    }
}
