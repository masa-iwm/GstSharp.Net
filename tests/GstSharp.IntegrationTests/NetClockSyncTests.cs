using System.Net;
using Gst;
using Gst.Gio;
using Gst.Net;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;
using SocketAddress = Gst.Gio.SocketAddress;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The clock half of the Net module over the loopback interface: a time
/// provider and a client clock that synchronises to it, the provider that
/// answers nothing, and the meta that carries a socket address on a buffer.
/// </summary>
/// <remarks>
/// <para>
/// The provider binds port 0 and the port it actually got is read back off it,
/// the way <c>gstnetclientclock.c</c>'s own <c>test_functioning</c> does: a
/// fixed port is a test that fails on a machine where something else is
/// listening.
/// </para>
/// <para>
/// A client clock is not only the object it hands back. The library keeps a
/// process wide table of the internal clocks it has built, keyed by the
/// address and the port they talk to (gstnetclientclock.c:85, the file scope
/// <c>clocks</c> list, filled at :1414), and a second client aimed at the same
/// endpoint shares the internal clock of the first. Nothing here asserts that
/// two clients are distinct for that reason, and the cache is also why the
/// second test would be misled if the operating system handed it the port the
/// first test had just given up - it would find a clock that is already
/// synchronised. Both providers below stay alive for the whole of their test,
/// which is what keeps the two ports apart.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class NetClockSyncTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public NetClockSyncTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A client clock pointed at a provider on the loopback interface
    /// synchronises, and then reads roughly the same time as the clock the
    /// provider serves.
    /// </summary>
    /// <remarks>
    /// The bound on the difference is a second, which is loose on purpose: the
    /// question is whether the two clocks talk to each other at all, not how
    /// precisely they agree, and a precision bound would be a test of the
    /// machine the suite runs on.
    /// </remarks>
    [Fact]
    public void AClientClockSynchronisesToAProviderOnTheLoopback()
    {
        using Clock system = SystemClock.Obtain();

        using NetTimeProvider? provider = NetTimeProvider.New(system, "127.0.0.1", 0);
        Assert.NotNull(provider);

        int port = provider.Port;
        Assert.True(port > 0, $"the provider bound port {port}, which is not a port.");

        _output.WriteLine($"provider on port {port}");

        NetClientClock client = NetClientClock.New("gstsharp-client", "127.0.0.1", port, ClockTime.Zero);

        try
        {
            Assert.True(
                client.WaitForSync(ClockTime.FromSeconds(10)),
                "the client clock never synchronised to a provider on the loopback interface.");
            Assert.True(client.IsSynced());

            ClockTime clientTime = client.GetTime();
            ClockTime systemTime = system.GetTime();

            ulong difference = clientTime.Nanoseconds > systemTime.Nanoseconds
                ? clientTime.Nanoseconds - systemTime.Nanoseconds
                : systemTime.Nanoseconds - clientTime.Nanoseconds;

            _output.WriteLine($"client and system differ by {difference} ns");

            Assert.True(
                difference < ClockTime.FromSeconds(1).Nanoseconds,
                $"the synchronised client clock is {difference} ns away from the clock it follows.");
        }
        finally
        {
            // The client goes first: it is the one talking to the provider.
            client.Dispose();
        }
    }

    /// <summary>
    /// A provider that was switched off answers no client, so a clock pointed
    /// at it never synchronises.
    /// </summary>
    /// <remarks>
    /// The provider is built active and switched off afterwards, which is also
    /// the only order that can be measured: <c>active</c> only gates the
    /// answers (gstnettimeprovider.c:52, <c>IS_ACTIVE</c>, read on every
    /// request), the socket stays bound either way, and the port has to be
    /// readable before the client can be aimed at it.
    /// </remarks>
    [Fact]
    public void AnInactiveProviderAnswersNoClient()
    {
        using Clock system = SystemClock.Obtain();

        using NetTimeProvider? provider = NetTimeProvider.New(system, "127.0.0.1", 0);
        Assert.NotNull(provider);

        int port = provider.Port;
        Assert.True(port > 0, $"the provider bound port {port}, which is not a port.");

        provider.Active = false;
        Assert.False(provider.Active);

        _output.WriteLine($"inactive provider on port {port}");

        NetClientClock client = NetClientClock.New("gstsharp-silent", "127.0.0.1", port, ClockTime.Zero);

        try
        {
            Assert.False(
                client.WaitForSync(ClockTime.FromSeconds(2)),
                "a client synchronised to a provider that answers nothing.");
            Assert.False(client.IsSynced());
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// The net address meta carries the socket address it was given, and a
    /// buffer that was never given one answers nothing.
    /// </summary>
    [Fact]
    public void ANetAddressMetaCarriesTheSocketAddressItWasGiven()
    {
        IPEndPoint endpoint = new(IPAddress.Loopback, 5004);

        using SocketAddress? address = SocketAddress.FromSystemAddress(endpoint.Serialize());
        Assert.NotNull(address);

        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        NetAddressMeta added = NetGlobal.BufferAddNetAddressMeta(buffer, address);
        Assert.NotNull(added);

        NetAddressMeta? read = NetGlobal.BufferGetNetAddressMeta(buffer);
        Assert.NotNull(read);

        using SocketAddress? carried = read.Addr;
        Assert.NotNull(carried);

        IPEndPoint template = new(IPAddress.Any, 0);
        IPEndPoint round = Assert.IsType<IPEndPoint>(template.Create(carried.ToSystemAddress()));

        _output.WriteLine($"the meta carries {round}");

        Assert.Equal(endpoint.Address, round.Address);
        Assert.Equal(endpoint.Port, round.Port);

        using Buffer bare = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));
        Assert.Null(NetGlobal.BufferGetNetAddressMeta(bare));
    }
}
