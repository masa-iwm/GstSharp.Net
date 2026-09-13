// The WebRTC sample: two webrtcbin elements in one pipeline negotiate a call
// with each other and one of them plays back what the other sends.
//
// Usage: WebRtcLoopback [--buffers <count>] [--timeout <seconds>]
//                       [--stun-server <url>] [--print-sdp]
//                       [--native-path <directory>] [--flavor msvc|mingw]
//                       [--help]
//
// What it shows: the offer/answer round trip and the ICE candidate exchange of
// webrtcbin, driven entirely through the generic signal surface of the binding
// — webrtcbin has no .gir, so every one of its signals is emitted by name with
// Element.EmitSignal and connected by name with Object.ConnectSignal — and the
// media that flows once the two agree, counted at an appsink.
//
// Three kinds of thread run application code here, which is why every piece of
// shared state below is an Interlocked counter or a volatile field and why no
// handler holds a lock while it calls back into GStreamer:
//
//   * each webrtcbin runs a private thread with a main loop of its own, and
//     that is where "on-negotiation-needed", "on-ice-candidate" and the change
//     function of every promise are called;
//   * "pad-added" and the appsink's "new-sample" arrive on streaming threads;
//   * the thread that started the run only polls the bus and reads the counter.
//
// There is no GMainLoop in this sample, unlike the C example it ports: nothing
// here is dispatched through a main context the application owns, since each
// webrtcbin pumps its own, so the polled bus every other sample in this
// repository uses is enough. An exception thrown on one of those foreign
// threads cannot reach the bus, so each handler catches its own and leaves the
// message in a field the poll loop reads.
//
// The shortcut this sample takes is the signalling: both peers live in this
// process, so the offer, the answer and the candidates are handed straight to
// the other webrtcbin as the objects they are. A real application has a
// channel of its own between the peers and puts GetSdp().AsText() and the
// candidate string on it.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;
using Gst.Sdp;
using Gst.WebRTC;

return Loopback.Run(args);

/// <summary>The sample.</summary>
internal static class Loopback
{
    /// <summary>The factories the run needs, checked before anything is built.</summary>
    private static readonly string[] RequiredElements =
    [
        "webrtcbin",
        "nicesrc",
        "dtlssrtpenc",
        "opusenc",
        "opusdec",
        "rtpopuspay",
        "rtpopusdepay",
        "appsink",
    ];

    /// <summary>
    /// Reads the command line, checks the installation and runs the call.
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
                Console.Error.WriteLine($"WebRtcLoopback: {exception.Message}");
                PrintUsage(Console.Error);
                return 1;
            }

            if (options.Help)
            {
                PrintUsage(Console.Out);
                return 0;
            }

            // Two module initialisers, because the sample needs the types of
            // two modules in the registry: the session description the offer
            // and the answer travel as, and the appsink the received audio is
            // counted at. Both calls end in the same GstSharp.Initialize, which
            // is why the second one is a no operation.
            GstApp.Initialize(options.Native);
            GstWebRTC.Initialize(options.Native);

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");

            if (Missing() is { Count: > 0 } missing)
            {
                Console.Error.WriteLine(
                    $"WebRtcLoopback: this installation has no {string.Join(", ", missing)}. " +
                    "The sample needs the webrtc and nice plugins of gst-plugins-bad and the opus " +
                    "elements of the base and good plugin sets.");
                return 1;
            }

            session = new Session(options);
            return session.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"WebRtcLoopback: {exception}");
            return 1;
        }
        finally
        {
            session?.Dispose();
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Names the factories of <see cref="RequiredElements"/> the registry does
    /// not have.
    /// </summary>
    /// <returns>The missing names, empty when the installation is complete.</returns>
    private static List<string> Missing()
    {
        List<string> missing = [];

        foreach (string name in RequiredElements)
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

        writer.WriteLine("Usage: WebRtcLoopback [options]");
        writer.WriteLine();
        writer.WriteLine("  --buffers <count>      How many decoded buffers end the run. Default 100.");
        writer.WriteLine("  --timeout <seconds>    How long the run may take. Default 30.");
        writer.WriteLine("  --stun-server <url>    The stun-server of both peers. Default none.");
        writer.WriteLine("  --print-sdp            Print the offer and the answer.");
        writer.WriteLine("  --native-path <dir>    Where to look for the native GStreamer.");
        writer.WriteLine("  --flavor msvc|mingw    Which Windows build to look for.");
        writer.WriteLine("  --help                 Print this.");
    }
}

/// <summary>
/// One run: the pipeline, the two peers and everything the handlers share.
/// </summary>
internal sealed class Session : IDisposable
{
    /// <summary>The sending half and the two peers, in one pipeline.</summary>
    private const string Description =
        "audiotestsrc is-live=true wave=sine ! audioconvert ! audioresample ! opusenc ! " +
        "rtpopuspay pt=96 ! queue ! " +
        "application/x-rtp,media=audio,encoding-name=OPUS,payload=96 ! " +
        "webrtcbin name=send webrtcbin name=recv";

    /// <summary>The branch the received stream is decoded and counted in.</summary>
    private const string ReceiveBranch =
        "rtpopusdepay ! opusdec ! appsink name=sink emit-signals=true sync=false";

    /// <summary>How long one poll of the bus waits.</summary>
    private static readonly ClockTime PollInterval = ClockTime.FromMilliseconds(100);

    private readonly Options _options;
    private readonly Stopwatch _elapsed = new();

    /// <summary>The pipeline, which this class builds and therefore releases.</summary>
    private Pipeline? _pipeline;

    /// <summary>The sending peer. Interned, so it is not released here.</summary>
    private Element? _send;

    /// <summary>The receiving peer. Interned, so it is not released here.</summary>
    private Element? _recv;

    /// <summary>The sink of the receive branch. Interned as well.</summary>
    private AppSink? _sink;

    private Promise? _offerPromise;
    private Promise? _answerPromise;

    private ulong _negotiationHandler;
    private ulong _sendCandidateHandler;
    private ulong _recvCandidateHandler;

    private EventHandler<Element.PadAddedSignalArgs>? _padAdded;
    private AppSink.NewSampleHandler? _newSample;
    private Action<Exception>? _trap;

    private long _buffers;
    private int _sendToRecvCandidates;
    private int _recvToSendCandidates;
    private int _negotiationStarted;
    private int _branchBuilt;
    private volatile string? _failure;
    private volatile string? _offerSdp;
    private volatile string? _answerSdp;

    /// <summary>Initialises one run.</summary>
    /// <param name="options">The command line of the run.</param>
    internal Session(Options options) => _options = options;

    /// <summary>
    /// Builds the pipeline, negotiates the call and waits for the media.
    /// </summary>
    /// <returns>0 when the requested number of buffers arrived, 1 otherwise.</returns>
    internal int Run()
    {
        if (Global.ParseLaunch(Description) is not Pipeline pipeline)
        {
            Console.Error.WriteLine("WebRtcLoopback: the description did not produce a pipeline.");
            return 1;
        }

        _pipeline = pipeline;

        if (pipeline.GetByName("send") is not { } send || pipeline.GetByName("recv") is not { } recv)
        {
            Console.Error.WriteLine("WebRtcLoopback: the pipeline has no send or no recv peer.");
            return 1;
        }

        _send = send;
        _recv = recv;

        if (_options.StunServer is { } stun)
        {
            send.SetProperty("stun-server", stun);
            recv.SetProperty("stun-server", stun);
        }

        // A handler that throws on one of the foreign threads is reported here
        // rather than lost: the trampolines of the binding catch what a handler
        // lets escape and raise this event instead of unwinding into native
        // code. The try/catch in every handler below is the first line of the
        // same defence; this one also covers what the binding itself may throw
        // while it marshals an argument.
        _trap = exception => Fail($"a callback failed: {exception.Message}");
        ExceptionTrap.UnhandledException += _trap;

        Connect(send, recv);

        Bus bus = pipeline.GetBus();
        int status = Drive(pipeline, bus);

        PrintSummary();
        return status;
    }

    /// <summary>Releases what the run built.</summary>
    public void Dispose()
    {
        // The pipeline goes to NULL before anything else: the NULL transition
        // of a webrtcbin quits and joins its own thread, so no handler can be
        // running by the time it returns, and only then is it safe to take the
        // promises and the handlers away.
        _pipeline?.SetState(State.Null);

        if (_trap is { } trap)
        {
            ExceptionTrap.UnhandledException -= trap;
            _trap = null;
        }

        if (_send is { } send)
        {
            send.RemoveHandler(_negotiationHandler);
            send.RemoveHandler(_sendCandidateHandler);
        }

        if (_recv is { } recv)
        {
            recv.RemoveHandler(_recvCandidateHandler);

            if (_padAdded is { } padAdded)
            {
                recv.PadAdded -= padAdded;
            }
        }

        if (_sink is { } sink && _newSample is { } newSample)
        {
            sink.NewSample -= newSample;
        }

        // Freeing a promise that is still pending does not expire it: it only
        // produces a GLib warning, and no change function runs. See Take for
        // why a result other than a reply is not an error here either.
        _offerPromise?.Dispose();
        _answerPromise?.Dispose();
        _pipeline?.Dispose();
        _pipeline = null;
    }

    /// <summary>
    /// Connects everything the negotiation needs, before the pipeline runs.
    /// </summary>
    /// <param name="send">The sending peer.</param>
    /// <param name="recv">The receiving peer.</param>
    private void Connect(Element send, Element recv)
    {
        _negotiationHandler = send.ConnectSignal("on-negotiation-needed", (_, _) =>
        {
            OnNegotiationNeeded();
            return null;
        });

        // The candidate of one peer is handed to the other one through the
        // action signal, which is the call the C examples make as well. The
        // ICE level gst_webrtc_ice_add_candidate is deliberately not used: it
        // takes a promise that some versions never answer.
        _sendCandidateHandler = send.ConnectSignal("on-ice-candidate", (_, args) =>
        {
            ForwardCandidate(recv, args, ref _sendToRecvCandidates, "send");
            return null;
        });

        _recvCandidateHandler = recv.ConnectSignal("on-ice-candidate", (_, args) =>
        {
            ForwardCandidate(send, args, ref _recvToSendCandidates, "recv");
            return null;
        });

        _padAdded = (_, args) => OnPadAdded(args.NewPad);
        recv.PadAdded += _padAdded;
    }

    /// <summary>
    /// Runs the pipeline and waits for the buffers, the bus or the deadline.
    /// </summary>
    /// <param name="pipeline">The pipeline of the run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <returns>0 when the requested number of buffers arrived, 1 otherwise.</returns>
    private int Drive(Pipeline pipeline, Bus bus)
    {
        _elapsed.Start();

        if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
        {
            Console.Error.WriteLine("WebRtcLoopback: the pipeline refused to go to PLAYING.");
            return 1;
        }

        try
        {
            while (_elapsed.Elapsed < _options.Timeout)
            {
                if (_failure is { } failure)
                {
                    Console.Error.WriteLine($"WebRtcLoopback: {failure}");
                    return 1;
                }

                if (Interlocked.Read(ref _buffers) >= _options.Buffers)
                {
                    return 0;
                }

                using Message? message = bus.TimedPopFiltered(PollInterval, MessageType.Error | MessageType.Eos);

                if (message is null)
                {
                    continue;
                }

                if (message.Type == MessageType.Error)
                {
                    (GException error, string? debug) = message.ParseError();
                    Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
                    Console.Error.WriteLine($"debug:       {debug ?? "none"}");
                    return 1;
                }

                Console.Error.WriteLine("WebRtcLoopback: end of stream before the buffers arrived.");
                return 1;
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"WebRtcLoopback: {Interlocked.Read(ref _buffers)} of {_options.Buffers} buffers " +
                $"within {_options.Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            // The run is over, so the media stops here and not in Dispose: the
            // counters the summary prints are read after this returns, and the
            // NULL transition of a webrtcbin is also what joins the thread its
            // callbacks run on. Dispose calls this again for the paths that
            // never reached PLAYING.
            _elapsed.Stop();
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Asks the sending peer for an offer, once.
    /// </summary>
    /// <remarks>This runs on the private thread of the sending peer.</remarks>
    private void OnNegotiationNeeded()
    {
        try
        {
            // webrtcbin may ask again, and a second offer would race the answer
            // of the first one.
            if (Interlocked.Exchange(ref _negotiationStarted, 1) != 0)
            {
                return;
            }

            // The promise is stored before it is handed over, because the
            // change function reads it back and the reply may already be there
            // when the emission returns.
            _offerPromise = Promise.NewWithChangeFunc(_ => OnOfferCreated());
            _send!.EmitSignal("create-offer", null, _offerPromise.Handle);
        }
        catch (Exception exception) when (Report(exception, "on-negotiation-needed"))
        {
        }
    }

    /// <summary>
    /// Hands the offer to both peers and asks the receiving one to answer it.
    /// </summary>
    /// <remarks>This runs on the private thread of the sending peer.</remarks>
    private void OnOfferCreated()
    {
        try
        {
            if (Take(_offerPromise, "offer") is not { } offer)
            {
                return;
            }

            using (offer)
            {
                if (_options.PrintSdp)
                {
                    using SDPMessage sdp = offer.GetSdp();
                    _offerSdp = sdp.AsText();
                }

                // The description travels as itself: the value copies a boxed
                // argument, so this wrapper stays the owner of what it holds
                // and the peers keep copies of their own. The second argument
                // is the promise the caller does not need, and a null boxed
                // argument is a null pointer, which is what the C examples pass
                // as well.
                _send!.EmitSignal("set-local-description", offer, null);
                _recv!.EmitSignal("set-remote-description", offer, null);

                _answerPromise = Promise.NewWithChangeFunc(_ => OnAnswerCreated());
                _recv.EmitSignal("create-answer", null, _answerPromise.Handle);
            }
        }
        catch (Exception exception) when (Report(exception, "create-offer"))
        {
        }
    }

    /// <summary>
    /// Hands the answer to both peers, which completes the negotiation.
    /// </summary>
    /// <remarks>This runs on the private thread of the receiving peer.</remarks>
    private void OnAnswerCreated()
    {
        try
        {
            if (Take(_answerPromise, "answer") is not { } answer)
            {
                return;
            }

            using (answer)
            {
                if (_options.PrintSdp)
                {
                    using SDPMessage sdp = answer.GetSdp();
                    _answerSdp = sdp.AsText();
                }

                _recv!.EmitSignal("set-local-description", answer, null);
                _send!.EmitSignal("set-remote-description", answer, null);
            }
        }
        catch (Exception exception) when (Report(exception, "create-answer"))
        {
        }
    }

    /// <summary>
    /// Reads the session description a promise was answered with.
    /// </summary>
    /// <param name="promise">The promise to read.</param>
    /// <param name="field">The field the description is in.</param>
    /// <returns>
    /// The description, which the caller disposes, or <see langword="null"/>
    /// when the promise was not answered with one.
    /// </returns>
    /// <remarks>
    /// A promise that was not replied to is not a failure: a caller may
    /// <see cref="Promise.Interrupt"/> one, and a promise still pending when it
    /// is freed only produces a GLib warning, with no change function. Only a
    /// replied promise carries anything; when webrtcbin refuses the operation
    /// or is shutting down, its reply has an "error" field and no description.
    /// </remarks>
    private WebRTCSessionDescription? Take(Promise? promise, string field)
    {
        if (promise is null || promise.Wait() != PromiseResult.Replied)
        {
            return null;
        }

        using Structure? reply = promise.GetReply();

        if (reply is null)
        {
            Fail($"the {field} promise was answered with nothing.");
            return null;
        }

        // The failure of webrtcbin is a reply as well, and its field is called
        // "error". Saying so turns a missing description into a diagnosis.
        if (reply.HasField("error"))
        {
            Fail($"webrtcbin refused to build the {field}: {reply}");
            return null;
        }

        // The wrapper owns a copy of the field, which is why the callers
        // dispose it.
        if (reply.GetBoxed<WebRTCSessionDescription>(field) is not { } description)
        {
            Fail($"the {field} reply has no \"{field}\" field: {reply}");
            return null;
        }

        return description;
    }

    /// <summary>
    /// Hands a local candidate of one peer to the other one.
    /// </summary>
    /// <param name="peer">The peer that is told about the candidate.</param>
    /// <param name="args">The arguments of the emission.</param>
    /// <param name="counter">The counter of this direction.</param>
    /// <param name="from">The name of the peer the candidate is from.</param>
    /// <remarks>This runs on the private thread of the peer that found it.</remarks>
    private void ForwardCandidate(Element peer, object?[] args, ref int counter, string from)
    {
        try
        {
            if (args is not [uint mlineIndex, string candidate])
            {
                Fail($"the on-ice-candidate of {from} did not carry an index and a candidate.");
                return;
            }

            Interlocked.Increment(ref counter);
            peer.EmitSignal("add-ice-candidate", mlineIndex, candidate);
        }
        catch (Exception exception) when (Report(exception, "on-ice-candidate"))
        {
        }
    }

    /// <summary>
    /// Builds the receiving branch for the stream the peer announced.
    /// </summary>
    /// <param name="newPad">The pad the receiving peer added.</param>
    /// <remarks>This runs on a streaming thread of the receiving peer.</remarks>
    private void OnPadAdded(Pad newPad)
    {
        try
        {
            if (newPad.Direction != PadDirection.Src)
            {
                return;
            }

            // One audio stream is negotiated, so one branch is built. A second
            // pad would need a second one, and this sample has nothing to do
            // with it.
            if (Interlocked.Exchange(ref _branchBuilt, 1) != 0)
            {
                return;
            }

            // The bin is built here, so it is disposed here; the pipeline takes
            // a reference of its own when it takes the bin in.
            using Bin branch = Global.ParseBinFromDescription(ReceiveBranch, ghostUnlinkedPads: true);

            if (!_pipeline!.Add(branch))
            {
                Fail("the pipeline did not take the receiving branch.");
                return;
            }

            if (branch.GetByName("sink") is not AppSink sink)
            {
                Fail("the receiving branch has no appsink.");
                return;
            }

            _sink = sink;
            _newSample = OnNewSample;
            sink.NewSample += _newSample;

            branch.SyncStateWithParent();

            // Interned, and ghosted by the parse above: the branch has exactly
            // one unlinked sink pad.
            if (branch.GetStaticPad("sink") is not { } sinkPad)
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
        catch (Exception exception) when (Report(exception, "pad-added"))
        {
        }
    }

    /// <summary>
    /// Counts one decoded buffer.
    /// </summary>
    /// <param name="sender">The sink that has one.</param>
    /// <param name="args">The arguments of the emission, which carries none.</param>
    /// <returns>What the sink is told about the flow.</returns>
    /// <remarks>
    /// This runs on the streaming thread of the sink, and the sample pulls what
    /// the signal announced: without the pull the sink would hold every sample
    /// it ever announced.
    /// </remarks>
    private FlowReturn OnNewSample(object? sender, EventArgs args)
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
        catch (Exception exception) when (Report(exception, "new-sample"))
        {
            return FlowReturn.Error;
        }
    }

    /// <summary>Prints what the run added up to.</summary>
    private void PrintSummary()
    {
        if (_offerSdp is { } offer)
        {
            Console.WriteLine("offer:");
            Console.WriteLine(offer);
        }

        if (_answerSdp is { } answer)
        {
            Console.WriteLine("answer:");
            Console.WriteLine(answer);
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"buffers:     {Interlocked.Read(ref _buffers)} of {_options.Buffers} in " +
            $"{_elapsed.Elapsed.TotalMilliseconds:F0} ms, candidates send to recv " +
            $"{Volatile.Read(ref _sendToRecvCandidates)}, recv to send " +
            $"{Volatile.Read(ref _recvToSendCandidates)}"));
    }

    /// <summary>
    /// Records what a handler threw and swallows it.
    /// </summary>
    /// <param name="exception">What the handler threw.</param>
    /// <param name="where">The signal the handler is on.</param>
    /// <returns>Always <see langword="true"/>, so the filter catches.</returns>
    /// <remarks>
    /// This is an exception filter rather than a catch body so that the whole
    /// of it is one expression: what matters is that nothing is thrown back at
    /// native code, and that the poll loop of the main thread learns about it —
    /// a throw on one of these threads is the one failure the bus cannot show.
    /// </remarks>
    private bool Report(Exception exception, string where)
    {
        Fail($"the {where} handler failed: {exception.Message}");
        return true;
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

    /// <summary>Gets how many decoded buffers end the run.</summary>
    internal long Buffers { get; private set; } = 100;

    /// <summary>Gets how long the run may take.</summary>
    internal TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the STUN server both peers are given, if any.</summary>
    internal string? StunServer { get; private set; }

    /// <summary>Gets a value indicating whether the SDP is printed.</summary>
    internal bool PrintSdp { get; private set; }

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

                case "--stun-server":
                    options.StunServer = ValueOf(arguments, ref i);
                    break;

                case "--print-sdp":
                    options.PrintSdp = true;
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
                    throw new ArgumentException(
                        $"\"{arguments[i]}\" is not a known argument.",
                        nameof(arguments));
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
