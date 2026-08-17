// Basic tutorial 8: short-cutting the pipeline — an application that generates
// the media itself with appsrc and reads it back out of an appsink, with a tee
// sending the same stream three ways.
//
// Ported from basic-tutorial-8.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/short-cutting-the-pipeline.html
//
// Usage: BasicTutorial08 [--headless] [--chunks <count>]
//                        [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop, and therefore no g_idle_add. The C program starts feeding
//     by adding an idle source to the main loop when appsrc asks for data and
//     removes it again when appsrc has enough. Here need-data and enough-data
//     move a flag, and the loop that already polls the bus pushes while the
//     flag is up. That is the same handshake against the loop the application
//     owns, which is the shape docs/ownership.md argues for.
//
//   * The signal path, not SetSimpleCallbacks. This binding also has
//     AppSrc.SetSimpleCallbacks and AppSink.SetSimpleCallbacks, which cost less
//     per buffer than a signal emission — but they stand for
//     gst_app_src_set_simple_callbacks, which GStreamer added in 1.28, while
//     this binding supports 1.24 and up. The tutorial is what a newcomer runs
//     against whatever GStreamer their distribution ships, and the signals have
//     been there all along. Use the callbacks when the floor is 1.28 and the
//     buffer rate is high; the wiring below is otherwise identical.
//
//   * gst_app_src_push_buffer takes the buffer over, and so does PushBuffer
//     here: after the call the wrapper owns nothing, which is what its disposed
//     state means. The `using` around it stays correct because Dispose is
//     idempotent. See docs/ownership.md, "Calls that consume their argument".
//
//   * gst_audio_info_set_format is not bound (girs/skip-report.md), so the caps
//     are written out with Caps.FromString. Every field that
//     gst_audio_info_to_caps would have produced has to be there — layout
//     included, or audioconvert has nothing to negotiate against.
//
//   * The new-sample handler runs on a streaming thread of the sink and the
//     need-data handler on one of the source, exactly as in C. Both do the
//     least they can, and an exception that leaves either of them is caught by
//     the binding and reported through Gst.Interop.ExceptionTrap.
//
//   * wavescope lives in gst-plugins-bad. Where it is not installed the
//     visualization branch of the tee is left out and the program says so:
//     an element is a plugin that may or may not be there, and a program that
//     discovers that at run time is more useful than one that dies.
//
//   * --headless and --chunks are not part of the tutorial. --headless swaps
//     the auto sinks for fakesinks that do not wait for the clock, and --chunks
//     bounds the run: the C program plays until it is interrupted, and this one
//     pushes a fixed number of buffers and then ends the stream.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;

return AppIntegration.Run(args);

internal static class AppIntegration
{
    /// <summary>How many bytes travel in each buffer.</summary>
    private const int ChunkSize = 1024;

    /// <summary>How many samples per second the source claims to produce.</summary>
    private const int SampleRate = 44100;

    /// <summary>The format the source produces and the sink expects.</summary>
    /// <remarks>
    /// This is what gst_audio_info_to_caps builds in the C program. The layout
    /// field is not decoration: raw audio caps without it are incomplete and
    /// audioconvert refuses them.
    /// </remarks>
    private const string AudioCaps =
        "audio/x-raw,format=S16LE,rate=44100,channels=1,layout=interleaved";

    /// <summary>
    /// Builds the three-way pipeline, feeds it and reads one branch back.
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

            // GstApp.Initialize rather than GstSharp.Initialize: it is a call
            // into GstSharp.Net.App, and only that runs the module initialiser
            // which puts GstAppSrc and GstAppSink into the type registry.
            // Without it the two casts below are silently null.
            GstApp.Initialize(options.Native);
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chunks:      {options.Chunks}"));

            if (ElementFactory.Make("appsrc", "audio_source") is not AppSrc source ||
                ElementFactory.Make("appsink", "app_sink") is not AppSink appSink)
            {
                Console.Error.WriteLine("BasicTutorial08: the application elements could not be created.");
                return 1;
            }

            Element? tee = ElementFactory.Make("tee", "tee");
            Element? audioQueue = ElementFactory.Make("queue", "audio_queue");
            Element? audioConvert = ElementFactory.Make("audioconvert", "audio_convert1");
            Element? audioResample = ElementFactory.Make("audioresample", "audio_resample");
            Element? audioSink = ElementFactory.Make(options.AudioSink, "audio_sink");
            Element? appQueue = ElementFactory.Make("queue", "app_queue");
            Pipeline? pipeline = Pipeline.New("test-pipeline");

            if (tee is null || audioQueue is null || audioConvert is null ||
                audioResample is null || audioSink is null || appQueue is null || pipeline is null)
            {
                Console.Error.WriteLine("BasicTutorial08: not all elements could be created.");
                return 1;
            }

            using (pipeline)
            {
                using (Caps? caps = Caps.FromString(AudioCaps))
                {
                    if (caps is null)
                    {
                        Console.Error.WriteLine("BasicTutorial08: the audio caps could not be parsed.");
                        return 1;
                    }

                    // Both calls copy the caps, so the one wrapper here is
                    // enough and it is released when this scope ends.
                    source.SetCaps(caps);
                    appSink.SetCaps(caps);
                }

                // Timestamps are in time rather than in bytes, which is what
                // makes the buffers below say when they are to be played.
                Global.UtilSetObjectArg(source, "format", "time");

                // The signals of an appsink are off by default, because
                // emitting one costs something on every single buffer.
                appSink.EmitSignals = true;

                if (options.Headless)
                {
                    Global.UtilSetObjectArg(audioSink, "sync", "false");
                }

                pipeline.AddMany(source, tee, audioQueue, audioConvert, audioResample, audioSink, appQueue, appSink);

                if (!source.Link(tee) ||
                    !audioQueue.Link(audioConvert, audioResample, audioSink) ||
                    !appQueue.Link(appSink))
                {
                    Console.Error.WriteLine("BasicTutorial08: the elements could not be linked.");
                    return 1;
                }

                List<Pad> requested = [];

                try
                {
                    if (!RequestBranch(tee, audioQueue, "audio", requested) ||
                        !RequestBranch(tee, appQueue, "app", requested) ||
                        !AddVisualization(pipeline, tee, options, requested))
                    {
                        return 1;
                    }

                    return Play(pipeline, source, appSink, options);
                }
                finally
                {
                    // gst_element_release_request_pad. The pads themselves are
                    // interned GObject wrappers, so the unref that follows it
                    // in C has no counterpart here.
                    foreach (Pad pad in requested)
                    {
                        tee.ReleaseRequestPad(pad);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial08: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Adds the visualization branch of the tee, when the plugin that draws it
    /// is installed.
    /// </summary>
    /// <param name="pipeline">The pipeline the branch is added to.</param>
    /// <param name="tee">The tee the branch hangs off.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <param name="requested">The request pads to release afterwards.</param>
    /// <returns><see langword="false"/> when the branch is wanted but could not be built.</returns>
    private static bool AddVisualization(Pipeline pipeline, Element tee, Options options, List<Pad> requested)
    {
        // wavescope is in gst-plugins-bad, which plenty of installations do not
        // have. That is not an error: it is one branch of three.
        if (ElementFactory.Make("wavescope", "visual") is not Element visual)
        {
            Console.WriteLine("visual:      wavescope is not installed, running without the video branch.");
            return true;
        }

        Element? queue = ElementFactory.Make("queue", "video_queue");
        Element? convert = ElementFactory.Make("audioconvert", "audio_convert2");
        Element? videoConvert = ElementFactory.Make("videoconvert", "video_convert");
        Element? videoSink = ElementFactory.Make(options.VideoSink, "video_sink");

        if (queue is null || convert is null || videoConvert is null || videoSink is null)
        {
            Console.Error.WriteLine("BasicTutorial08: the video branch could not be created.");
            return false;
        }

        Global.UtilSetObjectArg(visual, "shader", "none");
        Global.UtilSetObjectArg(visual, "style", "dots");

        if (options.Headless)
        {
            Global.UtilSetObjectArg(videoSink, "sync", "false");
        }

        pipeline.AddMany(queue, convert, visual, videoConvert, videoSink);

        if (!queue.Link(convert, visual, videoConvert, videoSink))
        {
            Console.Error.WriteLine("BasicTutorial08: the video branch could not be linked.");
            return false;
        }

        return RequestBranch(tee, queue, "video", requested);
    }

    /// <summary>
    /// Asks the tee for a source pad and links it to the head of a branch.
    /// </summary>
    /// <param name="tee">The tee to ask.</param>
    /// <param name="head">The element the branch starts with.</param>
    /// <param name="label">What the branch is called, for the log line.</param>
    /// <param name="requested">The request pads to release afterwards.</param>
    /// <returns><see langword="true"/> when the branch is linked.</returns>
    private static bool RequestBranch(Element tee, Element head, string label, List<Pad> requested)
    {
        // A tee has no source pad until one is asked for: "src_%u" is the name
        // of the template, and the pad that comes back is called src_0, src_1
        // and so on.
        Pad? teePad = tee.RequestPadSimple("src_%u");
        Pad? queuePad = head.GetStaticPad("sink");

        if (teePad is null || queuePad is null)
        {
            Console.Error.WriteLine($"BasicTutorial08: the {label} branch has no pad to link.");
            return false;
        }

        requested.Add(teePad);
        Console.WriteLine($"tee:         got request pad {teePad.Name} for the {label} branch.");

        PadLinkReturn result = teePad.Link(queuePad);

        if (result == PadLinkReturn.Ok)
        {
            return true;
        }

        Console.Error.WriteLine($"BasicTutorial08: the {label} branch could not be linked ({result}).");
        return false;
    }

    /// <summary>
    /// Runs the pipeline: pushes while the source is hungry, reads the bus in
    /// between, and ends the stream once every chunk has been handed over.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="source">The source to feed.</param>
    /// <param name="appSink">The sink to read back.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, AppSrc source, AppSink appSink, Options options)
    {
        Waveform waveform = new();
        Demand demand = new();
        Counter received = new();
        int pushed = 0;
        bool ended = false;

        // start_feed and stop_feed of the C program. There they add and remove
        // an idle source; here they raise and lower a flag that the loop below
        // reads. Both handlers run on a streaming thread of the source.
        EventHandler<AppSrc.NeedDataSignalArgs> onNeedData = (sender, args) => demand.Hungry = true;
        EventHandler onEnoughData = (sender, args) => demand.Hungry = false;

        AppSink.NewSampleHandler onNewSample = (sender, args) =>
        {
            // The signal says a sample is ready, so this does not block. The
            // only thing the tutorial does with it is count it and print a
            // star, exactly as the C program does.
            using Sample? sample = appSink.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            received.Add();
            Console.Write('*');
            return FlowReturn.Ok;
        };

        source.NeedData += onNeedData;
        source.EnoughData += onEnoughData;
        appSink.NewSample += onNewSample;

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("BasicTutorial08: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                // The idle handler of the C program, inlined into the loop the
                // application owns. It stops as soon as the source says it has
                // enough, and hands the thread back to the bus either way.
                while (demand.Hungry && pushed < options.Chunks)
                {
                    FlowReturn flow = Push(source, waveform);

                    if (flow != FlowReturn.Ok)
                    {
                        Console.Error.WriteLine($"BasicTutorial08: the source answered {flow}.");
                        return 1;
                    }

                    pushed++;
                }

                if (pushed >= options.Chunks && !ended)
                {
                    // Nothing else will come, and this is what turns that into
                    // the end-of-stream message the loop is waiting for.
                    source.EndOfStream();
                    ended = true;
                }

                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(50),
                    MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                Console.WriteLine();

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine(
                        $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                    return 1;
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"pushed:      {pushed}"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"received:    {received.Value}"));

                // The appsink is one branch of the tee, so it sees every buffer
                // the source pushed. Anything else means the branch dropped
                // something.
                return received.Value == pushed ? 0 : 1;
            }

            Console.WriteLine();
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"BasicTutorial08: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // Back to NULL first: a handler must not be disconnected while the
            // streaming thread is still inside it, and that thread is gone once
            // the pipeline is stopped.
            pipeline.SetState(State.Null);
            appSink.NewSample -= onNewSample;
            source.EnoughData -= onEnoughData;
            source.NeedData -= onNeedData;
        }
    }

    /// <summary>
    /// Fills one buffer with the next slice of the waveform and hands it to the
    /// source.
    /// </summary>
    /// <param name="source">The source to push into.</param>
    /// <param name="waveform">The generator of the samples.</param>
    /// <returns>What the source answered.</returns>
    private static FlowReturn Push(AppSrc source, Waveform waveform)
    {
        using Gst.Buffer? buffer = Gst.Buffer.NewAllocate(null, ChunkSize, null);

        if (buffer is null)
        {
            return FlowReturn.Error;
        }

        // gst_util_uint64_scale in the C program; plain arithmetic is exact
        // here because the numbers are far from overflowing.
        ulong samples = (ulong)(ChunkSize / sizeof(short));

        buffer.SetPts(ClockTime.FromNanoseconds(
            waveform.Samples * ClockTime.NanosecondsPerSecond / SampleRate));
        buffer.SetDuration(ClockTime.FromNanoseconds(
            samples * ClockTime.NanosecondsPerSecond / SampleRate));

        // The span points into the memory of the buffer for as long as the
        // scope lives and not one byte longer: MapScope is a ref struct, so it
        // cannot escape, and disposing it unmaps the memory again.
        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            waveform.Fill(MemoryMarshal.Cast<byte, short>(map.Span));
        }

        // PushBuffer consumes the buffer: after this the wrapper owns nothing,
        // and the `using` above is what makes an early return safe rather than
        // what releases it here.
        return source.PushBuffer(buffer);
    }

    /// <summary>
    /// Reports a failure that was caught on a callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    private static void OnCallbackFailure(Exception exception) =>
        Console.Error.WriteLine($"BasicTutorial08: a handler failed: {exception}");

    /// <summary>
    /// Whether the source is asking for data. Written on a streaming thread and
    /// read on the thread that pushes, which is what makes it volatile.
    /// </summary>
    private sealed class Demand
    {
        private volatile bool _hungry;

        /// <summary>Gets or sets a value indicating whether the source wants more.</summary>
        internal bool Hungry
        {
            get => _hungry;
            set => _hungry = value;
        }
    }

    /// <summary>
    /// How many samples the sink saw. Incremented on the streaming thread of
    /// the sink and read on the thread that started the run.
    /// </summary>
    private sealed class Counter
    {
        private int _value;

        /// <summary>Gets how many samples arrived.</summary>
        internal int Value => Volatile.Read(ref _value);

        /// <summary>Counts one sample.</summary>
        internal void Add() => Interlocked.Increment(ref _value);
    }

    /// <summary>
    /// The waveform generator of the C program, translated statement by
    /// statement. It is two coupled oscillators: one sweeps the frequency of
    /// the other.
    /// </summary>
    private sealed class Waveform
    {
        private float _a;
        private float _b = 1;
        private float _c;
        private float _d = 1;

        /// <summary>Gets how many samples have been generated so far.</summary>
        internal ulong Samples { get; private set; }

        /// <summary>
        /// Writes the next samples.
        /// </summary>
        /// <param name="destination">The samples to fill.</param>
        internal void Fill(Span<short> destination)
        {
            _c += _d;
            _d -= _c / 1000;

            float frequency = 1100 + (1000 * _d);

            for (int i = 0; i < destination.Length; i++)
            {
                _a += _b;
                _b -= _a / frequency;
                destination[i] = (short)(500 * _a);
            }

            Samples += (ulong)destination.Length;
        }
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets a value indicating whether the run needs no display and no sound card.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the factory of the audio sink to build.</summary>
        internal string AudioSink => Headless ? "fakesink" : "autoaudiosink";

        /// <summary>Gets the factory of the video sink to build.</summary>
        internal string VideoSink => Headless ? "fakesink" : "autovideosink";

        /// <summary>Gets how many buffers are pushed before the stream ends.</summary>
        internal int Chunks { get; private set; } = 400;

        /// <summary>Gets how long the run may take.</summary>
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
                    case "--headless":
                        options.Headless = true;
                        break;

                    case "--chunks":
                        options.Chunks = int.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture);
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
                        throw new ArgumentException(
                            $"\"{arguments[i]}\" is not a known argument.",
                            nameof(arguments));
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
