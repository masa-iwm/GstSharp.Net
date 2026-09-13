using Gst.Rtsp;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written <see cref="RTSPTransport.Parse"/> of
/// <c>Custom/RTSPTransport.cs</c>: a transport header read into an object, and
/// the failing branch that has to free the object the C allocated before it
/// gave up.
/// </summary>
/// <remarks>
/// <c>gst_rtsp_transport_parse</c> allocates the transport itself and fills it
/// in, so a parse that fails leaves an object behind that nobody would ever
/// see. The wrapper frees it and answers nothing, which is the branch the
/// second test is here for: without it the failure would be a leak per
/// malformed header.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspTransportTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspTransportTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A transport header parses into the fields it names and prints back into
    /// a header that still carries them.
    /// </summary>
    [Fact]
    public void ATransportStringParsesAndPrintsBack()
    {
        Assert.Equal(
            RTSPResult.Ok,
            RTSPTransport.Parse("RTP/AVP;unicast;client_port=5000-5001", out RTSPTransport? transport));
        Assert.NotNull(transport);

        try
        {
            Assert.Equal(RTSPResult.Ok, transport.GetMediaType(out string? mediaType));
            Assert.Equal("application/x-rtp", mediaType);

            Assert.Equal(RTSPTransMode.Rtp, transport.Trans);
            Assert.Equal(RTSPLowerTrans.Udp, transport.LowerTransport);
            Assert.Equal(5000, transport.ClientPort.Min);
            Assert.Equal(5001, transport.ClientPort.Max);

            string? text = transport.AsText();
            Assert.NotNull(text);

            _output.WriteLine(text);

            Assert.Contains("client_port=5000-5001", text, StringComparison.Ordinal);
            Assert.Contains("unicast", text, StringComparison.Ordinal);
        }
        finally
        {
            Assert.Equal(RTSPResult.Ok, transport.Free());
        }
    }

    /// <summary>
    /// A header that is not a transport answers no object at all, and the
    /// object the C had already allocated is not left behind.
    /// </summary>
    [Fact]
    public void AnUnparsableTransportAnswersNoObject()
    {
        RTSPResult result = RTSPTransport.Parse("not a transport", out RTSPTransport? transport);

        _output.WriteLine($"the parse answered {result}");

        Assert.NotEqual(RTSPResult.Ok, result);
        Assert.Null(transport);
    }
}
