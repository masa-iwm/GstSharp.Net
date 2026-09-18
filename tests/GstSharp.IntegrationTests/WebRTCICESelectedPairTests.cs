using Gst;
using Gst.WebRTC;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>WebRTCICE.GetSelectedPair</c>, the one call of the binding that answers
/// a version with an exception because the C answers it with an abort.
/// </summary>
/// <remarks>
/// <para>
/// Upstream commit <c>e4eb90d489</c> (first tags 1.27.50 and 1.28.0) removed
/// the libnice implementation of the vfunc and left the
/// <c>g_assert (GST_WEBRTC_ICE_GET_CLASS (ice)-&gt;get_selected_pair)</c> of
/// the dispatcher in place (1.28.6 <c>ice.c:326</c>), so on every 1.28.x the
/// call would abort the process. The hand written member throws
/// <see cref="NotSupportedException"/> there instead, which is what the 1.28
/// leg of this test measures — the process surviving the call is half of the
/// assertion.
/// </para>
/// <para>
/// Below 1.27.50 the implementation is there and answers the question. A stream
/// fresh from <c>AddStream</c> has selected nothing, which
/// <c>gst_webrtc_nice_get_selected_pair</c> (nice.c 1.24.13:1249-1274,
/// identical at 1.26.11) answers with <c>FALSE</c>, leaving both out pointers
/// untouched: no <c>g_return_val_if_fail</c> of that path fires on such a
/// stream, nothing is logged and nothing is allocated. The wrapper starts both
/// natives at zero, so both wrappers come back <see langword="null"/>. That
/// leg runs on the Linux floor of the matrix.
/// </para>
/// <para>
/// The agent is the <c>ice-agent</c> property of <c>webrtcbin</c>, read the
/// way <c>WebRTCICECandidateTests</c> reads it, including the reference the
/// element should have taken of it.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class WebRTCICESelectedPairTests
{
    /// <summary>Initialises one test.</summary>
    public WebRTCICESelectedPairTests() => GstWebRTC.Initialize();

    /// <summary>
    /// The call refuses itself on a library whose implementation of it is
    /// gone, and answers "no pair" on one that still has it. Either way the
    /// process is alive afterwards.
    /// </summary>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void TheSelectedPairOfAStreamThatSelectedNothing()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-selected-pair");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "selected-pair"));
        Assert.True(pipeline.Add(webrtc));

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

        using WebRTCICE ice = webrtc.GetProperty<WebRTCICE>("ice-agent");

        // The agent webrtcbin builds is never sunk (gst_webrtc_nice_new is a
        // bare g_object_new, nice.c:1935), so reading the property hands the
        // floating reference to the wrapper; this restores the one the element
        // should have taken and gst_webrtc_bin_dispose releases. See the long
        // note in WebRTCICECandidateTests, which owns this story.
        Gst.Interop.GObjectNative.ObjectRef(ice.Handle);

        using WebRTCICEStream? stream = ice.AddStream(1);
        Assert.NotNull(stream);

        // gst_element_dispose refuses an element that is not in NULL
        // (1.28.6 gstelement.c:3423-3431: a g_critical and no dispose), so the
        // pipeline goes back before the using disposes it, on the failing path
        // too.
        try
        {
            // The member under test is deprecated upstream as of 1.28, which
            // is what the binding marks it with; the test of a deprecated
            // member is where the obsolete call is the point.
#pragma warning disable CS0618
            // The member gates on 1.27.50 while this branches on the symbol
            // NativeAvailability probes, whose first tag is 1.27.90, so the
            // unstable 1.27 band is knowingly unmeasured here: no release
            // series lives there.
            if (NativeAvailability.Has128)
            {
                Assert.Throws<NotSupportedException>(
                    () => ice.GetSelectedPair(stream, out _, out _));
            }
            else
            {
                bool selected = ice.GetSelectedPair(
                    stream,
                    out WebRTCICECandidateStats? localStats,
                    out WebRTCICECandidateStats? remoteStats);

                Assert.False(selected);
                Assert.Null(localStats);
                Assert.Null(remoteStats);
            }
#pragma warning restore CS0618
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
