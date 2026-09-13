// Playback tutorial 6: audio visualization — finding the visualization
// elements the registry has, and telling playbin to draw with one of them.
//
// Ported from playback-tutorial-6.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/audio-visualization.html
//
// Usage: PlaybackTutorial06 [<uri-or-file>] [--headless]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * gst_element_factory_get_klass and gst_element_factory_get_longname are
//     function macros over gst_element_factory_get_metadata, declared
//     introspectable="0" in the .gir and therefore not bound. The two calls are
//     spelled GetMetadata("klass") and GetMetadata("long-name"), which is what
//     those macros expand to.
//
//   * GstPlayFlags is a GFlags type that the playback plugin registers at run
//     time, so no .gir declares it and this binding has no managed type for it.
//     The visualization flag is therefore written as the plain uint bit it is.
//
//   * gst_plugin_feature_list_free has no counterpart here. FeatureFilter hands
//     back an IReadOnlyList of wrappers that already own their references; the
//     spine of the GList is freed inside the call, and the references go back
//     with the wrappers.
//
//   * The visualization element is never disposed, and the C never unrefs it
//     either. Only the pipeline is the application's to release; playbin takes
//     a reference of its own when vis-plugin is written.
//
//   * The default URI is the upstream one, http://radio.hbr1.com:19800/ambient.ogg.
//     That radio station has not answered for years, so a run with no argument
//     is expected to fail to connect; the tutorial is kept honest by leaving
//     its own media in place and letting a local file be given instead.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card. The
//     visualization chain is still built and still runs into the fakesink,
//     because playsink only draws a visualization when the media has no video
//     stream of its own — so give this an audio-only file if the point is to
//     see the chain being used at all.
//
//   * The C program blocks on gst_bus_timed_pop_filtered with
//     GST_CLOCK_TIME_NONE, which never returns for a radio stream. Here the
//     same call is made in 50 ms slices inside a loop bounded by --timeout, and
//     the bound is a success rather than a failure when the source was never
//     going to end: a live pipeline, or an http URI, is deliberately endless.
//     A local file that did not reach EOS in time is a failure.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return AudioVisualization.Run(args);

internal static class AudioVisualization
{
    /// <summary>The media of the upstream tutorial.</summary>
    private const string DefaultUri = "http://radio.hbr1.com:19800/ambient.ogg";

    /// <summary>
    /// The one bit of <c>GstPlayFlags</c> this tutorial names. The type is a
    /// GFlags the playback plugin registers when it is loaded, so it appears in
    /// no <c>.gir</c> file and this binding declares no managed type for it;
    /// the property is read and written as the <see cref="uint"/> it holds.
    /// </summary>
    private const uint PlayFlagVis = 0x8;

    /// <summary>
    /// Plays a stream and draws it.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the run ended as it was meant to, 1 on any error.</returns>
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

            if (SelectFactory() is not ElementFactory selected)
            {
                Console.WriteLine("No visualization plugins found!");
                return 1;
            }

            Console.WriteLine($"Selected '{selected.GetMetadata("long-name")}'");

            // The element is not disposed. A wrapper that is not the pipeline
            // is never the application's to release — playbin takes its own
            // reference when vis-plugin is written, and the C original does not
            // unref it either. See docs/ownership.md.
            if (selected.Create(null) is not Element visualization)
            {
                Console.Error.WriteLine("PlaybackTutorial06: the visualization element could not be created.");
                return 1;
            }

            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybackTutorial06: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                // Set the visualization flag. The read-modify-write is the
                // C original's: playbin's default already has other bits.
                uint flags = pipeline.GetProperty<uint>("flags");
                flags |= PlayFlagVis;
                pipeline.SetProperty("flags", flags);

                // vis-plugin is object-valued, so it goes in through a
                // GValue: a plain string setter cannot carry an element.
                using (Gst.GObject.Value value = Gst.GObject.Value.New(visualization.NativeType))
                {
                    value.SetObject(visualization);
                    pipeline.SetProperty("vis-plugin", value);
                }

                if (options.Headless && !Silence(pipeline))
                {
                    return 1;
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial06: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Lists the visualization elements the registry knows and picks one.
    /// </summary>
    /// <returns>The factory to draw with, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The filter is the C original's <c>filter_vis_features</c>: a feature
    /// that is an element factory and whose <c>klass</c> mentions
    /// Visualization. <c>GST_ELEMENT_FACTORY_KLASS_VISUALIZATION</c> is a plain
    /// <c>#define</c> that no <c>.gir</c> carries, so the string it expands to
    /// is written out.
    /// </remarks>
    private static ElementFactory? SelectFactory()
    {
        System.Collections.Generic.IReadOnlyList<PluginFeature> features =
            Registry.Get().FeatureFilter(IsVisualization, first: false);

        ElementFactory? selected = null;

        Console.WriteLine("Available visualization plugins:");

        foreach (PluginFeature feature in features)
        {
            if (feature is not ElementFactory factory)
            {
                continue;
            }

            string name = factory.GetMetadata("long-name") ?? factory.GetName() ?? "?";
            Console.WriteLine($"  {name}");

            if (selected is null || name.StartsWith("GOOM", StringComparison.Ordinal))
            {
                selected = factory;
            }
        }

        return selected;

        static bool IsVisualization(PluginFeature feature) =>
            feature is ElementFactory factory &&
            factory.GetMetadata("klass") is { } klass &&
            klass.Contains("Visualization", StringComparison.Ordinal);
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
                Console.Error.WriteLine("PlaybackTutorial06: fakesink could not be created.");
                return false;
            }

            // A visualization is drawn against the clock the audio runs on, so
            // the sinks have to keep time.
            Global.UtilSetObjectArg(sink, "sync", "true");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the pipeline until it ends, fails, or runs out of time.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as it was meant to, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            StateChangeReturn started = pipeline.SetState(State.Playing);

            if (started == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial06: Unable to set the pipeline to the playing state.");
                return 1;
            }

            bool endless = started == StateChangeReturn.NoPreroll ||
                options.Uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                options.Uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine(
                        $"Error received from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"Debugging information: {debug ?? "none"}");
                    return 1;
                }

                Console.WriteLine("End-Of-Stream reached.");
                return 0;
            }

            // A radio stream has no end, so running out of time is how a
            // bounded run of the upstream program is meant to finish. A file
            // that did not reach its own end in the time given has failed.
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"bound:       {options.Timeout.TotalSeconds:F0} s elapsed."));

            return endless ? 0 : 1;
        }
        finally
        {
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

        /// <summary>Gets a value indicating whether the run needs no display.</summary>
        internal bool Headless { get; private set; }

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
