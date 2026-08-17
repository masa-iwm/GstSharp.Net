using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Gst;
using Gst.GLib;
using Gst.GObject;

/// <summary>
/// The run of one pipeline: the bus loop of <c>gst-launch-1.0</c> and the state
/// machine around it, ported onto a polled bus.
/// </summary>
/// <remarks>
/// The fields below are the static variables of <c>gst-launch.c</c> — the
/// target state, whether the pipeline prerolled, whether it is buffering, live,
/// in progress, waiting for an end of stream — because the behavior of the tool
/// is what those variables do to each other.
/// </remarks>
internal sealed partial class Launcher
{
    /// <summary>A state change that failed, which is <c>-1</c> in the C tool.</summary>
    private const int ExitStateChangeFailure = -1;

    /// <summary>The pipeline ran to its end.</summary>
    private const int ExitNoError = 0;

    /// <summary>An element posted an error.</summary>
    private const int ExitError = 1;

    /// <summary>How long one poll of the bus waits.</summary>
    private static readonly ClockTime PollSlice = ClockTime.FromMilliseconds(100);

    /// <summary>How often the position line is refreshed.</summary>
    private static readonly TimeSpan PositionInterval = TimeSpan.FromMilliseconds(100);

    private readonly Options _options;
    private readonly Pipeline _pipeline;

    private State _targetState = State.Paused;
    private bool _isLive;
    private bool _buffering;
    private bool _prerolled;
    private bool _inProgress;
    private bool _waitingEos;
    private bool _interrupting;
    private bool _quit;
    private int _exitCode = ExitNoError;
    private ClockTime _startedAt = ClockTime.None;
    private int _interruptsRequested;

    /// <summary>
    /// Initialises a run.
    /// </summary>
    /// <param name="options">The command line of the process.</param>
    /// <param name="pipeline">The pipeline to run.</param>
    private Launcher(Options options, Pipeline pipeline)
    {
        _options = options;
        _pipeline = pipeline;
    }

    /// <summary>
    /// Reads the command line, initialises GStreamer and runs the pipeline.
    /// </summary>
    /// <param name="arguments">The arguments of the process.</param>
    /// <returns>
    /// The exit code of the C tool: 0 for a run that ended, 1 for an error, and
    /// -1 for a state change that failed.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The tool turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        Options options;

        try
        {
            options = Options.Parse(arguments);
        }
        catch (OptionException error)
        {
            // The wording of the option parser of GLib, which is what the C
            // tool prints here.
            Console.Error.WriteLine($"Error initializing: {error.Message}");
            return 1;
        }

        if (options.Help)
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        try
        {
            // The --gst-* arguments travel with the options and reach
            // gst_init_check from here.
            GstSharp.Initialize(options.Native);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Error initializing: {error.Message}");
            return 1;
        }

        if (options.Version)
        {
            PrintVersion();
            return 0;
        }

        try
        {
            // The C tool installs its fault handler here, after the version
            // line and before the pipeline is built.
            using IDisposable? faults = InstallFaultHandler(options);

            return Launch(options);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{Options.ProgramName}: {error}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Builds the pipeline from the description and runs it.
    /// </summary>
    /// <param name="options">The command line of the process.</param>
    /// <returns>The exit code.</returns>
    private static int Launch(Options options)
    {
        Element parsed;

        try
        {
            parsed = Global.ParseLaunch(options.Description);
        }
        catch (GException error)
        {
            Console.Error.WriteLine($"ERROR: pipeline could not be constructed: {error.Message}.");
            return 1;
        }

        // A description that holds a single element parses into that element
        // rather than into a pipeline, and the tool puts it in one.
        if (parsed is Pipeline pipeline)
        {
            using (pipeline)
            {
                return new Launcher(options, pipeline).Execute();
            }
        }

        if (ElementFactory.Make("pipeline", null) is not Pipeline wrapper)
        {
            Console.Error.WriteLine("ERROR: the 'pipeline' element wasn't found.");
            return 1;
        }

        using (wrapper)
        {
            // The element belongs to the bin now: it is not disposed here, and
            // the pipeline is, because this code built it. See
            // docs/ownership.md.
            wrapper.Add(parsed);
            return new Launcher(options, wrapper).Execute();
        }
    }

    /// <summary>
    /// Takes the pipeline to PAUSED, runs the bus loop and takes it back to
    /// NULL.
    /// </summary>
    /// <returns>The exit code.</returns>
    private int Execute()
    {
        // The bus wrapper is interned and shared with every other lookup of the
        // same bus, so it is not disposed here.
        Bus bus = _pipeline.GetBus();

        CULong watch = default;
        bool watching = false;

        // Windows only, and in the place the C tool does it: after the pipeline
        // is built and before anything else is printed.
        uint timerResolution = EnableTimerResolution();

        try
        {
            if (_options.Verbose)
            {
                // Every property that anything in the pipeline writes arrives
                // as a message on the bus, which is what makes it readable from
                // this thread rather than from a streaming thread.
                watch = _pipeline.AddPropertyDeepNotifyWatch(null, true);
                watching = true;
            }

            Print("Setting pipeline to PAUSED ...");

            switch (_pipeline.SetState(State.Paused))
            {
                case StateChangeReturn.Failure:
                    // The element that refused says why on the bus. The C tool
                    // prints it from its sync handler, so there too the error
                    // comes before the line below.
                    DrainErrors(bus);
                    Console.Error.WriteLine("Failed to set pipeline to PAUSED.");
                    _exitCode = ExitStateChangeFailure;
                    return _exitCode;

                case StateChangeReturn.NoPreroll:
                    Print("Pipeline is live and does not need PREROLL ...");
                    _isLive = true;
                    break;

                case StateChangeReturn.Async:
                    Print("Pipeline is PREROLLING ...");
                    break;

                default:
                    break;
            }

            RunLoop(bus);

            PrintExecutionTime();
            return _exitCode;
        }
        finally
        {
            // No need to see all those pad caps going to NULL etc.
            if (watching)
            {
                _pipeline.RemovePropertyNotifyWatch(watch);
            }

            // Back to NULL before anything is released: a pipeline that is
            // still PLAYING when its last reference goes away leaves its
            // streaming threads running.
            Print("Setting pipeline to NULL ...");
            _pipeline.SetState(State.Null);

            // Undo timeBeginPeriod where the C tool undoes it.
            ClearTimerResolution(timerResolution);

            Print("Freeing pipeline ...");
        }
    }

    /// <summary>
    /// Polls the bus until the pipeline ends, fails or is interrupted.
    /// </summary>
    /// <param name="bus">The bus of the pipeline.</param>
    private void RunLoop(Bus bus)
    {
        bool positions = _options.WantsPosition(out bool outputIsTty);
        Stopwatch tick = Stopwatch.StartNew();

        ConsoleCancelEventHandler cancel = OnCancelKeyPress;
        Console.CancelKeyPress += cancel;

        // Where the C tool adds its SIGINT and SIGHUP sources: after the
        // pipeline reached PAUSED, before the position ticker starts.
        using IDisposable? hangUp = InstallHangUpHandler();

        // Not a gst-launch option: these drive the same handler the console
        // does, so that both interrupts can be run without a signal.
        List<Timer> interrupts = [];

        try
        {
            foreach (TimeSpan delay in _options.InterruptAfter)
            {
                interrupts.Add(new Timer(_ => RequestInterrupt(), null, delay, Timeout.InfiniteTimeSpan));
            }

            while (!_quit)
            {
                using (Message? message = bus.TimedPop(PollSlice))
                {
                    // An application with no main loop drains the queue of
                    // pending releases itself, and once per poll of the bus is
                    // the natural place. See docs/ownership.md.
                    GstSharp.DrainPendingReleases();

                    if (message is not null)
                    {
                        Handle(message);
                    }
                }

                if (positions && tick.Elapsed >= PositionInterval)
                {
                    tick.Restart();
                    PrintPosition(outputIsTty);
                }
            }
        }
        finally
        {
            foreach (Timer timer in interrupts)
            {
                timer.Dispose();
            }

            Console.CancelKeyPress -= cancel;
        }
    }

    /// <summary>
    /// Turns Ctrl+C into the application message the tool acts on.
    /// </summary>
    /// <param name="sender">The console.</param>
    /// <param name="e">Whether the process is allowed to end.</param>
    /// <remarks>
    /// The first two interrupts belong to the tool; a third one is the way out
    /// of a pipeline that will not stop, and letting it through ends the
    /// process. The C tool takes its handler off after the first interrupt, so
    /// there the second one already ends it.
    /// </remarks>
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = Interlocked.Increment(ref _interruptsRequested) <= 2;

        if (e.Cancel)
        {
            RequestInterrupt();
        }
    }

    /// <summary>
    /// Posts the interrupt onto the bus, which is where the C tool handles it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <c>intr_handler</c>. It runs on whichever thread the console or
    /// the timer gives it and does exactly one thing there — post an
    /// application message — because posting is thread safe and everything else
    /// the interrupt causes has to happen on the thread that owns the pipeline.
    /// The bus loop picks the message up in
    /// <see cref="MessageType.Application"/> and calls <see cref="Interrupt"/>.
    /// </para>
    /// <para>
    /// The structure name is the one the C tool uses, <c>GstLaunchInterrupt</c>,
    /// and both calls on the way consume their argument.
    /// </para>
    /// </remarks>
    private void RequestInterrupt()
    {
        Print("handling interrupt.");

        using Structure structure = Structure.NewEmpty("GstLaunchInterrupt");

        using (Value value = Value.New(GType.String))
        {
            value.SetString("Pipeline interrupted");
            structure.SetValue("message", value);
        }

        using Message message = Message.NewApplication(_pipeline, structure);

        _pipeline.PostMessage(message);
    }

    /// <summary>
    /// Stops the pipeline, which is what the C bus handler does when the
    /// application message arrives.
    /// </summary>
    private void Interrupt()
    {
        Print("Interrupt: Stopping pipeline ...");
        _interrupting = true;

        if (!_options.EosOnShutdown)
        {
            _quit = true;
            return;
        }

        if (_waitingEos)
        {
            Print("Interrupt while waiting for EOS - stopping pipeline...");
            _quit = true;
            return;
        }

        Print("EOS on shutdown enabled -- Forcing EOS on the pipeline");

        // SendEvent consumes the event: it takes the reference over and the
        // wrapper owns nothing afterwards, which is what its disposed state
        // means. The using stays correct and is the recommended shape.
        using (Event eos = Event.NewEos())
        {
            _pipeline.SendEvent(eos);
        }

        Print("Waiting for EOS...");
        _waitingEos = true;
    }

    /// <summary>
    /// Acts on one message, which is the bus handler of the C tool with its
    /// sync handler folded in.
    /// </summary>
    /// <param name="message">The message to act on.</param>
    private void Handle(Message message)
    {
        if (_options.Messages)
        {
            PrintMessageDump(message);
        }

        switch (message.Type)
        {
            case MessageType.Error:
                HandleError(message);
                break;

            case MessageType.NewClock:
            {
                // A clock is a GObject wrapper, interned and not disposed here.
                message.ParseNewClock(out Clock? clock);
                Print($"New clock: {clock?.Name ?? "NULL"}");
                break;
            }

            case MessageType.ClockLost:
                Print("Clock lost, selecting a new one");
                _pipeline.SetState(State.Paused);
                _pipeline.SetState(State.Playing);
                break;

            case MessageType.Eos:
                Print($"Got EOS from element \"{message.SourceName ?? "(NULL)"}\".");

                if (_options.EosOnShutdown)
                {
                    Print("EOS received - stopping pipeline...");
                }

                _quit = true;
                break;

            case MessageType.Tag:
                if (_options.Tags)
                {
                    HandleTag(message);
                }

                break;

            case MessageType.Toc:
                if (_options.Toc)
                {
                    HandleToc(message);
                }

                break;

            case MessageType.Info:
            {
                (GException _, string? debug) = message.ParseInfo();

                if (debug is not null)
                {
                    Print("INFO:");
                    Print(debug);
                }

                break;
            }

            case MessageType.Warning:
                HandleWarning(message);
                break;

            case MessageType.StateChanged:
                HandleStateChanged(message);
                break;

            case MessageType.Buffering:
                HandleBuffering(message);
                break;

            case MessageType.Latency:
                Print("Redistribute latency...");
                _pipeline.RecalculateLatency();
                break;

            case MessageType.RequestState:
            {
                message.ParseRequestState(out State state);
                Print($"Setting state to {StateName(state)} as requested by {PathOf(message)}...");
                _pipeline.SetState(state);
                break;
            }

            case MessageType.Progress:
                HandleProgress(message);
                break;

            case MessageType.Application:
            {
                using Structure? structure = message.GetStructure();

                // This application message is posted when an interrupt was
                // caught and the pipeline has to stop.
                if (structure is not null && structure.HasName("GstLaunchInterrupt"))
                {
                    Interrupt();
                }

                break;
            }

            case MessageType.Element:
            {
                using Structure? structure = message.GetStructure();

                if (structure is not null && structure.HasName("missing-plugin"))
                {
                    Print($"Missing element: {structure.GetString("name") ?? "(no description)"}");
                }

                break;
            }

            case MessageType.HaveContext:
                HandleHaveContext(message);
                break;

            case MessageType.PropertyNotify:
                HandlePropertyNotify(message);
                break;

            default:
                // Just be quiet by default.
                break;
        }
    }

    /// <summary>
    /// Reports an error and ends the run, which is what the sync handler of the
    /// C tool does.
    /// </summary>
    /// <param name="message">The error message.</param>
    private void HandleError(Message message)
    {
        Global.DebugBinToDotFileWithTs(_pipeline, DebugGraphDetails.All, "gst-launch.error");

        PrintErrorMessage(message);

        if (_targetState == State.Paused)
        {
            Console.Error.WriteLine("ERROR: pipeline doesn't want to preroll.");
        }
        else if (_interrupting)
        {
            Print("An error happened while waiting for EOS");
        }

        _exitCode = ExitError;
        _quit = true;
    }

    /// <summary>
    /// Reports a warning and dumps the graph.
    /// </summary>
    /// <param name="message">The warning message.</param>
    private void HandleWarning(Message message)
    {
        string path = PathOf(message);

        Global.DebugBinToDotFileWithTs(_pipeline, DebugGraphDetails.All, "gst-launch.warning");

        (GException warning, string? debug) = message.ParseWarning();
        Print($"WARNING: from element {path}: {warning.Message}");

        if (debug is not null)
        {
            Print("Additional debug info:");
            Print(debug);
        }
    }

    /// <summary>
    /// Acts on a state change of the pipeline: dumps the graph and, on the
    /// first PAUSED, starts playing.
    /// </summary>
    /// <param name="message">The state changed message.</param>
    private void HandleStateChanged(Message message)
    {
        // Every element posts its own transitions; only the ones of the
        // pipeline describe the run as a whole.
        if (message.Src is not Gst.Object source || source.Handle != _pipeline.Handle)
        {
            return;
        }

        message.ParseStateChanged(out State old, out State current, out State _);

        Global.DebugBinToDotFileWithTs(
            _pipeline,
            DebugGraphDetails.All,
            $"gst-launch.{StateName(old)}_{StateName(current)}");

        if (_targetState != State.Paused || current != _targetState)
        {
            return;
        }

        _prerolled = true;
        Print("Pipeline is PREROLLED ...");

        // Ignore when we are buffering, since then we mess with the states
        // ourselves.
        if (_buffering)
        {
            Print("Prerolled, waiting for buffering to finish...");
            return;
        }

        if (_inProgress)
        {
            Print("Prerolled, waiting for progress to finish...");
            return;
        }

        DoInitialPlay();
    }

    /// <summary>
    /// Pauses and resumes the pipeline around a buffering source.
    /// </summary>
    /// <param name="message">The buffering message.</param>
    private void HandleBuffering(Message message)
    {
        message.ParseBuffering(out int percent);
        PrintPartial($"buffering... {percent}%  \r");

        // No state management needed for live pipelines.
        if (_isLive)
        {
            return;
        }

        if (percent == 100)
        {
            _buffering = false;

            if (_targetState == State.Paused)
            {
                DoInitialPlay();
                return;
            }

            if (_targetState == State.Playing)
            {
                Print("Done buffering, setting pipeline to PLAYING ...");
                _pipeline.SetState(State.Playing);
            }

            return;
        }

        if (!_buffering && _targetState == State.Playing)
        {
            Print("Buffering, setting pipeline to PAUSED ...");
            _pipeline.SetState(State.Paused);
        }

        _buffering = true;
    }

    /// <summary>
    /// Reports progress and starts playing once the last task is done.
    /// </summary>
    /// <param name="message">The progress message.</param>
    private void HandleProgress(Message message)
    {
        message.ParseProgress(out ProgressType type, out string? code, out string? text);

        switch (type)
        {
            case ProgressType.Start:
            case ProgressType.Continue:
                _inProgress = true;
                break;

            case ProgressType.Complete:
            case ProgressType.Canceled:
            case ProgressType.Error:
                _inProgress = false;
                break;

            default:
                break;
        }

        Print($"Progress: ({code}) {text}");

        if (!_inProgress && _prerolled && _targetState == State.Paused)
        {
            DoInitialPlay();
        }
    }

    /// <summary>
    /// Reports a context an element published.
    /// </summary>
    /// <param name="message">The message.</param>
    private void HandleHaveContext(Message message)
    {
        message.ParseHaveContext(out Context? context);

        if (context is null)
        {
            return;
        }

        using (context)
        {
            using Structure structure = context.GetStructure();

            Print($"Got context from element '{message.SourceName}': " +
                $"{context.GetContextType()}={structure}");
        }
    }

    /// <summary>
    /// Starts playing and remembers when, which is where the execution time is
    /// measured from.
    /// </summary>
    private void DoInitialPlay()
    {
        Print("Setting pipeline to PLAYING ...");

        _startedAt = Global.UtilGetTimestamp();

        if (_pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            Console.Error.WriteLine("ERROR: pipeline doesn't want to play.");
            _exitCode = ExitStateChangeFailure;

            // The error message will be posted later.
            return;
        }

        _targetState = State.Playing;
    }

    /// <summary>
    /// Prints whatever the bus already holds after a state change that failed.
    /// </summary>
    /// <param name="bus">The bus of the pipeline.</param>
    private void DrainErrors(Bus bus)
    {
        // PopFiltered returns immediately, so this drains the bus without
        // blocking when it is empty.
        while (bus.PopFiltered(MessageType.Error) is Message message)
        {
            using (message)
            {
                PrintErrorMessage(message);

                if (_targetState == State.Paused)
                {
                    Console.Error.WriteLine("ERROR: pipeline doesn't want to preroll.");
                }
            }
        }
    }
    /// <summary>
    /// Names a state the way the C tool prints it.
    /// </summary>
    /// <param name="state">The state to name.</param>
    /// <returns>The upper-case name of the state.</returns>
    /// <remarks>
    /// The library has two functions for this: <c>gst_element_state_get_name</c>
    /// since 1.0, deprecated in 1.28, and <c>gst_state_get_name</c> since 1.28.
    /// The binding therefore offers a member that warns and a member that does
    /// not exist on the 1.24 floor the sample runs on in CI. The five names have
    /// been frozen for the whole life of GStreamer 1.x, so the sample spells
    /// them itself instead of choosing which trap to step into.
    /// </remarks>
    private static string StateName(State state) => state switch
    {
        State.VoidPending => "VOID_PENDING",
        State.Null => "NULL",
        State.Ready => "READY",
        State.Paused => "PAUSED",
        State.Playing => "PLAYING",
        _ => ((int)state).ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

}
