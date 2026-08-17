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

            GstSharp.Initialize(options);

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

            if (!RunManagedSubclass())
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
    /// the whole subclassing path: the registration, the shared
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
