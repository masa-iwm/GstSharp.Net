// Basic tutorial 1: Hello world — the smallest GStreamer program there is.
//
// Ported from basic-tutorial-1.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/hello-world.html
//
// Usage: BasicTutorial01 [<uri-or-file>] [--native-path <directory>]
//                        [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * gst_init is GstSharp.Initialize. It also resolves and loads the native
//     library, which C does not have to do: there the linker did it.
//
//   * The C program waits forever in one gst_bus_timed_pop_filtered with
//     GST_CLOCK_TIME_NONE. This polls the bus in short slices instead, which is
//     the house style of the binding: a .NET application owns its thread and
//     asks the bus for the next message it cares about, so the same loop can
//     also do work of its own and can be bounded by --timeout. See
//     docs/ownership.md, "Applications without a main loop".
//
//   * There is no gst_object_unref and no gst_message_unref. A Message is a
//     mini object, so its wrapper owns a reference and `using` releases it; the
//     bus is a GObject wrapper, interned and shared with every other lookup, so
//     nothing here disposes it. The pipeline is the one sanctioned Dispose:
//     this code built it, and it goes back to NULL before it is released.
//
//   * The media defaults to the same Sintel trailer the upstream page uses, so
//     a manual run reproduces the tutorial exactly. Passing a local file
//     instead is what makes the sample runnable without a network.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return HelloWorld.Run(args);

internal static class HelloWorld
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// Builds the pipeline, plays it and reports what the bus said.
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
            Console.WriteLine($"uri:         {options.Uri}");

            // gst_parse_launch. A description that holds a single element
            // parses into that element rather than into a pipeline, and playbin
            // is a pipeline, so the cast succeeds — through the GType registry,
            // which is what turns the GstPipeline that comes back into this
            // wrapper.
            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("BasicTutorial01: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                return Play(pipeline, options.Timeout);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial01: {exception}");
            return 1;
        }
        finally
        {
            // The last drain of the release queue. A GObject finalizer may not
            // unref, so it enqueues the release instead and somebody has to
            // pump the queue on a thread that may call native code.
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Plays the pipeline until it ends, fails or runs out of time.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="timeout">How long the pipeline may take.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, TimeSpan timeout)
    {
        try
        {
            pipeline.SetState(State.Playing);

            // The bus is an interned GObject wrapper, shared with every other
            // lookup of the same bus, so it is not disposed here.
            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    // Nothing to report in this slice. Draining here is what
                    // keeps a long run from holding on to wrappers whose
                    // finalizer already ran.
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    // The next tutorial parses the error properly; this one
                    // only says that there was one, as the C original does.
                    (GException error, _) = message.ParseError();
                    Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
                    return 1;
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                return 0;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial01: no end of stream within {timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // Back to NULL before the pipeline is released: one that is still
            // PLAYING when its last reference goes away leaves its streaming
            // threads running.
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the media to play.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets how long the pipeline may take.</summary>
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

            // gst_filename_to_uri, which escapes the path the way GStreamer
            // itself does. A Windows path with a drive letter comes back as
            // file:///C:/... from it.
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
