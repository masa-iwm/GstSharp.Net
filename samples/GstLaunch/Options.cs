using System.Globalization;
using System.Text;
using Gst;
using Gst.Interop;

/// <summary>
/// The command line could not be read.
/// </summary>
/// <remarks>
/// The option parser of GLib reports these as a plain sentence after
/// <c>Error initializing:</c>, so the message is the whole of the report and
/// carries no parameter name.
/// </remarks>
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
/// The command line of the sample: the options of <c>gst-launch-1.0</c>, the
/// <c>--gst-*</c> options that are handed to GStreamer itself, and the few the
/// samples of this repository share.
/// </summary>
internal sealed class Options
{
    /// <summary>The name the usage text calls the program.</summary>
    internal const string ProgramName = "GstLaunch";

    private readonly List<TimeSpan> _interruptAfter = [];
    private readonly List<string> _exclude = [];
    private readonly List<string> _initArgs = [];
    private readonly List<string> _description = [];

    /// <summary>Gets a value indicating whether tags are printed (<c>-t</c>).</summary>
    internal bool Tags { get; private set; }

    /// <summary>Gets a value indicating whether the table of contents is printed (<c>-c</c>).</summary>
    internal bool Toc { get; private set; }

    /// <summary>Gets a value indicating whether property changes are printed (<c>-v</c>).</summary>
    internal bool Verbose { get; private set; }

    /// <summary>Gets a value indicating whether progress information is suppressed (<c>-q</c>).</summary>
    internal bool Quiet { get; private set; }

    /// <summary>Gets a value indicating whether every message is printed (<c>-m</c>).</summary>
    internal bool Messages { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the pipeline is ended with an end of
    /// stream rather than torn down (<c>-e</c>).
    /// </summary>
    internal bool EosOnShutdown { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the fault handler is left uninstalled
    /// (<c>-f</c>). It only means anything where there is one to install; see
    /// <see cref="Launcher.InstallFaultHandler"/>.
    /// </summary>
    internal bool NoFault { get; private set; }

    /// <summary>Gets a value indicating whether the position line is suppressed.</summary>
    internal bool NoPosition { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the position line is printed even when
    /// the output is redirected.
    /// </summary>
    internal bool ForcePosition { get; private set; }

    /// <summary>Gets a value indicating whether the version was asked for.</summary>
    internal bool Version { get; private set; }

    /// <summary>Gets a value indicating whether the usage text was asked for.</summary>
    internal bool Help { get; private set; }

    /// <summary>
    /// Gets when to call the interrupt handler as though Ctrl+C had been
    /// pressed, counted from the start of the run. Repeating the option
    /// reaches the second interrupt, which is the one that stops a pipeline
    /// that is waiting for its end of stream. This is not an option of
    /// <c>gst-launch-1.0</c>.
    /// </summary>
    internal IReadOnlyList<TimeSpan> InterruptAfter => _interruptAfter;

    /// <summary>Gets the properties that <c>-v</c> does not report (<c>-X</c>).</summary>
    internal IReadOnlyList<string> Exclude => _exclude;

    /// <summary>Gets the options of the native loader.</summary>
    internal GstSharpOptions Native { get; } = new();

    /// <summary>
    /// Gets the pipeline description, built from what is left over the way
    /// <c>gst_parse_launchv</c> builds it.
    /// </summary>
    /// <remarks>
    /// <c>gst_parse_launchv</c> has no binding, so the argument vector is
    /// turned into the one description that <c>Global.ParseLaunch</c> takes.
    /// That is not a plain join: the C function escapes every argument first
    /// and only then concatenates, which is what keeps
    /// <c>location=my file.mp4</c> — one argument that the shell already
    /// unquoted — from being re-split by the parser. See
    /// <see cref="Escape(string)"/>.
    /// </remarks>
    internal string Description
    {
        get
        {
            StringBuilder description = new(1024);

            foreach (string argument in _description)
            {
                // gst_parse_launchv_full appends a space after every argument,
                // including the last one.
                description.Append(Escape(argument)).Append(' ');
            }

            return description.ToString();
        }
    }

    /// <summary>
    /// Gets the usage text, in the shape the option parser of GLib prints it.
    /// </summary>
    internal static string Usage =>
        $"""
        Usage:
          {ProgramName} [OPTION...] PIPELINE-DESCRIPTION

        Help Options:
          -h, --help                       Show help options

        Application Options:
          -t, --tags                       Output tags (also known as metadata)
          -c, --toc                        Output TOC (chapters and editions)
          -v, --verbose                    Output status information and property notifications
          -q, --quiet                      Do not print any progress information
          -m, --messages                   Output messages
          -X, --exclude=PROPERTY-NAME      Do not output status information for the specified
                                           property if verbose output is enabled (can be used
                                           multiple times)
          -f, --no-fault                   Do not install a fault handler (POSIX only)
          -e, --eos-on-shutdown            Force EOS on sources before shutting the pipeline down
              --version                    Print version information and exit
              --no-position                Do not print current position of pipeline. If this
                                           option is unspecified, the position will be printed
                                           when stdout is a TTY. To enable printing position when
                                           stdout is not a TTY, use "force-position" option
              --force-position             Allow printing current position of pipeline even if
                                           stdout is not a TTY. This option has no effect if the
                                           "no-position" option is specified
              --gst-*                      Passed on to GStreamer, for example
                                           --gst-debug-level=3. Only the --option=value form is
                                           understood here

        Sample Options:
              --native-path=DIRECTORY      Where to load the native GStreamer from
              --flavor=msvc|mingw          Which Windows build of GStreamer to load
              --interrupt-after=SECONDS    Interrupt the run as though Ctrl+C had been pressed
                                           (can be used multiple times). Not a gst-launch option;
                                           it exists so that the interrupt path can be exercised
                                           without a signal
        """;

    /// <summary>
    /// Reads the command line.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="OptionException">An option is unknown or incomplete.</exception>
    internal static Options Parse(string[] arguments)
    {
        Options options = new();
        bool literal = false;

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            if (literal)
            {
                options._description.Add(argument);
                continue;
            }

            // Everything after "--" is the description, which is how a
            // description that starts with a dash is passed.
            if (argument == "--")
            {
                literal = true;
                continue;
            }

            // The GStreamer options are not this program's to understand: they
            // travel to gst_init the way they do in the C tool.
            if (argument.StartsWith("--gst-", StringComparison.Ordinal))
            {
                options._initArgs.Add(argument);
                continue;
            }

            (string name, string? inlineValue) = Split(argument);

            switch (name)
            {
                case "-t" or "--tags":
                    options.Tags = true;
                    break;

                case "-c" or "--toc":
                    options.Toc = true;
                    break;

                case "-v" or "--verbose":
                    options.Verbose = true;
                    break;

                case "-q" or "--quiet":
                    options.Quiet = true;
                    break;

                case "-m" or "--messages":
                    options.Messages = true;
                    break;

                case "-e" or "--eos-on-shutdown":
                    options.EosOnShutdown = true;
                    break;

                case "-f" or "--no-fault":
                    options.NoFault = true;
                    break;

                case "--no-position":
                    options.NoPosition = true;
                    break;

                case "--force-position":
                    options.ForcePosition = true;
                    break;

                case "--version":
                    options.Version = true;
                    break;

                case "-h" or "--help":
                    options.Help = true;
                    break;

                case "-X" or "--exclude":
                    options._exclude.Add(ValueOf(arguments, ref i, name, inlineValue));
                    break;

                case "--native-path":
                    options.Native.NativeSearchPath = ValueOf(arguments, ref i, name, inlineValue);
                    break;

                case "--flavor":
                {
                    string flavor = ValueOf(arguments, ref i, name, inlineValue);

                    options.Native.WindowsFlavor = flavor.ToUpperInvariant() switch
                    {
                        "MSVC" => GstFlavor.Msvc,
                        "MINGW" => GstFlavor.MinGW,
                        _ => throw new OptionException(
                            $"Cannot parse flavor value \"{flavor}\" for --flavor: use msvc or mingw"),
                    };
                    break;
                }

                case "--interrupt-after":
                    options._interruptAfter.Add(ParseSeconds(ValueOf(arguments, ref i, name, inlineValue)));
                    break;

                default:
                    // A token that is not an option belongs to the description.
                    // One that looks like an option and is not known is the
                    // error the option parser of GLib reports.
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OptionException($"Unknown option {argument}");
                    }

                    options._description.Add(argument);
                    break;
            }
        }

        if (options._initArgs.Count > 0)
        {
            options.Native.InitArgs = [.. options._initArgs];
        }

        return options;
    }

    /// <summary>
    /// Tells whether the position of the pipeline is printed while it runs.
    /// </summary>
    /// <param name="outputIsTty">
    /// Whether the output is a terminal, which is what decides between a line
    /// that is overwritten and one that scrolls.
    /// </param>
    /// <returns><see langword="true"/> when the position is printed.</returns>
    internal bool WantsPosition(out bool outputIsTty)
    {
        outputIsTty = !Console.IsOutputRedirected;
        return !NoPosition && (outputIsTty || ForcePosition);
    }

    /// <summary>
    /// Escapes one argument of the vector the way <c>_gst_parse_escape</c> of
    /// <c>gst/gstparse.c</c> does, so that the concatenation parses back into
    /// the arguments it was built from.
    /// </summary>
    /// <param name="argument">The argument to escape.</param>
    /// <returns>The escaped argument.</returns>
    /// <remarks>
    /// The rule is short and worth stating exactly, because it is the whole
    /// difference between this and a join: a space outside a double quoted run
    /// is prefixed with a backslash, and a double quote flips "inside a quoted
    /// run" unless the quote itself was escaped. Nothing else is touched, so an
    /// argument that carries no space comes back unchanged and the description
    /// reads as it was typed.
    /// </remarks>
    private static string Escape(string argument)
    {
        StringBuilder escaped = new(argument.Length);
        bool inQuotes = false;

        for (int i = 0; i < argument.Length; i++)
        {
            char character = argument[i];

            if (character == '"' && (!inQuotes || argument[i - 1] != '\\'))
            {
                inQuotes = !inQuotes;
            }

            if (character == ' ' && !inQuotes)
            {
                escaped.Append('\\');
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }

    /// <summary>
    /// Reads a number of seconds.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <returns>The duration.</returns>
    /// <exception cref="OptionException">The text is not a number.</exception>
    private static TimeSpan ParseSeconds(string text)
    {
        if (!double.TryParse(text, CultureInfo.InvariantCulture, out double seconds) || seconds < 0)
        {
            throw new OptionException(
                $"Cannot parse number of seconds \"{text}\" for --interrupt-after");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Splits an option from the value that is written into the same token.
    /// </summary>
    /// <param name="argument">The token to split.</param>
    /// <returns>The name of the option and the value it carries, if any.</returns>
    private static (string Name, string? Value) Split(string argument)
    {
        int equals = argument.IndexOf('=', StringComparison.Ordinal);

        return equals < 0
            ? (argument, null)
            : (argument[..equals], argument[(equals + 1)..]);
    }

    /// <summary>
    /// Reads the value of an option, from the same token or the next one.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <param name="index">The index of the option, advanced past its value.</param>
    /// <param name="name">The name of the option, for the error message.</param>
    /// <param name="inlineValue">The value that followed an <c>=</c>, if any.</param>
    /// <returns>The value.</returns>
    /// <exception cref="OptionException">The option has no value.</exception>
    private static string ValueOf(string[] arguments, ref int index, string name, string? inlineValue)
    {
        if (inlineValue is not null)
        {
            return inlineValue;
        }

        if (index + 1 >= arguments.Length)
        {
            throw new OptionException($"Missing argument for {name}");
        }

        return arguments[++index];
    }
}
