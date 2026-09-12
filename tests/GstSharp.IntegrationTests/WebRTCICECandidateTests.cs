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

        // webrtcbin creates its ICE agent with g_object_new and never sinks
        // it. It is built in constructed out of gst_webrtc_nice_new
        // (gstwebrtcbin.c:9069), which is a bare g_object_new
        // (nice.c:1935), and unlike every other child the element owns it is
        // never handed to gst_object_ref_sink, while dispose releases it with
        // gst_object_unref (gstwebrtcbin.c:9092). The one reference the
        // element holds is therefore still the floating one, which GObject
        // defines as owned by nobody. Reading the property adds a temporary
        // reference of its own, the binding sinks the floating handle into the
        // wrapper as it must, and once that temporary reference is dropped the
        // wrapper holds the only reference there is. The g_object_ref below
        // restores the reference webrtcbin should have taken, and is
        // deliberately never released, because gst_webrtc_bin_dispose is what
        // releases it. Remove it when upstream sinks the agent: it is then one
        // leaked object per run.
        Gst.Interop.GObjectNative.ObjectRef(ice.Handle);

        using WebRTCICEStream? stream = ice.AddStream(1);
        Assert.NotNull(stream);

        for (int round = 0; round < 2; round++)
        {
            WebRTCICECandidateStats[] local = ice.GetLocalCandidates(stream);
            WebRTCICECandidateStats[] remote = ice.GetRemoteCandidates(stream);

            // Nothing has gathered and no peer has answered, so both listings
            // are the empty array the walk produces for a block that holds
            // nothing but its terminator. That is the whole measurement here:
            // a candidate handed to the agent by name did not become visible
            // to these getters before gathering when this test was written, so
            // there is no non-empty answer to be had without a network.
            Assert.Empty(local);
            Assert.Empty(remote);

            // Disposing what came back is what the ownership of the elements
            // means, and the second round is what says releasing the block did
            // not take the state of the agent with it.
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
