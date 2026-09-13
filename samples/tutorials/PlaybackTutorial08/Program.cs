// Playback tutorial 8: hardware-accelerated video decoding — how the
// autoplugger chooses a decoder, and how an application changes that choice.
//
// Ported from the `enable_factory` snippet of the upstream page, which is the
// only code that page carries; there is no playback-tutorial-8.c. The upstream
// tutorial code is tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The
// walkthrough that explains what it does is upstream and is not reproduced
// here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/hardware-accelerated-video-decoding.html
//
// Usage: PlaybackTutorial08 [--enable <factory>]... [--disable <factory>]...
//                           [<uri-or-file>] [--headless]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the snippet and from the page, and why:
//
//   * The snippet is a function with nothing around it. EnableFactory below is
//     that function, line for line; the program around it is this port's, and
//     exists so that the effect of the call can be seen: it lists the video
//     decoders the autoplugger would consider, in the order it would consider
//     them, and then — if media was given — says which decoder actually got
//     plugged.
//
//   * The snippet returns silently when the factory does not exist. Here that
//     prints the name and exits 1, because a silent return leaves an
//     unattended run nothing to gate on: a typo in a factory name would look
//     exactly like a success.
//
//   * gst_registry_get_default is the pre-1.0 spelling and is not bound. The
//     call is Registry.Get(), which is what that macro resolves to today.
//
//   * GST_ELEMENT_FACTORY_TYPE_DECODER and GST_ELEMENT_FACTORY_TYPE_MEDIA_VIDEO
//     are plain #defines in gstelementfactory.h (lines 155 and 179 of
//     GStreamer 1.28), not enumeration members, so no .gir carries them and
//     nothing binds them. They are declared below as the two constants those
//     macros expand to, which is the only way to call the bound
//     ElementFactory.ListGetElements with the mask the autoplugger uses.
//
//   * ListGetElements is asked for MARGINAL and above, which is the threshold
//     decodebin itself uses. A factory that --disable has just put at NONE is
//     therefore not in the list at all rather than in it with a rank of zero;
//     the program says so on a line of its own instead of leaving the reader to
//     wonder where the name went.
//
//   * The page also names the GST_PLUGIN_FEATURE_RANK environment variable as
//     the way to do all of this without writing any code —
//     `GST_PLUGIN_FEATURE_RANK=vah264dec:NONE` and the like. That needs no
//     program at all and is what to reach for first.
//
//   * --headless is not part of the tutorial. It gives playbin fakesinks so
//     that the program runs where there is no display and no sound card.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return HardwareDecoding.Run(args);

internal static class HardwareDecoding
{
    /// <summary>
    /// <c>GST_ELEMENT_FACTORY_TYPE_DECODER</c>, gstelementfactory.h line 155.
    /// A plain <c>#define</c>, so no <c>.gir</c> declares it and nothing binds
    /// it; the value is written out here.
    /// </summary>
    private const ulong FactoryTypeDecoder = 1UL << 0;

    /// <summary>
    /// <c>GST_ELEMENT_FACTORY_TYPE_MEDIA_VIDEO</c>, gstelementfactory.h line
    /// 179, for the same reason.
    /// </summary>
    private const ulong FactoryTypeMediaVideo = 1UL << 49;

    /// <summary>
    /// Changes the ranks that were asked for, lists the decoders, and plays
    /// the media if there is any.
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

            foreach ((string name, bool enable) in options.Ranks)
            {
                if (!EnableFactory(name, enable))
                {
                    Console.Error.WriteLine($"PlaybackTutorial08: no factory is called \"{name}\".");
                    return 1;
                }

                Console.WriteLine($"{(enable ? "enabled" : "disabled"),-8}     {name}");
            }

            ListDecoders(options);

            if (options.Uri is null)
            {
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine($"uri:         {options.Uri}");

            if (Global.ParseLaunch($"playbin uri={options.Uri}") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybackTutorial08: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                if (options.Headless && !Silence(pipeline))
                {
                    return 1;
                }

                return Play(pipeline, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"PlaybackTutorial08: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// The <c>enable_factory</c> of the upstream page: raises a factory above
    /// every other decoder, or takes it out of the running altogether.
    /// </summary>
    /// <param name="name">The factory to change, for example <c>vavp9dec</c>.</param>
    /// <param name="enable">
    /// <see langword="true"/> to rank it PRIMARY + 1, <see langword="false"/>
    /// to rank it NONE.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the registry has no such factory, which the
    /// C snippet answers with a silent return.
    /// </returns>
    /// <remarks>
    /// PRIMARY + 1 puts the factory above the ones that commonly carry PRIMARY,
    /// and NONE makes the autoplugging mechanism never select it. Any number
    /// would do; the four names are a convenience.
    /// </remarks>
    private static bool EnableFactory(string name, bool enable)
    {
        Registry registry = Registry.Get();

        if (ElementFactory.Find(name) is not ElementFactory factory)
        {
            return false;
        }

        factory.SetRank(enable ? (uint)Rank.Primary + 1 : (uint)Rank.None);
        registry.AddFeature(factory);
        return true;
    }

    /// <summary>
    /// Prints the video decoders the autoplugger would consider, best first.
    /// </summary>
    /// <param name="options">The command line of the sample.</param>
    private static void ListDecoders(Options options)
    {
        System.Collections.Generic.IReadOnlyList<ElementFactory> factories =
            ElementFactory.ListGetElements(FactoryTypeDecoder | FactoryTypeMediaVideo, Rank.Marginal);

        Console.WriteLine();
        Console.WriteLine("Video decoders the autoplugger would consider:");

        System.Collections.Generic.HashSet<string> listed = new(StringComparer.Ordinal);

        foreach (ElementFactory factory in factories
            .OrderByDescending(factory => factory.GetRank())
            .ThenBy(factory => factory.GetName(), StringComparer.Ordinal))
        {
            string name = factory.GetName() ?? "?";
            _ = listed.Add(name);

            string mark = options.Ranks.Any(rank => string.Equals(rank.Name, name, StringComparison.Ordinal))
                ? " *"
                : string.Empty;

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {factory.GetRank(),5}  {name}  ({factory.GetMetadata("long-name")}){mark}"));
        }

        // A factory that was just ranked NONE is below the MARGINAL threshold
        // this list was asked for, so it is missing rather than present with a
        // zero: say so, or the name simply vanishes.
        foreach ((string name, bool enable) in options.Ranks)
        {
            if (!enable && !listed.Contains(name))
            {
                Console.WriteLine($"  (none)  {name}  — ranked NONE, below the threshold of this list");
            }
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
    private static bool Silence(Pipeline playbin)
    {
        foreach (string property in (string[])["video-sink", "audio-sink"])
        {
            if (ElementFactory.Make("fakesink", null) is not Element sink)
            {
                Console.Error.WriteLine("PlaybackTutorial08: fakesink could not be created.");
                return false;
            }

            Global.UtilSetObjectArg(sink, "sync", "true");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Plays the media and says which decoder was plugged into it.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the stream ended, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "PlaybackTutorial08: Unable to set the pipeline to the playing state.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();
            bool reported = false;

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

                        if (!reported &&
                            message.Src is { } source &&
                            source.Handle == pipeline.Handle)
                        {
                            message.ParseStateChanged(out _, out State current, out _);

                            if (current == State.Playing)
                            {
                                reported = true;
                                ReportDecoders(pipeline);
                            }
                        }

                        continue;
                    }
                }

                GstSharp.DrainPendingReleases();
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial08: nothing ended the run within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Walks the whole pipeline and names every decoder inside it.
    /// </summary>
    /// <param name="pipeline">The pipeline that has reached PLAYING.</param>
    /// <remarks>
    /// IterateRecurse descends into the bins uridecodebin and decodebin build,
    /// which is where the decoder that was actually chosen lives. The iterator
    /// is a boxed type, so it is disposed like any other.
    /// </remarks>
    private static void ReportDecoders(Pipeline pipeline)
    {
        Console.WriteLine();
        Console.WriteLine("Decoders that were plugged:");

        using Iterator elements = pipeline.IterateRecurse();

        foreach (Element element in elements.Items<Element>())
        {
            if (element.GetFactory() is not ElementFactory factory)
            {
                continue;
            }

            if (factory.GetMetadata("klass") is { } klass &&
                klass.Contains("Decoder", StringComparison.Ordinal))
            {
                Console.WriteLine($"  {factory.GetName()}  ({element.GetName()})");
            }
        }
    }

    /// <summary>
    /// One <c>--enable</c> or <c>--disable</c> as it was written.
    /// </summary>
    /// <param name="Name">The factory to change.</param>
    /// <param name="Enable">Whether it is being raised or forbidden.</param>
    private readonly record struct RankChange(string Name, bool Enable);

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        private readonly List<RankChange> _ranks = [];

        /// <summary>Gets the media to play, or <see langword="null"/>.</summary>
        internal string? Uri { get; private set; }

        /// <summary>Gets a value indicating whether the run needs no display.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets how long the run may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(120);

        /// <summary>Gets the rank changes, in the order they were written.</summary>
        internal IReadOnlyList<RankChange> Ranks => _ranks;

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
                    case "--enable":
                        options._ranks.Add(new RankChange(Cli.ValueOf(arguments, ref i), Enable: true));
                        break;

                    case "--disable":
                        options._ranks.Add(new RankChange(Cli.ValueOf(arguments, ref i), Enable: false));
                        break;

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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || options.Uri is not null)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Uri = Cli.ToUri(arguments[i]);
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
