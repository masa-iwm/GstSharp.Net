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
/// endpoint shares the internal clock of the first. An entry outlives its last
/// client: finalising the last clock on an entry only schedules its removal
/// sixty seconds later (:1214), and until then the internal clock keeps
/// running. A client built against a surviving entry inherits the state that
/// entry is in at construction time (:1427-1428, a synchronised cached clock
/// marks the new client synchronised straight away).
/// </para>
/// <para>
/// So a client pointed at a port some earlier client had synchronised on would
/// report itself synchronised without anything answering it. The test below
/// creates both providers before either client, which is what keeps the two
/// ports distinct - the operating system cannot hand the second provider the
/// port the first one still holds.
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
    /// A client clock pointed at an active provider on the loopback interface
    /// synchronises and then reads roughly the same time as the clock that
    /// provider serves, while a client pointed at a provider that was switched
    /// off never synchronises.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two halves are one test because both providers have to exist before
    /// either client does, for the reason the class remarks give.
    /// </para>
    /// <para>
    /// The bound on the difference is a second, which is loose on purpose: the
    /// question is whether the two clocks talk to each other at all, not how
    /// precisely they agree, and a precision bound would be a test of the
    /// machine the suite runs on.
    /// </para>
    /// <para>
    /// The silent provider is built active and switched off afterwards, which
    /// is also the only order that can be measured: <c>active</c> only gates
    /// the answers (gstnettimeprovider.c:52, <c>IS_ACTIVE</c>, read on every
    /// request), the socket stays bound either way, and the port has to be
    /// readable before the client can be aimed at it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AClientClockSynchronisesToAnActiveProviderAndNotToASilentOne()
    {
        using Clock system = SystemClock.Obtain();

        // Both providers are bound before either client is built, so the two
        // ports cannot be the same one twice over.
        using NetTimeProvider? answering = NetTimeProvider.New(system, "127.0.0.1", 0);
        Assert.NotNull(answering);

        using NetTimeProvider? silent = NetTimeProvider.New(system, "127.0.0.1", 0);
        Assert.NotNull(silent);

        int answeringPort = answering.Port;
        int silentPort = silent.Port;

        Assert.True(answeringPort > 0, $"the provider bound port {answeringPort}, which is not a port.");
        Assert.True(silentPort > 0, $"the provider bound port {silentPort}, which is not a port.");
        Assert.NotEqual(answeringPort, silentPort);

        silent.Active = false;
        Assert.False(silent.Active);

        _output.WriteLine($"answering provider on port {answeringPort}, silent provider on port {silentPort}");

        // The clients are declared after the providers, so they are disposed
        // before them: a client is the one talking to a provider.
        using NetClientClock client =
            NetClientClock.New("gstsharp-client", "127.0.0.1", answeringPort, ClockTime.Zero);

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

        using NetClientClock deaf =
            NetClientClock.New("gstsharp-silent", "127.0.0.1", silentPort, ClockTime.Zero);

        Assert.False(
            deaf.WaitForSync(ClockTime.FromSeconds(2)),
            "a client synchronised to a provider that answers nothing.");
        Assert.False(deaf.IsSynced());
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
