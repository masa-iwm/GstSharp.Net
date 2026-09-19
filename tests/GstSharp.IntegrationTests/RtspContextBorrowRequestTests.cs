using System.Net.Sockets;
using System.Text;
using Gst.GLib;
using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>RTSPContext.BorrowRequest()</c>, the lent request of a context. A real
/// client speaks RTSP over the loopback interface, because nothing short of a
/// request builds a context that carries one.
/// </summary>
/// <remarks>
/// <para>
/// The arrangement is the one
/// <see cref="RtspClientRequirementsTests"/> uses: the thread pool of the
/// server is set to zero threads, so the client runs on the context iterated
/// here, and the socket is spoken from a task of its own.
/// </para>
/// <para>
/// What is measured is that the borrow is the request itself rather than a copy
/// of it: the <c>pre-options-request</c> handler adds a header through
/// <c>BorrowRequest()</c>, and the <c>options-request</c> handler that runs
/// after the server has served the request finds that header on the copy
/// <c>GetRequest()</c> answers. A header a handler adds to the copy instead
/// reaches nobody, which the second half asserts.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspContextBorrowRequestTests
{
    /// <summary>How long any wait here is allowed to take.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>The header the borrowing handler writes into the request.</summary>
    private const string Borrowed = "X-GstSharp-Borrowed";

    /// <summary>The header the handler writes into a copy of the request.</summary>
    private const string Copied = "X-GstSharp-Copied";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspContextBorrowRequestTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A header added to the borrowed request is on the request the server goes
    /// on to read, and a header added to a copy of it is on nothing.
    /// </summary>
    [Fact]
    public void AHeaderAddedToTheBorrowedRequestIsOnTheRequestTheServerReads()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);
        threadPool.SetMaxThreads(0);

        // An exception thrown inside a handler is trapped rather than reported,
        // so every handler records what it saw and the assertions are made on
        // the test thread.
        bool borrowedWasNull = true;
        RTSPResult borrowedAdd = RTSPResult.Error;
        RTSPResult copiedAdd = RTSPResult.Error;
        string? seenBorrowed = null;
        string? seenCopied = null;
        bool served = false;

        server.ClientConnected += (_, connected) =>
        {
            connected.Object.PreOptionsRequest += (_, args) =>
            {
                Gst.RtspServer.RTSPContext ctx = args.Ctx;

                using Gst.Rtsp.RTSPMessage? request = ctx.BorrowRequest();
                borrowedWasNull = request is null;
                if (request is not null)
                {
                    borrowedAdd = request.AddHeaderByName(Borrowed, "yes");
                }

                // The other half of the pair: a copy takes the header and
                // nothing reads that copy afterwards.
                using Gst.Rtsp.RTSPMessage? copy = ctx.GetRequest();
                if (copy is not null)
                {
                    copiedAdd = copy.AddHeaderByName(Copied, "yes");
                }

                return RTSPStatusCode.Ok;
            };

            connected.Object.OptionsRequest += (_, args) =>
            {
                Gst.RtspServer.RTSPContext ctx = args.Ctx;

                using Gst.Rtsp.RTSPMessage? request = ctx.GetRequest();
                if (request is null)
                {
                    return;
                }

                request.GetHeaderByName(Borrowed, out seenBorrowed, 0);
                request.GetHeaderByName(Copied, out seenCopied, 0);
                served = true;
            };
        };

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        _output.WriteLine($"source {sourceId} on 127.0.0.1:{port}");

        try
        {
            string response = Exchange(context, port);

            Assert.False(borrowedWasNull, "the borrowed request of a pre-options-request context was null.");
            Assert.Equal(RTSPResult.Ok, borrowedAdd);
            Assert.Equal(RTSPResult.Ok, copiedAdd);
            Assert.True(served, "the options-request handler never read the request.");
            Assert.StartsWith("RTSP/1.0 200", response, StringComparison.Ordinal);
            Assert.Equal("yes", seenBorrowed);
            Assert.Null(seenCopied);
        }
        finally
        {
            Assert.True(server.Detach(sourceId, context));
        }

        // The teardown of RtspClientRequirementsTests, for the same reason: the
        // server holds a reference of its own to every client it took and lets
        // it go from the closed signal of that client.
        Assert.Empty(server.ClientFilter((_, _) => RTSPFilterResult.Remove));

        Assert.True(
            PumpUntil(context, () => DisposeAll(server.ClientFilter(null)) == 0),
            "a client was still managed after the filter removed it.");
    }

    /// <summary>
    /// Sends one <c>OPTIONS</c> request and reads the answer, iterating the
    /// context of the server while the task that holds the socket waits on it.
    /// </summary>
    /// <param name="context">The context the server is attached to.</param>
    /// <param name="port">The port the server is listening on.</param>
    /// <returns>The response, up to and including its blank line.</returns>
    private static string Exchange(MainContext context, int port)
    {
        Task<string> exchange = Task.Run(() => Request(port));

        Assert.True(
            PumpUntil(context, () => exchange.IsCompleted),
            "the server never answered the OPTIONS request.");

        return exchange.GetAwaiter().GetResult();
    }

    /// <summary>Speaks one request and reads one response over a socket.</summary>
    /// <param name="port">The port the server is listening on.</param>
    /// <returns>The response, up to and including its blank line.</returns>
    private static string Request(int port)
    {
        using TcpClient socket = new("127.0.0.1", port);
        using NetworkStream stream = socket.GetStream();

        byte[] request = Encoding.ASCII.GetBytes("OPTIONS * RTSP/1.0\r\nCSeq: 1\r\n\r\n");
        stream.Write(request, 0, request.Length);
        stream.Flush();

        StringBuilder response = new();
        byte[] buffer = new byte[512];

        // A response ends at its blank line: nothing here asks for a body, and
        // reading to the end of the stream would wait for a connection the
        // server keeps open.
        while (!response.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
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
