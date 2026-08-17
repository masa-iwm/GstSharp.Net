using Gst;
using Gst.WebRTC;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Closing a data channel.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_webrtc_data_channel_close</c> is a function of the library as well as
/// the <c>close</c> action signal — the function emits the signal, whose
/// default handler is the class's virtual method — so the generated
/// <see cref="WebRTCDataChannel.Close"/> is the whole binding and nothing has
/// to be emitted by name. Both routes are exercised below, because that
/// equivalence is the reason no hand written binding is needed.
/// </para>
/// <para>
/// <b>What a close can be observed to do without a peer is very little, and
/// this test says only what it measured.</b> A channel that
/// <c>webrtcbin</c> never associated with an SCTP transport has nowhere to
/// queue the closing procedure, so it stays in the state it was created in and
/// <c>on-close</c> does not fire. What the test holds the binding to is the
/// part the binding owns: the call resolves, it does not throw, it is safe to
/// repeat, and it never leaves the channel reporting
/// <see cref="WebRTCDataChannelState.Open"/>.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class WebRTCDataChannelCloseTests
{
    /// <summary>How long an asynchronous state change is given to appear.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public WebRTCDataChannelCloseTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A channel that never reached a peer accepts a close, through the
    /// function and through the action signal behind it, and closing it twice
    /// is not an error.
    /// </summary>
    [RequiresElementFact("webrtcbin", "nicesrc", "dtlssrtpenc")]
    public void AChannelThatNeverOpenedCanBeClosed()
    {
        using Pipeline pipeline = Pipeline.New("webrtc-close");

        Element webrtc = Assert.IsAssignableFrom<Element>(ElementFactory.Make("webrtcbin", "closer"));
        Assert.True(pipeline.Add(webrtc));

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Ready));

            object? created = webrtc.EmitSignal("create-data-channel", "test-channel", null);
            WebRTCDataChannel channel = Assert.IsAssignableFrom<WebRTCDataChannel>(created);

            WebRTCDataChannelState before = channel.ReadyState;
            Assert.NotEqual(WebRTCDataChannelState.Open, before);

            int closedNotifications = 0;
            ulong handler = channel.ConnectSignal("on-close", (_, _) =>
            {
                Interlocked.Increment(ref closedNotifications);
                return null;
            });

            try
            {
                // The generated binding of gst_webrtc_data_channel_close. A
                // missing entry point would surface here.
                channel.Close();
                Assert.NotEqual(WebRTCDataChannelState.Open, channel.ReadyState);

                // The closing procedure is asynchronous where it happens at
                // all, so the channel is given a moment before it is read.
                Thread.Sleep(SettleTime);

                WebRTCDataChannelState settled = channel.ReadyState;
                _output.WriteLine(
                    $"{before} -> {settled}, on-close fired {Volatile.Read(ref closedNotifications)} time(s)");

                Assert.NotEqual(WebRTCDataChannelState.Open, settled);

                // Closing twice, and closing through the action signal the
                // function stands for, are both allowed.
                channel.Close();
                Assert.Null(channel.EmitSignal("close"));

                Assert.NotEqual(WebRTCDataChannelState.Open, channel.ReadyState);
            }
            finally
            {
                channel.RemoveHandler(handler);
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }
}
