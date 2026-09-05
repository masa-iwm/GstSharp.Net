using System.Net.Sockets;
using System.Text;
using Gst.GLib;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>GstRTSPClient::check-requirements</c>, the one signal of the corpus whose
/// handler is handed a NULL terminated vector of strings. A real client speaks
/// RTSP over the loopback interface, because the vector is built by the server
/// out of the <c>Require</c> headers of a request and nothing short of a
/// request produces one.
/// </summary>
/// <remarks>
/// <para>
/// The server is built with <c>SetMaxThreads(0)</c> on its thread pool, so the
/// client it takes runs on the server's own context and the iteration below is
/// the only thing that drives it. The socket is therefore spoken from a task of
/// its own: a blocking read on the test thread would stop the pump and wait for
/// an answer that only the pump can produce.
/// </para>
/// <para>
/// The C side is what the assertions are written against. A handler that
/// answers a non-empty string has that string reported as the unsupported
/// options: the client answers <c>551 Option not supported</c> and carries them
/// back in an <c>Unsupported</c> header. A handler that answers the empty
/// string says every option is supported and the request is served.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspClientRequirementsTests
{
    /// <summary>How long any wait here is allowed to take.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    /// <summary>The option no build of GStreamer supports.</summary>
    private const string Requirement = "x-gst-unsupported";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspClientRequirementsTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The handler sees the requirements of the request as an array of its own,
    /// and both answers it may give reach the client that sent the request.
    /// </summary>
    [Fact]
    public void TheRequirementsOfARequestReachTheHandlerAsAnArray()
    {
        using MainContext context = MainContext.New();
        using RTSPServer server = RTSPServer.New();

        server.SetAddress("127.0.0.1");
        server.SetService("0");

        RTSPThreadPool? threadPool = server.GetThreadPool();
        Assert.NotNull(threadPool);
        threadPool.SetMaxThreads(0);

        string[]? seen = null;
        string answer = string.Empty;

        server.ClientConnected += (_, connected) =>
            connected.Object.CheckRequirements += (_, requirements) =>
            {
                seen = requirements.Arr;
                return answer;
            };

        uint sourceId = server.Attach(context);
        Assert.True(sourceId > 0, "Attach answered 0, which is its failure.");

        int port = server.GetBoundPort();
        Assert.True(port > 0, $"bound port {port} is not a port.");

        _output.WriteLine($"source {sourceId} on 127.0.0.1:{port}");

        try
        {
            // The unsupported options are what the handler answered, so the
            // request is refused and the answer is quoted back to the client.
            answer = Requirement;
            string refused = Exchange(context, port);

            Assert.NotNull(seen);
            Assert.Equal([Requirement], seen);
            Assert.StartsWith("RTSP/1.0 551", refused, StringComparison.Ordinal);
            Assert.Contains("Unsupported: " + Requirement, refused, StringComparison.Ordinal);

            // The empty string says every option is supported, and the request
            // is served like any other.
            seen = null;
            answer = string.Empty;
            string served = Exchange(context, port);

            Assert.NotNull(seen);
            Assert.Equal([Requirement], seen);
            Assert.StartsWith("RTSP/1.0 200", served, StringComparison.Ordinal);
        }
        finally
        {
            Assert.True(server.Detach(sourceId, context));
        }
    }

    /// <summary>
    /// Sends one <c>OPTIONS</c> request carrying a requirement and reads the
    /// answer, iterating the context of the server while the task that holds
    /// the socket waits on it.
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

        byte[] request = Encoding.ASCII.GetBytes(
            "OPTIONS * RTSP/1.0\r\nCSeq: 1\r\nRequire: " + Requirement + "\r\n\r\n");
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
