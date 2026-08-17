using System.Globalization;
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
/// One <c>DEVICE_CLASSES[:FILTER_CAPS]</c> argument, split the way the C tool
/// splits it.
/// </summary>
/// <param name="Classes">The device classes to keep, for example <c>Video/Source</c>.</param>
/// <param name="Caps">The caps written after the colon, if any.</param>
internal readonly record struct DeviceFilter(string Classes, string? Caps);

/// <summary>
/// The command line of the sample: the options of
/// <c>gst-device-monitor-1.0</c>, the <c>--gst-*</c> options that are handed to
/// GStreamer itself, and the few the samples of this repository share.
/// </summary>
internal sealed class Options
{
    /// <summary>The name the usage text calls the program.</summary>
    internal const string ProgramName = "GstDeviceMonitor";

    private readonly List<string> _initArgs = [];
    private readonly List<DeviceFilter> _filters = [];

    /// <summary>
    /// Gets a value indicating whether the run keeps going after the initial
    /// listing (<c>-f</c>).
    /// </summary>
    internal bool Follow { get; private set; }

    /// <summary>
    /// Gets a value indicating whether devices of hidden providers are listed
    /// (<c>-i</c>).
    /// </summary>
    internal bool IncludeHidden { get; private set; }

    /// <summary>Gets a value indicating whether the version was asked for.</summary>
    internal bool Version { get; private set; }

    /// <summary>Gets a value indicating whether the usage text was asked for.</summary>
    internal bool Help { get; private set; }

    /// <summary>
    /// Gets how long a <c>--follow</c> run listens before it stops on its own,
    /// or <see langword="null"/> to listen forever as the C tool does. This is
    /// not an option of <c>gst-device-monitor-1.0</c>; it exists so that the
    /// hotplug path can be run without a console signal to end it.
    /// </summary>
    internal TimeSpan? FollowFor { get; private set; }

    /// <summary>Gets the device filters that were named on the command line.</summary>
    internal IReadOnlyList<DeviceFilter> Filters => _filters;

    /// <summary>Gets the options of the native loader.</summary>
    internal GstSharpOptions Native { get; } = new();

    /// <summary>
    /// Gets the usage text, in the shape the option parser of GLib prints it.
    /// </summary>
    internal static string Usage =>
        $"""
        Usage:
          {ProgramName} [OPTION...] [DEVICE_CLASSES[:FILTER_CAPS]] [DEVICE_CLASSES[:FILTER_CAPS]] …

        Help Options:
          -h, --help                       Show help options

        Application Options:
              --version                    Print version information and exit
          -f, --follow                     Don't exit after showing the initial device list, but
                                           wait for devices to added/removed.
          -i, --include-hidden             Include devices from hidden device providers.
              --gst-*                      Passed on to GStreamer, for example
                                           --gst-debug-level=3. Only the --option=value form is
                                           understood here

        Sample Options:
              --native-path=DIRECTORY      Where to load the native GStreamer from
              --flavor=msvc|mingw          Which Windows build of GStreamer to load
              --follow-for=SECONDS         Stop a --follow run after this long. Not a
                                           gst-device-monitor option; it exists so that the
                                           hotplug path can be exercised without a signal
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
                options._filters.Add(SplitFilter(argument));
                continue;
            }

            // Everything after "--" is a filter, which is how a filter that
            // starts with a dash is passed.
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
                case "-f" or "--follow":
                    options.Follow = true;
                    break;

                case "-i" or "--include-hidden":
                    options.IncludeHidden = true;
                    break;

                case "--version":
                    options.Version = true;
                    break;

                case "-h" or "--help":
                    options.Help = true;
                    break;

                case "--follow-for":
                    options.FollowFor = ParseSeconds(ValueOf(arguments, ref i, name, inlineValue));
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
                    // A token that is not an option is a device filter. One
                    // that looks like an option and is not known is the error
                    // the option parser of GLib reports.
                    if (argument.StartsWith('-') && argument.Length > 1)
                    {
                        throw new OptionException($"Unknown option {argument}");
                    }

                    options._filters.Add(SplitFilter(argument));
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
    /// Splits one <c>DEVICE_CLASSES[:FILTER_CAPS]</c> argument.
    /// </summary>
    /// <param name="argument">The argument to split.</param>
    /// <returns>The classes and the caps.</returns>
    /// <remarks>
    /// The C tool splits with <c>g_strsplit (*arg, ":", 2)</c>, so only the
    /// first colon separates and every later one belongs to the caps — which is
    /// what makes <c>Video/Source:video/x-raw(memory:DMABuf)</c> parse.
    /// </remarks>
    private static DeviceFilter SplitFilter(string argument)
    {
        int colon = argument.IndexOf(':', StringComparison.Ordinal);

        return colon < 0
            ? new DeviceFilter(argument, null)
            : new DeviceFilter(argument[..colon], argument[(colon + 1)..]);
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
                $"Cannot parse number of seconds \"{text}\" for --follow-for");
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
