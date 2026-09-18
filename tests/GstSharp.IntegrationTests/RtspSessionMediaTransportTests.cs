using System.Threading;
using System.Threading.Tasks;
using Gst.GLib;
using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The transports of a session media: one slot per stream of the media, all
/// empty until a client sets a stream up, and a container the reading does not
/// over-release.
/// </summary>
/// <remarks>
/// <para>
/// No client connects here. A session media is built straight out of a
/// prepared media, which is the state a server is in between the DESCRIBE that
/// created the session and the first SETUP, and that is exactly the state the
/// empty slots describe.
/// </para>
/// <para>
/// <c>gst_rtsp_session_media_new</c> refuses a media that is not prepared, and
/// preparing one without a <c>GstRTSPThread</c> — which this binding does not
/// hand out, see the thread entries of <c>girs/overlays/fixups.json</c> — runs
/// the preparation on the default main context. So the call is made on a task
/// while this thread iterates that context, the same pump the server tests
/// use: a blocking wait here would stop the very loop the preparation needs.
/// </para>
/// <para>
/// The preparation is bounded twice over - the pump below gives up after its
/// own deadline and <c>gst_rtsp_media_prepare</c> gives up after twenty
/// seconds of its own - so a failure here is red rather than a hang. What a
/// failed preparation does leave behind is the bus watch it attached to the
/// default main context, which every later test of this collection then
/// carries; the server tests run their own <c>MainContext</c> and are not
/// affected. That is accepted for as long as <c>GstRTSPThread</c> has no
/// managed spelling.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspSessionMediaTransportTests
{
    /// <summary>
    /// One payloader, so the media collects exactly one stream and the array
    /// has exactly one slot.
    /// </summary>
    private const string Launch = "( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    /// <summary>How long a preparation or an unpreparation may take.</summary>
    private static readonly System.TimeSpan Deadline = System.TimeSpan.FromSeconds(30);

    /// <summary>
    /// A session media that no client has set up answers one empty slot per
    /// stream, and answers it as often as it is asked: the container carries
    /// one reference for the caller and the elements stay the session's, so a
    /// second read is not a second release.
    /// </summary>
    [RequiresElementFact("rtpL16pay")]
    public void ASessionMediaAnswersOneEmptySlotPerStreamBeforeSetup()
    {
        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://127.0.0.1:8554/test", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            RTSPMedia? media = factory.Construct(url);
            Assert.NotNull(media);

            // Construct hands the media out locked.
            media.Unlock();
            uint streams = media.NStreams();
            Assert.Equal(1u, streams);

            Task<bool> preparing = Task.Run(() => media.Prepare(null));
            Assert.True(
                PumpUntil(() => preparing.IsCompleted),
                "the media never finished preparing.");
            Assert.True(preparing.Result, "the media refused to prepare.");

            RTSPSessionMedia sessionMedia = RTSPSessionMedia.New("/test", media);

            // The session media was handed a reference minted for it and the
            // wrapper keeps the one it holds, so it is still the caller's and
            // still answers.
            Assert.False(media.IsDisposed);
            Assert.Equal(RTSPMediaStatus.Prepared, media.GetStatus());

            using RTSPMedia? attached = sessionMedia.GetMedia();
            Assert.NotNull(attached);

            for (int round = 0; round < 2; round++)
            {
                RTSPStreamTransport?[] transports = sessionMedia.GetTransports();

                Assert.Equal((int)streams, transports.Length);
                Assert.All(transports, Assert.Null);

                // The single slot twin reads the same nothing.
                Assert.Null(sessionMedia.GetTransport(0));
            }

            // The session media survived both reads, which is what says the
            // container reference was dropped once per read and no more.
            Assert.Equal(streams, attached.NStreams());
            Assert.Equal(RTSPState.Init, sessionMedia.GetRtspState());

            sessionMedia.Dispose();

            Task<bool> unpreparing = Task.Run(attached.Unprepare);
            Assert.True(
                PumpUntil(() => unpreparing.IsCompleted),
                "the media never finished unpreparing.");
        }
    }

    /// <summary>
    /// <c>gst_rtsp_session_manage_media</c> is handed the media the session
    /// serves. The session keeps a reference minted for it, and the wrapper
    /// stays the caller's with the handlers it carries still connected.
    /// </summary>
    [RequiresElementFact("rtpL16pay")]
    public void ManagingAMediaKeepsTheMediaWrapperAndItsHandlers()
    {
        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://127.0.0.1:8554/managed", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            RTSPMedia? media = factory.Construct(url);
            Assert.NotNull(media);

            media.Unlock();

            // A handler the caller connects before handing the media over. The
            // consuming shape would have run DisconnectAll and taken it off
            // again; the handover leaves it connected, which the unpreparation
            // at the end of the test is what proves.
            int unprepared = 0;
            media.Unprepared += (_, _) => Interlocked.Increment(ref unprepared);

            Task<bool> preparing = Task.Run(() => media.Prepare(null));
            Assert.True(
                PumpUntil(() => preparing.IsCompleted),
                "the media never finished preparing.");
            Assert.True(preparing.Result, "the media refused to prepare.");

            using RTSPSession session = RTSPSession.New("handover-session");
            RTSPSessionMedia sessionMedia = session.ManageMedia("/managed", media);

            // The session media holds the minted reference; the wrapper is the
            // caller's and still answers.
            Assert.False(media.IsDisposed);
            Assert.Equal(RTSPMediaStatus.Prepared, media.GetStatus());

            using RTSPMedia? attached = sessionMedia.GetMedia();
            Assert.NotNull(attached);
            Assert.Same(media, attached);

            Task<bool> unpreparing = Task.Run(media.Unprepare);
            Assert.True(
                PumpUntil(() => unpreparing.IsCompleted),
                "the media never finished unpreparing.");

            Assert.True(
                PumpUntil(() => Volatile.Read(ref unprepared) > 0),
                "the handler connected before the handover never fired.");

            // The answer is whether the session still holds media, not
            // whether the release happened: this one was the only one.
            Assert.False(session.ReleaseMedia(sessionMedia));

            // The session media wrapper is the test's, the way the sibling
            // above treats its own: released explicitly rather than left to
            // the finalizer, which would run the unpreparation of the media
            // off the finalizer thread.
            sessionMedia.Dispose();
        }
    }

    /// <summary>
    /// <c>gst_rtsp_session_manage_media</c> refuses a media that is neither
    /// prepared nor suspended (rtsp-session.c:268-272) before it takes the
    /// reference minted for the call, so the binding releases that reference
    /// again and the count of the media lands back where it started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is prepared here and nothing is pumped: a media straight out of
    /// <c>Construct</c> is unprepared, which is the state the C refuses, so the
    /// test is a call and two reads.
    /// </para>
    /// <para>
    /// The refusal is a <c>g_return_val_if_fail</c>, so the run prints a GLib
    /// CRITICAL and goes on; the suite does not promote one to a failure. On
    /// 1.28 the check stands inside <c>#ifndef G_DISABLE_CHECKS</c>, so a
    /// library built without them manages the media instead. What the running
    /// library answered is what the assertions are gated on.
    /// </para>
    /// </remarks>
    [RequiresElementFact("rtpL16pay")]
    public void ManagingAnUnpreparedMediaReleasesTheMintedReference()
    {
        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsp://127.0.0.1:8554/refused", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            RTSPMedia? media = factory.Construct(url);
            Assert.NotNull(media);

            // Construct hands the media out locked, and leaves it unprepared.
            media.Unlock();
            Assert.Equal(RTSPMediaStatus.Unprepared, media.GetStatus());

            using RTSPSession session = RTSPSession.New("refusal-session");

            uint before = RefCountOf(media.Handle);
            RTSPSessionMedia? managed = null;
            try
            {
                managed = session.ManageMedia("/refused", media);
            }
            catch (System.InvalidOperationException)
            {
                // The refusal: the C answered NULL, and the member has a non
                // nullable return, so it raises rather than handing one out.
                // The release stands ahead of the raise.
            }

            uint after = RefCountOf(media.Handle);

            if (managed is null)
            {
                Assert.Equal(before, after);
                Assert.False(media.IsDisposed);
                Assert.Equal(RTSPMediaStatus.Unprepared, media.GetStatus());
                media.Dispose();
                return;
            }

            // A library built without its checks managed the media after all.
            // Then the session owns it, and what is left is to give it back.
            Assert.False(session.ReleaseMedia(managed));
            managed.Dispose();
            media.Dispose();
        }
    }

    /// <summary>Reads the <c>ref_count</c> field of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to read.</param>
    /// <returns>The reference count at that moment.</returns>
    /// <remarks>
    /// A <c>GObject</c> begins with its <c>GTypeInstance</c>, which is one
    /// pointer, and the reference count is the field behind it.
    /// </remarks>
    private static unsafe uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>
    /// Iterates the default main context until a condition holds or the
    /// deadline passes.
    /// </summary>
    /// <param name="done">The condition that ends the wait.</param>
    /// <returns>
    /// <see langword="true"/> when the condition was met before the deadline.
    /// </returns>
    private static bool PumpUntil(System.Func<bool> done)
    {
        MainContext context = MainContext.Default;
        System.DateTime end = System.DateTime.UtcNow + Deadline;

        while (true)
        {
            while (context.Iteration(false))
            {
                if (done())
                {
                    return true;
                }

                if (System.DateTime.UtcNow >= end)
                {
                    return false;
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
}
