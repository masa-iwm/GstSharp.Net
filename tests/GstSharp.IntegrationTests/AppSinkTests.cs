using System.Diagnostics;
using Gst;
using Gst.App;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>appsink</c> half of the acceptance requirements: the type registry
/// across module boundaries, the two ways of getting a sample out of the sink
/// and the ownership of everything that comes with one.
/// </summary>
/// <remarks>
/// <para>
/// These are the items of §5 of <c>docs/acceptance-processrecorderapp.md</c>
/// that a single machine can decide: §5.1 (the registry), §5.4 in the short
/// form (a <c>new-sample</c> handler that pulls and releases) and the
/// non blocking contract of <c>try_pull_sample</c> that the polling loop of the
/// application depends on. The full §5.2 and §5.4 soak runs, which watch the
/// resident set of a process for a minute, belong to the M3 pipeline and are
/// not run here: this suite has to stay fast and has to fail for a reason, not
/// for a machine that was busy.
/// </para>
/// <para>
/// What the short form does instead is watch the reference counts themselves,
/// which is deterministic. An <c>appsink</c> keeps one <c>GstSample</c> around
/// and refills it for every frame, but only while it is writable, so a sample
/// wrapper that fails to release its reference forces the sink to allocate a
/// new one — the handle changes, and that is exactly the leak the acceptance
/// application hit.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSinkTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSinkTests(ITestOutputHelper output)
    {
        _output = output;

        // Naming AppSink in a cast does not run the module initialiser of
        // GstSharp.Net.App, and until it has run the type registry has no entry
        // for GstAppSink. GstApp.Initialize is the call an application makes;
        // see Gst.App.GstApp for the whole story.
        GstApp.Initialize();
    }

    /// <summary>
    /// §5.1: both sinks of a tee are found by name and both are appsinks. A
    /// null here would be the silent failure of §2.1 of the acceptance
    /// requirements, where the wrapper is created for the closest type the
    /// registry does know and every appsink call is out of reach.
    /// </summary>
    [Fact]
    public void TeePipelineResolvesEveryAppSinkThroughTheTypeRegistry()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=1 ! video/x-raw,format=GRAY8,width=16,height=16 ! tee name=t " +
            "t. ! queue ! appsink name=first sync=false " +
            "t. ! queue ! appsink name=second sync=false"));

        using Element? firstElement = pipeline.GetByName("first");
        using Element? secondElement = pipeline.GetByName("second");

        AppSink first = Assert.IsType<AppSink>(firstElement);
        AppSink second = Assert.IsType<AppSink>(secondElement);

        _output.WriteLine($"first: {first.Name}, second: {second.Name}");

        Assert.Equal("first", first.Name);
        Assert.Equal("second", second.Name);
        Assert.NotEqual(first.Handle, second.Handle);

        // Members that only the appsink wrapper has, which is what the cast was
        // for in the first place. A sink that was never started answers "the
        // stream is over", and its signals are off until somebody asks for
        // them.
        Assert.True(first.IsEos());
        Assert.False(first.EmitSignals);
        Assert.False(second.EmitSignals);
    }

    /// <summary>
    /// §5.4, short form: <c>emit-signals</c> and a handler that pulls the
    /// sample the signal announced, releases it again and leaves the reference
    /// counts of the sink exactly where they were.
    /// </summary>
    [Fact]
    public unsafe void NewSampleHandlerPullsAndReleasesEveryFrame()
    {
        const int Frames = 200;

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=200 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);
        using Bus bus = pipeline.GetBus();

        nint[] samples = new nint[Frames];
        int[] sampleReferences = new int[Frames];
        int[] bufferHeld = new int[Frames];
        int[] bufferReleased = new int[Frames];
        int pulled = -1;

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        // An assertion that fails on the streaming thread is caught by the
        // trampoline and never reaches the runner, so the handler records what
        // it sees and everything is decided on this thread after the run.
        ExceptionTrap.UnhandledException += OnFailure;

        AppSink.NewSampleHandler handler = (sender, args) =>
        {
            int slot = Interlocked.Increment(ref pulled);

            using Sample? sample = sink.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            if (slot < Frames)
            {
                samples[slot] = sample.Handle;
                sampleReferences[slot] = Refcount(sample.Handle);

                if (sample.GetBuffer() is Buffer buffer)
                {
                    nint handle = buffer.Handle;
                    bufferHeld[slot] = Refcount(handle);

                    // The wrapper gives its reference back here; the sample
                    // still owns the buffer, so reading the count again is not
                    // a use after free.
                    buffer.Dispose();
                    bufferReleased[slot] = Refcount(handle);
                }
            }

            // A collection in the middle of the run, on the streaming thread:
            // the finalizer of a mini object unrefs directly, so this must not
            // deadlock against the pull that is going on.
            if (slot % 50 == 49)
            {
                GC.Collect();
            }

            return FlowReturn.Ok;
        };

        sink.EmitSignals = true;
        sink.NewSample += handler;

        long before = GC.GetTotalMemory(forceFullCollection: true);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
            sink.NewSample -= handler;
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long after = GC.GetTotalMemory(forceFullCollection: true);

        _output.WriteLine($"pulled {pulled + 1} samples, managed memory {before} -> {after} bytes");
        _output.WriteLine($"sample handle: {samples[0]:X}, references: {sampleReferences[0]}");
        _output.WriteLine($"buffer references: {bufferHeld[0]} held, {bufferReleased[0]} released");

        lock (failures)
        {
            Assert.Empty(failures);
        }

        Assert.Equal(Frames, pulled + 1);

        // Every frame came out of the same GstSample. The sink refills the one
        // it keeps, which it can only do while nobody else holds it: one leaked
        // wrapper and the handles start to differ.
        Assert.Single(samples.Distinct());
        Assert.NotEqual(nint.Zero, samples[0]);

        // Two references while the wrapper lives: the one the sink keeps and
        // the one that was handed to the caller. Flat over 200 frames.
        Assert.Equal([2], sampleReferences.Distinct());

        // And the ownership policy per getter: Sample.GetBuffer hands out a
        // wrapper that owns a reference, and Dispose gives exactly that one
        // back.
        for (int i = 0; i < Frames; i++)
        {
            Assert.Equal(bufferReleased[i] + 1, bufferHeld[i]);
        }

        Assert.Equal([bufferHeld[0]], bufferHeld.Distinct());
    }

    /// <summary>
    /// The non blocking contract of the pull loop: a sink that has nothing to
    /// give returns null when the timeout is over, and not later. This is what
    /// lets the application own the bus and the sink from one thread.
    /// </summary>
    [Fact]
    public void TryPullSampleGivesUpWhenTheTimeoutIsOver()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "idle"));
        AppSink sink = Assert.IsType<AppSink>(element);

        Stopwatch elapsed = Stopwatch.StartNew();
        Sample? sample = sink.TryPullSample(ClockTime.FromMilliseconds(100));
        TimeSpan waited = elapsed.Elapsed;

        _output.WriteLine($"try_pull_sample on a sink that was never started returned after {waited.TotalMilliseconds:F0} ms");

        Assert.Null(sample);
        Assert.True(waited < TimeSpan.FromSeconds(5), $"try_pull_sample blocked for {waited}.");
    }

    /// <summary>
    /// The other end of the same contract: once the stream is over, the pull
    /// loop is handed null for good, which is how it learns that it is done.
    /// </summary>
    [Fact]
    public void TryPullSampleReturnsNullAfterTheEndOfTheStream()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=3 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            int frames = 0;
            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < RunTimeout)
            {
                using Sample? sample = sink.TryPullSample(ClockTime.FromMilliseconds(100));

                if (sample is null)
                {
                    if (sink.IsEos())
                    {
                        break;
                    }

                    continue;
                }

                frames++;
            }

            _output.WriteLine($"pulled {frames} samples in {elapsed.Elapsed.TotalMilliseconds:F0} ms");

            Assert.Equal(3, frames);
            Assert.True(sink.IsEos());

            // A sink at the end of the stream keeps saying no, and says it
            // immediately.
            Stopwatch again = Stopwatch.StartNew();
            Assert.Null(sink.TryPullSample(ClockTime.FromMilliseconds(100)));
            Assert.True(again.Elapsed < TimeSpan.FromSeconds(5), $"try_pull_sample blocked for {again.Elapsed}.");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The dynamic signal path of the core against the generated appsink: the
    /// same action signal, emitted by name, on the wrapper the type registry
    /// produced.
    /// </summary>
    /// <remarks>
    /// The acceptance application needs this for the action signals that no
    /// <c>.gir</c> describes as a method. Here it is cross checked against the
    /// generated method, which calls the C function the signal stands for.
    /// </remarks>
    [Fact]
    public void ActionSignalOfTheTypedSinkAgreesWithTheGeneratedMethod()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "dynamic"));
        AppSink sink = Assert.IsType<AppSink>(element);

        Assert.True(SignalQuery.TryLookup(sink.NativeType, "try-pull-sample", out SignalQuery query));
        _output.WriteLine(query.ToString());

        // Nothing was ever pushed into this sink, so both ways of asking come
        // back empty handed rather than with a sample nobody owns.
        Assert.Null(sink.EmitSignal("try-pull-sample", 0UL));
        Assert.Equal(nint.Zero, sink.EmitSignal<nint>("try-pull-sample", 0UL));
        Assert.Null(sink.TryPullSample(ClockTime.Zero));
    }

    /// <summary>
    /// Reads the reference count out of the mini object header.
    /// </summary>
    /// <param name="handle">The mini object to look at.</param>
    /// <returns>The number of references that are held.</returns>
    private static unsafe int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;
}
