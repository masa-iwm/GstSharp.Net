using Gst;
using Gst.Sdp;
using Gst.WebRTC;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The scenario the WebRTC binding advertises, end to end: a
/// <c>webrtcbin</c> is asked for an offer and the offer is read out of the
/// reply.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that says the WebRTC surface is complete rather than
/// half of one. Every endpoint could already <em>receive</em> a description —
/// <c>WebRTCSessionDescription.New</c> builds one out of an SDP message and
/// <c>set-remote-description</c> takes it — and none could produce one, because
/// <c>create-offer</c> answers through a promise whose reply is a
/// <see cref="Structure"/> with a boxed field in it, and no bound call could
/// read a field as a <c>GValue</c>. The three pieces that close it are all
/// exercised below: <see cref="Gst.GObject.Object.EmitSignal(string, object?[])"/>
/// for the action signal, <see cref="Structure.GetBoxed{T}(string)"/> for the
/// field, and the type registry for the wrapper.
/// </para>
/// <para>
/// <c>webrtcbin</c> lives in the bad plugins, which the CI matrix installs as
/// libraries without the plugins on Linux and MinGW, so the test is gated on
/// the element being there. Where it is absent the gap is a skip with a reason
/// rather than a silent pass.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class WebRTCOfferTests
{
    /// <summary>
    /// How long the offer may take. Building one is local work — no network, no
    /// ICE gathering — so this is a deadlock guard rather than a budget.
    /// </summary>
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// One audio transceiver, which is what gives the offer a media section.
    /// The payload and the clock rate are the ones an Opus offer carries; no
    /// encoder is involved, because a transceiver negotiates caps rather than
    /// encoding anything.
    /// </summary>
    private const string AudioCaps =
        "application/x-rtp,media=(string)audio,encoding-name=(string)OPUS,payload=(int)96,clock-rate=(int)48000";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public WebRTCOfferTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.WebRTC.GstWebRTC: the boxed type of the session description
        // is only in the registry once the module initialiser of this assembly
        // has run, and naming the type in a generic argument is not a call into
        // it.
        GstWebRTC.Initialize();
    }

    /// <summary>
    /// <c>create-offer</c> answers with a session description, and the binding
    /// reads it out of the promise reply.
    /// </summary>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void CreateOfferAnswersWithASessionDescriptionInItsPromiseReply()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-offer");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "sendrecv"));
        Assert.True(pipeline.Add(webrtc));

        try
        {
            // webrtcbin starts the thread that answers a promise when it leaves
            // the NULL state, and refuses to build an offer before that.
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            AddAudioTransceiver(webrtc);

            using Structure reply = CreateOffer(webrtc);

            _output.WriteLine($"the promise replied with {reply}");

            // The failure path of webrtcbin is a reply as well, and its field
            // is called "error". Saying so here turns a wrong state into a
            // diagnosis instead of a null.
            Assert.False(
                reply.HasField("error"),
                $"webrtcbin refused to build an offer: {reply}");

            // The bridge under test: a boxed field read back as the wrapper of
            // its own module, through the type registry.
            using WebRTCSessionDescription? offer = reply.GetBoxed<WebRTCSessionDescription>("offer");

            Assert.NotNull(offer);
            Assert.Equal(WebRTCSDPType.Offer, offer.Type);

            using SDPMessage sdp = offer.GetSdp();
            string text = sdp.AsText();

            Assert.StartsWith("v=0", text, StringComparison.Ordinal);
            Assert.Contains("m=audio", text, StringComparison.Ordinal);
            Assert.Contains("OPUS/48000", text, StringComparison.Ordinal);
            Assert.True(sdp.MediasLen() >= 1);

            _output.WriteLine(text);
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// The binary half of a data channel: a channel created through the action
    /// signal carries a handler for its binary message signal, and a send on a
    /// channel that is not open is refused rather than fatal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A message that actually arrives needs two peers and a completed
    /// negotiation, which is not what the integration suite of a binding should
    /// build. What is measured here is what the binding owns: that the signal
    /// the generator skips can be connected and disconnected — connecting to a
    /// signal that does not exist throws — and that
    /// <see cref="WebRTCDataChannel.SendData"/> answers a channel that never
    /// opened instead of following the library into a null dereference.
    /// </para>
    /// <para>
    /// <b>That last one is not a formality.</b> Both the deprecated
    /// <c>send-data</c> action signal and
    /// <c>gst_webrtc_data_channel_send_data_full</c> segfault on a channel in
    /// the connecting state, while the string half of the same class fails an
    /// assertion and returns. The <see cref="WebRTCDataChannel.ReadyState"/>
    /// check in the binding is what stands between an application and that
    /// crash, and this is the test that says it is there.
    /// </para>
    /// <para>
    /// The channel is a <c>GstWebRTCDataChannel</c>, which is a type of this
    /// module arriving from a signal of an element of another one — the case
    /// the type registry exists for. That the cast below succeeds is that
    /// registration, measured.
    /// </para>
    /// </remarks>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void ADataChannelRefusesABinarySendBeforeItIsOpenAndCarriesAMessageHandler()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-data");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "sendrecv"));
        Assert.True(pipeline.Add(webrtc));

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            object? created = webrtc.EmitSignal("create-data-channel", "test-channel", null);
            WebRTCDataChannel channel = Assert.IsAssignableFrom<WebRTCDataChannel>(created);

            // Nobody is on the other end, so the channel never opens. The
            // number it reports is the zero of the C enumeration, which the
            // introspection data does not describe and the bound enumeration
            // therefore has no name for; what matters is that it is not Open.
            Assert.NotEqual(WebRTCDataChannelState.Open, channel.ReadyState);

            int received = 0;
            byte[]? kept = null;

            void OnMessage(object? sender, WebRTCDataChannel.OnMessageDataSignalArgs args)
            {
                Interlocked.Increment(ref received);

                // The block belongs to the binding and is gone when this
                // returns, so keeping it means copying it.
                kept = args.Data?.ToArray();
            }

            // Connecting is the assertion: ConnectSignal refuses a signal the
            // object does not have, so reaching the next line says that
            // "on-message-data" was found and that the trampoline matched it.
            channel.OnMessageData += OnMessage;

            try
            {
                Assert.False(channel.SendData([1, 2, 3, 4]));
                Assert.False(channel.SendData([]));

                Assert.Equal(0, Volatile.Read(ref received));
                Assert.Null(kept);
            }
            finally
            {
                channel.OnMessageData -= OnMessage;
            }

            _output.WriteLine($"the data channel is a {channel.NativeType.Name} in state {channel.ReadyState}");
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Adds one audio transceiver, so that the offer has a media section.
    /// </summary>
    /// <param name="webrtc">The <c>webrtcbin</c> to add it to.</param>
    /// <remarks>
    /// <c>add-transceiver</c> is an action signal whose arguments are an
    /// enumeration and a <c>GstCaps</c>. The caps travel as their handle, which
    /// is how <see cref="Gst.GObject.Value.CreateFor"/> takes a mini object: the
    /// value copies it through the boxed copy function, which for a mini object
    /// is <c>gst_mini_object_ref</c>, so the wrapper stays the owner of the
    /// reference it holds.
    /// </remarks>
    private static void AddAudioTransceiver(Element webrtc)
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString(AudioCaps));

        object? transceiver = webrtc.EmitSignal(
            "add-transceiver",
            WebRTCRTPTransceiverDirection.Sendrecv,
            caps.Handle);

        Assert.IsAssignableFrom<WebRTCRTPTransceiver>(transceiver);
    }

    /// <summary>
    /// Emits <c>create-offer</c> and waits for the reply.
    /// </summary>
    /// <param name="webrtc">The <c>webrtcbin</c> to ask.</param>
    /// <returns>The reply of the promise, which the caller disposes.</returns>
    /// <remarks>
    /// The promise is created with a change function rather than waited on
    /// directly, so that a <c>webrtcbin</c> that never answers fails this test
    /// instead of hanging the run. <see cref="Gst.Promise.Wait"/> returns
    /// immediately once the state has moved, which the event below reports.
    /// </remarks>
    private static Structure CreateOffer(Element webrtc)
    {
        using ManualResetEventSlim replied = new(initialState: false);

        using Promise promise = Promise.NewWithChangeFunc(_ => replied.Set());

        // Two arguments: the options structure, which nothing here needs, and
        // the promise, which travels as its handle the way every boxed
        // argument does. The promise is a mini object, so the value refs it and
        // this wrapper keeps the reference it owns.
        Assert.Null(webrtc.EmitSignal("create-offer", (object?)null, promise.Handle));

        Assert.True(
            replied.Wait(ReplyTimeout),
            "webrtcbin never answered the create-offer promise.");

        Assert.Equal(PromiseResult.Replied, promise.Wait());

        return Assert.IsType<Structure>(promise.GetReply());
    }
}
