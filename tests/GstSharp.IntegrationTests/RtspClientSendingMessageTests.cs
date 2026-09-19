using System.Net.Sockets;
using System.Text;
using Gst.GLib;
using Gst.Interop;
using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GstRTSPClient::send-message</c>, the one signal of the corpus whose
/// introspection data names an argument type the C never registered, and whose
/// event is therefore hand written rather than generated.
/// </summary>
/// <remarks>
/// <para>
/// The C registers the signal with
/// <c>(GST_TYPE_RTSP_CONTEXT, G_TYPE_POINTER)</c> and emits the context
/// together with the message it is about to write to the connection - a
/// response on every path this test drives, though
/// <c>gst_rtsp_client_send_message</c> takes a request just as well
/// (1.28.6 <c>rtsp-client.c:531-535</c> and <c>:935</c>), while the gir has
/// called the first argument a <c>GstRTSPSession</c> since 2014. The generated
/// event followed the gir and wrapped a structure on the stack of the caller as
/// a <c>GObject</c>, so it raised on every emission before the handler ran;
/// what is asserted here is that the hand written one reaches the handler, that
/// the context it carries is the real one, and that the message is lent rather
/// than copied.
/// </para>
/// <para>
/// A real client speaks RTSP over the loopback interface, as in
/// <see cref="RtspClientRequirementsTests"/>: the messages a server sends by
/// itself are the responses it owes, and nothing short of a request produces
/// one. The socket is spoken
/// from a task of its own, because the server takes its clients on the
/// iterating context - <c>SetMaxThreads(0)</c> - and a blocking read on the
/// test thread would stop the pump the answer has to come from.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspClientSendingMessageTests
{
    /// <summary>How long any wait here is allowed to take.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>The header the handler writes into the borrowed message.</summary>
    private const string ProbeHeader = "X-GstSharp-SendingMessage";

    /// <summary>What it says.</summary>
    private const string ProbeValue = "edited-in-place";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspClientSendingMessageTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The handler is reached, the context it is handed is the one of the
    /// request, and the header it adds to the lent message is the header the
    /// client reads off the wire.
    /// </summary>
    [RequiresElementFact("audiotestsrc", "audioconvert", "rtpL16pay")]
    public void TheHandlerIsHandedTheContextAndTheMessageTheClientIsAboutToRead()
    {
        int trapped = 0;
        string? firstTrap = null;
        void OnTrapped(Exception exception)
        {
            if (Interlocked.Increment(ref trapped) == 1)
            {
                firstTrap = exception.GetType().Name + ": " + exception.Message;
            }
        }

        ExceptionTrap.UnhandledException += OnTrapped;

        try
        {
            Run();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnTrapped;
        }

        Assert.Equal(0, trapped);
        Assert.Null(firstTrap);
    }

    /// <summary>Drives the server, the client and the assertions.</summary>
    private void Run()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);
        threadPool.SetMaxThreads(0);

        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch("( audiotestsrc is-live=true ! audioconvert ! rtpL16pay name=pay0 pt=96 )");

        RTSPMountPoints? mountPoints = server.GetMountPoints();
        Assert.NotNull(mountPoints);
        mountPoints.AddFactory("/test", factory);

        int handled = 0;
        int responses = 0;
        bool everySeenContextHadAClient = true;
        bool everySeenContextHadAConnection = true;
        bool secondHandlerSawTheHeader = true;
        int sessionsOnFirstResponse = -1;
        RTSPClient? client = null;

        server.ClientConnected += (_, connected) =>
        {
            client = connected.Object;

            connected.Object.SendingMessage += (_, args) =>
            {
                if (Interlocked.Increment(ref handled) == 1)
                {
                    // The first response of the exchange answers an OPTIONS
                    // request, which opens no session, so the context carries
                    // none. The obsolete property is what the generated shape
                    // promised, and reading it is the whole of its contract:
                    // it answers the session of the context, null included.
#pragma warning disable CS0618 // The shim is under test here, so its use is intended.
                    sessionsOnFirstResponse = args.Session is null ? 0 : 1;
#pragma warning restore CS0618
                }

                // The context is the one the client built for the request it is
                // answering: both of these are filled by handle_request itself
                // (rtsp-client.c:4018 and following), which is what a snapshot
                // taken over a wrong pointer could not show. The client is the
                // very wrapper the connection signal handed out, since a
                // GObject read out of a field is interned; it is not disposed
                // here, because that would release the reference for every
                // holder of it, the server's included. The connection is an
                // opaque record borrowed from the structure and owns nothing.
                everySeenContextHadAClient &= ReferenceEquals(args.Ctx.Client, connected.Object);
                everySeenContextHadAConnection &= args.Ctx.Conn is not null;

                if (args.Message.Type == RTSPMsgType.Response)
                {
                    Interlocked.Increment(ref responses);
                }

                args.Message.AddHeaderByName(ProbeHeader, ProbeValue);
            };

            // A second handler, connected after the first, is handed the same
            // message rather than a copy of it: what it reads is what the
            // handler before it wrote. This is the in-place edit seen from
            // inside the emission; the client below sees it from outside.
            connected.Object.SendingMessage += (_, args) =>
            {
                RTSPResult result = args.Message.GetHeaderByName(ProbeHeader, out string? value, 0);
                secondHandlerSawTheHeader &= result == RTSPResult.Ok
                    && string.Equals(value, ProbeValue, StringComparison.Ordinal);
            };
        };

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        _output.WriteLine($"source {sourceId} on 127.0.0.1:{port}");

        string conversation;
        try
        {
            conversation = Exchange(context, port);
        }
        finally
        {
            Assert.True(server.Detach(sourceId, context));
        }

        _output.WriteLine(conversation);

        // The handler ran for both responses, which the generated event never
        // managed once.
        Assert.True(handled >= 2, $"the handler ran {handled} time(s).");
        Assert.Equal(handled, responses);
        Assert.True(everySeenContextHadAClient, "a context carried no client.");
        Assert.True(everySeenContextHadAConnection, "a context carried no connection.");
        Assert.True(secondHandlerSawTheHeader, "the second handler was handed a copy of the message.");
        Assert.Equal(0, sessionsOnFirstResponse);

        // Both responses reached the client, and both carry the header the
        // handler added to the message the server was about to send: the
        // wrapper wrote into the message itself rather than into a boxed copy
        // of it.
        Assert.Contains("RTSP/1.0 200", conversation, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/sdp", conversation, StringComparison.Ordinal);

        int seen = 0;
        int at = 0;
        string header = ProbeHeader + ": " + ProbeValue;
        while ((at = conversation.IndexOf(header, at, StringComparison.Ordinal)) >= 0)
        {
            seen++;
            at += header.Length;
        }

        Assert.Equal(handled, seen);

        // The server holds a reference of its own to every client it took
        // (rtsp-server.c:1110) and lets it go when that client is closed
        // (:1129), which the socket being gone does not by itself deliver. A
        // Remove verdict closes it (rtsp-server.c:1497-1500) and, with no
        // client thread of its own to wait for, unmanages and unrefs it inside
        // the filter call - where it is still managed at all, since the pump
        // above also delivers the end of the socket the client task closed,
        // which closes it just the same. The list a filter answers holds the
        // clients it answered Ref for, so a Remove filter answers an empty one
        // whatever it saw: nothing is learnt from that answer, and what is
        // asserted is the state it leaves behind. Neither request opens a
        // session, so no session pool step is needed.
        int filtered = 0;
        Assert.Empty(server.ClientFilter((_, _) =>
        {
            filtered++;
            return RTSPFilterResult.Remove;
        }));

        _output.WriteLine($"the remove filter saw {filtered} client(s)");

        Assert.True(
            PumpUntil(context, () => DisposeAll(server.ClientFilter(null)) == 0),
            "a client was still managed after the filter removed it.");

        // Every added handler is rooted by a handle the closure of the client
        // frees when the client is finalized, and the first of the two captures
        // the client wrapper, so the wrapper keeps the toggle reference that
        // keeps the client alive that keeps the handle rooted. The server has
        // let its own reference go by now, so disposing the wrapper here is
        // what breaks that circle - and finalize is the one place the media the
        // DESCRIBE prepared is taken back down (clean_cached_media,
        // rtsp-client.c:825), which a leaked client would leave running for the
        // rest of the process.
        Assert.NotNull(client);
        WeakProbe.Arm(client.Handle);
        client.Dispose();

        Assert.True(
            PumpUntil(context, () => WeakProbe.Freed == 1),
            "the client outlived the wrapper that held the last reference of it.");
    }

    /// <summary>
    /// Speaks one <c>OPTIONS</c> and one <c>DESCRIBE</c> request over a single
    /// connection, iterating the context of the server while the task that
    /// holds the socket waits on it.
    /// </summary>
    /// <param name="context">The context the server is attached to.</param>
    /// <param name="port">The port the server is listening on.</param>
    /// <returns>Both responses, one after the other.</returns>
    private static string Exchange(MainContext context, int port)
    {
        Task<string> exchange = Task.Run(() => Converse(port));

        Assert.True(
            PumpUntil(context, () => exchange.IsCompleted),
            "the server never answered both requests.");

        return exchange.GetAwaiter().GetResult();
    }

    /// <summary>Speaks the two requests and reads the two responses.</summary>
    /// <param name="port">The port the server is listening on.</param>
    /// <returns>Both responses, one after the other.</returns>
    private static string Converse(int port)
    {
        using TcpClient socket = new("127.0.0.1", port);

        // A read that is never answered has to end as a failed task rather than
        // as a thread blocked for the life of the test process: the deadline of
        // the pump is what bounds the test, and this is what bounds the task it
        // waits for.
        socket.ReceiveTimeout = socket.SendTimeout = (int)Deadline.TotalMilliseconds;

        using NetworkStream stream = socket.GetStream();

        string options = Request(stream, "OPTIONS * RTSP/1.0\r\nCSeq: 1\r\n\r\n", body: false);
        string describe = Request(
            stream,
            $"DESCRIBE rtsp://127.0.0.1:{port}/test RTSP/1.0\r\nCSeq: 2\r\nAccept: application/sdp\r\n\r\n",
            body: true);

        return options + describe;
    }

    /// <summary>Writes one request and reads one response off the socket.</summary>
    /// <param name="stream">The connection to the server.</param>
    /// <param name="request">The request, terminated by its blank line.</param>
    /// <param name="body">Whether the response carries a body to read as well.</param>
    /// <returns>The response.</returns>
    private static string Request(NetworkStream stream, string request, bool body)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();

        StringBuilder response = new();
        byte[] buffer = new byte[512];

        // A response ends at its blank line unless it carries a body, whose
        // length its own Content-Length header states; reading to the end of
        // the stream would wait for a connection the server keeps open.
        while (!IsComplete(response.ToString(), body))
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            response.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        return response.ToString();
    }

    /// <summary>Answers whether a response has been read in full.</summary>
    /// <param name="response">What has arrived so far.</param>
    /// <param name="body">Whether a body is expected after the headers.</param>
    /// <returns><see langword="true"/> when nothing more is owed.</returns>
    private static bool IsComplete(string response, bool body)
    {
        int end = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end < 0)
        {
            return false;
        }

        if (!body)
        {
            return true;
        }

        const string Length = "Content-Length:";
        int at = response.IndexOf(Length, StringComparison.OrdinalIgnoreCase);
        if (at < 0 || at > end)
        {
            return true;
        }

        int lineEnd = response.IndexOf("\r\n", at, StringComparison.Ordinal);
        string stated = response[(at + Length.Length)..lineEnd].Trim();

        return int.TryParse(stated, out int expected)
            && response.Length - (end + 4) >= expected;
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
    /// Iterates the context until a condition holds or the deadline passes.
    /// </summary>
    /// <param name="context">The context to iterate.</param>
    /// <param name="done">The condition to wait for.</param>
    /// <returns><see langword="true"/> when the condition held in time.</returns>
    private static bool PumpUntil(MainContext context, Func<bool> done)
    {
        System.DateTime end = System.DateTime.UtcNow + Deadline;

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

            if (System.DateTime.UtcNow >= end)
            {
                return false;
            }

            Thread.Sleep(5);
        }
    }
}
