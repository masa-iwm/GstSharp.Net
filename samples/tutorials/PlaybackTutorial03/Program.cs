// Playback tutorial 3: short-cutting the pipeline — the same generated
// waveform as basic tutorial 8, but pushed into the appsrc that playbin builds
// for itself when it is given the URI appsrc://.
//
// Ported from playback-tutorial-3.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/playback/short-cutting-the-pipeline.html
//
// Usage: PlaybackTutorial03 [--headless] [--chunks <count>]
//                           [--native-path <directory>] [--flavor msvc|mingw]
//                           [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * No GMainLoop, and therefore no g_idle_add. The C program starts feeding
//     by adding an idle source to the main loop when appsrc asks for data and
//     removes it again when appsrc has enough. No g_idle_add is bound, and
//     these ports run no main loop for a GLibSynchronizationContext to post
//     onto, so need-data and enough-data move a flag, and the loop that polls
//     the bus pushes while the flag is up. That is the same handshake against
//     the application's own loop, which is the shape docs/ownership.md argues
//     for, and it is what BasicTutorial08 does with the same waveform.
//
//   * source-setup is a signal of playbin, an element of the playback plugin
//     that no .gir describes, so there is no generated event for it. It is
//     connected by name with Object.ConnectSignal, which is the machinery a
//     generated event is built on; the element it carries arrives as the
//     wrapper its GType is registered for, which is Gst.App.AppSrc once
//     GstApp.Initialize has run. Initialising that module before connecting is
//     therefore not optional — see the remarks on DynamicSignalHandler.
//
//   * The C emits the push-buffer action signal. AppSrc.PushBuffer is the same
//     call without the emission, and it consumes the buffer: after it the
//     wrapper owns nothing, which is what its disposed state means. The
//     `using` around it stays correct because Dispose is idempotent. See
//     docs/ownership.md, "Calls that consume their argument".
//
//   * The appsrc is never disposed. It belongs to playbin, which built it and
//     will release it; the C original does not unref it either. Only the
//     pipeline is the application's.
//
//   * --headless and --chunks are not part of the tutorial. --headless gives
//     playbin fakesinks, so that the run needs no sound card, and --chunks
//     bounds it: the C program plays until it is interrupted, and this one
//     pushes a fixed number of buffers, ends the stream and waits for the EOS
//     that comes back.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Gst;
using Gst.App;
using Gst.Audio;
using Gst.GLib;
using Gst.Interop;

return ShortCutting.Run(args);

internal static class ShortCutting
{
    /// <summary>How many bytes travel in each buffer.</summary>
    private const int ChunkSize = 1024;

    /// <summary>How many samples per second the source claims to produce.</summary>
    private const int SampleRate = 44100;

    /// <summary>
    /// Feeds a playbin that has no URI to read, only an application to ask.
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

            // Both modules have to be initialised before the signal below is
            // connected: GstApp puts GstAppSrc into the type registry, which is
            // what makes the element the signal carries arrive as an AppSrc
            // rather than a plain Element, and GstAudio is where AudioInfo
            // lives. Each call initialises the binding as a whole, so the
            // loader options are given once.
            GstAudio.Initialize(options.Native);
            GstApp.Initialize();
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chunks:      {options.Chunks}"));

            // appsrc:// is a URI appsrc itself handles, so playbin builds one
            // and hands it over instead of reading anything.
            if (Global.ParseLaunch("playbin uri=appsrc://") is not Pipeline pipeline)
            {
                Console.Error.WriteLine("PlaybackTutorial03: the description did not produce a pipeline.");
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
            Console.Error.WriteLine($"PlaybackTutorial03: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
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
                Console.Error.WriteLine("PlaybackTutorial03: fakesink could not be created.");
                return false;
            }

            // Nothing here listens, so the sink has no reason to wait for the
            // clock: an unattended run of a few seconds of audio finishes in
            // the time it takes to generate it.
            Global.UtilSetObjectArg(sink, "sync", "false");

            using Gst.GObject.Value value = Gst.GObject.Value.New(sink.NativeType);
            value.SetObject(sink);
            playbin.SetProperty(property, value);
        }

        return true;
    }

    /// <summary>
    /// Runs the pipeline: pushes while the source is hungry, reads the bus in
    /// between, and ends the stream once every chunk has been handed over.
    /// </summary>
    /// <param name="pipeline">The playbin to run.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 on end of stream, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Options options)
    {
        Waveform waveform = new();
        Feeder feeder = new();
        int pushed = 0;
        bool ended = false;

        // source_setup of the C program. It runs while the pipeline is going to
        // PLAYING, on whichever thread gets there first, and everything the
        // tutorial configures on the source is configured inside it.
        ulong setup = pipeline.ConnectSignal("source-setup", feeder.OnSourceSetup);

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("PlaybackTutorial03: the pipeline refused to go to PLAYING.");
                return 1;
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                // The idle handler of the C program, inlined into the loop the
                // application owns. There is nothing to push until playbin has
                // built its source, and nothing to push after the source says
                // it has enough.
                while (feeder.Source is { } source && feeder.Hungry && pushed < options.Chunks)
                {
                    FlowReturn flow = Push(source, waveform);

                    if (flow != FlowReturn.Ok)
                    {
                        Console.Error.WriteLine($"PlaybackTutorial03: the source answered {flow}.");
                        return 1;
                    }

                    pushed++;
                }

                if (pushed >= options.Chunks && !ended && feeder.Source is { } finished)
                {
                    // Nothing else will come, and this is what turns that into
                    // the end-of-stream message the loop is waiting for.
                    finished.EndOfStream();
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
                        $"Error received from element {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"Debugging information: {debug ?? "none"}");
                    return 1;
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"pushed:      {pushed}"));
                return 0;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"PlaybackTutorial03: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // Back to NULL first: a handler must not be disconnected while the
            // thread that runs it is still inside it, and those threads are
            // gone once the pipeline is stopped.
            pipeline.SetState(State.Null);
            pipeline.RemoveHandler(setup);
            feeder.Disconnect();
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
        Console.Error.WriteLine($"PlaybackTutorial03: a handler failed: {exception}");

    /// <summary>
    /// The source playbin built, and whether it is asking for data. Everything
    /// here is written on a thread of the pipeline and read on the thread that
    /// pushes, which is what the volatile fields are for.
    /// </summary>
    private sealed class Feeder
    {
        private volatile AppSrc? _source;
        private volatile bool _hungry;

        /// <summary>Gets the source playbin built, once it exists.</summary>
        /// <remarks>
        /// The wrapper is never disposed: the element belongs to playbin, which
        /// built it and releases it.
        /// </remarks>
        internal AppSrc? Source => _source;

        /// <summary>Gets a value indicating whether the source wants more.</summary>
        internal bool Hungry => _hungry;

        /// <summary>
        /// Configures the source playbin has just created.
        /// </summary>
        /// <param name="sender">The playbin the signal was emitted on.</param>
        /// <param name="arguments">The element that was created.</param>
        /// <returns><see langword="null"/>: the signal returns nothing.</returns>
        /// <exception cref="InvalidOperationException">
        /// The element the signal carried is not an <c>appsrc</c>.
        /// </exception>
        internal object? OnSourceSetup(Gst.GObject.Object sender, object?[] arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            if (arguments.Length == 0 || arguments[0] is not AppSrc source)
            {
                throw new InvalidOperationException(
                    "The source-setup signal of playbin carried no appsrc.");
            }

            Console.WriteLine("Source has been created. Configuring.");

            // gst_audio_info_set_format followed by gst_audio_info_to_caps,
            // which is how the C program writes the caps it is about to push:
            // signed 16 bit samples, one channel, at the rate the waveform is
            // generated for.
            using (AudioInfo info = AudioInfo.New())
            {
                info.SetFormat(AudioFormat.S16, SampleRate, 1, default);

                // SetCaps copies the caps, so the one wrapper here is enough
                // and it is released when this scope ends.
                using Caps caps = info.ToCaps();
                source.SetCaps(caps);
            }

            // Timestamps are in time rather than in bytes, which is what makes
            // the buffers say when they are to be played.
            Global.UtilSetObjectArg(source, "format", "time");

            source.NeedData += OnNeedData;
            source.EnoughData += OnEnoughData;
            _source = source;
            return null;
        }

        /// <summary>
        /// Disconnects whatever was connected to the source.
        /// </summary>
        internal void Disconnect()
        {
            if (_source is not { } source)
            {
                return;
            }

            source.EnoughData -= OnEnoughData;
            source.NeedData -= OnNeedData;
            _source = null;
        }

        /// <summary>start_feed of the C program.</summary>
        /// <param name="sender">The source that is asking.</param>
        /// <param name="arguments">How much it is asking for.</param>
        private void OnNeedData(object? sender, AppSrc.NeedDataSignalArgs arguments)
        {
            if (!_hungry)
            {
                Console.WriteLine("Start feeding");
                _hungry = true;
            }
        }

        /// <summary>stop_feed of the C program.</summary>
        /// <param name="sender">The source that has had enough.</param>
        /// <param name="arguments">Nothing: the signal carries no argument.</param>
        private void OnEnoughData(object? sender, EventArgs arguments)
        {
            if (_hungry)
            {
                Console.WriteLine("Stop feeding");
                _hungry = false;
            }
        }
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
        /// <summary>Gets a value indicating whether the run needs no sound card.</summary>
        internal bool Headless { get; private set; }

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
