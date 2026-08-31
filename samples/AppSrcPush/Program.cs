// The appsrc sample: an application that is the source of the media, pushing
// buffers into the pipeline in push mode -- that is, only while appsrc says it
// wants them.
//
// Usage: AppSrcPush [--buffers <count>] [--output <file>]
//                   [--native-path <directory>] [--flavor msvc|mingw]
//                   [--timeout <seconds>]
//
// Push mode is the handshake this sample exists for. An appsrc that is left to
// itself will take every buffer it is given and queue them, which turns the
// application into the thing that decides how much memory the pipeline uses;
// the "need-data" and "enough-data" signals are how it hands that decision
// back. They are emitted on a streaming thread of the source when its queue
// falls under and rises over the max-bytes it was configured with, and the
// application is expected to push between the one and the other and to stop in
// between. This program does exactly that and counts both signals, so a run
// prints how often the pipeline asked and how often it said enough.
//
// What this is not: appsrc also has a pull mode, where the source emits
// "need-data" for every single buffer and expects one back, and a set of simple
// callbacks (gst_app_src_set_simple_callbacks, GStreamer 1.28) that cost less
// than a signal emission per buffer. The signals are used here because they are
// what every supported GStreamer has -- the floor of this binding is 1.24 --
// and because they are what the upstream documentation of appsrc describes.
// BasicTutorial08 is the same handshake inside a tutorial, with a tee and an
// appsink on the other end; this sample is the source half on its own, with the
// bytes going somewhere they can be counted.
//
// The pipeline is appsrc ! audioconvert ! fakesink, or filesink when --output
// names a file, and what comes out there is the raw signed 16 bit samples that
// were pushed in, in the order they were pushed: the run is a gate on the byte
// count as much as on the exit code, which is why it prints both.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;

return Push.Run(args);

internal static class Push
{
    /// <summary>How many samples travel in each buffer.</summary>
    private const int SamplesPerBuffer = 1024;

    /// <summary>How many bytes that is, which is what the sink counts.</summary>
    private const int BytesPerBuffer = SamplesPerBuffer * sizeof(short);

    /// <summary>How many samples per second the source claims to produce.</summary>
    private const int SampleRate = 44100;

    /// <summary>The pitch of the sine, in hertz.</summary>
    private const double Frequency = 440;

    /// <summary>The peak of the sine, well under the full scale of the format.</summary>
    private const double Amplitude = 12000;

    /// <summary>
    /// The format the source produces. The layout field is not decoration: raw
    /// audio caps without it are incomplete and audioconvert refuses them.
    /// </summary>
    private const string AudioCaps =
        "audio/x-raw,format=S16LE,rate=44100,channels=1,layout=interleaved";

    /// <summary>
    /// Builds the pipeline, feeds it while it is hungry and reports what came
    /// out of the other end.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when every buffer was pushed and the stream ended, 1 otherwise.</returns>
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
            // which puts GstAppSrc into the type registry. Without it the cast
            // below is silently null.
            GstApp.Initialize(options.Native);

            // A handler that runs on a streaming thread must not let an
            // exception unwind into native code, so the binding catches and
            // reports it here instead.
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"sink:        {options.Sink}");
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"buffers:     {options.Buffers} of {BytesPerBuffer} bytes"));

            if (ElementFactory.Make("appsrc", "source") is not AppSrc source)
            {
                Console.Error.WriteLine("AppSrcPush: the appsrc could not be created.");
                return 1;
            }

            Element? convert = ElementFactory.Make("audioconvert", "convert");
            Element? sink = ElementFactory.Make(options.Sink, "sink");
            Pipeline? pipeline = Pipeline.New("push-pipeline");

            if (convert is null || sink is null || pipeline is null)
            {
                Console.Error.WriteLine("AppSrcPush: not all elements could be created.");
                return 1;
            }

            using (pipeline)
            {
                using (Caps? caps = Caps.FromString(AudioCaps))
                {
                    if (caps is null)
                    {
                        Console.Error.WriteLine("AppSrcPush: the audio caps could not be parsed.");
                        return 1;
                    }

                    // The call copies the caps, so the one wrapper here is
                    // enough and it is released when this scope ends.
                    source.SetCaps(caps);
                }

                // Timestamps are in time rather than in bytes, which is what
                // makes the buffers below say when they are to be played.
                Global.UtilSetObjectArg(source, "format", "time");

                // Not a live source: there is no clock forcing the rate, so the
                // pipeline runs as fast as the sink will take it and the only
                // brake is the handshake this sample is about.
                Global.UtilSetObjectArg(source, "is-live", "false");

                // The queue that decides when the two signals are emitted. The
                // default is 200000 bytes; a smaller one means the handshake
                // happens several times in even a short run, which is what
                // makes the counters below say something.
                Global.UtilSetObjectArg(source, "max-bytes", "65536");

                if (options.Output is null)
                {
                    // fakesink counts and drops. Not waiting for the clock is
                    // what keeps an unattended run short.
                    Global.UtilSetObjectArg(sink, "sync", "false");
                }
                else
                {
                    Global.UtilSetObjectArg(sink, "location", options.Output);
                }

                pipeline.AddMany(source, convert, sink);

                if (!source.Link(convert, sink))
                {
                    Console.Error.WriteLine("AppSrcPush: the elements could not be linked.");
                    return 1;
                }

                (int status, int pushed) = Play(pipeline, source, options);

                // Verify is called from here rather than from inside Play,
                // because what it weighs is a file that filesink only closes
                // when the pipeline leaves PLAYING — and that happens in
                // Play's finally, after its last statement has run.
                return status == 0 ? Verify(pushed, options) : status;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AppSrcPush: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Runs the pipeline: pushes while the source is hungry, reads the bus in
    /// between, and ends the stream once every buffer has been handed over.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="source">The source to feed.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>
    /// 0 when the stream ended and 1 on any failure, and how many buffers were
    /// handed over either way.
    /// </returns>
    private static (int Status, int Pushed) Play(Pipeline pipeline, AppSrc source, Options options)
    {
        Sine sine = new();
        Demand demand = new();
        int pushed = 0;
        bool ended = false;

        // Both handlers run on a streaming thread of the source, and both do
        // the least they can: they move a flag the loop below reads.
        EventHandler<AppSrc.NeedDataSignalArgs> onNeedData = (sender, args) => demand.Want();
        EventHandler onEnoughData = (sender, args) => demand.Stop();

        source.NeedData += onNeedData;
        source.EnoughData += onEnoughData;

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("AppSrcPush: the pipeline refused to go to PLAYING.");
                return (1, pushed);
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                // Push while the source is asking and there is anything left to
                // push. Stopping the moment it says enough is the whole point:
                // the queue of the source stays bounded, and the memory of the
                // run is the memory of one buffer at a time.
                while (demand.Hungry && pushed < options.Buffers)
                {
                    FlowReturn flow = PushOne(source, sine);

                    if (flow != FlowReturn.Ok)
                    {
                        Console.Error.WriteLine($"AppSrcPush: the source answered {flow}.");
                        return (1, pushed);
                    }

                    pushed++;
                }

                if (pushed >= options.Buffers && !ended)
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

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine(
                        $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                    return (1, pushed);
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"pushed:      {pushed} buffers, {(long)pushed * BytesPerBuffer} bytes"));
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"handshake:   need-data {demand.NeedCount} times, enough-data {demand.EnoughCount} times"));

                return (0, pushed);
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"AppSrcPush: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
            return (1, pushed);
        }
        finally
        {
            // Back to NULL first: a handler must not be disconnected while the
            // streaming thread is still inside it, and that thread is gone once
            // the pipeline is stopped.
            pipeline.SetState(State.Null);
            source.EnoughData -= onEnoughData;
            source.NeedData -= onNeedData;
        }
    }

    /// <summary>
    /// Checks that as much came out of the pipeline as went into it.
    /// </summary>
    /// <param name="pushed">How many buffers were handed over.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run is sound, 1 otherwise.</returns>
    /// <remarks>
    /// Without <c>--output</c> there is nothing to weigh but the count. With
    /// it there is: the samples are pushed as they are and audioconvert has
    /// nothing to convert them to, so the file is exactly the bytes that were
    /// written into the buffers. It is only that once the pipeline is back in
    /// NULL, which is why this runs after <see cref="Play"/> has returned and
    /// not from inside it.
    /// </remarks>
    private static int Verify(int pushed, Options options)
    {
        if (pushed != options.Buffers)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"AppSrcPush: the stream ended after {pushed} of {options.Buffers} buffers."));
            return 1;
        }

        if (options.Output is null)
        {
            return 0;
        }

        long expected = (long)pushed * BytesPerBuffer;
        long actual = new FileInfo(options.Output).Length;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"output:      {options.Output}, {actual} bytes"));

        if (actual == expected)
        {
            return 0;
        }

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"AppSrcPush: the file holds {actual} bytes and {expected} were pushed."));
        return 1;
    }

    /// <summary>
    /// Fills one buffer with the next slice of the sine and hands it to the
    /// source.
    /// </summary>
    /// <param name="source">The source to push into.</param>
    /// <param name="sine">The generator of the samples.</param>
    /// <returns>What the source answered.</returns>
    private static FlowReturn PushOne(AppSrc source, Sine sine)
    {
        using Gst.Buffer? buffer = Gst.Buffer.NewAllocate(null, BytesPerBuffer, null);

        if (buffer is null)
        {
            return FlowReturn.Error;
        }

        buffer.SetPts(ClockTime.FromNanoseconds(
            sine.Samples * ClockTime.NanosecondsPerSecond / SampleRate));
        buffer.SetDuration(ClockTime.FromNanoseconds(
            (ulong)SamplesPerBuffer * ClockTime.NanosecondsPerSecond / SampleRate));

        // The span points into the memory of the buffer for as long as the
        // scope lives and not one byte longer: MapScope is a ref struct, so it
        // cannot escape, and disposing it unmaps the memory again.
        using (Gst.Buffer.MapScope map = buffer.Map(MapFlags.Write))
        {
            sine.Fill(MemoryMarshal.Cast<byte, short>(map.Span));
        }

        // PushBuffer consumes the buffer: after this the wrapper owns nothing,
        // and the `using` above is what makes an early return safe rather than
        // what releases it here. See docs/ownership.md, "Calls that consume
        // their argument".
        return source.PushBuffer(buffer);
    }

    /// <summary>
    /// Reports a failure that was caught on a callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    private static void OnCallbackFailure(Exception exception) =>
        Console.Error.WriteLine($"AppSrcPush: a handler failed: {exception}");

    /// <summary>
    /// Whether the source is asking for data, and how often it changed its
    /// mind. Written on a streaming thread and read on the thread that pushes.
    /// </summary>
    private sealed class Demand
    {
        private volatile bool _hungry;
        private int _needCount;
        private int _enoughCount;

        /// <summary>Gets a value indicating whether the source wants more.</summary>
        internal bool Hungry => _hungry;

        /// <summary>Gets how often <c>need-data</c> was emitted.</summary>
        internal int NeedCount => Volatile.Read(ref _needCount);

        /// <summary>Gets how often <c>enough-data</c> was emitted.</summary>
        internal int EnoughCount => Volatile.Read(ref _enoughCount);

        /// <summary>Records that the source asked for data.</summary>
        internal void Want()
        {
            Interlocked.Increment(ref _needCount);
            _hungry = true;
        }

        /// <summary>Records that the source has enough.</summary>
        internal void Stop()
        {
            Interlocked.Increment(ref _enoughCount);
            _hungry = false;
        }
    }

    /// <summary>
    /// A sine of a fixed pitch, generated sample by sample so that the buffers
    /// join without a step at the seam.
    /// </summary>
    private sealed class Sine
    {
        /// <summary>Gets how many samples have been generated so far.</summary>
        internal ulong Samples { get; private set; }

        /// <summary>
        /// Writes the next samples.
        /// </summary>
        /// <param name="destination">The samples to fill.</param>
        internal void Fill(Span<short> destination)
        {
            for (int i = 0; i < destination.Length; i++)
            {
                double phase = 2 * Math.PI * Frequency * (Samples + (ulong)i) / SampleRate;
                destination[i] = (short)(Amplitude * Math.Sin(phase));
            }

            Samples += (ulong)destination.Length;
        }
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets how many buffers are pushed before the stream ends.</summary>
        internal int Buffers { get; private set; } = 200;

        /// <summary>Gets the file the samples are written to, when there is one.</summary>
        internal string? Output { get; private set; }

        /// <summary>Gets the factory of the sink to build.</summary>
        internal string Sink => Output is null ? "fakesink" : "filesink";

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
                    case "--buffers":
                        options.Buffers = int.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture);
                        break;

                    case "--output":
                        options.Output = Cli.ValueOf(arguments, ref i);
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

            if (options.Buffers <= 0)
            {
                throw new ArgumentException("--buffers needs a positive count.", nameof(arguments));
            }

            return options;
        }
    }
}

/// <summary>
/// The command line handling this sample needs. Every sample in this directory
/// is a self-contained program, so this is repeated per project rather than
/// shared.
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
