using System.Diagnostics;
using Gst;
using Gst.App;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The flow control contract of <c>appsrc</c>: the signals that ask for data
/// and say when there is enough of it, the end of the stream, and the
/// non blocking preroll of the sink at the other end.
/// </summary>
/// <remarks>
/// <para>
/// <c>need-data</c> is raised on the streaming thread of the source, so the
/// handler below pushes from that thread and the test thread only waits. The
/// counters it keeps are read with <see cref="Volatile"/> for that reason.
/// </para>
/// <para>
/// The second test never starts an element at all. <c>appsrc</c> queues what
/// is pushed into it in any state - only a source that was stopped or flushed
/// refuses, and a fresh one is neither (gstappsrc.c: <c>priv-&gt;flushing</c>
/// is set by <c>gst_app_src_stop</c> and <c>gst_app_src_unlock</c> and by
/// nothing else) - which is what makes the queue accounting observable without
/// a pipeline.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSrcStreamTests
{
    private const int BufferSize = 1764;
    private const int Buffers = 8;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSrcStreamTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.App.GstApp: the casts below only work once the module
        // initialiser of GstSharp.Net.App has run.
        GstApp.Initialize();
    }

    /// <summary>
    /// A source that pushes one buffer per <c>need-data</c> and ends the
    /// stream after the eighth hands the sink eight samples and then the end
    /// of the stream.
    /// </summary>
    /// <remarks>
    /// The preroll sample is the first buffer, and <c>pull-sample</c> hands
    /// that same first buffer out again, so eight pushes are eight non null
    /// answers of <c>pull-sample</c> even though one of them was already seen
    /// as the preroll. <c>enough-data</c> cannot be raised here: the default
    /// <c>max-bytes</c> of 200000 is far above the 14112 bytes this pushes.
    /// </remarks>
    [RequiresElementFact("appsrc", "appsink")]
    public void NeedDataDrivesThePushesAndEosEndsTheStream()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(
            Global.ParseLaunch("appsrc name=src ! appsink name=sink sync=false"));

        using Element? sourceElement = pipeline.GetByName("src");
        using Element? sinkElement = pipeline.GetByName("sink");

        AppSrc source = Assert.IsType<AppSrc>(sourceElement);
        AppSink sink = Assert.IsType<AppSink>(sinkElement);

        using Caps caps = Assert.IsType<Caps>(
            Caps.FromString("audio/x-raw,format=S16LE,rate=44100,channels=1,layout=interleaved"));

        source.SetCaps(caps);
        source.SetStreamType(AppStreamType.Stream);
        source.SetEmitSignals(true);

        int pushed = 0;
        int needed = 0;
        int enough = 0;

        void OnNeedData(object? sender, AppSrc.NeedDataSignalArgs args)
        {
            Interlocked.Increment(ref needed);

            if (Interlocked.Increment(ref pushed) > Buffers)
            {
                return;
            }

            Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, BufferSize, null));

            // push_buffer is transfer full: the wrapper is spent by the call.
            if (source.PushBuffer(buffer) != FlowReturn.Ok)
            {
                return;
            }

            if (Volatile.Read(ref pushed) == Buffers)
            {
                source.EndOfStream();
            }
        }

        void OnEnoughData(object? sender, EventArgs args) => Interlocked.Increment(ref enough);

        source.NeedData += OnNeedData;
        source.EnoughData += OnEnoughData;

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            using (Sample? preroll = sink.PullPreroll())
            {
                Assert.NotNull(preroll);

                using Buffer? prerolled = preroll.GetBuffer();
                Assert.NotNull(prerolled);
                Assert.Equal((ulong)BufferSize, prerolled.GetSize());
            }

            for (int i = 0; i < Buffers; i++)
            {
                using Sample? sample = sink.PullSample();
                Assert.NotNull(sample);

                using Buffer? buffer = sample.GetBuffer();
                Assert.NotNull(buffer);
                Assert.Equal((ulong)BufferSize, buffer.GetSize());
            }

            Assert.Null(sink.PullSample());
            Assert.True(sink.IsEos());
        }
        finally
        {
            source.NeedData -= OnNeedData;
            source.EnoughData -= OnEnoughData;
            pipeline.SetState(State.Null);
        }

        _output.WriteLine($"need-data {Volatile.Read(ref needed)} times, enough-data {Volatile.Read(ref enough)} times");

        Assert.True(Volatile.Read(ref needed) > 0, "need-data was never raised.");
        Assert.Equal(0, Volatile.Read(ref enough));
    }

    /// <summary>
    /// A source whose queue is full raises <c>enough-data</c> on the thread
    /// that pushed, once, and queues the buffer anyway.
    /// </summary>
    /// <remarks>
    /// <c>gst_app_src_push_internal</c> asks whether the queue is full before
    /// it enqueues (gstappsrc.c:2577), and raises <c>enough-data</c> from
    /// there on the pushing thread (gstappsrc.c:2609) only on the first pass
    /// of its loop (gstappsrc.c:2665). With <c>block</c> false and no leaky
    /// type - both defaults - the second pass breaks out of the loop
    /// (gstappsrc.c:2677) and the buffer is queued regardless, so two pushes
    /// of 1764 bytes against a limit of 1764 leave 3528 bytes queued and
    /// exactly one signal behind.
    /// </remarks>
    [RequiresElementFact("appsrc")]
    public void EnoughDataFiresSynchronouslyWhenTheQueueIsFull()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsrc", "full"));
        AppSrc source = Assert.IsType<AppSrc>(element);

        source.SetMaxBytes(BufferSize);
        source.SetEmitSignals(true);

        int enough = 0;
        int thread = 0;

        void OnEnoughData(object? sender, EventArgs args)
        {
            Interlocked.Increment(ref enough);
            Volatile.Write(ref thread, Environment.CurrentManagedThreadId);
        }

        source.EnoughData += OnEnoughData;

        try
        {
            for (int i = 0; i < 2; i++)
            {
                Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, BufferSize, null));
                Assert.Equal(FlowReturn.Ok, source.PushBuffer(buffer));
            }

            _output.WriteLine($"enough-data {Volatile.Read(ref enough)} times, {source.GetCurrentLevelBytes()} bytes queued");

            Assert.Equal(1, Volatile.Read(ref enough));
            Assert.Equal(Environment.CurrentManagedThreadId, Volatile.Read(ref thread));
            Assert.Equal((ulong)(2 * BufferSize), source.GetCurrentLevelBytes());
        }
        finally
        {
            source.EnoughData -= OnEnoughData;
        }
    }

    /// <summary>
    /// <c>try_pull_preroll</c> gives the timeout back to its caller rather
    /// than blocking on a sink that will never preroll.
    /// </summary>
    [RequiresElementFact("appsink")]
    public void TryPullPrerollGivesUpWhenNothingPrerolls()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsink", "idle-preroll"));
        AppSink sink = Assert.IsType<AppSink>(element);

        Stopwatch elapsed = Stopwatch.StartNew();
        Sample? sample = sink.TryPullPreroll(ClockTime.FromMilliseconds(100));
        TimeSpan waited = elapsed.Elapsed;

        _output.WriteLine($"try_pull_preroll on a sink that was never started returned after {waited.TotalMilliseconds:F0} ms");

        Assert.Null(sample);
        Assert.True(waited < Patience, $"try_pull_preroll blocked for {waited}.");
    }
}
