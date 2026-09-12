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

            // Construct hands the media out locked, and the stream count has
            // to be read before the media is handed over: the constructor of a
            // session media consumes the wrapper.
            media.Unlock();
            uint streams = media.NStreams();
            Assert.Equal(1u, streams);

            Task<bool> preparing = Task.Run(() => media.Prepare(null));
            Assert.True(
                PumpUntil(() => preparing.IsCompleted),
                "the media never finished preparing.");
            Assert.True(preparing.Result, "the media refused to prepare.");

            RTSPSessionMedia sessionMedia = RTSPSessionMedia.New("/test", media);
            Assert.True(media.IsDisposed);

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
