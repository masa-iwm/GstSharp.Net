// The port of gst-rtsp-server's examples/test-launch.c: an RTSP server that
// serves one mount point built from a gst-launch description, and that shuts
// itself down in the documented order when it is asked to stop.
//
// Usage: RtspServer [<launch line>] [--port <port>] [--address <address>]
//                   [--native-path <directory>] [--flavor msvc|mingw]
//                   [--timeout <seconds>]
//
// Without a launch line it serves a test tone, so that the sample runs on a
// machine with no media on it. With --timeout 0, which is the default, it
// serves until Ctrl-C.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.GLib;
using Gst.Interop;
using Gst.RtspServer;

return RtspServerSample.Run(args);

internal static class RtspServerSample
{
    /// <summary>The pipeline the mount point is built from by default.</summary>
    private const string DefaultLaunch = "( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    /// <summary>The path the factory is mounted at.</summary>
    private const string Mount = "/test";

    /// <summary>How long the asynchronous half of the shutdown may take.</summary>
    private static readonly TimeSpan ShutdownDeadline = TimeSpan.FromSeconds(10);

    /// <summary>How many media the factory has configured so far.</summary>
    private static int _configured;

    /// <summary>How many clients the server has accepted so far.</summary>
    private static int _connected;

    /// <summary>Whether Ctrl-C was seen.</summary>
    private static volatile bool _stopping;

    /// <summary>
    /// Builds the server, serves the mount point and shuts it down again.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 on a clean shutdown, 1 on any failure.</returns>
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
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");
            Console.WriteLine($"launch:      {options.Launch}");

            // The server runs on a context of this sample's own rather than on
            // the default one, and this thread is what iterates it. That is
            // the same arrangement as the other samples here - the application
            // owns its thread and no main loop runs behind its back - and it
            // is what makes the shutdown below expressible: Detach needs the
            // very context Attach was given, and the wait for the clients to
            // finish closing needs a context that is still being iterated
            // after the server has stopped accepting.
            using MainContext context = MainContext.New();
            using RTSPServer server = RTSPServer.New();

            server.SetAddress(options.Address);
            server.SetService(options.Port);
            server.ClientConnected += OnClientConnected;

            using RTSPMountPoints? mounts = server.GetMountPoints();
            if (mounts is null)
            {
                Console.Error.WriteLine("RtspServer: the server has no mount points.");
                return 1;
            }

            using RTSPMediaFactory factory = RTSPMediaFactory.New();
            factory.SetLaunch(options.Launch);
            factory.SetShared(true);

            // Connected before the mount, the way test-launch.c does it. The
            // hand written AddFactory is what makes that hold: it mints the
            // reference the C call consumes and leaves this wrapper - and this
            // handler with it - alive. See docs/ownership.md.
            factory.MediaConfigure += OnMediaConfigure;

            mounts.AddFactory(Mount, factory);

            uint sourceId = server.Attach(context);
            if (sourceId == 0)
            {
                Console.Error.WriteLine($"RtspServer: nothing could be bound on {options.Address}:{options.Port}.");
                return 1;
            }

            // With --port 0 the service is picked by the operating system, so
            // the port that is announced is the one that was bound.
            string host = options.Address is "0.0.0.0" or "::" ? "127.0.0.1" : options.Address;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"stream:      rtsp://{host}:{server.GetBoundPort()}{Mount}"));

            return Serve(server, context, sourceId, options.Timeout);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RtspServer: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Iterates the context of the server until Ctrl-C or the timeout, then
    /// shuts the server down.
    /// </summary>
    /// <param name="server">The server that is attached.</param>
    /// <param name="context">The context <paramref name="server"/> is attached to.</param>
    /// <param name="sourceId">The identifier <c>Attach</c> answered.</param>
    /// <param name="timeout">How long to serve, or zero to serve until Ctrl-C.</param>
    /// <returns>0 on a clean shutdown, 1 when a client never finished closing.</returns>
    private static int Serve(RTSPServer server, MainContext context, uint sourceId, TimeSpan timeout)
    {
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            Console.WriteLine(timeout == TimeSpan.Zero
                ? "serving:     until Ctrl-C"
                : string.Create(CultureInfo.InvariantCulture, $"serving:     for {timeout.TotalSeconds:F0} s"));

            Stopwatch elapsed = Stopwatch.StartNew();

            while (!_stopping && (timeout == TimeSpan.Zero || elapsed.Elapsed < timeout))
            {
                Pump(context);
                Thread.Sleep(5);
            }

            return Shutdown(server, context, sourceId);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
        }
    }

    /// <summary>
    /// Runs the documented steps that stop a server.
    /// </summary>
    /// <param name="server">The server that is attached.</param>
    /// <param name="context">The context <paramref name="server"/> is attached to.</param>
    /// <param name="sourceId">The identifier <c>Attach</c> answered.</param>
    /// <returns>0 on a clean shutdown, 1 when a client never finished closing.</returns>
    private static int Shutdown(RTSPServer server, MainContext context, uint sourceId)
    {
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"shutdown:    {Volatile.Read(ref _connected)} client(s), {Volatile.Read(ref _configured)} media configured"));

        // 1. Stop accepting. Detach takes the context Attach was given:
        //    g_source_remove would search the default context only.
        if (!server.Detach(sourceId, context))
        {
            Console.Error.WriteLine("RtspServer: the source of the server was already gone.");
            return 1;
        }

        // 2. Close every connection. The close itself completes later, on the
        //    thread of the client. The filter answers the clients it was asked
        //    to reference, not the ones it removed, so the count is taken
        //    first, with the null filter that only lists them.
        int clients = server.ClientFilter(null).Count;
        server.ClientFilter(static (_, _) => RTSPFilterResult.Remove);

        // 3. Drop the sessions: closing a client does not remove its session,
        //    and it is the session going away that unprepares the media and
        //    stops its pipeline.
        using RTSPSessionPool? sessionPool = server.GetSessionPool();
        sessionPool?.Filter(static (_, _) => RTSPFilterResult.Remove);

        // 4. Wait for the asynchronous half of the close, while still
        //    iterating the context the clients were dispatched on.
        Stopwatch elapsed = Stopwatch.StartNew();
        while (server.ClientFilter(null).Count > 0)
        {
            if (elapsed.Elapsed > ShutdownDeadline)
            {
                Console.Error.WriteLine("RtspServer: a client was still managed after the filter removed it.");
                return 1;
            }

            Pump(context);
            Thread.Sleep(5);
        }

        // 5. RTSPThreadPool.Cleanup is deliberately not called: it joins every
        //    thread of the process wide pool and blocks forever when a client
        //    is still closing. The process is ending anyway, and a media
        //    thread stops itself once the session media that owns it is
        //    finalised, which step 3 above makes happen.
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"stopped:     {clients} client(s) closed in {elapsed.Elapsed.TotalSeconds:F2} s"));

        return 0;
    }

    /// <summary>
    /// Dispatches whatever the context of the server holds, without blocking.
    /// </summary>
    /// <param name="context">The context of the server.</param>
    /// <remarks>
    /// The iteration may not block: Ctrl-C and the timeout are both decided
    /// outside the context and would not wake a blocking iteration. The sleep
    /// of the callers is what keeps the loop from spinning. Draining here is
    /// what an application that runs neither the default context nor a main
    /// loop owes the wrapper cache: the handler arguments below are wrappers
    /// minted on a thread of the pool.
    /// </remarks>
    private static void Pump(MainContext context)
    {
        while (context.Iteration(false))
        {
        }

        GstSharp.DrainPendingReleases();
    }

    /// <summary>
    /// Notes a media the factory has just configured.
    /// </summary>
    /// <param name="sender">The factory of the mount point.</param>
    /// <param name="arguments">The media that was configured.</param>
    /// <remarks>
    /// This runs on a thread of the pool with the lock of the media held, so
    /// it must not call <c>Lock()</c>, <c>Construct()</c> or <c>Prepare()</c>,
    /// and it asks the media nothing at all, because the calls that would
    /// answer take that same lock.
    /// </remarks>
    private static void OnMediaConfigure(object? sender, RTSPMediaFactory.MediaConfigureSignalArgs arguments)
        => Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"media:       configured, {Interlocked.Increment(ref _configured)} so far"));

    /// <summary>
    /// Notes a client the server has just accepted.
    /// </summary>
    /// <param name="sender">The server.</param>
    /// <param name="arguments">The client that connected.</param>
    /// <remarks>
    /// This one runs on the thread that iterates the attached context, which
    /// is the thread of <see cref="Serve"/>: the signal is emitted before the
    /// client is handed to the pool.
    /// </remarks>
    private static void OnClientConnected(object? sender, RTSPServer.ClientConnectedSignalArgs arguments)
        => Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"client:      connected, {Interlocked.Increment(ref _connected)} so far"));

    /// <summary>
    /// Turns Ctrl-C into a request to shut down instead of into an exit.
    /// </summary>
    /// <param name="sender">The console.</param>
    /// <param name="arguments">The key press.</param>
    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs arguments)
    {
        arguments.Cancel = true;
        _stopping = true;
    }

    /// <summary>
    /// The command line of the sample.
    /// </summary>
    private sealed class Options
    {
        /// <summary>Gets the description the mount point is built from.</summary>
        internal string Launch { get; private set; } = DefaultLaunch;

        /// <summary>Gets the service to listen on, as a port or a name.</summary>
        internal string Port { get; private set; } = "8554";

        /// <summary>Gets the address to listen on.</summary>
        internal string Address { get; private set; } = "0.0.0.0";

        /// <summary>Gets how long to serve, or zero to serve until Ctrl-C.</summary>
        internal TimeSpan Timeout { get; private set; }

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
            bool launched = false;

            for (int i = 0; i < arguments.Length; i++)
            {
                switch (arguments[i])
                {
                    case "--port":
                        options.Port = ValueOf(arguments, ref i);
                        break;

                    case "--address":
                        options.Address = ValueOf(arguments, ref i);
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
                        if (arguments[i].StartsWith("--", StringComparison.Ordinal) || launched)
                        {
                            throw new ArgumentException(
                                $"\"{arguments[i]}\" is not a known argument.",
                                nameof(arguments));
                        }

                        options.Launch = arguments[i];
                        launched = true;
                        break;
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
