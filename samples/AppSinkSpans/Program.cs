// The zero copy sample: it pulls raw video out of an appsink and reads every
// frame through a Span<byte> that points straight at the mapped GStreamer
// memory, with no copy in between.
//
// Usage: AppSinkSpans [--mode pull|signal] [--native-path <directory>]
//                     [--flavor msvc|mingw] [--timeout <seconds>]
//
// Both modes run the same pipeline and compute the same checksum over the same
// frames, so the totals of a "--mode pull" run and a "--mode signal" run have
// to match: the two ways of getting a sample out of an appsink differ in who
// calls, not in what is delivered.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;

return Spans.Run(args);

internal static class Spans
{
    /// <summary>The pipeline both modes run.</summary>
    private const string Description =
        "videotestsrc num-buffers=120 ! video/x-raw,format=RGBA,width=320,height=240 ! appsink name=sink";

    /// <summary>The name of the sink in <see cref="Description"/>.</summary>
    private const string SinkName = "sink";

    /// <summary>
    /// Builds the pipeline, runs it in the requested mode and prints the
    /// totals.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when every frame arrived, 1 on any failure.</returns>
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
            // which puts GstAppSink into the type registry. Without it the cast
            // of the sink below is silently null.
            GstApp.Initialize(options.Native);

            // A handler that runs on a streaming thread must not let an
            // exception unwind into native code, so the binding catches and
            // reports it here instead. Without this the signal mode could print
            // a plausible looking total after a handler that never ran to its
            // end.
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");
            Console.WriteLine($"mode:        {(options.Mode == Mode.Pull ? "pull" : "signal")}");
            Console.WriteLine($"pipeline:    {Description}");

            if (Global.ParseLaunch(Description) is not Pipeline pipeline)
            {
                Console.Error.WriteLine("AppSinkSpans: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                // The cast is the type registry at work: gst_bin_get_by_name
                // returns a GstElement, and only the map from its GType to this
                // wrapper turns it into an appsink. A null here is the silent
                // failure a missing registry entry produces.
                if (pipeline.GetByName(SinkName) is not AppSink sink)
                {
                    Console.Error.WriteLine($"AppSinkSpans: \"{SinkName}\" is not an appsink.");
                    return 1;
                }

                // The sink and the bus are interned GObject wrappers, shared
                // with every other lookup of the same object, so neither is
                // disposed here. The pipeline is the sanctioned exception:
                // this code built it and sets it back to NULL before releasing
                // it. See docs/ownership.md.
                Bus bus = pipeline.GetBus();

                Totals totals = new();

                int status = options.Mode == Mode.Pull
                    ? RunPull(pipeline, bus, sink, totals, options.Timeout)
                    : RunSignal(pipeline, bus, sink, totals, options.Timeout);

                Print(totals);
                return status;
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AppSinkSpans: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Runs the pipeline and pulls the samples on this thread.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <param name="sink">The sink to pull from.</param>
    /// <param name="totals">The totals to fill.</param>
    /// <param name="timeout">How long the run may take.</param>
    /// <returns>0 when the sink reached the end of the stream, 1 otherwise.</returns>
    /// <remarks>
    /// This is the polling shape: no main loop, no callback, a bounded wait per
    /// sample and the bus polled for whatever went wrong.
    /// </remarks>
    private static int RunPull(Pipeline pipeline, Bus bus, AppSink sink, Totals totals, TimeSpan timeout)
    {
        // A pull that finds nothing waits this long before the loop looks at
        // the bus again.
        ClockTime slice = ClockTime.FromMilliseconds(100);

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("AppSinkSpans: the pipeline refused to go to PLAYING.");
                return Drain(bus);
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                // try_pull_sample returns null on a timeout and after the end of
                // the stream; it never blocks longer than the timeout, which is
                // what lets one thread own both the sink and the bus.
                using Sample? sample = sink.TryPullSample(slice);

                if (sample is null)
                {
                    if (sink.IsEos())
                    {
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return CheckBus(bus);
                    }

                    if (CheckBus(bus) != 0)
                    {
                        return 1;
                    }

                    continue;
                }

                Read(sample, totals);
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"AppSinkSpans: no end of stream within {timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Runs the pipeline and lets the sink call back into the sample whenever a
    /// frame is ready.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <param name="sink">The sink to pull from.</param>
    /// <param name="totals">The totals to fill.</param>
    /// <param name="timeout">How long the run may take.</param>
    /// <returns>0 when the pipeline reached the end of the stream, 1 otherwise.</returns>
    /// <remarks>
    /// The handler runs on the streaming thread of the sink, so it does the
    /// least it can: it pulls the sample the signal announced, reads it and
    /// releases it again. Everything that has to be reported waits for the end
    /// of the run on the thread that started it.
    /// </remarks>
    private static int RunSignal(Pipeline pipeline, Bus bus, AppSink sink, Totals totals, TimeSpan timeout)
    {
        AppSink.NewSampleHandler handler = (sender, args) =>
        {
            // pull_sample is what the signal is for: the sample is already
            // there, so this does not block.
            using Sample? sample = sink.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            Read(sample, totals);
            return FlowReturn.Ok;
        };

        // The signals of an appsink are off by default, because emitting them
        // costs something on every single frame.
        sink.EmitSignals = true;
        sink.NewSample += handler;

        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("AppSinkSpans: the pipeline refused to go to PLAYING.");
                return Drain(bus);
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Eos | MessageType.Error);

                if (message is null)
                {
                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    PrintError(message);
                    return 1;
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                return 0;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"AppSinkSpans: no end of stream within {timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // Back to NULL first: the handler must not be disconnected while
            // the streaming thread is still in it, and that thread is gone once
            // the pipeline is stopped.
            pipeline.SetState(State.Null);
            sink.NewSample -= handler;
        }
    }

    /// <summary>
    /// Reads one frame: its caps say how large it is, and its memory is folded
    /// into the running checksum without being copied anywhere.
    /// </summary>
    /// <param name="sample">The sample to read.</param>
    /// <param name="totals">The totals to fill.</param>
    private static void Read(Sample sample, Totals totals)
    {
        int width = 0;
        int height = 0;

        // The caps of a sample describe the frame it carries. GetStructure
        // hands out a wrapper that owns what it points at, like every other
        // getter of the binding, so it is disposed here as well.
        using (Caps? caps = sample.GetCaps())
        {
            if (caps is not null && caps.GetSize() > 0)
            {
                using Structure structure = caps.GetStructure(0);
                structure.GetInt("width", out width);
                structure.GetInt("height", out height);
            }
        }

        using Gst.Buffer? buffer = sample.GetBuffer();

        if (buffer is null)
        {
            return;
        }

        // The span points into the memory of the buffer for as long as the
        // scope lives, and not one byte longer: MapScope is a ref struct, so it
        // cannot escape this frame, and disposing it unmaps the memory again.
        using Gst.Buffer.MapScope map = buffer.Map(MapFlags.Read);
        Span<byte> frame = map.Span;

        totals.Add(width, height, frame);
    }

    /// <summary>
    /// Prints whatever error the bus holds.
    /// </summary>
    /// <param name="bus">The bus to look at.</param>
    /// <returns>0 when nothing failed, 1 when an error was posted.</returns>
    private static int CheckBus(Bus bus)
    {
        using Message? message = bus.PopFiltered(MessageType.Error);

        if (message is null)
        {
            return 0;
        }

        PrintError(message);
        return 1;
    }

    /// <summary>
    /// Prints everything the bus already holds after a failed state change.
    /// </summary>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <returns>1, because this is only reached after a failure.</returns>
    private static int Drain(Bus bus)
    {
        while (bus.PopFiltered(MessageType.Error) is Message message)
        {
            using (message)
            {
                PrintError(message);
            }
        }

        return 1;
    }

    /// <summary>
    /// Prints an error message together with the element that posted it.
    /// </summary>
    /// <param name="message">The error message.</param>
    private static void PrintError(Message message)
    {
        (GException error, string? debug) = message.ParseError();

        Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
    }

    /// <summary>
    /// Prints what the run added up to.
    /// </summary>
    /// <param name="totals">The totals of the run.</param>
    private static void Print(Totals totals)
    {
        (int frames, long bytes, ulong checksum, int width, int height) = totals.Read();

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"frames:      {frames}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"resolution:  {width}x{height}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bytes:       {bytes}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"checksum:    0x{checksum:X16}"));
    }

    /// <summary>
    /// Reports a failure that was caught on a callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    private static void OnCallbackFailure(Exception exception) =>
        Console.Error.WriteLine($"AppSinkSpans: a sink callback failed: {exception}");

    /// <summary>
    /// What the run adds up to. The signal mode fills this from the streaming
    /// thread, so every access is under the lock.
    /// </summary>
    private sealed class Totals
    {
        private readonly Lock _lock = new();
        private int _frames;
        private long _bytes;
        private ulong _checksum = Fnv.Offset;
        private int _width;
        private int _height;

        /// <summary>
        /// Adds one frame.
        /// </summary>
        /// <param name="width">The width the caps of the frame report.</param>
        /// <param name="height">The height the caps of the frame report.</param>
        /// <param name="frame">The memory of the frame.</param>
        internal void Add(int width, int height, ReadOnlySpan<byte> frame)
        {
            // The digest is computed outside the lock: the span belongs to the
            // buffer of the caller, and it cannot be handed to another thread
            // anyway.
            ulong digest = Fnv.Of(frame);

            lock (_lock)
            {
                _frames++;
                _bytes += frame.Length;
                _checksum = Fnv.Combine(_checksum, digest);
                _width = width;
                _height = height;
            }
        }

        /// <summary>
        /// Reads the totals.
        /// </summary>
        /// <returns>The number of frames, the number of bytes, the checksum and the frame size.</returns>
        internal (int Frames, long Bytes, ulong Checksum, int Width, int Height) Read()
        {
            lock (_lock)
            {
                return (_frames, _bytes, _checksum, _width, _height);
            }
        }
    }

    /// <summary>
    /// The 64 bit FNV-1a hash, which is short, dependency free and good enough
    /// to notice that two runs read different bytes.
    /// </summary>
    private static class Fnv
    {
        /// <summary>The offset basis the hash starts from.</summary>
        internal const ulong Offset = 14695981039346656037;

        /// <summary>The prime the hash multiplies by.</summary>
        private const ulong Prime = 1099511628211;

        /// <summary>
        /// Hashes one frame.
        /// </summary>
        /// <param name="data">The bytes to hash.</param>
        /// <returns>The hash of <paramref name="data"/>.</returns>
        internal static ulong Of(ReadOnlySpan<byte> data)
        {
            ulong hash = Offset;

            foreach (byte value in data)
            {
                hash = (hash ^ value) * Prime;
            }

            return hash;
        }

        /// <summary>
        /// Folds the hash of one frame into the hash of the run.
        /// </summary>
        /// <param name="running">The hash of the frames so far.</param>
        /// <param name="digest">The hash of the next frame.</param>
        /// <returns>The new hash of the run.</returns>
        internal static ulong Combine(ulong running, ulong digest)
        {
            ulong hash = running;

            for (int shift = 0; shift < 64; shift += 8)
            {
                hash = (hash ^ (byte)(digest >> shift)) * Prime;
            }

            return hash;
        }
    }

    /// <summary>How the samples are taken out of the sink.</summary>
    private enum Mode
    {
        /// <summary>The application pulls them.</summary>
        Pull,

        /// <summary>The sink announces them with <c>new-sample</c>.</summary>
        Signal,
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets how the samples are taken out of the sink.</summary>
        internal Mode Mode { get; private set; } = Mode.Pull;

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
                    case "--mode":
                        options.Mode = ValueOf(arguments, ref i).ToUpperInvariant() switch
                        {
                            "PULL" => Mode.Pull,
                            "SIGNAL" => Mode.Signal,
                            string other => throw new ArgumentException(
                                $"\"{other}\" is not a mode. Use pull or signal.",
                                nameof(arguments)),
                        };
                        break;

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
                        throw new ArgumentException(
                            $"\"{arguments[i]}\" is not a known argument.",
                            nameof(arguments));
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
