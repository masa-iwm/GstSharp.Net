using Gst;
using Gst.App;
using Gst.Video;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The one signal argument the overlays hand over as a borrow:
/// <c>GstAppSink::propose-allocation</c>, whose query the handler is expected
/// to fill and whose emitter reads back what was written into it.
/// </summary>
/// <remarks>
/// <para>
/// This is the managed half of the upstream check
/// <c>test_query_allocation_signals</c>
/// (gst-plugins-base/tests/check/elements/appsink.c): an allocation query is run
/// against the static sink pad of an <c>appsink</c> with <c>emit-signals</c>
/// switched on, the handler adds the video meta API to it, and the caller of
/// the query finds it there afterwards. No pipeline is built and no element
/// leaves the NULL state, which is what upstream does as well and what keeps
/// the check free of any wait.
/// </para>
/// <para>
/// The query only reaches the handler writable because the wrapper borrows it.
/// A wrapper holding a reference of its own would put the query at a reference
/// count of two, <c>gst_query_add_allocation_meta</c> would refuse it with a
/// critical, and the meta would never be found - which is the bug these tests
/// stand for. Everything the handler observes is recorded into fields and
/// asserted after the query returns: an exception thrown inside a trampoline is
/// reported through the exception trap and never reaches the test.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AppSinkProposeAllocationSignalTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public AppSinkProposeAllocationSignalTests(ITestOutputHelper output)
    {
        _output = output;
        GstApp.Initialize();
    }

    /// <summary>
    /// The handler writes into the query the emitter goes on to read, which is
    /// the whole point of the borrow, and the borrowed wrapper refuses
    /// <see cref="Gst.Query.MakeWritable"/> while it does.
    /// </summary>
    [RequiresGStreamerFact(24)]
    public void TheHandlerFillsTheVeryQueryTheEmitterReadsBack()
    {
        Gst.GObject.GType videoApi = VideoGlobal.VideoMetaApiGetType();

        using Element element = Assert.IsAssignableFrom<Element>(
            ElementFactory.Make("appsink", "propose-allocation"));
        AppSink sink = Assert.IsAssignableFrom<AppSink>(element);
        sink.EmitSignals = true;

        int handlerRuns = 0;
        bool wasWritable = false;
        bool refusedMakeWritable = false;
        QueryType seenType = QueryType.Unknown;

        bool Handler(object? sender, AppSink.ProposeAllocationSignalArgs args)
        {
            handlerRuns++;
            seenType = args.Query.Type;
            wasWritable = args.Query.IsWritable;

            try
            {
                args.Query.MakeWritable();
            }
            catch (InvalidOperationException)
            {
                // A borrowed wrapper owns no reference to give away, and the
                // query is writable already.
                refusedMakeWritable = true;
            }

            args.Query.AddAllocationMeta(videoApi, null);
            return true;
        }

        sink.ProposeAllocation += Handler;
        try
        {
            using Pad pad = Assert.IsAssignableFrom<Pad>(element.GetStaticPad("sink"));
            using Query query = Query.NewAllocation(caps: null, needPool: false);

            Assert.True(pad.Query(query));

            _output.WriteLine(
                $"runs: {handlerRuns}, type: {seenType}, writable: {wasWritable}, "
                + $"refused: {refusedMakeWritable}");

            Assert.Equal(1, handlerRuns);
            Assert.Equal(QueryType.Allocation, seenType);

            // The borrow is what leaves it writable: the emitter holds the only
            // reference, and GObject took none because the signal registered
            // the argument with G_SIGNAL_TYPE_STATIC_SCOPE.
            Assert.True(wasWritable);
            Assert.True(refusedMakeWritable);

            // And the proof that the write reached the emitter rather than a
            // copy of its query: the caller of the query finds the meta.
            Assert.True(query.FindAllocationMeta(videoApi, out uint index));
            Assert.Equal(0u, index);
            Assert.Equal(1u, query.GetNAllocationMetas());
        }
        finally
        {
            sink.ProposeAllocation -= Handler;
        }
    }

    /// <summary>
    /// The wrapper is scoped to the handler: it borrows, so disposal frees
    /// nothing, but it is detached when the handler returns and the query the
    /// caller owns is untouched by that.
    /// </summary>
    [RequiresGStreamerFact(24)]
    public void TheBorrowedWrapperIsDeadOnceTheHandlerReturned()
    {
        using Element element = Assert.IsAssignableFrom<Element>(
            ElementFactory.Make("appsink", "propose-allocation-scope"));
        AppSink sink = Assert.IsAssignableFrom<AppSink>(element);
        sink.EmitSignals = true;

        Query? kept = null;

        bool Handler(object? sender, AppSink.ProposeAllocationSignalArgs args)
        {
            kept = args.Query;
            return true;
        }

        sink.ProposeAllocation += Handler;
        try
        {
            using Pad pad = Assert.IsAssignableFrom<Pad>(element.GetStaticPad("sink"));
            using Query query = Query.NewAllocation(caps: null, needPool: false);

            Assert.True(pad.Query(query));

            Assert.NotNull(kept);
            Assert.True(kept.IsDisposed);
            Assert.Throws<ObjectDisposedException>(() => kept.Type);

            // Nothing of the query the caller owns was released with it.
            Assert.Equal(QueryType.Allocation, query.Type);
        }
        finally
        {
            sink.ProposeAllocation -= Handler;
        }
    }
}
