using Gst;
using Gst.WebRTC;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The ownership of what <c>WebRTCICE.AddStream</c> hands out: the agent keeps
/// the stream in its own map and the caller gets a reference of its own, on
/// every runtime the binding supports.
/// </summary>
/// <remarks>
/// <para>
/// The call is annotated <c>(transfer full)</c>, but the reference is missing
/// in every 1.24.x release and in 1.26.0 through 1.26.9; it is present from
/// 1.26.10 and from 1.27.50 / 1.28.0 on, the first tags of upstream
/// <c>ea6200de</c> (MR 10312) and of its 1.26 backport <c>dbc83c3ec1</c>.
/// Before the fix the C returned the agent's own reference untouched, so a
/// wrapper that adopted it freed a stream the agent still listed. The binding
/// takes the missing reference on the older runtimes, which is what this test
/// measures — and it measures the same number on a 1.28 runtime, where the C
/// takes it instead.
/// </para>
/// <para>
/// <c>GstWebRTCICE</c> is abstract and <c>add_stream</c> is a virtual method on
/// it, so the test needs the nice implementation the plugin brings; the element
/// requirement is what gates it.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class WebRTCICEAddStreamTests
{
    /// <summary>Initialises one test.</summary>
    public WebRTCICEAddStreamTests() => GstWebRTC.Initialize();

    /// <summary>
    /// A stream that was just added is held twice: once by the stream map of
    /// the agent that created it, once by the wrapper the call handed back.
    /// </summary>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void AnAddedStreamIsHeldByTheAgentAndByTheWrapper()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-add-stream");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "add-stream"));
        Assert.True(pipeline.Add(webrtc));

        Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

        using WebRTCICE ice = webrtc.GetProperty<WebRTCICE>("ice-agent");

        // The reference webrtcbin should have taken: as of 1.28 it builds its
        // ICE agent with a bare g_object_new and never sinks it, so reading the
        // property and sinking the floating handle into the wrapper would
        // otherwise leave the element with no reference of its own. See the
        // comment on WebRTCICECandidateTests, which this preamble is taken
        // from. It is deliberately never released.
        Gst.Interop.GObjectNative.ObjectRef(ice.Handle);

        using WebRTCICEStream stream =
            ice.AddStream(1) ?? throw new InvalidOperationException("The ICE agent added no stream.");

        // Two references: one held by the stream map of the agent, one by the
        // wrapper. That count is the same on every runtime, but it is reached
        // in two different ways — from 1.26.10 and from 1.27.50 / 1.28.0 on the
        // C takes the second one itself, and before that the binding takes it.
        // Without the fix a runtime that predates it (every 1.24.x release, and
        // 1.26.0 through 1.26.9) reads 1 here, because the wrapper adopted the
        // agent's only reference and disposing it would free a stream the agent
        // still lists.
        Assert.Equal(2u, RefCountOf(stream.Handle));
        GC.KeepAlive(stream);

        pipeline.SetState(State.Null);
    }

    /// <summary>Reads the <c>ref_count</c> field of a <c>GObject</c>.</summary>
    /// <param name="handle">The instance to read.</param>
    /// <returns>The reference count at that moment.</returns>
    /// <remarks>
    /// A <c>GObject</c> begins with its <c>GTypeInstance</c>, which is one
    /// pointer, and the reference count is the field behind it.
    /// </remarks>
    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));
}
