// Basic tutorial 5: GUI toolkit integration — a window with a video area, the
// transport buttons, a seek slider and the tags of every stream.
//
// Ported from basic-tutorial-5.c of the GStreamer project, which is
// tri-licensed BSD-2-Clause / MIT / LGPL-2.1-or-later. The walkthrough that
// explains what the program does is upstream and is not reproduced here:
// https://gstreamer.freedesktop.org/documentation/tutorials/basic/toolkit-integration.html
//
// Usage: BasicTutorial05 [<uri-or-file>] [--headless-selftest]
//                        [--native-path <directory>] [--flavor msvc|mingw]
//                        [--timeout <seconds>]
//
// Where this port differs from the C original, and why:
//
//   * The toolkit is different, and so is the GStreamer API that carries the
//     video. Upstream 1.28 builds a glsinkbin around gtkglsink (gtksink when
//     there is no GL), reads a GtkWidget off the sink's "widget" property and
//     packs that widget into a GtkBox. That route needs GTK bindings in the
//     process, and GTK is GObject: a managed GTK binding would bring a second
//     type registry, a second set of toggle references on the same GObject and
//     a second main context into a process that already has this binding's
//     own. So this port uses the toolkit-neutral route the GStreamer
//     documentation describes instead — GstVideoOverlay, which hands the sink
//     a window handle the toolkit already owns — with Avalonia as the toolkit.
//     Same lesson, different toolkit and a different GStreamer interface; the
//     README of this directory says so at length.
//
//   * There is no GMainLoop and no gtk_main. Avalonia runs the loop, and the
//     bus is read on a DispatcherTimer tick, which is the same "poll the bus
//     where the application does its own work" shape the rest of these ports
//     use. gst_bus_add_signal_watch has no counterpart here, so error, end of
//     stream, state-changed and application messages are popped with a zero
//     timeout on each tick; the cost is that an error is seen up to one tick
//     late, which for a sample that already refreshes once a second is
//     invisible.
//
//   * The application message of the C original is kept, and kept for its
//     original reason. video-tags-changed and friends are emitted on a
//     streaming thread, so upstream posts an empty "tags-changed" structure on
//     the bus and reads the tags back on the main thread. The same trick works
//     here unchanged, because the tick that pops the bus already runs on the
//     UI thread: the bus is the thread hand-off, and no Dispatcher.UIThread.Post
//     is needed. That is why this port did not replace it with one.
//
//   * The window handle is taken from an Avalonia NativeControlHost — HWND on
//     Windows, an X11 XID on Linux, an NSView on macOS — and given to playbin
//     from a sync bus handler, when the sink posts prepare-window-handle. The
//     C original never needs this because the sink it builds is a widget. See
//     PlayerWindow for why the handler has to be a sync one.
//
//   * --headless-selftest is not part of the tutorial. It opens no window and
//     starts no toolkit: it initializes GStreamer, makes a playbin, asserts
//     that the playbin proxies GstVideoOverlay and exits. That is what CI runs,
//     because none of the runners has a display. --timeout is the other
//     addition: with no window to close by hand, it bounds a run.
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Gst;
using Gst.Interop;
using Gst.Video;

return ToolkitIntegration.Run(args);

internal static class ToolkitIntegration
{
    /// <summary>The media of the upstream tutorial.</summary>
    internal const string DefaultUri =
        "https://gstreamer.freedesktop.org/data/media/sintel_trailer-480p.webm";

    /// <summary>
    /// The command line, which the window reads when the toolkit builds it.
    /// </summary>
    internal static Options Current { get; private set; } = new();

    /// <summary>
    /// What the process exits with. The window raises it to 1 when the bus
    /// posted an error, which is the rule every tutorial in this directory
    /// follows.
    /// </summary>
    internal static int ExitCode { get; set; }

    /// <summary>
    /// Plays the media in a window of its own.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the window was closed with no error on the bus.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            Options options = Options.Parse(arguments);
            Current = options;

            // GstVideo.Initialize rather than GstSharp.Initialize: it is a call
            // into the GstVideo assembly, which is what makes sure that its
            // module initialiser has run and that GstVideoOverlay is a type the
            // registry can cast an element to.
            GstVideo.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");

            if (options.SelfTest)
            {
                return SelfTest();
            }

            Console.WriteLine($"uri:         {options.Uri}");

            int lifetime = StartToolkit(arguments);
            return lifetime != 0 ? lifetime : ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"BasicTutorial05: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Asserts what this tutorial rests on, without a display and without the
    /// toolkit.
    /// </summary>
    /// <returns>0 when the assertion held.</returns>
    /// <remarks>
    /// The assertion is on playbin and not on a concrete sink on purpose.
    /// playbin implements GstVideoOverlay itself and proxies it to whatever
    /// video sink it ends up building, so this needs no display, no plugin
    /// beyond playback and no per-operating-system branch — where an assertion
    /// on, say, <c>d3d11videosink</c> would not even find the factory on four
    /// of the six CI legs.
    /// </remarks>
    private static int SelfTest()
    {
        using Element? playbin = ElementFactory.Make("playbin", "playbin");

        if (playbin is null)
        {
            Console.Error.WriteLine("BasicTutorial05: playbin could not be created.");
            return 1;
        }

        bool proxied = playbin.As<IVideoOverlay>() is not null;
        Console.WriteLine($"selftest:    playbin proxies GstVideoOverlay = {proxied}");

        return proxied ? 0 : 1;
    }

    /// <summary>
    /// Hands the process over to Avalonia.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>What the desktop lifetime exited with.</returns>
    /// <remarks>
    /// This is a method of its own so that the self test never touches a type
    /// of the toolkit: nothing of Avalonia is loaded on the path CI runs.
    /// </remarks>
    private static int StartToolkit(string[] arguments) =>
        AppBuilder.Configure<TutorialApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .StartWithClassicDesktopLifetime(arguments);

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    internal sealed class Options
    {
        /// <summary>Gets the media to play.</summary>
        internal string Uri { get; private set; } = DefaultUri;

        /// <summary>Gets a value indicating whether no window is opened.</summary>
        internal bool SelfTest { get; private set; }

        /// <summary>
        /// Gets how long the run may take, or <see cref="TimeSpan.Zero"/> when
        /// nothing but the window closing ends it.
        /// </summary>
        internal TimeSpan Timeout { get; private set; } = TimeSpan.Zero;

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
                    case "--headless-selftest":
                        options.SelfTest = true;
                        break;

                    case "--native-path":
                        options.Native.NativeSearchPath = Cli.ValueOf(arguments, ref i);
                        break;

                    case "--flavor":
                        options.Native.WindowsFlavor = Cli.FlavorOf(Cli.ValueOf(arguments, ref i));
                        break;

                    case "--timeout":
                        options.Timeout = Cli.SecondsOf(arguments, ref i);
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
/// The application object the toolkit needs. It exists to install the theme —
/// a window built in code has no control templates without one — and to hand
/// the desktop lifetime its main window.
/// </summary>
internal sealed class TutorialApp : Application
{
    /// <inheritdoc/>
    public override void Initialize() => Styles.Add(new FluentTheme());

    /// <inheritdoc/>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new PlayerWindow();
        }

        base.OnFrameworkInitializationCompleted();
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
    /// Reads a number of seconds that follows an option.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <param name="index">The index of the option, advanced to its value.</param>
    /// <returns>The duration.</returns>
    internal static TimeSpan SecondsOf(string[] arguments, ref int index) =>
        TimeSpan.FromSeconds(double.Parse(ValueOf(arguments, ref index), CultureInfo.InvariantCulture));

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
