// The NativeAOT smoke test: it initialises GstSharp.Net, asks GStreamer for an
// element, and releases it again. Everything it touches has to survive
// trimming and ahead of time compilation, which is what the gate publishes:
//   dotnet publish samples/AotSmoke -r win-x64 -c Release /p:PublishAot=true
//
// Usage: AotSmoke [--native-path <directory>] [--flavor msvc|mingw]
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using Gst;
using Gst.Base;
using Gst.Controller;
using Gst.Interop;

return Smoke.Run(args);

internal static partial class Smoke
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The smoke test turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            GstSharpOptions options = ParseOptions(arguments);

            // GstBase.Initialize is GstSharp.Initialize plus the deterministic
            // registration of the GstBase types, which the managed source and
            // sink below are built on.
            GstBase.Initialize(options);

            // This assembly brings its own [LibraryImport] stubs, so it has to
            // resolve them through the loader as well. The libraries are loaded
            // by now, so this only maps the logical name of the module onto the
            // installation that GstSharp.Initialize pinned.
            NativeLoader.EnsureRegistered(typeof(Smoke).Assembly);

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"version:     {GstSharp.NativeVersion}"));
            Console.WriteLine($"description: {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");

            nint element = ElementFactoryMake("fakesink", "smoke");
            if (element == nint.Zero)
            {
                Console.Error.WriteLine("AotSmoke: gst_element_factory_make returned NULL for \"fakesink\".");
                return 1;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"fakesink:    0x{element:x}"));

            // The factory hands out a floating reference. Sinking it first is
            // what turns it into one that this code owns, and keeps GLib from
            // complaining about a floating object that is finalized.
            ObjectRefSink(element);
            ObjectUnref(element);

            if (!RunManagedSubclass() || !RunManagedPipeline() || !RunManagedAudioAndVideoSinks()
                || !RunManagedAudioEncoder() || !RunBindingModule() || !RunPropertiesByName()
                || !RunPadChainFunction() || !RunFactoryMadeManagedElement())
            {
                return 1;
            }

            GstSharp.DrainPendingReleases();

            Console.WriteLine("OK");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"AotSmoke: {exception}");
            return 1;
        }
    }

    /// <summary>
    /// Registers a managed <c>GstElement</c> subclass, builds one and drives it
    /// through a state change, so that the ahead of time compiler has to keep
    /// the plainest subclassing path: the registration, the shared
    /// <c>class_init</c>, the unmanaged trampoline of the overridden slot and
    /// the chain-up through the class struct mirrors.
    /// </summary>
    /// <returns><see langword="true"/> when the override ran and chained up.</returns>
    private static bool RunManagedSubclass()
    {
        Console.WriteLine($"subclass:    {ManagedElement.RegisteredType.Name}");

        using ManagedElement managed = new();

        StateChangeReturn up = managed.SetState(State.Ready);
        StateChangeReturn down = managed.SetState(State.Null);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"override:    {up} then {down}, {managed.Transitions} managed change_state calls"));

        if (up != StateChangeReturn.Success || down != StateChangeReturn.Success || managed.Transitions != 2)
        {
            Console.Error.WriteLine("AotSmoke: the managed change_state override did not run as expected.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Runs a pipeline made of two managed elements: a <c>GstPushSrc</c>
    /// subclass that produces buffers from C# and a <c>GstBaseSink</c> subclass
    /// that consumes them, linked to each other and driven by GStreamer's own
    /// streaming thread.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every buffer the source produced reached the
    /// sink and the stream ended on the bus.
    /// </returns>
    /// <remarks>
    /// This is the real demonstration of the surface, and the reason it is in
    /// the smoke test: the class configuration, the buffer producing slot with
    /// its ownership handover, the borrowed buffer of the render slot and the
    /// negotiation are all only reachable from a running pipeline, so only a
    /// running pipeline proves that ILC kept them.
    /// </remarks>
    private static bool RunManagedPipeline()
    {
        const int Buffers = 4;

        using Pipeline pipeline = Pipeline.New("aot-smoke-managed");
        ManagedSource source = new() { BufferCount = Buffers };
        ManagedSink sink = new();

        Console.WriteLine($"pipeline:    {ManagedSource.RegisteredType.Name} -> {ManagedSink.RegisteredType.Name}");

        if (!pipeline.AddMany(source, sink) || !source.Link(sink))
        {
            Console.Error.WriteLine("AotSmoke: the managed elements could not be linked.");
            return false;
        }

        Bus bus = pipeline.GetBus();
        MessageType seen = MessageType.Unknown;

        try
        {
            pipeline.SetState(State.Playing);

            // No main loop here, so the bus is polled the way a batch
            // application polls it.
            for (int slice = 0; slice < 100 && seen == MessageType.Unknown; slice++)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Eos | MessageType.Error);

                seen = message?.Type ?? MessageType.Unknown;
                GstSharp.DrainPendingReleases();
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        byte[] rendered = sink.Rendered;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"produced:    {source.Produced} buffers, rendered {rendered.Length}"));
        Console.WriteLine($"bytes:       [{string.Join(", ", rendered)}]");
        Console.WriteLine($"caps:        {sink.NegotiatedCaps}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"bus:         {seen}"));

        if (seen != MessageType.Eos || source.Produced != Buffers || rendered.Length != Buffers)
        {
            Console.Error.WriteLine("AotSmoke: the managed pipeline did not run to its end.");
            return false;
        }

        for (int i = 0; i < rendered.Length; i++)
        {
            if (rendered[i] != i)
            {
                Console.Error.WriteLine("AotSmoke: the managed sink saw buffers the managed source did not send.");
                return false;
            }
        }

        if (!source.Cycled)
        {
            Console.Error.WriteLine("AotSmoke: the managed source was not started and stopped.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drives a managed <c>GstAudioSink</c> and a managed <c>GstVideoSink</c>
    /// with the test sources of the base plugins, so that the ahead of time
    /// compiler keeps the trampolines of <c>GstSharp.Net.Audio</c> and
    /// <c>GstSharp.Net.Video</c> as well.
    /// </summary>
    /// <returns><see langword="true"/> when both sinks saw what was sent.</returns>
    /// <remarks>
    /// The audio sink is the only element of the sample whose slot is handed a
    /// raw pointer with a count beside it: the write of an audio sink is
    /// projected onto a span the trampoline builds over memory the ring buffer
    /// owns, and nothing else in the smoke test reaches that shape.
    /// </remarks>
    private static bool RunManagedAudioAndVideoSinks()
    {
        const int Buffers = 5;

        using ManagedAudioSink audio = new();
        using ManagedVideoSink video = new();

        Console.WriteLine($"audio sink:  {ManagedAudioSink.RegisteredType.Name}");
        Console.WriteLine($"video sink:  {ManagedVideoSink.RegisteredType.Name}");

        if (!RunSourceInto("audiotestsrc", audio, Buffers) || !RunSourceInto("videotestsrc", video, Buffers))
        {
            return false;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"written:     {audio.Written} bytes, shown {video.Shown} frames"));

        if (audio.Written <= 0 || video.Shown <= 0)
        {
            Console.Error.WriteLine("AotSmoke: the managed audio or video sink saw nothing.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drives a managed <c>GstAudioEncoder</c>, which is the shape no sink of
    /// the sample has: its <c>handle_frame</c> slot is called with the samples
    /// the base class collected and once more with no buffer at all for the
    /// drain, and its <c>set_format</c> slot is lent a <c>GstAudioInfo</c> that
    /// only means anything for the length of the call.
    /// </summary>
    /// <returns><see langword="true"/> when the encoder coded and was drained.</returns>
    private static bool RunManagedAudioEncoder()
    {
        const int Buffers = 5;

        using ManagedAudioEncoder encoder = new();
        Element? sink = ElementFactory.Make("fakesink", "encoded");

        if (sink is null)
        {
            Console.Error.WriteLine("AotSmoke: fakesink is missing from the installation.");
            return false;
        }

        using (sink)
        {
            Console.WriteLine($"audio encoder: {ManagedAudioEncoder.RegisteredType.Name}");

            if (!RunSourceInto("audiotestsrc", sink, Buffers, encoder))
            {
                return false;
            }
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"encoded:     {encoder.Encoded} buffers at {encoder.Rate} Hz, {encoder.Drains} drain(s)"));

        if (encoder.Encoded <= 0 || encoder.Rate <= 0 || encoder.Drains <= 0)
        {
            Console.Error.WriteLine("AotSmoke: the managed audio encoder coded nothing or was never drained.");
            return false;
        }

        return true;
    }

    /// <summary>Runs a bounded test source into one sink and waits for the end.</summary>
    /// <param name="factory">The factory of the test source.</param>
    /// <param name="sink">The managed sink.</param>
    /// <param name="buffers">How many buffers the source produces.</param>
    /// <param name="through">An element to put between the source and the sink, if any.</param>
    /// <returns><see langword="true"/> when the stream ended on the bus.</returns>
    private static bool RunSourceInto(string factory, Element sink, int buffers, Element? through = null)
    {
        using Pipeline pipeline = Pipeline.New("aot-smoke-" + factory);
        Element? source = ElementFactory.Make(factory, "source");

        if (source is null)
        {
            Console.Error.WriteLine($"AotSmoke: {factory} is missing from the installation.");
            return false;
        }

        using Gst.GObject.Value count = Gst.GObject.Value.New(Gst.GObject.GType.Int);
        count.SetInt(buffers);
        source.SetProperty("num-buffers", count);

        bool linked = through is null
            ? pipeline.AddMany(source, sink) && source.Link(sink)
            : pipeline.AddMany(source, through, sink) && source.Link(through) && through.Link(sink);

        if (!linked)
        {
            Console.Error.WriteLine($"AotSmoke: {factory} could not be linked to the managed element.");
            return false;
        }

        Bus bus = pipeline.GetBus();
        MessageType seen = MessageType.Unknown;

        try
        {
            pipeline.SetState(State.Playing);

            for (int slice = 0; slice < 100 && seen == MessageType.Unknown; slice++)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Eos | MessageType.Error);

                seen = message?.Type ?? MessageType.Unknown;
                GstSharp.DrainPendingReleases();
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{factory,-12} bus: {seen}"));

        if (seen != MessageType.Eos)
        {
            Console.Error.WriteLine($"AotSmoke: the {factory} pipeline did not run to its end.");
            return false;
        }

        // The pipeline is taken down before it is disposed, so the sink it
        // still holds is released with it; the managed wrapper of the sink
        // outlives both, which is what the counters are read from.
        _ = pipeline.Remove(sink);

        if (through is not null)
        {
            _ = pipeline.Remove(through);
        }

        return true;
    }

    /// <summary>
    /// Exercises a binding module that lives outside the runtime assembly and
    /// is written entirely against the public SPI: <c>GstSharp.Net.Controller</c>.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the control source interpolated the way its
    /// mode says.
    /// </returns>
    /// <remarks>
    /// Three things have to survive ILC for this to work, and none of them is a
    /// build warning when it does not: the module initialiser of another
    /// assembly, which teaches the loader the file names of
    /// <c>libgstcontroller-1.0</c>; the function pointers of its type table,
    /// which the registry calls to resolve the <c>GType</c> and to build the
    /// wrapper; and the property round trip through <c>GValue</c>. A control
    /// source needs no plugin and no pipeline, so this stays a few
    /// milliseconds.
    /// </remarks>
    private static bool RunBindingModule()
    {
        InterpolationControlSource source = InterpolationControlSource.New();
        source.Mode = InterpolationMode.Linear;
        source.Set(ClockTime.Zero, 0.0);
        source.Set(ClockTime.FromSeconds(1), 1.0);

        bool answered = source.TryGetValue(ClockTime.FromMilliseconds(500), out double middle);

        Console.WriteLine($"module:      GstController from {NativeLoader.GetLoadedModulePath("GstController") ?? "the library search path"}");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"controlled:  {source.Count} points, {source.Mode} at 0.5s is {middle}"));

        if (!answered || Math.Abs(middle - 0.5) > 1e-9 || source.Count != 2)
        {
            Console.Error.WriteLine("AotSmoke: the binding module did not interpolate as expected.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Registers a managed subclass as an element factory and makes one through
    /// that factory, so that the ahead of time compiler has to keep the path
    /// that wraps an instance GStreamer created: the static abstract
    /// <c>CreateWrapper</c> of the subclass, instantiated for its own type.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the factory answered the managed type and
    /// its override ran.
    /// </returns>
    private static bool RunFactoryMadeManagedElement()
    {
        if (!ManagedFactoryElement.RegisterFactory())
        {
            Console.Error.WriteLine("AotSmoke: the managed element factory could not be registered.");
            return false;
        }

        using Element? made = ElementFactory.Make(ManagedFactoryElement.FactoryName, "made");

        if (made is not ManagedFactoryElement managed)
        {
            Console.Error.WriteLine(
                $"AotSmoke: the factory answered {made?.GetType().Name ?? "nothing"} instead of the managed type.");
            return false;
        }

        StateChangeReturn up = managed.SetState(State.Ready);
        StateChangeReturn down = managed.SetState(State.Null);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"fabricated:  {ManagedFactoryElement.RegisteredType.Name}, {managed.Transitions} managed change_state calls"));

        if (up != StateChangeReturn.Success || down != StateChangeReturn.Success || managed.Transitions != 2)
        {
            Console.Error.WriteLine("AotSmoke: the fabricated wrapper did not receive its state changes.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Drives a managed chain function, whose trampoline recovers the delegate
    /// from the pad it is called with rather than from a user data pointer.
    /// </summary>
    /// <returns>Whether the buffer reached the handler.</returns>
    private static bool RunPadChainFunction()
    {
        using Pad pad = Pad.New("sink", PadDirection.Sink);
        int seen = 0;
        pad.SetChainFunction((_, _, buffer) =>
        {
            seen += (int)buffer.GetSize();
            return FlowReturn.Ok;
        });

        if (!pad.SetActive(true))
        {
            Console.Error.WriteLine("AotSmoke: the pad did not activate.");
            return false;
        }

        // A pad warns about data that arrives before the stream has been
        // opened, so the sample opens it the way an element would.
        pad.SendEvent(Event.NewStreamStart("aot-smoke"));
        using Segment segment = Segment.New();
        segment.Init(Format.Time);
        pad.SendEvent(Event.NewSegment(segment));

        using Gst.Buffer buffer = Gst.Buffer.NewAllocate(null, 8, null)
            ?? throw new InvalidOperationException("gst_buffer_new_allocate returned NULL.");
        FlowReturn flow = pad.Chain(buffer);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"chain:       {flow}, {seen} byte(s)"));
        pad.SetChainFunction(null);
        return flow == FlowReturn.Ok && seen == 8;
    }

    /// <summary>
    /// Reads and writes properties by name, which is the route to every element
    /// that no <c>.gir</c> file describes.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every property answered what was written to
    /// it.
    /// </returns>
    /// <remarks>
    /// The pair is generic over the managed type of the property, and generic
    /// code is where ILC has the most room to leave something behind: the
    /// enumeration read goes through <c>Enum.ToObject</c> over a type argument,
    /// and the wrapper read goes through the type registry with one. Neither is
    /// a build warning when it fails, so both are asked here.
    /// </remarks>
    private static bool RunPropertiesByName()
    {
        using Element sink = ElementFactory.Make("fakesink", "properties")
            ?? throw new InvalidOperationException("gst_element_factory_make returned NULL for \"fakesink\".");

        sink.SetProperty("sync", true);
        sink.SetProperty("ts-offset", 250_000_000L);
        sink.SetProperty("blocksize", 8192);

        // The pad is an interned wrapper and is not disposed here. Its direction
        // is an enumeration the bindings declare, so it reads as one.
        Pad pad = sink.GetStaticPad("sink")
            ?? throw new InvalidOperationException("the fakesink has no sink pad.");

        bool synchronised = sink.GetProperty<bool>("sync");
        long offset = sink.GetProperty<long>("ts-offset");
        uint blockSize = sink.GetProperty<uint>("blocksize");
        string? name = sink.GetProperty<string>("name");
        PadDirection direction = pad.GetProperty<PadDirection>("direction");
        using Structure? stats = sink.GetProperty<Structure>("stats");

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"property:    {name} sync={synchronised} ts-offset={offset} blocksize={blockSize} pad={direction}"));
        Console.WriteLine($"boxed:       {stats?.GetName() ?? "nothing"}");

        if (!synchronised || offset != 250_000_000L || blockSize != 8192 ||
            name != "properties" || direction != PadDirection.Sink || stats is null)
        {
            Console.Error.WriteLine("AotSmoke: a property did not answer what was written to it.");
            return false;
        }

        return true;
    }

    [LibraryImport("Gst", EntryPoint = "gst_element_factory_make", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint ElementFactoryMake(string factoryName, string? name);

    [LibraryImport("Gst", EntryPoint = "gst_object_ref_sink")]
    private static partial nint ObjectRefSink(nint instance);

    [LibraryImport("Gst", EntryPoint = "gst_object_unref")]
    private static partial void ObjectUnref(nint instance);

    private static GstSharpOptions ParseOptions(string[] arguments)
    {
        GstSharpOptions options = new();

        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--native-path":
                    options.NativeSearchPath = ValueOf(arguments, ref i);
                    break;

                case "--flavor":
                    options.WindowsFlavor = ValueOf(arguments, ref i).ToUpperInvariant() switch
                    {
                        "MSVC" => GstFlavor.Msvc,
                        "MINGW" => GstFlavor.MinGW,
                        string other => throw new ArgumentException(
                            $"\"{other}\" is not a flavor. Use msvc or mingw.",
                            nameof(arguments)),
                    };
                    break;

                default:
                    throw new ArgumentException($"\"{arguments[i]}\" is not a known argument.", nameof(arguments));
            }
        }

        return options;
    }

    private static string ValueOf(string[] arguments, ref int index)
    {
        if (index + 1 >= arguments.Length)
        {
            throw new ArgumentException($"\"{arguments[index]}\" needs a value.", nameof(arguments));
        }

        return arguments[++index];
    }
}
