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
/// The command line of the sample: the one option of <c>gst-typefind-1.0</c>,
/// the <c>--gst-*</c> options that are handed to GStreamer itself, and the few
/// the samples of this repository share.
/// </summary>
internal sealed class Options
{
    /// <summary>The name the usage text calls the program.</summary>
    internal const string ProgramName = "GstTypefind";

    private readonly List<string> _initArgs = [];
    private readonly List<string> _files = [];

    /// <summary>Gets a value indicating whether the version was asked for.</summary>
    internal bool Version { get; private set; }

    /// <summary>Gets a value indicating whether the usage text was asked for.</summary>
    internal bool Help { get; private set; }

    /// <summary>
    /// Gets a value indicating whether a file whose type was not found ends the
    /// run with a non-zero exit code. This is not an option of
    /// <c>gst-typefind-1.0</c>, which always exits 0 once it has files to look
    /// at; it exists so that an automated run can gate on the result.
    /// </summary>
    internal bool FailOnUnknown { get; private set; }

    /// <summary>Gets the files and directories to identify.</summary>
    internal IReadOnlyList<string> Files => _files;

    /// <summary>Gets the options of the native loader.</summary>
    internal GstSharpOptions Native { get; } = new();

    /// <summary>
    /// Gets the usage text, in the shape the option parser of GLib prints it.
    /// </summary>
    internal static string Usage =>
        $"""
        Usage:
          {ProgramName} [OPTION...] FILES

        Help Options:
          -h, --help                       Show help options

        Application Options:
              --version                    Print version information and exit
              --gst-*                      Passed on to GStreamer, for example
                                           --gst-debug-level=3. Only the --option=value form is
                                           understood here

        Sample Options:
              --native-path=DIRECTORY      Where to load the native GStreamer from
              --flavor=msvc|mingw          Which Windows build of GStreamer to load
              --fail-on-unknown            Exit with 1 when the type of a file could not be
                                           found. Not a gst-typefind option; it exists so that an
                                           automated run can gate on the result rather than only
                                           on the absence of a crash
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
                options._files.Add(argument);
                continue;
            }

            // Everything after "--" is a file name, which is how a file whose
            // name starts with a dash is passed.
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
                case "--version":
                    options.Version = true;
                    break;

                case "-h" or "--help":
                    options.Help = true;
                    break;

                case "--fail-on-unknown":
                    options.FailOnUnknown = true;
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

                default:
                    // A token that is not an option is a file name. One that
                    // looks like an option and is not known is the error the
                    // option parser of GLib reports.
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OptionException($"Unknown option {argument}");
                    }

                    options._files.Add(argument);
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
