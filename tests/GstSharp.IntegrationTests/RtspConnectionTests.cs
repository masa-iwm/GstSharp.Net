using System.Threading.Tasks;
using Gst.GLib;
using Gst.Rtsp;
using Gst.RtspServer;
using Gst.Sdp;
using Xunit;
using Xunit.Abstractions;
using DateTime = System.DateTime;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The client side of the Rtsp module against a real server: a connection on
/// the loopback interface asks a mount for its options and for its
/// description, and reads both answers back.
/// </summary>
/// <remarks>
/// <para>
/// The server is built the way <c>RtspServerTests</c> builds one - port 0,
/// <c>SetMaxThreads(0)</c>, attached to a context this test thread iterates -
/// so the test thread is the engine of the server and must never block on it.
/// <see cref="RTSPConnection.SendUsec"/> and
/// <see cref="RTSPConnection.ReceiveUsec"/> do block, so the whole client
/// exchange runs on a task and the test thread does nothing but pump until
/// that task is finished. A blocking receive on this thread would be waiting
/// for a server that is waiting for it.
/// </para>
/// <para>
/// No request below carries a <c>CSeq</c> header of its own. The connection
/// writes one out of its own counter when it turns a request into bytes
/// (gstrtspconnection.c:1954-1960, the request branch of
/// <c>message_to_string</c>), so a hand written one would be a second
/// <c>CSeq</c> on the wire.
/// </para>
/// <para>
/// Upstream mirror: <c>rtspconnection.c</c>'s
/// <c>test_rtspconnection_send_receive</c>.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspConnectionTests
{
    private const string Launch = "( audiotestsrc is-live=true ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    private const long RequestTimeout = 5L * 1000 * 1000;

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspConnectionTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// One connection sends an <c>OPTIONS</c> and a <c>DESCRIBE</c> to a mount
    /// of a server on the loopback interface, and the server answers both: the
    /// methods it supports, and an SDP describing the one audio stream of the
    /// mount.
    /// </summary>
    [RequiresElementFact("rtpL16pay", "audiotestsrc", "audioconvert")]
    public void OptionsAndDescribeRoundTripOverALoopbackConnection()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);

        // Every client lands on the context of the source that dispatched it,
        // which is the one below: this test thread drives the whole server.
        threadPool.SetMaxThreads(0);

        RTSPMountPoints? mounts = server.GetMountPoints();
        Assert.NotNull(mounts);

        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);
        factory.SetShared(true);

        mounts.AddFactory("/test", factory);

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        Task<Exchange> exchange = Task.Run(() => Converse(port));

        Assert.True(
            PumpUntil(context, () => exchange.IsCompleted),
            "the client exchange never finished while this thread drove the server.");

        Exchange answers = exchange.GetAwaiter().GetResult();

        _output.WriteLine($"Public: {string.Join(", ", answers.Public)}");
        _output.WriteLine(answers.Sdp);

        Assert.Contains("DESCRIBE", answers.Public);
        Assert.Contains("SETUP", answers.Public);

        Assert.Equal(SDPResult.Ok, SDPMessage.New(out SDPMessage? described));
        Assert.NotNull(described);

        using (described)
        {
            Assert.Equal(SDPResult.Ok, SDPMessage.ParseBuffer(answers.Body, described));
            Assert.Equal(1u, described.MediasLen());
            Assert.Equal("audio", described.GetMedia(0).GetMedia());
        }

        // 1. Stop accepting.
        Assert.True(server.Detach(sourceId, context));

        // 2. Close every connection; the close itself completes later.
        Assert.Empty(server.ClientFilter((_, _) => RTSPFilterResult.Remove));

        // 3. Drop the sessions: DESCRIBE makes none, but the order is the one
        //    the documentation gives and is kept whole here.
        RTSPSessionPool? sessionPool = server.GetSessionPool();
        Assert.NotNull(sessionPool);
        Assert.Empty(sessionPool.Filter((_, _) => RTSPFilterResult.Remove));

        // 4. Wait for the asynchronous half of the close.
        Assert.True(
            PumpUntil(context, () => DisposeAll(server.ClientFilter(null)) == 0),
            "a client was still managed after the filter removed it.");
    }

    /// <summary>
    /// Runs the whole client side of the exchange on a thread of its own.
    /// </summary>
    /// <param name="port">The port the server bound.</param>
    /// <returns>What the two answers carried.</returns>
    private static Exchange Converse(int port)
    {
        string location = $"rtsp://127.0.0.1:{port}/test";

        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse(location, out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            Assert.Equal(RTSPResult.Ok, RTSPConnection.Create(url, out RTSPConnection? connection));
            Assert.NotNull(connection);

            try
            {
                Assert.Equal(RTSPResult.Ok, connection.ConnectUsec(RequestTimeout));

                RTSPMessage options = Request(connection, RTSPMethod.Options, location, null);
                List<string> supported = [];

                using (options)
                {
                    // The server writes one Public header with every method
                    // in it, and the client side splits a header that may
                    // appear more than once at each of its commas. Public is
                    // such a header (gstrtspdefs.c:110, the {"Public", TRUE}
                    // row that gst_rtsp_header_allow_multiple reads at
                    // :535-540), so parse_line runs its comma scan over the
                    // value (gstrtspconnection.c:2490 onward) and the methods
                    // arrive as one entry each.
                    for (int i = 0; ; i++)
                    {
                        if (options.GetHeader(RTSPHeaderField.Public, out string? method, i) != RTSPResult.Ok)
                        {
                            break;
                        }

                        Assert.NotNull(method);
                        supported.Add(method);
                    }
                }

                Assert.NotEmpty(supported);

                RTSPMessage describe = Request(connection, RTSPMethod.Describe, location, "application/sdp");
                string? contentType;
                byte[]? body;

                using (describe)
                {
                    Assert.Equal(
                        RTSPResult.Ok,
                        describe.GetHeader(RTSPHeaderField.ContentType, out contentType, 0));
                    Assert.Equal(RTSPResult.Ok, describe.GetBody(out body));
                }

                Assert.Equal("application/sdp", contentType);
                Assert.NotNull(body);

                return new Exchange(supported, body, System.Text.Encoding.UTF8.GetString(body));
            }
            finally
            {
                // Close and free are the two halves of the teardown, and the
                // wrapper of this opaque record does neither on its own: it is
                // not disposable, so nothing here frees twice.
                connection.Close();
                Assert.Equal(RTSPResult.Ok, connection.Free());
            }
        }
    }

    /// <summary>
    /// Sends one request and reads the response that answers it.
    /// </summary>
    /// <param name="connection">The connection, already connected.</param>
    /// <param name="method">The method of the request.</param>
    /// <param name="uri">The request URI.</param>
    /// <param name="accept">What to accept, or <see langword="null"/>.</param>
    /// <returns>The response, which the caller disposes.</returns>
    private static RTSPMessage Request(
        RTSPConnection connection,
        RTSPMethod method,
        string uri,
        string? accept)
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? request));
        Assert.NotNull(request);

        using (request)
        {
            Assert.Equal(RTSPResult.Ok, request.InitRequest(method, uri));

            if (accept is not null)
            {
                Assert.Equal(RTSPResult.Ok, request.AddHeader(RTSPHeaderField.Accept, accept));
            }

            Assert.Equal(RTSPResult.Ok, connection.SendUsec(request, RequestTimeout));
        }

        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? response));
        Assert.NotNull(response);

        Assert.Equal(RTSPResult.Ok, connection.ReceiveUsec(response, RequestTimeout));
        Assert.Equal(
            RTSPResult.Ok,
            response.ParseResponse(out RTSPStatusCode code, out string? reason, out _));
        Assert.Equal(RTSPStatusCode.Ok, code);
        Assert.NotNull(reason);

        return response;
    }

    /// <summary>
    /// Disposes every wrapper of a transfer full list and answers how many
    /// there were.
    /// </summary>
    /// <typeparam name="T">The wrapper type of the list.</typeparam>
    /// <param name="owned">The list a filter answered.</param>
    /// <returns>The number of items the list held.</returns>
    private static int DisposeAll<T>(IReadOnlyList<T> owned)
        where T : Gst.GObject.Object
    {
        foreach (T item in owned)
        {
            item.Dispose();
        }

        return owned.Count;
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
    private static bool PumpUntil(MainContext context, Func<bool> done)
    {
        DateTime end = DateTime.UtcNow + Deadline;

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

            if (DateTime.UtcNow >= end)
            {
                return false;
            }

            Thread.Sleep(5);
        }
    }

    /// <summary>What the two responses of the exchange carried.</summary>
    /// <param name="Public">The methods the <c>Public</c> header of the options response named.</param>
    /// <param name="Body">The body of the describe response.</param>
    /// <param name="Sdp">The same body as text, for the test output.</param>
    private sealed record Exchange(IReadOnlyList<string> Public, byte[] Body, string Sdp);
}
