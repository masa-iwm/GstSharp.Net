using Gst;
using Gst.WebRTC;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two candidate listings of the ICE agent of a <c>webrtcbin</c>: both
/// answer an array the caller owns, and both are readable on a stream that has
/// gathered nothing yet.
/// </summary>
/// <remarks>
/// <para>
/// The agent is the <c>ice-agent</c> property of <c>webrtcbin</c>, which is
/// the only way a user reaches the one the element built for itself, and the
/// stream is minted with <c>AddStream</c>. Nothing here negotiates: gathering
/// needs a network and a peer, so what is measured is the shape of the answer
/// and the ownership of it, not its content.
/// </para>
/// <para>
/// <c>GstWebRTCICE</c> is abstract and the two getters are a virtual method on
/// it, so the test needs the nice implementation the plugin brings; the
/// element requirement is what gates it.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class WebRTCICECandidateTests
{
    /// <summary>Initialises one test.</summary>
    public WebRTCICECandidateTests() => GstWebRTC.Initialize();

    /// <summary>
    /// A stream that has gathered nothing answers both getters with an array
    /// the caller owns, and calling them twice is not a double free: each call
    /// adopts its own elements and releases the block they came in on its own.
    /// </summary>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void AnIceAgentListsTheCandidatesOfAStreamItWasGiven()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-candidates");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "candidates"));
        Assert.True(pipeline.Add(webrtc));

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

        using WebRTCICE ice = webrtc.GetProperty<WebRTCICE>("ice-agent");

        // webrtcbin builds its agent with g_object_new and keeps the floating
        // reference that comes out of it, without ever sinking one
        // (gstwebrtcbin.c: the agent is built in constructed and released with
        // gst_object_unref in dispose). Wrapping the object sinks that
        // reference into the wrapper, which leaves the element holding a
        // pointer it no longer owns, so the reference the element is counted
        // on to have is handed back here. It is deliberately never released:
        // gst_webrtc_bin_dispose is what releases it.
        Gst.Interop.GObjectNative.ObjectRef(ice.Handle);

        using WebRTCICEStream? stream = ice.AddStream(1);
        Assert.NotNull(stream);

        for (int round = 0; round < 2; round++)
        {
            WebRTCICECandidateStats[] local = ice.GetLocalCandidates(stream);
            WebRTCICECandidateStats[] remote = ice.GetRemoteCandidates(stream);

            Assert.NotNull(local);
            Assert.NotNull(remote);

            // A stream that has gathered nothing answers the empty array, and
            // one that has gathered answers candidates the caller owns; both
            // are a pass, and disposing what came back is what the ownership
            // of the elements means.
            foreach (WebRTCICECandidateStats candidate in local)
            {
                candidate.Dispose();
            }

            foreach (WebRTCICECandidateStats candidate in remote)
            {
                candidate.Dispose();
            }
        }

        pipeline.SetState(State.Null);
    }
}
