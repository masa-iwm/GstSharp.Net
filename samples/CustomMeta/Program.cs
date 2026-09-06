// The custom metadata sample: a typed GstMeta of this application's own,
// registered from managed code, attached to every frame that is pushed into a
// pipeline, and read back off the frames that come out of a conversion which
// allocated fresh buffers for them.
//
// Usage: CustomMeta [--count <buffers>] [--timeout <seconds>]
//                   [--native-path <directory>] [--flavor msvc|mingw]
//
// What it demonstrates:
//
//   * Gst.Meta.ApiTypeRegister: minting the metadata API type the items answer
//     for, with an empty tag list (see below).
//   * Gst.Meta.Register<T>: an implementation whose item is a GstMeta header
//     followed by one unmanaged payload of this program's own -- here a
//     sequence number and a capture time -- plus a transformation delegate.
//   * Gst.Buffer.AddMeta and Gst.Meta.Payload<T>(): attaching one item per
//     frame and writing the payload through a reference into the item itself.
//   * Gst.Buffer.GetMeta and the same Payload<T>() on the other side of a real
//     conversion: the buffers the appsink hands back are not the buffers that
//     went in, so an item that is there was carried, not merely still around.
//
// Which of the two mechanisms carries the item here:
//
//   Both, and neither is enough on its own. What decides that the item is
//   offered to the output buffer at all is the element: videoconvert is
//   GstVideoConvertScale in 1.28, and it overrides transform_meta rather than
//   taking the default of GstBaseTransform (gstvideoconvertscale.c:775-830).
//   That override asks gst_meta_api_type_tags_contain_only whether the tags of
//   the API are within {video, orientation, size}, and that answer is TRUE for
//   an API with no tags at all (gstmeta.c: "if (!tags) return TRUE;"), so an
//   API tagged only video or orientation is copied as well, a size tag is
//   routed through the scaling quarks instead, and only an unrelated tag falls
//   through to the base class default, which drops every tagged API
//   (gstbasetransform.c:505-517). An empty tag list is the one answer both the
//   override and that default accept, which is why the API below is registered
//   with one: it keeps the sample correct in front of elements that override
//   transform_meta and in front of the ones that do not.
//   What then does the copying is the transformation delegate handed to
//   Register<T>: an implementation registered without one is not carried
//   across a copy at all, exactly as a null transform_func is not in C. So the
//   empty tag list decides that the item is offered, and the delegate is what
//   puts it -- and its payload -- on the new buffer.
//
// The lifetime rules it follows, all of them from docs/ownership.md, section
// "Authoring a metadata implementation":
//
//   * A registration is permanent for the process: there is no unregistering,
//     the name has to be unique in the process, and the delegates stay
//     reachable for as long as the library can call them. This program
//     therefore registers exactly once and never again.
//   * The transformation delegate runs on whatever thread touches the buffer.
//     Here that is the streaming thread of videoconvert, not the thread that
//     pushed the frame, so the delegate touches nothing but the two buffers it
//     was handed.
//   * A Meta wrapper addresses storage inside its buffer and dies with the
//     item, so no wrapper here outlives the scope of the buffer it came from.
//   * An exception out of any of the six delegates would be caught at the
//     interop boundary and reported to Gst.Interop.ExceptionTrap rather than
//     unwound into native code, which is why the trap is subscribed to below.
//
// How to run:
//
//   dotnet run --project samples/CustomMeta
//
// It needs appsrc and appsink (the app plugin) and videoconvert
// (gst-plugins-base). The run is bounded by --count frames and by --timeout,
// and it exits 0 only when every frame came back with its own sequence number
// on it. Everything else exits 1: a missing element, a lost item and a
// mismatched payload are each reported by name, and anything that stops the
// stream halfway is popped off the bus as an error message and printed with
// the text the element gave it. Only a stream that stalls without an error
// runs into the timeout, which reports the count of the frames that did make
// it on the last line.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.GObject;
using Gst.Interop;

return MetaSample.Run(args);

internal static class MetaSample
{
    /// <summary>
    /// The pipeline the sample runs. The conversion is between two real
    /// formats on purpose: identity and queue hand the very same buffer on, so
    /// finding the item on the other side of one of those would prove nothing.
    /// </summary>
    private const string Description =
        "appsrc name=src format=time "
        + "caps=video/x-raw,format=I420,width=64,height=48,framerate=30/1 ! "
        + "videoconvert ! video/x-raw,format=RGB ! appsink name=sink sync=false";

    /// <summary>The width of a frame this sample generates.</summary>
    private const int Width = 64;

    /// <summary>The height of a frame this sample generates.</summary>
    private const int Height = 48;

    /// <summary>How many bytes one I420 frame of that size weighs.</summary>
    private const int FrameBytes = Width * Height * 3 / 2;

    /// <summary>How many frames a second the caps above claim.</summary>
    private const int FrameRate = 30;

    /// <summary>The name of the source in the description.</summary>
    private const string SourceName = "src";

    /// <summary>The name of the sink in the description.</summary>
    private const string SinkName = "sink";

    /// <summary>
    /// The name of the metadata API, which becomes a GType name and has to be
    /// unique in the process.
    /// </summary>
    private const string ApiName = "GstSharpSampleCaptureMetaApi";

    /// <summary>The name of the implementation, a GType name of its own.</summary>
    private const string ImplementationName = "GstSharpSampleCaptureMeta";

    /// <summary>
    /// Registers the implementation and runs the pipeline that carries it.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when every frame came back with its item, 1 otherwise.</returns>
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

            // A delegate that runs on a streaming thread must not let an
            // exception unwind into native code, so the binding catches it and
            // reports it here instead.
            ExceptionTrap.UnhandledException += OnCallbackFailure;

            Registration registration = Registration.Create();

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"meta:        {ImplementationName} over {ApiName}");
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"api type:    {(ulong)registration.Api.Value}"));
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"frames:      {options.Count} of {FrameBytes} bytes, {Width}x{Height} I420"));
            Console.WriteLine($"pipeline:    {Description}");

            if (Global.ParseLaunch(Description) is not Pipeline pipeline)
            {
                Console.Error.WriteLine("CustomMeta: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                // The two elements and the bus are interned GObject wrappers,
                // shared with every other lookup of the same object, so none
                // of them is disposed here. The pipeline is the sanctioned
                // exception: this code built it and sets it back to NULL
                // before releasing it. See docs/ownership.md.
                if (pipeline.GetByName(SourceName) is not AppSrc source ||
                    pipeline.GetByName(SinkName) is not AppSink sink)
                {
                    Console.Error.WriteLine("CustomMeta: the pipeline has no appsrc and appsink pair.");
                    return 1;
                }

                return Play(pipeline, source, sink, registration, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"CustomMeta: {exception}");
            return 1;
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnCallbackFailure;
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Pushes every frame, ends the stream and then reads the items back off
    /// the frames the conversion produced.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="source">The source the frames are pushed into.</param>
    /// <param name="sink">The sink the converted frames are pulled from.</param>
    /// <param name="registration">The metadata implementation to attach and to read.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run is sound, 1 otherwise.</returns>
    private static int Play(
        Pipeline pipeline,
        AppSrc source,
        AppSink sink,
        Registration registration,
        Options options)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("CustomMeta: the pipeline refused to go to PLAYING.");
                return 1;
            }

            for (int i = 0; i < options.Count; i++)
            {
                FlowReturn flow = PushOne(source, registration, i);

                if (flow != FlowReturn.Ok)
                {
                    Console.Error.WriteLine($"CustomMeta: the source answered {flow}.");
                    return 1;
                }
            }

            // Nothing else will come. Without this the sink never reports the
            // end of stream and the loop below would only ever time out.
            if (source.EndOfStream() != FlowReturn.Ok)
            {
                Console.Error.WriteLine("CustomMeta: the source refused the end of stream.");
                return 1;
            }

            return Pull(pipeline.GetBus(), sink, registration, options);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Fills one frame, attaches an item of this sample's implementation to it
    /// and hands it to the source.
    /// </summary>
    /// <param name="source">The source to push into.</param>
    /// <param name="registration">The implementation the item belongs to.</param>
    /// <param name="sequence">Which frame this is.</param>
    /// <returns>What the source answered.</returns>
    private static FlowReturn PushOne(AppSrc source, Registration registration, int sequence)
    {
        using Gst.Buffer? frame = Gst.Buffer.NewAllocate(null, FrameBytes, null);

        if (frame is null)
        {
            return FlowReturn.Error;
        }

        frame.SetPts(ClockTime.FromNanoseconds(
            (ulong)sequence * ClockTime.NanosecondsPerSecond / FrameRate));
        frame.SetDuration(ClockTime.FromNanoseconds(
            ClockTime.NanosecondsPerSecond / FrameRate));

        // The picture itself is a flat grey that steps with the frame number,
        // so that videoconvert has real work to do and no two frames are the
        // same. The span points into the memory of the buffer for as long as
        // the scope lives and not one byte longer.
        using (Gst.Buffer.MapScope map = frame.Map(MapFlags.Write))
        {
            map.Span.Fill((byte)(16 + (sequence % 200)));
        }

        // The attachment allocates the item and zero fills its payload, and
        // the reference Payload<T>() answers addresses that payload where it
        // lies -- inside the item, inside the buffer.
        if (frame.AddMeta(registration.Info, 0) is not { } item)
        {
            Console.Error.WriteLine("CustomMeta: the item could not be attached to a frame.");
            return FlowReturn.Error;
        }

        item.Payload<Capture>() = new Capture
        {
            Sequence = (ulong)sequence,
            CaptureTime = (ulong)Stopwatch.GetTimestamp(),
        };

        // PushBuffer consumes the buffer: after this the wrapper owns nothing,
        // and the `using` above is what makes an early return safe rather than
        // what releases it here. See docs/ownership.md, "Calls that consume
        // their argument".
        return source.PushBuffer(frame);
    }

    /// <summary>
    /// Pulls every converted frame and reads the item back off it.
    /// </summary>
    /// <param name="bus">The bus of the pipeline, watched for errors.</param>
    /// <param name="sink">The sink to pull from.</param>
    /// <param name="registration">The implementation the item belongs to.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when every frame carried its own sequence number, 1 otherwise.</returns>
    private static int Pull(Bus bus, AppSink sink, Registration registration, Options options)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        int pulled = 0;

        while (pulled < options.Count && elapsed.Elapsed < options.Timeout)
        {
            using Sample? sample = sink.TryPullSample(ClockTime.FromMilliseconds(100));

            if (sample is null)
            {
                // Nothing came within the 100 ms, so this is the moment to ask
                // the bus whether the stream failed rather than merely being
                // slow. The pop takes a zero timeout: it answers with whatever
                // is already queued and never waits, which keeps the loop on
                // its own clock and turns a negotiation or plugin failure into
                // a message with text on it instead of the full --timeout.
                using (Message? failure = bus.TimedPopFiltered(
                    ClockTime.Zero,
                    MessageType.Error))
                {
                    if (failure is not null)
                    {
                        (GException error, string? debug) = failure.ParseError();

                        Console.Error.WriteLine(
                            $"CustomMeta: {failure.SourceName ?? "?"}: {error.Message}");
                        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                        return 1;
                    }
                }

                if (sink.IsEos())
                {
                    break;
                }

                GstSharp.DrainPendingReleases();
                continue;
            }

            using Gst.Buffer? converted = sample.GetBuffer();

            if (converted is null)
            {
                Console.Error.WriteLine("CustomMeta: a sample carried no buffer.");
                return 1;
            }

            if (converted.GetMeta(registration.Api) is not { } carried)
            {
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"CustomMeta: frame {pulled} came out of the conversion without its item."));
                return 1;
            }

            Capture payload = carried.Payload<Capture>();

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"frame:       {pulled,3}  {converted.GetSize(),6} bytes  sequence {payload.Sequence,3}  captured at {payload.CaptureTime}"));

            if (payload.Sequence != (ulong)pulled)
            {
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"CustomMeta: frame {pulled} carries sequence {payload.Sequence}."));
                return 1;
            }

            pulled++;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"carried:     {pulled} of {options.Count} items"));

        if (pulled != options.Count)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"CustomMeta: {pulled} of {options.Count} frames arrived within {options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Reports a failure that was caught on a callback boundary.
    /// </summary>
    /// <param name="exception">The exception that was caught.</param>
    private static void OnCallbackFailure(Exception exception) =>
        Console.Error.WriteLine($"CustomMeta: a delegate failed: {exception}");

    /// <summary>
    /// The payload every item of this sample carries: which frame it was
    /// attached to and when that frame was made. Both fields are eight bytes
    /// wide, which is the strongest alignment an item may ask for.
    /// </summary>
    private struct Capture
    {
        /// <summary>Which frame this is, counted from zero.</summary>
        internal ulong Sequence;

        /// <summary>The timestamp the frame was generated at.</summary>
        internal ulong CaptureTime;
    }

    /// <summary>
    /// Carries one item from the buffer that goes into the conversion onto the
    /// buffer that comes out of it.
    /// </summary>
    /// <param name="transbuf">The buffer the item is to be added to.</param>
    /// <param name="meta">The item of <paramref name="buffer"/>.</param>
    /// <param name="buffer">The buffer that carries <paramref name="meta"/>.</param>
    /// <param name="type">What is being done to the buffer.</param>
    /// <param name="data">The data of that operation.</param>
    /// <returns>Whether the item was carried.</returns>
    /// <remarks>
    /// This runs on the streaming thread of the conversion. It is handed the
    /// item of the source buffer and has to add one of its own to
    /// <paramref name="transbuf"/>: nothing is copied for it. For a copy --
    /// which is what <paramref name="type"/> says here --
    /// <paramref name="data"/> always addresses a GstMetaTransformCopy, and
    /// the region that was copied is a field inside that block; this payload
    /// does not depend on the bytes of the frame, so it is carried whatever
    /// the region is.
    /// </remarks>
    private static bool Transform(
        Gst.Buffer transbuf,
        Meta meta,
        Gst.Buffer buffer,
        Quark type,
        nint data)
    {
        Capture payload = meta.Payload<Capture>();

        if (transbuf.AddMeta(meta.Info, 0) is not { } added)
        {
            return false;
        }

        added.Payload<Capture>() = payload;
        return true;
    }

    /// <summary>
    /// The one registration of this process: the API type and the
    /// implementation block made over it.
    /// </summary>
    /// <param name="Api">The metadata API type the items answer for.</param>
    /// <param name="Info">The implementation block the items were made from.</param>
    private sealed record Registration(GType Api, MetaInfo Info)
    {
        /// <summary>
        /// Registers the implementation. A registration is permanent, so this
        /// runs exactly once in the life of the process.
        /// </summary>
        /// <returns>What was registered.</returns>
        /// <exception cref="InvalidOperationException">The library refused the registration.</exception>
        internal static Registration Create()
        {
            // The tag list is empty on purpose: the transform_meta override of
            // videoconvert copies an API whose tags are contained in its own
            // set, and no tags at all is contained in every set, while the
            // GstBaseTransform default that other elements keep drops every
            // tagged API. See the header for the two code paths.
            GType api = Meta.ApiTypeRegister(ApiName, []);

            MetaInfo info = Meta.Register<Capture>(api, ImplementationName, transform: Transform);

            return new Registration(api, info);
        }
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets how many frames are pushed before the stream ends.</summary>
        internal int Count { get; private set; } = 25;

        /// <summary>Gets how long the run may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

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
                    case "--count":
                        options.Count = int.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture);
                        break;

                    case "--timeout":
                        options.Timeout = TimeSpan.FromSeconds(double.Parse(
                            Cli.ValueOf(arguments, ref i),
                            CultureInfo.InvariantCulture));
                        break;

                    case "--native-path":
                        options.Native.NativeSearchPath = Cli.ValueOf(arguments, ref i);
                        break;

                    case "--flavor":
                        options.Native.WindowsFlavor = Cli.FlavorOf(Cli.ValueOf(arguments, ref i));
                        break;

                    default:
                        throw new ArgumentException(
                            $"\"{arguments[i]}\" is not a known argument.",
                            nameof(arguments));
                }
            }

            if (options.Count <= 0)
            {
                throw new ArgumentException("--count needs a positive count.", nameof(arguments));
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
