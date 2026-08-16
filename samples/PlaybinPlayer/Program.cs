// The M1 exit criteria sample: it builds a pipeline from a description, runs
// it, and drives it from a polled bus, without a main loop and without a single
// signal handler.
//
// Usage: PlaybinPlayer [<uri>] [--native-path <directory>] [--flavor msvc|mingw]
//                      [--timeout <seconds>]
//
// Without a URI it plays a generated test pattern into a fakesink, so that the
// sample runs on a headless machine; with one it plays it through playbin.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return Player.Run(args);

internal static class Player
{
    private const string TestPattern =
        "videotestsrc num-buffers=300 ! videoconvert ! fakesink sync=false";

    /// <summary>
    /// Builds the pipeline, runs it and reports what the bus said.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
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
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");

            string description = options.Uri is null ? TestPattern : $"playbin uri={options.Uri}";
            Console.WriteLine($"pipeline:    {description}");

            // A description that holds a single element parses into that
            // element rather than into a pipeline. The cast goes through the
            // type registry: it is what turns the GstPipeline that
            // gst_parse_launch returns into this wrapper.
            if (Global.ParseLaunch(description) is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybinPlayer: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                // The bus wrapper is an interned GObject wrapper, shared with
                // every other lookup of the same bus, so it is not disposed
                // here. The pipeline is the sanctioned exception: this code
                // built it and sets it back to NULL before releasing it. See
                // docs/ownership.md.
                Bus bus = pipeline.GetBus();

                return Play(pipeline, bus, options.Timeout);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybinPlayer: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Runs the pipeline and pumps its bus until it ends, fails or runs out of
    /// time.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <param name="timeout">How long the pipeline may take.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Bus bus, TimeSpan timeout)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("PlaybinPlayer: the pipeline refused to go to PLAYING.");

                // The element that refused normally says why on the bus.
                Drain(bus);
                return 1;
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                // No main loop and no signals: the application owns the thread
                // and asks the bus for the next message it cares about.
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos | MessageType.StateChanged);

                if (message is null)
                {
                    continue;
                }

                switch (message.Type)
                {
                    case MessageType.Eos:
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    case MessageType.Error:
                        PrintError(message);
                        return 1;

                    case MessageType.StateChanged:
                        PrintStateChanged(message, pipeline);
                        break;

                    default:
                        break;
                }
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybinPlayer: no end of stream within {timeout.TotalSeconds:F0} s."));
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

    /// <summary>
    /// Prints the state transitions of the pipeline itself.
    /// </summary>
    /// <param name="message">The state changed message.</param>
    /// <param name="pipeline">The pipeline that is being run.</param>
    private static void PrintStateChanged(Message message, Pipeline pipeline)
    {
        // Every element posts its own transitions; only the ones of the
        // pipeline describe the run as a whole.
        Gst.Object? source = message.Src;
        if (source is null || source.Handle != pipeline.Handle)
        {
            return;
        }

        message.ParseStateChanged(out State old, out State current, out State pending);

        // gst_element_state_get_name is deprecated in 1.28 and its
        // replacement, gst_state_get_name, is a function of the enumeration
        // itself, which the generator does not emit yet. The names of the
        // managed enumeration say the same thing.
        string suffix = pending == State.VoidPending ? string.Empty : $" (pending {pending})";

        Console.WriteLine($"state:       {old} -> {current}{suffix}");
    }

    /// <summary>
    /// Prints an error message together with the element that posted it.
    /// </summary>
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

    /// <summary>
    /// Prints whatever the bus already holds after a failed state change.
    /// </summary>
    /// <param name="bus">The bus of the pipeline.</param>
    private static void Drain(Bus bus)
    {
        // PopFiltered returns immediately, so this drains the bus without any
        // main loop and without blocking when it is empty.
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
        /// <summary>Gets the media to play, or <see langword="null"/>.</summary>
        internal string? Uri { get; private set; }

        /// <summary>Gets how long the pipeline may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(60);

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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || options.Uri is not null)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uri = arguments[i];
                        break;
                }
            }

            return options;
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
