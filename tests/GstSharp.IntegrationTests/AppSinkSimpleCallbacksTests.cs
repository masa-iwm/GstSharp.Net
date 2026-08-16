using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gst;
using Gst.App;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;
using Object = Gst.GObject.Object;

namespace GstSharp.IntegrationTests;

/// <summary>
/// §4.2 of the acceptance requirements: the simple callbacks of the
/// <c>appsink</c> and the <c>appsrc</c>, installed against the library that is
/// actually there.
/// </summary>
/// <remarks>
/// <para>
/// The install is the one call of this pair that is written by hand, because it
/// takes its argument over (<c>transfer-ownership="full"</c>), and everything
/// this suite measures follows from that: the set of callbacks is sealed once
/// it is installed, the managed state of every callback is owned by native code
/// from then on, and it is released when — and only when — GStreamer lets go of
/// the set. Nothing here is decided by reading the annotations; the reference
/// counts and the collector answer instead.
/// </para>
/// <para>
/// The two tests that watch a <see cref="WeakReference"/> die are the ones that
/// pin the destroy notification. They build the closure inside a helper that is
/// never inlined, so that no local of the test frame roots what the closure
/// captured, and they assert that the probe is alive first: a probe that was
/// never captured would die on its own and prove nothing.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSinkSimpleCallbacksTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSinkSimpleCallbacksTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.App.GstApp: the casts below only work once the module
        // initialiser of GstSharp.Net.App has run.
        GstApp.Initialize();
    }

    /// <summary>
    /// The path the acceptance application wants: a delegate that pulls, handed
    /// straight to the sink, with the signal machinery left switched off.
    /// </summary>
    [Fact]
    public void InstalledCallbackPullsEveryFrameWithTheSignalsOff()
    {
        const int Frames = 200;

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=200 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);
        using Bus bus = pipeline.GetBus();

        int pulled = 0;
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        // An assertion that fails on the streaming thread is caught by the
        // trampoline and never reaches the runner, so the callback records what
        // it sees and everything is decided on this thread after the run.
        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            sink.SetSimpleCallbacks(onNewSample: appsink =>
            {
                if (appsink is null)
                {
                    return FlowReturn.Error;
                }

                using Sample? sample = appsink.PullSample();

                if (sample is null)
                {
                    return FlowReturn.Eos;
                }

                Interlocked.Increment(ref pulled);
                return FlowReturn.Ok;
            });

            // Installing callbacks says nothing about the property, and the
            // property says nothing about the callbacks: while a set is
            // installed the sink emits no signals whatever it is set to.
            Assert.False(sink.EmitSignals);

            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        _output.WriteLine($"the installed callback pulled {Volatile.Read(ref pulled)} samples");

        lock (failures)
        {
            Assert.Empty(failures);
        }

        Assert.Equal(Frames, Volatile.Read(ref pulled));
        Assert.False(sink.EmitSignals);
    }

    /// <summary>
    /// The managed state of an installed callback is owned by native code, and
    /// tearing the sink down is what gives it back: the destroy notification of
    /// the slot runs, the <c>GCHandle</c> goes away and what the closure
    /// captured becomes collectible.
    /// </summary>
    /// <remarks>
    /// This is the break of the reference cycle that the documentation of
    /// <c>SetSimpleCallbacks</c> describes. A binding that never ran the destroy
    /// notification would leak one closure — and everything it captured — per
    /// installed set, and nothing but the collector can tell the difference.
    /// </remarks>
    [Fact]
    public void CallbackStateIsReleasedWhenTheSinkIsTornDown()
    {
        const int Frames = 10;

        Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=10 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);
        Bus bus = pipeline.GetBus();

        WeakReference probe;

        try
        {
            _ = InstallCountingCallbacks(sink, out probe);

            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
            Assert.Equal(Frames, ReadProbeCount(probe));
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        // Still installed, so still rooted: the GCHandle of the slot holds the
        // closure and the closure holds the probe. A probe that dies here was
        // never captured and the rest of the test would prove nothing.
        Collect();
        Assert.True(probe.IsAlive, "The probe was collected while the callbacks were still installed.");

        // The wrapper gives its reference of the sink back first, so that the
        // last one goes away inside Pipeline.Dispose: the bin drops its
        // children, the sink releases the set of callbacks and the destroy
        // notification runs on this thread.
        bus.Dispose();
        sink.Dispose();
        pipeline.Dispose();

        // The loop is belt and braces. Should a wrapper have been left for the
        // collector to find, the release of its toggle reference is queued by
        // the finalizer and performed by the drain.
        for (int attempt = 0; attempt < 10 && probe.IsAlive; attempt++)
        {
            Collect();
            Object.DrainPendingReleases();
        }

        _output.WriteLine($"the probe of the installed callback is alive: {probe.IsAlive}");

        Assert.False(probe.IsAlive, "The destroy notification of the callback slot never released its GCHandle.");
    }

    /// <summary>
    /// The three rules of an installed set: it cannot be changed afterwards, it
    /// can be replaced as a whole, and removing it hands the sink back to its
    /// signals.
    /// </summary>
    [Fact]
    public void InstalledSetIsSealedAndCanBeReplacedAndRemoved()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "appsrc name=source caps=video/x-raw,format=GRAY8,width=2,height=2,framerate=0/1 ! " +
            "appsink name=sink sync=false"));

        using Element? sourceElement = pipeline.GetByName("source");
        using Element? sinkElement = pipeline.GetByName("sink");
        AppSrc source = Assert.IsType<AppSrc>(sourceElement);
        AppSink sink = Assert.IsType<AppSink>(sinkElement);

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        int signalled = 0;
        AppSink.NewSampleHandler handler = (sender, args) =>
        {
            using Sample? sample = sink.PullSample();
            Interlocked.Increment(ref signalled);
            return sample is null ? FlowReturn.Eos : FlowReturn.Ok;
        };

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            // Asked for, connected, and silent for as long as a set of
            // callbacks is installed: the two paths are alternatives, and this
            // is the half of that claim that a property cannot express.
            sink.EmitSignals = true;
            sink.NewSample += handler;

            // The install consumes the set, which is how "it is not possible
            // anymore to change any of the callbacks inside it" is enforced
            // here: the wrapper is disposed and every setter throws.
            AppSinkSimpleCallbacks first = InstallCountingCallbacks(sink, out WeakReference firstProbe);

            Assert.True(first.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => first.SetNewSample(_ => FlowReturn.Ok));
            Assert.Throws<ObjectDisposedException>(() => first.Handle);

            Push(source);
            Assert.True(
                WaitUntil(() => ReadProbeCount(firstProbe) >= 1),
                "The first set of callbacks never saw a sample.");
            Assert.Equal(1, ReadProbeCount(firstProbe));
            Assert.Equal(0, Volatile.Read(ref signalled));

            // A second install replaces the first set. GStreamer releases the
            // one it held, so its slots are destroyed here, without any teardown
            // of the sink.
            AppSinkSimpleCallbacks second = InstallCountingCallbacks(sink, out WeakReference secondProbe);
            Assert.True(second.IsDisposed);

            for (int attempt = 0; attempt < 10 && firstProbe.IsAlive; attempt++)
            {
                Collect();
            }

            Assert.False(firstProbe.IsAlive, "Replacing the set of callbacks never released the state of the first one.");

            Push(source);
            Assert.True(
                WaitUntil(() => ReadProbeCount(secondProbe) >= 1),
                "The second set of callbacks never saw a sample.");
            Assert.Equal(1, ReadProbeCount(secondProbe));
            Assert.Equal(0, Volatile.Read(ref signalled));

            // And with no set installed the sink emits its signals again, which
            // is the only way back to the path the callbacks displaced. Nothing
            // is connected here that was not connected before the first
            // install; only the callbacks went away.
            sink.SetSimpleCallbacks(null);

            Push(source);
            Assert.True(
                WaitUntil(() => Volatile.Read(ref signalled) >= 1),
                "The sink emitted no new-sample signal after the callbacks were removed.");

            Assert.Equal(1, Volatile.Read(ref signalled));
        }
        finally
        {
            pipeline.SetState(State.Null);
            sink.NewSample -= handler;
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (failures)
        {
            Assert.Empty(failures);
        }
    }

    /// <summary>
    /// An exception that leaves a callback is trapped on the streaming thread,
    /// reported once and answered with <see cref="FlowReturn.Error"/>, which
    /// stops the pipeline instead of the process.
    /// </summary>
    [Fact]
    public void ExceptionFromACallbackIsTrappedAndStopsThePipeline()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "videotestsrc num-buffers=5 ! video/x-raw,format=GRAY8,width=16,height=16 ! " +
            "appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);
        using Bus bus = pipeline.GetBus();

        int calls = 0;
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            sink.SetSimpleCallbacks(onNewSample: appsink =>
            {
                Interlocked.Increment(ref calls);
                throw new InvalidOperationException("The callback of the test throws on purpose.");
            });

            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            // The trampoline answered GST_FLOW_ERROR, so the stream stops with
            // an error rather than reaching its end.
            Assert.NotNull(message);
            Assert.Equal(MessageType.Error, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        _output.WriteLine($"the throwing callback ran {Volatile.Read(ref calls)} times");

        Assert.True(Volatile.Read(ref calls) >= 1, "The throwing callback was never called.");

        lock (failures)
        {
            Assert.NotEmpty(failures);
            Assert.All(failures, failure => Assert.IsType<InvalidOperationException>(failure));
        }
    }

    /// <summary>
    /// The same install on the other side: a source that is fed from its
    /// <c>need-data</c> callback and ends its stream from there.
    /// </summary>
    [Fact]
    public void AppSrcCallbacksFeedThePipelineToTheEndOfTheStream()
    {
        const int Frames = 20;

        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "appsrc name=source caps=video/x-raw,format=GRAY8,width=2,height=2,framerate=0/1 ! " +
            "appsink name=sink sync=false"));

        using Element? sourceElement = pipeline.GetByName("source");
        using Element? sinkElement = pipeline.GetByName("sink");
        AppSrc source = Assert.IsType<AppSrc>(sourceElement);
        AppSink sink = Assert.IsType<AppSink>(sinkElement);
        using Bus bus = pipeline.GetBus();

        int wanted = 0;
        int enough = 0;
        int ended = 0;
        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        int frames = 0;

        try
        {
            source.SetSimpleCallbacks(
                onNeedData: (appsrc, length) =>
                {
                    if (appsrc is null)
                    {
                        return;
                    }

                    if (Interlocked.Increment(ref wanted) > Frames)
                    {
                        // Exactly once, however often the source asks again.
                        if (Interlocked.Exchange(ref ended, 1) == 0)
                        {
                            appsrc.EndOfStream();
                        }

                        return;
                    }

                    if (Buffer.NewAllocate(null, 4, null) is Buffer buffer)
                    {
                        // Consuming: the source owns the buffer afterwards.
                        appsrc.PushBuffer(buffer);
                    }
                },
                onEnoughData: appsrc => Interlocked.Increment(ref enough));

            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            // The sink is drained from here, because an appsink waits for its
            // queue to be consumed before it lets the end of the stream through.
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

            using Message? message = BusPump.WaitFor(bus, MessageType.Eos | MessageType.Error, RunTimeout);

            Assert.NotNull(message);
            Assert.Equal(MessageType.Eos, message.Type);
        }
        finally
        {
            pipeline.SetState(State.Null);
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        _output.WriteLine(
            $"the source was asked for data {Volatile.Read(ref wanted)} times and had enough " +
            $"{Volatile.Read(ref enough)} times, {frames} buffers arrived");

        lock (failures)
        {
            Assert.Empty(failures);
        }

        Assert.Equal(Frames, frames);
        Assert.True(sink.IsEos());
    }

    /// <summary>
    /// Builds a set of callbacks whose <c>new-sample</c> slot pulls and counts,
    /// and installs it.
    /// </summary>
    /// <param name="sink">The sink to install the callbacks on.</param>
    /// <param name="probe">
    /// A weak reference to the object the closure captured, which stays alive
    /// for exactly as long as the slot does.
    /// </param>
    /// <returns>
    /// The set of callbacks, which the install consumed and which is therefore
    /// disposed.
    /// </returns>
    /// <remarks>
    /// The closure is built here rather than in the test, and this method is
    /// never inlined, so that no local of the frame of the test roots what it
    /// captured: a debug build keeps its locals alive until the method returns,
    /// and the death of the probe would then measure the runner rather than the
    /// binding.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AppSinkSimpleCallbacks InstallCountingCallbacks(AppSink sink, out WeakReference probe)
    {
        Probe state = new();

        AppSinkSimpleCallbacks callbacks = AppSinkSimpleCallbacks.New();
        callbacks.SetNewSample(appsink =>
        {
            if (appsink is null)
            {
                return FlowReturn.Error;
            }

            using Sample? sample = appsink.PullSample();

            if (sample is null)
            {
                return FlowReturn.Eos;
            }

            state.Increment();
            return FlowReturn.Ok;
        });

        sink.SetSimpleCallbacks(callbacks);

        probe = new WeakReference(state);
        return callbacks;
    }

    /// <summary>Reads the count of a probe that may already be gone.</summary>
    /// <param name="probe">The weak reference to the probe.</param>
    /// <returns>The count, or <c>-1</c> when the probe was collected.</returns>
    /// <remarks>
    /// The strong reference lives in the frame of this method and nowhere else,
    /// so reading the count does not keep the probe alive for the caller.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ReadProbeCount(WeakReference probe) => probe.Target is Probe state ? state.Count : -1;

    /// <summary>Pushes one small buffer into a source.</summary>
    /// <param name="source">The source to push into.</param>
    private static void Push(AppSrc source)
    {
        Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 4, null));
        Assert.Equal(FlowReturn.Ok, source.PushBuffer(buffer));
    }

    /// <summary>Waits for something that happens on a streaming thread.</summary>
    /// <param name="condition">What is waited for.</param>
    /// <returns><see langword="true"/> when it happened in time.</returns>
    private static bool WaitUntil(Func<bool> condition)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < RunTimeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    /// <summary>Collects everything that is unreachable, finalizers included.</summary>
    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// The object a callback closure captures, so that its lifetime can be
    /// watched from outside.
    /// </summary>
    private sealed class Probe
    {
        private int _count;

        /// <summary>Gets how often the callback ran.</summary>
        internal int Count => Volatile.Read(ref _count);

        /// <summary>Counts one call of the callback.</summary>
        internal void Increment() => Interlocked.Increment(ref _count);
    }
}
