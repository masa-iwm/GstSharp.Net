using Gst;
using Gst.App;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// §5.6 of the acceptance requirements: <c>push_buffer</c> takes the buffer
/// over and <c>push_sample</c> does not, pinned with the reference counts the
/// library itself keeps.
/// </summary>
/// <remarks>
/// <para>
/// The two calls differ in one word of their annotation —
/// <c>transfer-ownership="full"</c> against <c>"none"</c> — and in nothing that
/// a compiler could check. Getting it wrong is a double free on one side and a
/// leak of one buffer per frame on the other, which is why this is measured
/// rather than assumed.
/// </para>
/// <para>
/// The source under test is a fresh <c>appsrc</c> that was never started: it
/// accepts what is pushed into it and queues it, and nothing takes it out
/// again, so the counts stand still while the test reads them. Every count is
/// read before the source is disposed, because the queue is flushed with it.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSrcTransferTests
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSrcTransferTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.App.GstApp: the cast below only works once the module
        // initialiser of GstSharp.Net.App has run.
        GstApp.Initialize();
    }

    /// <summary>
    /// <c>gst_app_src_push_buffer</c> is <c>transfer-ownership="full"</c>: the
    /// buffer belongs to the source afterwards, and the wrapper of the caller
    /// is left owning nothing.
    /// </summary>
    [Fact]
    public unsafe void PushTakesTheBufferOver()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsrc", "consuming"));
        AppSrc source = Assert.IsType<AppSrc>(element);

        Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        nint handle = buffer.Handle;

        Assert.Equal(1, Refcount(handle));

        Assert.Equal(FlowReturn.Ok, source.PushBuffer(buffer));

        int queued = Refcount(handle);
        _output.WriteLine($"references after the push: {queued}, queued buffers: {source.CurrentLevelBuffers}");

        // What the caller had is gone: the call consumed the reference of the
        // wrapper, so the wrapper is disposed and its handle is unreachable.
        // This is the whole pattern — construct, push, forget; never dispose a
        // buffer that was pushed, and never touch it again.
        Assert.True(buffer.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => buffer.Handle);

        // The one reference that is left is the one the queue of the source
        // holds. Two would mean the caller is leaking one buffer per frame,
        // none would mean the buffer is already freed and the queue holds a
        // dangling pointer.
        Assert.Equal(1, queued);
        Assert.Equal(1UL, source.CurrentLevelBuffers);

        // Disposing twice does nothing, so a `using` around a buffer that ends
        // up being pushed stays correct.
        buffer.Dispose();
        Assert.Equal(1, Refcount(handle));
    }

    /// <summary>
    /// <c>gst_app_src_push_sample</c> is <c>transfer-ownership="none"</c>: the
    /// source takes the buffer out of the sample and references that, and the
    /// sample itself stays with the caller, usable and unchanged.
    /// </summary>
    [Fact]
    public unsafe void PushSampleLeavesTheSampleWithTheCaller()
    {
        using Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("appsrc", "borrowing"));
        AppSrc source = Assert.IsType<AppSrc>(element);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,format=GRAY8,width=4,height=4"));
        using Sample sample = new(TestNatives.SampleNew(buffer.Handle, caps.Handle, 0, 0), Transfer.Full);

        int sampleBefore = Refcount(sample.Handle);
        int bufferBefore = Refcount(buffer.Handle);

        Assert.Equal(FlowReturn.Ok, source.PushSample(sample));

        int sampleAfter = Refcount(sample.Handle);
        int bufferAfter = Refcount(buffer.Handle);

        _output.WriteLine($"sample references: {sampleBefore} -> {sampleAfter}");
        _output.WriteLine($"buffer references: {bufferBefore} -> {bufferAfter}");

        // The sample was borrowed for the duration of the call and nothing
        // else: same count, same wrapper, still usable.
        Assert.Equal(sampleBefore, sampleAfter);
        Assert.False(sample.IsDisposed);

        using (Buffer? inside = sample.GetBuffer())
        {
            Assert.NotNull(inside);
            Assert.Equal(buffer.Handle, inside.Handle);
        }

        // What the source did take is a reference of the buffer inside the
        // sample, which is what ends up in its queue.
        Assert.Equal(bufferBefore + 1, bufferAfter);
        Assert.Equal(1UL, source.CurrentLevelBuffers);

        // And the caps of the sample were applied to the source, which is the
        // reason to push a sample rather than a buffer in the first place.
        using Caps? applied = source.Caps;
        Assert.NotNull(applied);
        Assert.True(applied.IsEqual(caps));
    }

    /// <summary>
    /// The same call in a running pipeline: a buffer that is pushed into an
    /// <c>appsrc</c> comes out of the <c>appsink</c> at the other end with its
    /// bytes intact.
    /// </summary>
    [Fact]
    public void PushedBufferArrivesAtTheOtherEnd()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "appsrc name=source caps=video/x-raw,format=GRAY8,width=2,height=2,framerate=0/1 ! " +
            "appsink name=sink sync=false"));

        using Element? sourceElement = pipeline.GetByName("source");
        using Element? sinkElement = pipeline.GetByName("sink");

        AppSrc source = Assert.IsType<AppSrc>(sourceElement);
        AppSink sink = Assert.IsType<AppSink>(sinkElement);

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 4, null));

            using (Buffer.MapScope map = buffer.Map(MapFlags.Write))
            {
                map.Span[0] = 1;
                map.Span[1] = 2;
                map.Span[2] = 3;
                map.Span[3] = 4;
            }

            Assert.Equal(FlowReturn.Ok, source.PushBuffer(buffer));
            Assert.True(buffer.IsDisposed);

            Assert.Equal(FlowReturn.Ok, source.EndOfStream());

            using Sample? sample = sink.TryPullSample(ClockTime.FromTimeSpan(RunTimeout));
            Assert.NotNull(sample);

            using Buffer? arrived = sample.GetBuffer();
            Assert.NotNull(arrived);

            using Buffer.MapScope read = arrived.Map(MapFlags.Read);
            Assert.Equal([1, 2, 3, 4], read.Span.ToArray());
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Reads the reference count out of the mini object header.
    /// </summary>
    /// <param name="handle">The mini object to look at.</param>
    /// <returns>The number of references that are held.</returns>
    private static unsafe int Refcount(nint handle) => ((MiniObjectRaw*)handle)->Refcount;
}
