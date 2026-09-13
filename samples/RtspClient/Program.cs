// The RTSP client: rtspsrc plays one mount point and the audio it receives is
// counted at an appsink. Without a URL the sample also hosts the mount it
// plays, so that it runs on a machine with no server on it.
//
// Usage: RtspClient [<rtsp url>] [--buffers <count>] [--timeout <seconds>]
//                   [--latency <milliseconds>] [--tcp]
//                   [--native-path <directory>] [--flavor msvc|mingw]
//                   [--help|-h]
//
// What it shows: the three things a client on rtspsrc has to get right.
// "select-stream" is a signal with a return value, connected by name through
// the generic surface, and what it answers decides whether the stream is set
// up at all — this sample takes the L16 stream and declines everything else.
// The pads of what it took appear while the pipeline is already running, so
// the receiving branch is built in "pad-added" and linked there. And the NULL
// transition is two RTSP requests, not one: PLAYING to PAUSED sends PAUSE and
// PAUSED to READY sends TEARDOWN, then READY to NULL joins the task of the
// element, which is why the summary is printed after the pipeline has stopped.
//
// Four kinds of thread run application code here, which is why every piece of
// shared state below is an Interlocked counter or a volatile field and why no
// handler holds a lock while it calls back into GStreamer:
//
//   * rtspsrc runs a task of its own for the RTSP conversation, and that is
//     where "select-stream" is called;
//   * "pad-added" and the appsink's "new-sample" arrive on streaming threads;
//   * in loopback mode the connection of the client is served on a thread of
//     the RTSP server's pool, which is where the TEARDOWN above is answered;
//   * the thread that started the run polls the bus, reads the counter and, in
//     loopback mode, iterates the context the server accepts on.
//
// There is no GMainLoop. The only main context in this sample is the one the
// loopback server is attached to, and the sample owns it and iterates it
// itself between two bus polls, exactly as samples/RtspServer does; everything
// else the run has to know arrives as a bus message.
//
// That loopback server is samples/RtspServer in miniature: the same test tone
// behind the same rtpL16pay mount, minus its options. Its shutdown here is the
// same four steps in the same order, and that sample is where they are
// documented. The order of the whole teardown matters once more: the pipeline
// goes to NULL first, on this thread, because the pool thread of the server is
// what answers its TEARDOWN — the server may only be taken down afterwards.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;
using Gst.RtspServer;

return RtspClientSample.Run(args);

/// <summary>The sample.</summary>
internal static class RtspClientSample
{
    /// <summary>The factories every run needs.</summary>
    private static readonly string[] RequiredElements =
    [
        "rtspsrc",
        "rtpL16depay",
        "audioconvert",
        "appsink",
    ];

    /// <summary>The factories the loopback server needs on top of those.</summary>
    private static readonly string[] LoopbackElements =
    [
        "audiotestsrc",
        "rtpL16pay",
    ];

    /// <summary>
    /// Reads the command line, checks the installation and plays the mount.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 when the requested number of buffers arrived, 1 otherwise.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        Session? session = null;

        try
        {
            Options options;

            try
            {
                options = Options.Parse(arguments);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"RtspClient: {exception.Message}");
                PrintUsage(Console.Error);
                return 1;
            }

            if (options.Help)
            {
                PrintUsage(Console.Out);
                return 0;
            }

            // The appsink the received audio is counted at is a type of the app
            // module, and this call ends in the same GstSharp.Initialize every
            // other sample makes. The RTSP server module needs no call of its
            // own: samples/RtspServer initialises nothing but GstSharp either.
            GstApp.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");

            if (Missing(options.Url is null) is { Count: > 0 } missing)
            {
                Console.Error.WriteLine(
                    $"RtspClient: this installation has no {string.Join(", ", missing)}. " +
                    "The sample needs the rtsp and rtp elements of gst-plugins-good and the " +
                    "appsink and audioconvert of gst-plugins-base.");
                return 1;
            }

            session = new Session(options);
            return session.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RtspClient: {exception}");
            return 1;
        }
        finally
        {
            session?.Dispose();
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Names the factories the run needs that the registry does not have.
    /// </summary>
    /// <param name="loopback">Whether the sample hosts the mount itself.</param>
    /// <returns>The missing names, empty when the installation is complete.</returns>
    private static List<string> Missing(bool loopback)
    {
        List<string> missing = [];

        foreach (string name in loopback ? [.. RequiredElements, .. LoopbackElements] : RequiredElements)
        {
            // A factory is a registry singleton behind an interned wrapper, so
            // nothing here owns one and nothing releases one.
            if (ElementFactory.Find(name) is null)
            {
                missing.Add(name);
            }
        }

        return missing;
    }

    /// <summary>Prints what the sample takes.</summary>
    /// <param name="writer">Where to print it.</param>
    private static void PrintUsage(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("Usage: RtspClient [<rtsp url>] [options]");
        writer.WriteLine();
        writer.WriteLine("  Without a URL the sample hosts the mount it plays, on 127.0.0.1.");
        writer.WriteLine();
        writer.WriteLine("  --buffers <count>      How many buffers end the run. Default 100.");
        writer.WriteLine("  --timeout <seconds>    How long the run may take. Default 30.");
        writer.WriteLine("  --latency <ms>         The latency of rtspsrc. Default 200.");
        writer.WriteLine("  --tcp                  Interleave the media over TCP only.");
        writer.WriteLine("  --native-path <dir>    Where to look for the native GStreamer.");
        writer.WriteLine("  --flavor msvc|mingw    Which Windows build to look for.");
        writer.WriteLine("  --help, -h             Print this.");
    }
}

/// <summary>
/// One run: the loopback server if there is one, the pipeline, and everything
/// the handlers share.
/// </summary>
internal sealed class Session : IDisposable
{
    /// <summary>The branch the received stream is decoded and counted in.</summary>
    private const string ReceiveBranch =
        "rtpL16depay ! audioconvert ! appsink name=sink emit-signals=true sync=false";

    /// <summary>The pipeline the loopback mount point is built from.</summary>
    private const string LoopbackLaunch = "( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    /// <summary>The path the loopback factory is mounted at.</summary>
    private const string LoopbackMount = "/test";

    /// <summary>The encoding the sample knows how to receive.</summary>
    private const string Encoding = "L16";

    /// <summary>How long one poll of the bus waits.</summary>
    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

    /// <summary>How long the asynchronous half of the server shutdown may take.</summary>
    private static readonly TimeSpan ShutdownDeadline = TimeSpan.FromSeconds(10);

    private readonly Options _options;
    private readonly Stopwatch _elapsed = new();

    /// <summary>The context the loopback server accepts on, if there is one.</summary>
    private MainContext? _context;

    /// <summary>The loopback server, if there is one.</summary>
    private RTSPServer? _server;

    /// <summary>The factory of the loopback mount point.</summary>
    private RTSPMediaFactory? _factory;

    /// <summary>The identifier the attach of the loopback server answered.</summary>
    private uint _sourceId;

    /// <summary>Whether the loopback server was taken down already.</summary>
    private bool _serverStopped;

    /// <summary>The pipeline, which this class builds and therefore releases.</summary>
    private Pipeline? _pipeline;

    /// <summary>The rtspsrc of the pipeline. Interned, so it is not released here.</summary>
    private Element? _source;

    /// <summary>The sink of the receive branch. Interned as well.</summary>
    private AppSink? _sink;

    /// <summary>The sink pad of the receive branch, which every source pad is linked to.</summary>
    private Pad? _branchSink;

    private ulong _selectStreamHandler;
    private EventHandler<Element.PadAddedSignalArgs>? _padAdded;
    private AppSink.NewSampleHandler? _newSample;
    private Action<Exception>? _trap;

    private long _buffers;
    private int _branchBuilt;
    private volatile string? _failure;
    private volatile bool _fellBack;

    /// <summary>The URL the run plays.</summary>
    private string _url = string.Empty;

    /// <summary>Initialises one run.</summary>
    /// <param name="options">The command line of the run.</param>
    internal Session(Options options) => _options = options;

    /// <summary>Gets a value indicating whether the sample hosts the mount it plays.</summary>
    private bool Loopback => _options.Url is null;

    /// <summary>
    /// Hosts the mount if it has to, builds the pipeline and waits for the media.
    /// </summary>
    /// <returns>0 when the requested number of buffers arrived, 1 otherwise.</returns>
    internal int Run()
    {
        if (Loopback)
        {
            if (!StartServer())
            {
                return 1;
            }
        }
        else
        {
            _url = _options.Url!;
        }

        Console.WriteLine($"url:         {_url}");
        Console.WriteLine($"protocols:   {(_options.Tcp ? "tcp" : "the element default")}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"latency:     {_options.Latency} ms"));

        Pipeline pipeline = Pipeline.New("rtsp-client");
        _pipeline = pipeline;

        if (ElementFactory.Make("rtspsrc", "source") is not { } source)
        {
            Console.Error.WriteLine("RtspClient: the rtspsrc could not be created.");
            return 1;
        }

        _source = source;

        if (!pipeline.Add(source))
        {
            Console.Error.WriteLine("RtspClient: the pipeline did not take the rtspsrc.");
            return 1;
        }

        source.SetProperty("location", _url);
        source.SetProperty("latency", _options.Latency);

        if (_options.Tcp)
        {
            // GstRTSPLowerTrans is a flags type, and the by-name setter takes a
            // plain number for one: 4 is TCP, so nothing is tried over UDP.
            source.SetProperty("protocols", 4u);
        }

        // A handler that throws on one of the foreign threads is reported here
        // rather than lost: the trampolines of the binding catch what a handler
        // lets escape and raise this event instead of unwinding into native
        // code. The try/catch in every handler below is the first line of the
        // same defence; this one also covers what the binding itself may throw
        // while it marshals an argument.
        _trap = exception => Fail($"a callback failed: {exception.Message}");
        ExceptionTrap.UnhandledException += _trap;

        Connect(source);

        Bus bus = pipeline.GetBus();
        int status = Drive(pipeline, bus);

        // The teardown of the loopback server runs after Drive has decided what
        // it answers, so a step of it that did not finish is picked up here:
        // the buffers may all have arrived and the run still be a failure. This
        // is the one place a failure left in the slot is printed, so a step that
        // expired after Drive had already failed for another reason is logged
        // too; setting the status again is then a no-op.
        if (_failure is { } late)
        {
            Console.Error.WriteLine($"RtspClient: {late}");
            status = 1;
        }

        PrintSummary();
        return status;
    }

    /// <summary>Releases what the run built.</summary>
    public void Dispose()
    {
        // The pipeline is already at NULL when the run completed, and this is
        // the path that never reached PLAYING. It is still the first thing that
        // happens, since the loopback server below may not go away while a
        // TEARDOWN of this pipeline is in flight.
        _pipeline?.SetState(State.Null);

        if (_trap is { } trap)
        {
            ExceptionTrap.UnhandledException -= trap;
            _trap = null;
        }

        if (_source is { } source)
        {
            source.RemoveHandler(_selectStreamHandler);

            if (_padAdded is { } padAdded)
            {
                source.PadAdded -= padAdded;
            }
        }

        if (_sink is { } sink && _newSample is { } newSample)
        {
            sink.NewSample -= newSample;
        }

        _pipeline?.Dispose();
        _pipeline = null;

        StopServer();

        _factory?.Dispose();
        _factory = null;
        _server?.Dispose();
        _server = null;
        _context?.Dispose();
        _context = null;
    }

    /// <summary>
    /// Hosts the mount that samples/RtspServer serves by default, on a port the
    /// operating system picks.
    /// </summary>
    /// <returns>Whether the server is accepting.</returns>
    private bool StartServer()
    {
        // The server runs on a context of this sample's own rather than on the
        // default one, and the thread that polls the bus is what iterates it.
        // See samples/RtspServer for why that is what makes the shutdown
        // expressible.
        _context = MainContext.New();
        _server = RTSPServer.New();

        _server.SetAddress("127.0.0.1");
        _server.SetService("0");

        // The mount points are the server's own and interned, so the wrapper is
        // left to the collector. See docs/ownership.md.
        RTSPMountPoints? mounts = _server.GetMountPoints();

        if (mounts is null)
        {
            Console.Error.WriteLine("RtspClient: the loopback server has no mount points.");
            return false;
        }

        _factory = RTSPMediaFactory.New();
        _factory.SetLaunch(LoopbackLaunch);

        // Shared, so a second client would be given the same media rather than
        // a second test tone. There is only one client here, but this is what
        // the server sample does and the difference is worth keeping.
        _factory.SetShared(true);

        mounts.AddFactory(LoopbackMount, _factory);

        _sourceId = _server.Attach(_context);

        if (_sourceId == 0)
        {
            Console.Error.WriteLine("RtspClient: nothing could be bound on 127.0.0.1.");
            return false;
        }

        _url = string.Create(
            CultureInfo.InvariantCulture,
            $"rtsp://127.0.0.1:{_server.GetBoundPort()}{LoopbackMount}");

        return true;
    }

    /// <summary>
    /// Runs the four documented steps that stop a server, once the pipeline is
    /// at NULL.
    /// </summary>
    /// <remarks>
    /// The same order as samples/RtspServer, which is where each step is
    /// explained. A step that does not finish is a message and a failure, not
    /// an exception: the run may already have its buffers.
    /// </remarks>
    private void StopServer()
    {
        if (_server is not { } server || _context is not { } context || _serverStopped)
        {
            return;
        }

        _serverStopped = true;

        // 1. Stop accepting, with the very context the attach was given.
        if (_sourceId != 0 && !server.Detach(_sourceId, context))
        {
            Fail("the source of the loopback server was already gone.");
            return;
        }

        // 2. Close every connection. The close itself completes later, on the
        //    pool thread of the client.
        DisposeAll(server.ClientFilter(null));
        server.ClientFilter(static (_, _) => RTSPFilterResult.Remove);

        // 3. Drop the sessions: it is the session going away that unprepares
        //    the media and stops its pipeline.
        RTSPSessionPool? sessionPool = server.GetSessionPool();
        sessionPool?.Filter(static (_, _) => RTSPFilterResult.Remove);

        // 4. Wait for the asynchronous half of the close, disposing what each
        //    poll answers and pumping the context while it runs.
        Stopwatch elapsed = Stopwatch.StartNew();

        while (DisposeAll(server.ClientFilter(null)) > 0)
        {
            if (elapsed.Elapsed > ShutdownDeadline)
            {
                Fail("a client of the loopback server was still managed after the filter removed it.");
                return;
            }

            Pump();
            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Disposes every wrapper of a transfer full list and answers how many
    /// there were.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the list.</typeparam>
    /// <param name="owned">The list a filter answered.</param>
    /// <returns>The number of items the list held.</returns>
    private static int DisposeAll<T>(IReadOnlyList<T> owned)
        where T : Gst.GObject.Object
    {
        foreach (T item in owned)
        {
            item.Dispose();
        }

        return owned.Count;
    }

    /// <summary>
    /// Dispatches whatever the context of the loopback server holds, without
    /// blocking, and drains the wrappers the pool thread minted.
    /// </summary>
    private void Pump()
    {
        if (_context is not { } context)
        {
            return;
        }

        while (context.Iteration(false))
        {
        }

        GstSharp.DrainPendingReleases();
    }

    /// <summary>
    /// Connects the two handlers that decide what the run receives.
    /// </summary>
    /// <param name="source">The rtspsrc of the pipeline.</param>
    private void Connect(Element source)
    {
        // A signal with a return value, connected by name: what the handler
        // answers is what decides whether the stream is set up. Anything but
        // the L16 stream is declined, so no other pad is ever added.
        _selectStreamHandler = source.ConnectSignal("select-stream", (_, arguments) => OnSelectStream(arguments));

        _padAdded = (_, arguments) => OnPadAdded(arguments.NewPad);
        source.PadAdded += _padAdded;
    }

    /// <summary>
    /// Decides whether one announced stream is set up.
    /// </summary>
    /// <param name="arguments">The index of the stream and the caps it announced.</param>
    /// <returns>The boxed answer of the signal.</returns>
    /// <remarks>
    /// This runs on the task thread of rtspsrc. The caps are borrowed: the
    /// emission owns them and the binding disposes the wrapper once this
    /// returns, so nothing here disposes them. A failure answers false, which
    /// is also what a null would have meant.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An exception may not unwind into the emission; the poll loop reports it.")]
    private object OnSelectStream(object?[] arguments)
    {
        try
        {
            uint index = arguments is [uint value, ..] ? value : 0;
            string media = "?";
            string encoding = "?";

            if (arguments is [_, Caps caps, ..] && caps.GetSize() != 0)
            {
                using Structure structure = caps.GetStructure(0);
                media = structure.GetString("media") ?? "?";
                encoding = structure.GetString("encoding-name") ?? "?";
            }

            bool selected = string.Equals(encoding, Encoding, StringComparison.Ordinal);

            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"stream {index}:    media={media} encoding-name={encoding} -> " +
                $"{(selected ? "selected" : "declined")}"));

            return selected;
        }
        catch (Exception exception)
        {
            Fail($"the select-stream handler failed: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// Builds the receiving branch, or links a later pad to the one that is
    /// already there.
    /// </summary>
    /// <param name="newPad">The pad rtspsrc added.</param>
    /// <remarks>
    /// This runs on a streaming thread. When no UDP arrives within its timeout,
    /// rtspsrc closes the session and reopens it over TCP, but it only does so
    /// while no source pad has been added yet: select-stream runs again and the
    /// pads then arrive here for the first time, after the reconnect. No pad is
    /// removed and added back. Linking a later pad to the branch that is
    /// already there is a guard for that arrangement rather than the expected
    /// path, and a further pad is never an error.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An exception may not unwind into the emission; the poll loop reports it.")]
    private void OnPadAdded(Pad newPad)
    {
        try
        {
            if (newPad.Direction != PadDirection.Src)
            {
                return;
            }

            string media = "?";
            string encoding = "?";

            using (Caps? caps = newPad.GetCurrentCaps())
            {
                if (caps is not null && caps.GetSize() != 0)
                {
                    using Structure structure = caps.GetStructure(0);
                    media = structure.GetString("media") ?? "?";
                    encoding = structure.GetString("encoding-name") ?? "?";
                }
            }

            if (!string.Equals(encoding, Encoding, StringComparison.Ordinal))
            {
                // select-stream declined everything else, so this cannot happen
                // against a server that honours it. It is reported and left
                // alone rather than made a failure.
                Console.WriteLine($"pad:         \"{newPad.Name}\" carries {media}/{encoding}, which is not received.");
                return;
            }

            Console.WriteLine($"pad:         \"{newPad.Name}\" carries {media}/{encoding}.");

            if (Interlocked.Exchange(ref _branchBuilt, 1) == 0 && !BuildBranch())
            {
                return;
            }

            if (_branchSink is not { } sinkPad)
            {
                Fail("the receiving branch has no sink pad.");
                return;
            }

            PadLinkReturn linked = newPad.Link(sinkPad);

            if (linked != PadLinkReturn.Ok)
            {
                Fail($"linking \"{newPad.Name}\" to the receiving branch answered {linked}.");
            }
        }
        catch (Exception exception)
        {
            Fail($"the pad-added handler failed: {exception.Message}");
        }
    }

    /// <summary>
    /// Builds the receiving branch and puts it into the running pipeline.
    /// </summary>
    /// <returns>Whether the branch is in the pipeline and playing.</returns>
    private bool BuildBranch()
    {
        // The bin is built here, so it is disposed here; the pipeline takes a
        // reference of its own when it takes the bin in. The parse ghosts the
        // one unlinked pad of the bin, which is the sink of the depayloader.
        using Bin branch = Global.ParseBinFromDescription(ReceiveBranch, ghostUnlinkedPads: true);

        if (!_pipeline!.Add(branch))
        {
            Fail("the pipeline did not take the receiving branch.");
            return false;
        }

        if (branch.GetByName("sink") is not AppSink sink)
        {
            Fail("the receiving branch has no appsink.");
            return false;
        }

        _sink = sink;
        _newSample = OnNewSample;
        sink.NewSample += _newSample;

        branch.SyncStateWithParent();

        // Kept for the length of the run, so that a later L16 source pad can be
        // linked to this very pad. That is a guard rather than the expected
        // path: a reconnect over TCP happens before any pad was added.
        _branchSink = branch.GetStaticPad("sink");

        return true;
    }

    /// <summary>
    /// Counts one received buffer.
    /// </summary>
    /// <param name="sender">The sink that has one.</param>
    /// <param name="arguments">The arguments of the emission, which carries none.</param>
    /// <returns>What the sink is told about the flow.</returns>
    /// <remarks>
    /// This runs on the streaming thread of the sink, and the sample pulls what
    /// the signal announced: without the pull the sink would hold every sample
    /// it ever announced.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "An exception may not unwind into the emission; the poll loop reports it.")]
    private FlowReturn OnNewSample(object? sender, EventArgs arguments)
    {
        try
        {
            using Sample? sample = _sink!.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            Interlocked.Increment(ref _buffers);
            return FlowReturn.Ok;
        }
        catch (Exception exception)
        {
            Fail($"the new-sample handler failed: {exception.Message}");
            return FlowReturn.Error;
        }
    }

    /// <summary>
    /// Plays the mount and waits for the buffers, the bus or the deadline.
    /// </summary>
    /// <param name="pipeline">The pipeline of the run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <returns>0 when the requested number of buffers arrived, 1 otherwise.</returns>
    private int Drive(Pipeline pipeline, Bus bus)
    {
        _elapsed.Start();

        // Asynchronous is the normal answer: rtspsrc has not spoken to the
        // server yet when this returns.
        if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            Console.Error.WriteLine("RtspClient: the pipeline refused to go to PLAYING.");
            return 1;
        }

        try
        {
            while (_elapsed.Elapsed < _options.Timeout)
            {
                if (_failure is not null)
                {
                    // Run prints it, once, after the teardown.
                    return 1;
                }

                if (Interlocked.Read(ref _buffers) >= _options.Buffers)
                {
                    return 0;
                }

                // The accept source of the loopback server lives on this
                // context. The connection it accepts is served on a thread of
                // the pool, so this is the only part of the server this thread
                // is responsible for.
                Pump();

                using Message? message = bus.TimedPopFiltered(
                    PollInterval,
                    MessageType.Error | MessageType.Eos | MessageType.Warning);

                if (message is null)
                {
                    continue;
                }

                if (message.Type == MessageType.Warning)
                {
                    // The fallback from UDP to TCP is a warning and a reconnect,
                    // not a failure, so it is printed and the run goes on.
                    (GException warning, string? warningDebug) = message.ParseWarning();
                    Console.WriteLine($"warning:     {message.SourceName ?? "?"}: {warning.Message}");
                    Console.WriteLine($"debug:       {warningDebug ?? "none"}");

                    // The one warning the summary cares about says it is
                    // retrying over TCP; anything else is just printed.
                    if (warning.Message.Contains("tcp", StringComparison.OrdinalIgnoreCase))
                    {
                        _fellBack = true;
                    }

                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                    return 1;
                }

                Console.Error.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"RtspClient: the stream ended after {Interlocked.Read(ref _buffers)} buffers."));
                return 1;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"RtspClient: {Interlocked.Read(ref _buffers)} of {_options.Buffers} buffers " +
                $"within {_options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // The NULL transition is what sends PAUSE and then TEARDOWN and
            // what joins the task of rtspsrc, so it runs here, on this thread,
            // while the loopback server is still up: the pool thread of its
            // client is what answers the TEARDOWN. Only then is the server
            // taken down.
            pipeline.SetState(State.Null);
            _elapsed.Stop();
            StopServer();
        }
    }

    /// <summary>Prints what the run added up to.</summary>
    private void PrintSummary()
    {
        string protocols = _options.Tcp
            ? "tcp"
            : _fellBack ? "the element default, fell back to tcp" : "the element default";

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"buffers:     {Interlocked.Read(ref _buffers)} in {_elapsed.Elapsed.TotalMilliseconds:F0} ms " +
            $"from {_url} over {protocols}"));
    }

    /// <summary>
    /// Records the first failure of the run.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    private void Fail(string message) => _failure ??= message;
}

/// <summary>The command line of the sample.</summary>
internal sealed class Options
{
    /// <summary>Gets a value indicating whether the usage was asked for.</summary>
    internal bool Help { get; private set; }

    /// <summary>Gets the URL to play, or null when the sample hosts one itself.</summary>
    internal string? Url { get; private set; }

    /// <summary>Gets how many received buffers end the run.</summary>
    internal long Buffers { get; private set; } = 100;

    /// <summary>Gets how long the run may take.</summary>
    internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the latency of rtspsrc in milliseconds.</summary>
    internal uint Latency { get; private set; } = 200;

    /// <summary>Gets a value indicating whether the media is interleaved over TCP only.</summary>
    internal bool Tcp { get; private set; }

    /// <summary>Gets the options of the native loader.</summary>
    internal GstSharpOptions Native { get; } = new();

    /// <summary>Reads the command line.</summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>The parsed options.</returns>
    /// <exception cref="ArgumentException">An argument is unknown, incomplete or out of range.</exception>
    internal static Options Parse(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Options options = new();

        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "-h" or "--help":
                    options.Help = true;
                    break;

                case "--buffers":
                    options.Buffers = Positive(arguments[i], ValueOf(arguments, ref i));
                    break;

                case "--timeout":
                    options.Timeout = TimeSpan.FromSeconds(
                        Positive(arguments[i], ValueOf(arguments, ref i)));
                    break;

                case "--latency":
                    options.Latency = (uint)NonNegative(arguments[i], ValueOf(arguments, ref i));
                    break;

                case "--tcp":
                    options.Tcp = true;
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

                default:
                    if (arguments[i].StartsWith('-'))
                    {
                        throw new ArgumentException(
                            $"\"{arguments[i]}\" is not a known argument.",
                            nameof(arguments));
                    }

                    if (options.Url is not null)
                    {
                        throw new ArgumentException("Only one URL can be played.", nameof(arguments));
                    }

                    options.Url = arguments[i];
                    break;
            }
        }

        return options;
    }

    /// <summary>
    /// Reads a count that has to be at least one.
    /// </summary>
    /// <param name="option">The option the value belongs to.</param>
    /// <param name="value">The value to read.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentException">The value is not a positive number.</exception>
    /// <remarks>
    /// Zero is refused rather than accepted as "no bound": a run that ends
    /// before it started would exit 1 with nothing to say about why.
    /// </remarks>
    private static long Positive(string option, string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ||
            number < 1)
        {
            throw new ArgumentException(
                $"\"{option}\" takes a number of at least one, and \"{value}\" is not one.",
                nameof(value));
        }

        return number;
    }

    /// <summary>
    /// Reads a value that may be zero.
    /// </summary>
    /// <param name="option">The option the value belongs to.</param>
    /// <param name="value">The value to read.</param>
    /// <returns>The number.</returns>
    /// <exception cref="ArgumentException">The value is not a number, or it is negative.</exception>
    /// <remarks>
    /// The latency is the one number of this sample that means something at
    /// zero: it is what a receiver that does its own buffering asks for.
    /// </remarks>
    private static long NonNegative(string option, string value)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ||
            number < 0 ||
            number > uint.MaxValue)
        {
            throw new ArgumentException(
                $"\"{option}\" takes a number of at least zero, and \"{value}\" is not one.",
                nameof(value));
        }

        return number;
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
