// Basic tutorial 6: media formats and pad capabilities — what an element is
// willing to produce or accept, and what it settled on once the pipeline runs.
//
// Ported from basic-tutorial-6.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/media-formats-and-pad-capabilities.html
//
// Usage: BasicTutorial06 [--headless] [--native-path <directory>]
//                        [--flavor msvc|mingw] [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * gst_element_factory_get_static_pad_templates is not bound: it hands back
//     a GList of GstStaticPadTemplate, a structure whose caps field is a
//     GstStaticCaps that the generator does not emit (girs/skip-report.md,
//     UnsupportedSignature). The templates of the element class say the same
//     thing and are bound, so this asks the factory for an element and reads
//     Element.GetPadTemplateList. The difference is that the templates come
//     from an instance rather than from the class, and instantiating a sink
//     does not touch its device — that happens on the way to READY.
//
//   * gst_element_factory_get_longname is a macro over the metadata table.
//     GetMetadata("long-name") is the same lookup, spelled out.
//
//   * gst_structure_foreach takes a callback per field. The binding walks the
//     fields instead — NFields and NthFieldName — and reads each one with
//     Structure.GetValue, which is where the GValue bridge of the binding
//     earns its place: it is the only way to see a field whose type has no
//     gst_structure_get_* of its own, and every field here is one of those.
//     gst_value_serialize is Gst.Global.ValueSerialize.
//
//   * The Structure that comes out of Caps.GetStructure is a copy, not a
//     window into the caps: a boxed wrapper of this binding always owns what it
//     points at. Writing to it would not write back. See docs/ownership.md.
//     Reading, which is all this tutorial does, is unaffected.
//
//   * A pad template and a factory are GObject wrappers, interned and shared,
//     so the four gst_object_unref calls of the C program have no counterpart.
//     Caps and Value are not: they are released by `using`.
//
//   * --headless is not part of the tutorial. It replaces the autoaudiosink
//     with a fakesink and bounds the source, so the program runs where there is
//     no sound card. The negotiated caps then say what a fakesink accepted
//     rather than what a sound card asked for, which is less interesting — that
//     is why this is the one tutorial here whose headless run is not a gate.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;

return PadCapabilities.Run(args);

internal static class PadCapabilities
{
    /// <summary>How many buffers the source produces when it has to end.</summary>
    private const string HeadlessBuffers = "100";

    /// <summary>
    /// Prints what the two factories can do, then what their elements agreed
    /// on.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the run ended as it should, 1 on any error.</returns>
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
            Console.WriteLine();

            // gst_element_factory_find. A factory is what the registry holds:
            // it describes an element without building one.
            ElementFactory? sourceFactory = ElementFactory.Find("audiotestsrc");
            ElementFactory? sinkFactory = ElementFactory.Find(options.Sink);

            if (sourceFactory is null || sinkFactory is null)
            {
                Console.Error.WriteLine("BasicTutorial06: not all element factories could be found.");
                return 1;
            }

            // gst_element_factory_create: the factory builds the element the
            // pipeline will run, and the same element answers what its pad
            // templates are.
            Element? source = sourceFactory.Create("source");
            Element? sink = sinkFactory.Create("sink");
            Pipeline? pipeline = Pipeline.New("test-pipeline");

            if (source is null || sink is null || pipeline is null)
            {
                Console.Error.WriteLine("BasicTutorial06: not all elements could be created.");
                return 1;
            }

            PrintPadTemplates(sourceFactory, source);
            PrintPadTemplates(sinkFactory, sink);

            using (pipeline)
            {
                pipeline.AddMany(source, sink);

                if (!source.Link(sink))
                {
                    Console.Error.WriteLine("BasicTutorial06: the elements could not be linked.");
                    return 1;
                }

                if (options.Headless)
                {
                    Global.UtilSetObjectArg(source, "num-buffers", HeadlessBuffers);
                    Global.UtilSetObjectArg(sink, "sync", "false");
                }

                // Nothing has been negotiated yet, so this is what the sink is
                // willing to take rather than what it took.
                Console.WriteLine("In NULL state:");
                PrintPadCapabilities(sink, "sink");

                return Play(pipeline, sink, options);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial06: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Plays the pipeline and prints the caps of the sink at every transition
    /// of the pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline to play.</param>
    /// <param name="sink">The sink whose pad is reported.</param>
    /// <param name="options">The command line of the sample.</param>
    /// <returns>0 when the run ended as it should, 1 on any error.</returns>
    private static int Play(Pipeline pipeline, Element sink, Options options)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine(
                    "BasicTutorial06: the pipeline refused to go to PLAYING (the bus says why).");
            }

            Bus bus = pipeline.GetBus();
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < options.Timeout)
            {
                using Message? message = bus.TimedPopFiltered(
                    ClockTime.FromMilliseconds(100),
                    MessageType.Error | MessageType.Eos | MessageType.StateChanged);

                if (message is null)
                {
                    GstSharp.DrainPendingReleases();
                    continue;
                }

                switch (message.Type)
                {
                    case MessageType.Error:
                        (GException error, string? debug) = message.ParseError();
                        Console.Error.WriteLine(
                            $"error:       from element {message.SourceName ?? "?"}: {error.Message}");
                        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                        return 1;

                    case MessageType.Eos:
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return 0;

                    case MessageType.StateChanged:
                        if (message.Src is { } source && source.Handle == pipeline.Handle)
                        {
                            message.ParseStateChanged(out State old, out State current, out _);
                            Console.WriteLine();
                            Console.WriteLine($"Pipeline state changed from {old} to {current}:");

                            // The interesting one is the transition into
                            // PAUSED: that is where the pads negotiate, so the
                            // caps stop being a list of possibilities and
                            // become one fixed format.
                            PrintPadCapabilities(sink, "sink");
                        }

                        break;

                    default:
                        break;
                }
            }

            if (options.Headless)
            {
                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"BasicTutorial06: no end of stream within {options.Timeout.TotalSeconds:F0} s."));
                return 1;
            }

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"stop:        after {options.Timeout.TotalSeconds:F0} s, the source has no end of its own."));
            return 0;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Prints the pad templates of a factory, and the caps each one allows.
    /// </summary>
    /// <param name="factory">The factory to describe.</param>
    /// <param name="element">An element the factory built, which carries the templates.</param>
    private static void PrintPadTemplates(ElementFactory factory, Element element)
    {
        Console.WriteLine($"Pad templates for {factory.GetMetadata("long-name") ?? factory.Name}:");

        if (factory.GetNumPadTemplates() == 0)
        {
            Console.WriteLine("  none");
            Console.WriteLine();
            return;
        }

        foreach (PadTemplate template in element.GetPadTemplateList())
        {
            // name-template, direction and presence are construct-only
            // properties of every GstPadTemplate. Reading them through the
            // GValue bridge is what a binding does where C reads a struct
            // field. The name is also the object name of the template, so
            // template.Name says the same thing as the first of the three.
            string name = StringProperty(template, "name-template");
            PadDirection direction = (PadDirection)EnumProperty(template, "direction");
            PadPresence presence = (PadPresence)EnumProperty(template, "presence");

            Console.WriteLine($"  {direction.ToString().ToUpperInvariant()} template: \"{name}\"");
            Console.WriteLine($"    Availability: {presence}");
            Console.WriteLine("    Capabilities:");

            using Caps caps = template.GetCaps();
            PrintCaps(caps, "      ");
            Console.WriteLine();
        }
    }

    /// <summary>
    /// Prints the caps that are currently on a pad of an element.
    /// </summary>
    /// <param name="element">The element whose pad is read.</param>
    /// <param name="padName">The name of the pad.</param>
    private static void PrintPadCapabilities(Element element, string padName)
    {
        // A pad is a GObject wrapper, interned and shared, so this is not
        // disposed where the C program calls gst_object_unref.
        Pad? pad = element.GetStaticPad(padName);

        if (pad is null)
        {
            Console.Error.WriteLine($"BasicTutorial06: there is no \"{padName}\" pad.");
            return;
        }

        // What the pad settled on, or — before it settled on anything — what it
        // would accept. Both wrappers own a reference, so both are released
        // here; the C program has one gst_caps_unref for the two paths.
        using Caps caps = pad.GetCurrentCaps() ?? pad.QueryCaps(null);

        Console.WriteLine($"Caps for the {padName} pad:");
        PrintCaps(caps, "      ");
    }

    /// <summary>
    /// Prints caps the way gst-inspect-1.0 does: one line per structure, one
    /// line per field.
    /// </summary>
    /// <param name="caps">The caps to print.</param>
    /// <param name="prefix">The indentation of every line.</param>
    private static void PrintCaps(Caps caps, string prefix)
    {
        if (caps.IsAny())
        {
            Console.WriteLine($"{prefix}ANY");
            return;
        }

        if (caps.IsEmpty())
        {
            Console.WriteLine($"{prefix}EMPTY");
            return;
        }

        for (uint i = 0; i < caps.GetSize(); i++)
        {
            // A copy of the structure, not a window into the caps.
            using Structure structure = caps.GetStructure(i);

            Console.WriteLine($"{prefix}{structure.GetName()}");

            for (uint field = 0; field < (uint)structure.NFields(); field++)
            {
                string name = structure.NthFieldName(field);

                // The field may hold an integer, a fraction, a list of
                // formats or a range. gst_value_serialize prints all of them,
                // and reading the field as a GValue is the only way to hand it
                // one.
                using Gst.GObject.Value value = structure.GetValue(name);

                Console.WriteLine($"{prefix}  {name,15}: {Global.ValueSerialize(value) ?? "?"}");
            }
        }
    }

    /// <summary>
    /// Reads a string property of an object.
    /// </summary>
    /// <param name="instance">The object to read from.</param>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value of the property.</returns>
    /// <remarks>
    /// GetProperty hands out a value the caller owns, so it is released here;
    /// that is the same rule every boxed wrapper of the binding follows.
    /// </remarks>
    private static string StringProperty(Gst.GObject.Object instance, string name)
    {
        using Gst.GObject.Value value = instance.GetProperty(name);
        return value.GetString() ?? "?";
    }

    /// <summary>
    /// Reads an enumeration property of an object.
    /// </summary>
    /// <param name="instance">The object to read from.</param>
    /// <param name="name">The name of the property.</param>
    /// <returns>The value of the property, as the integer the enumeration is.</returns>
    private static int EnumProperty(Gst.GObject.Object instance, string name)
    {
        using Gst.GObject.Value value = instance.GetProperty(name);
        return value.GetEnum();
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets a value indicating whether the run needs no sound card.</summary>
        internal bool Headless { get; private set; }

        /// <summary>Gets the factory of the sink to inspect and build.</summary>
        internal string Sink => Headless ? "fakesink" : "autoaudiosink";

        /// <summary>Gets how long the pipeline may take.</summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(20);

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
