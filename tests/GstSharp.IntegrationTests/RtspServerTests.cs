using Gst;
using Gst.GLib;
using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstRtspServer</c> binding against the library that is installed: the
/// hand written <see cref="RTSPServer.Detach"/> and
/// <see cref="RTSPMountPoints.AddFactory"/>, the overlays the module ships, and
/// one real client speaking RTSP to a server on the loopback interface.
/// </summary>
/// <remarks>
/// <para>
/// Every server here that takes a client is built with <c>SetMaxThreads(0)</c>
/// on its thread pool, which puts each client on the context of the source
/// that dispatched it — the server's own — instead of on a thread of the pool.
/// That makes the tests deterministic and single threaded, and it makes the
/// test thread's own iteration of that context <b>the only thing that drives
/// the server</b>. So
/// every wait below is <see cref="PumpUntil"/>: iterate the context, check the
/// condition, give up at a deadline. A blocking wait — a
/// <see cref="ManualResetEventSlim"/>, a <c>Task.Wait</c> on the test thread —
/// would stop the pump and deadlock against the very thing it waits for.
/// </para>
/// <para>
/// The 1.24 floor is the reason three members are missing from these tests:
/// <c>RTSPClient::pre-closed</c> and <c>RTSPStreamTransport:timed-out</c> only
/// exist from 1.28, and <c>gst_rtsp_media_get_dscp_qos</c> unlocks instead of
/// locking on 1.24. Connecting the first throws on a 1.24 library, reading the
/// second logs a critical, and calling the third corrupts the media lock, so
/// none of them is touched here.
/// </para>
/// <para>
/// <see cref="RTSPThreadPool.Cleanup"/> is never called. It joins every thread
/// of the class-wide pool, which the whole test assembly shares, and nothing
/// here needs it: a media thread stops itself when the session media that owns
/// it is finalised, which the session pool filter of the shutdown order below
/// makes happen.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class RtspServerTests
{
    /// <summary>The pipeline every media factory here is built from.</summary>
    private const string Launch = "( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    /// <summary>How long any wait here is allowed to take.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspServerTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// <see cref="RTSPServer.Attach"/> answers a source identifier on a private
    /// context, the server listens on a port the operating system picked, and
    /// <see cref="RTSPServer.Detach"/> takes that one source off again — once.
    /// </summary>
    [Fact]
    public void DetachRemovesTheSourceAttachAddedAndOnlyOnce()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");

        // "0" asks the operating system for a free port, which GetBoundPort
        // answers once the socket exists — that is, after Attach.
        server.SetService("0");

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        _output.WriteLine($"source {sourceId} on 127.0.0.1:{port}");

        Assert.True(server.Detach(sourceId, context));

        // The source is gone from the context, so finding it by id answers
        // nothing. This is the private-context path, which says so quietly
        // rather than logging the critical g_source_remove would.
        Assert.False(server.Detach(sourceId, context));
    }

    /// <summary>
    /// <see cref="RTSPMountPoints.AddFactory"/> leaves the factory wrapper
    /// alive and its handlers connected, which is the whole reason it is
    /// written by hand.
    /// </summary>
    /// <remarks>
    /// The handler is connected <b>before</b> the mount, which is the shape
    /// <c>test-launch.c</c> uses and the shape a consuming
    /// <c>AddFactory</c> would break: disposing the wrapper runs
    /// <c>DisconnectAll</c>, so the handler would be gone before the first
    /// media was built. <see cref="RTSPMountPoints.Match"/> answers the very
    /// factory that was mounted, and constructing through it still calls back.
    /// </remarks>
    [RequiresElementFact("rtpL16pay")]
    public void AddFactoryKeepsTheFactoryWrapperAndItsHandlers()
    {
        using RTSPMountPoints mounts = RTSPMountPoints.New();
        using RTSPMediaFactory factory = RTSPMediaFactory.New();

        factory.SetLaunch(Launch);
        factory.SetShared(true);

        int configured = 0;

        // Connected before the mount, on purpose.
        factory.MediaConfigure += (_, _) => Interlocked.Increment(ref configured);

        mounts.AddFactory("/test", factory);

        // The wrapper is the caller's still: the mount took a reference of its
        // own rather than the one this wrapper holds.
        Assert.False(factory.IsDisposed);

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://127.0.0.1:8554/test", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            RTSPMediaFactory? matched = mounts.Match("/test", out int matchedLength);
            Assert.NotNull(matched);
            Assert.Same(factory, matched);
            Assert.Equal("/test".Length, matchedLength);

            using RTSPMedia? media = matched.Construct(url);
            Assert.NotNull(media);

            // Construct hands the media out locked; releasing it is the
            // caller's to do, here and everywhere.
            media.Unlock();
        }

        Assert.Equal(1, Volatile.Read(ref configured));
        Assert.False(factory.IsDisposed);
    }

    /// <summary>
    /// <see cref="RTSPMediaFactory.Construct"/> answers a media whose lock the
    /// caller holds, and a second construct on a shared factory only returns
    /// once <see cref="RTSPMedia.Unlock"/> has been called.
    /// </summary>
    [RequiresElementFact("rtpL16pay")]
    public void ConstructAnswersALockedMediaThatUnlockReleases()
    {
        using RTSPMediaFactory factory = RTSPMediaFactory.New();

        factory.SetLaunch(Launch);
        factory.SetShared(true);

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://127.0.0.1:8554/test", out RTSPUrl? url));
        Assert.NotNull(url);
        using RTSPUrl parsed = url;

        RTSPMedia? first = factory.Construct(parsed);
        Assert.NotNull(first);

        // Without this the next construct of the shared media blocks on the
        // media lock the factory took on the caller's behalf, and a server in
        // this state serves exactly one client.
        first.Unlock();

        RTSPMedia? second = factory.Construct(parsed);
        Assert.NotNull(second);
        Assert.Same(first, second);
        second.Unlock();

        first.Dispose();
    }

    /// <summary>
    /// The three returns the gir calls non-null and the library answers
    /// <c>NULL</c> on: an unmounted path, an unknown role, and the URI of a
    /// factory nobody has given one.
    /// </summary>
    [Fact]
    public void TheThreeNullAnswersAreNullable()
    {
        using RTSPMountPoints mounts = RTSPMountPoints.New();
        Assert.Null(mounts.Match("/nothing-is-mounted-here", out _));

        using RTSPPermissions permissions = RTSPPermissions.New();
        Assert.Null(permissions.GetRole("no-such-role"));

        using RTSPMediaFactoryURI factory = RTSPMediaFactoryURI.New();
        Assert.Null(factory.GetUri());
    }

    /// <summary>
    /// <see cref="RTSPMedia.New"/> takes a reference of its own on the element
    /// and leaves the caller's wrapper alone, and gives that reference back
    /// when the media is disposed.
    /// </summary>
    /// <remarks>
    /// The gir says the element is <c>transfer-ownership="full"</c> and the C
    /// <c>gst_object_ref_sink</c>s it (<c>rtsp-media.c:695-696</c>). A wrapper
    /// never holds a floating object, so that sink is a plain reference and the
    /// consuming shape would have leaked one and killed the caller's wrapper.
    /// The overlay of the module says transfer none instead; this is the
    /// regression test for it.
    /// </remarks>
    [Fact]
    public void MediaNewReferencesTheElementRatherThanConsumingIt()
    {
        using Bin element = Bin.New("rtsp-media-element");

        uint before = RefCountOf(element.Handle);

        RTSPMedia media = RTSPMedia.New(element);

        Assert.False(element.IsDisposed);
        Assert.Equal(before + 1, RefCountOf(element.Handle));
        Assert.Same(element, media.GetElement());

        media.Dispose();

        // The media was the only other holder, so its finalisation gives the
        // reference back and the wrapper is left with what it started with.
        Assert.False(element.IsDisposed);
        Assert.Equal(before, RefCountOf(element.Handle));
    }

    /// <summary>
    /// The ONVIF constructors answer their own type, which the gir spells as
    /// the base type of each.
    /// </summary>
    [Fact]
    public void TheOnvifConstructorsAnswerTheOnvifTypes()
    {
        using RTSPOnvifServer server = RTSPOnvifServer.New();
        Assert.IsType<RTSPOnvifServer>(server);

        using RTSPOnvifMediaFactory factory = RTSPOnvifMediaFactory.New();
        Assert.IsType<RTSPOnvifMediaFactory>(factory);
    }

    /// <summary>
    /// A real client: <c>rtspsrc</c> describes, sets up and plays a mount of a
    /// server running on the test thread's own context, and the server is then
    /// shut down in the order the documentation gives.
    /// </summary>
    /// <remarks>
    /// The client pipeline goes to <see cref="State.Null"/> before any of the
    /// server teardown, so that its <c>TEARDOWN</c> request is answered by a
    /// server that is still serving. That state change is issued from a thread
    /// of the pool and waited for by pumping, because <c>rtspsrc</c> joins its
    /// own task on the way down and that task is waiting for the server this
    /// thread is the engine of.
    /// </remarks>
    [RequiresElementFact("rtspsrc", "rtpL16pay")]
    public void AClientPlaysAMountAndTheServerShutsDownInOrder()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        using RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);

        // Every client lands on the context of the source that dispatched it,
        // which is the one below: this test thread drives the whole server.
        threadPool.SetMaxThreads(0);

        using RTSPMountPoints? mounts = server.GetMountPoints();
        Assert.NotNull(mounts);

        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);
        factory.SetShared(true);

        int configured = 0;

        // Before the mount, and still connected after it.
        factory.MediaConfigure += (_, _) => Interlocked.Increment(ref configured);

        mounts.AddFactory("/test", factory);
        Assert.False(factory.IsDisposed);

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        Element client = Gst.Global.ParseLaunch(
            $"rtspsrc location=rtsp://127.0.0.1:{port}/test latency=0 ! fakesink sync=false");

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, client.SetState(State.Playing));

            Assert.True(
                PumpUntil(context, () => Volatile.Read(ref configured) > 0),
                "media-configure never fired: no client reached the mount.");

            // The handler survived the mount, which is what AddFactory is
            // hand written for.
            Assert.False(factory.IsDisposed);

            _output.WriteLine($"configured {Volatile.Read(ref configured)} media on port {port}");

            // Step zero of the teardown: the client says goodbye while the
            // server can still answer. rtspsrc joins its own task on the way
            // to NULL, and that task is waiting for this thread's pump, so
            // the state change cannot be made from this thread.
            System.Threading.Tasks.Task<StateChangeReturn> down = System.Threading.Tasks.Task.Run(() => client.SetState(State.Null));
            Assert.True(PumpUntil(context, () => down.IsCompleted), "rtspsrc never reached NULL.");
            Assert.NotEqual(StateChangeReturn.Failure, down.Result);
        }
        finally
        {
            client.Dispose();
        }

        // 1. Stop accepting.
        Assert.True(server.Detach(sourceId, context));

        // 2. Close every connection. The close itself completes later.
        server.ClientFilter((_, _) => RTSPFilterResult.Remove);

        // 3. Drop the sessions: a closing client does not remove its session,
        //    and it is the session going away that unprepares the media.
        using RTSPSessionPool? sessionPool = server.GetSessionPool();
        Assert.NotNull(sessionPool);
        sessionPool.Filter((_, _) => RTSPFilterResult.Remove);

        // 4. Wait for the asynchronous half of the close.
        Assert.True(
            PumpUntil(context, () => server.ClientFilter(null).Count == 0),
            "a client was still managed after the filter removed it.");

        Assert.Equal(0u, sessionPool.GetNSessions());
    }

    /// <summary>
    /// Iterates <paramref name="context"/> until <paramref name="done"/>
    /// answers <see langword="true"/> or <see cref="Deadline"/> passes.
    /// </summary>
    /// <param name="context">The context that drives the server.</param>
    /// <param name="done">The condition that ends the wait.</param>
    /// <returns>
    /// <see langword="true"/> when the condition was met before the deadline.
    /// </returns>
    /// <remarks>
    /// The iteration never blocks: <c>mayBlock</c> is false, so a context with
    /// nothing to dispatch answers immediately and the sleep below is what
    /// keeps the loop from spinning. A blocking iteration would be fine for the
    /// server alone, but the conditions here are also fed by other threads —
    /// the media bus thread, the task that lowers the client — and those would
    /// not wake the context.
    /// </remarks>
    private static bool PumpUntil(MainContext context, Func<bool> done)
    {
        System.DateTime end = System.DateTime.UtcNow + Deadline;

        while (true)
        {
            while (context.Iteration(false))
            {
                if (done())
                {
                    return true;
                }
            }

            if (done())
            {
                return true;
            }

            if (System.DateTime.UtcNow >= end)
            {
                return false;
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>
    /// Reads the reference count out of a <c>GObject</c>, which follows the
    /// <c>GTypeInstance</c> its first field is.
    /// </summary>
    /// <param name="handle">The instance.</param>
    /// <returns>The current reference count.</returns>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));
}
