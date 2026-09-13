using Gst.Rtsp;
using Gst.RtspServer;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The bookkeeping half of the RtspServer module, with no server and no client
/// in it: what a path matches, what a session pool hands out, what a session
/// says about itself, what an address pool reserves, and what a media factory
/// remembers of its configuration.
/// </summary>
/// <remarks>
/// Two of the answers below are <see langword="null"/> only because the
/// overlays say so. <c>gst_rtsp_mount_points_match</c> is annotated as
/// returning a factory and returns NULL for a path it does not know, which
/// <c>girs/overlays/fixups.json</c> corrects on
/// <c>gst_rtsp_mount_points_match#return</c>; without that correction the
/// miss below would be an exception rather than an answer.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspServerCatalogTests
{
    private const string Launch = "( audiotestsrc ! audioconvert ! rtpL16pay name=pay0 pt=96 )";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtspServerCatalogTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A mount matches the longest prefix of a path it knows, reports how much
    /// of the path it consumed, and answers nothing for a path it never had.
    /// </summary>
    /// <remarks>
    /// The <c>matched</c> count is the number of characters of the path the
    /// mount accounted for, so a request below the mount matches the mount and
    /// reports its own length
    /// (gst-rtsp-server/gst/rtsp-server/rtsp-mount-points.c:297-300, the
    /// <c>matched</c> handling of <c>gst_rtsp_mount_points_match</c>).
    /// </remarks>
    [Fact]
    public void MountPointsMatchTheLongestPrefixAndAnswerNullOtherwise()
    {
        using RTSPMountPoints mounts = RTSPMountPoints.New();

        using RTSPMediaFactory factory = RTSPMediaFactory.New();
        factory.SetLaunch(Launch);

        mounts.AddFactory("/test", factory);

        // The wrapper of a GObject is interned, so a match hands the very same
        // managed object back and holds the reference it minted; disposing it
        // would dispose the mounted factory, which runs DisconnectAll and takes
        // the handlers of a live mount off again.
        RTSPMediaFactory? exact = mounts.Match("/test", out int matched);
        Assert.NotNull(exact);
        Assert.Same(factory, exact);
        Assert.Equal(5, matched);

        RTSPMediaFactory? below = mounts.Match("/test/extra", out int deeper);
        Assert.NotNull(below);
        Assert.Same(factory, below);
        Assert.Equal(5, deeper);

        Assert.Null(mounts.Match("/nothing", out int missed));
        _output.WriteLine($"a path that is not mounted matched {missed} characters");

        mounts.RemoveFactory("/test");
        Assert.Null(mounts.Match("/test", out _));
    }

    /// <summary>
    /// A session pool creates sessions, finds them again by their identifier,
    /// refuses to go past the limit it was given and forgets the ones that are
    /// removed.
    /// </summary>
    [Fact]
    public void ASessionPoolCreatesFindsLimitsAndRemoves()
    {
        using RTSPSessionPool pool = RTSPSessionPool.New();

        using RTSPSession? session = pool.Create();
        Assert.NotNull(session);
        Assert.Equal(1u, pool.GetNSessions());

        string? sessionid = session.Sessionid;
        Assert.NotNull(sessionid);

        // The wrapper of a GObject is interned, so the find hands the very
        // same managed object back and the reference it minted is held by it;
        // disposing what the find answered would dispose the session itself.
        RTSPSession? found = pool.Find(sessionid);
        Assert.NotNull(found);
        Assert.Same(session, found);

        pool.SetMaxSessions(1);
        Assert.Null(pool.Create());

        Assert.True(pool.Remove(session));
        Assert.Equal(0u, pool.GetNSessions());

        // Nothing has expired, because nothing is left to expire.
        Assert.Equal(0u, pool.Cleanup());
    }

    /// <summary>
    /// A session answers the identifier it was built with, the timeout it was
    /// given, and a session header that carries both.
    /// </summary>
    [Fact]
    public void ASessionAnswersItsIdTimeoutAndHeader()
    {
        using RTSPSession session = RTSPSession.New("abc123");

        Assert.Equal("abc123", session.Sessionid);

        session.SetTimeout(45);
        Assert.Equal(45u, session.GetTimeout());

        string? header = session.GetHeader();
        Assert.NotNull(header);

        _output.WriteLine(header);

        Assert.StartsWith("abc123", header, StringComparison.Ordinal);
        Assert.Contains("timeout=45", header, StringComparison.Ordinal);
    }

    /// <summary>
    /// An address pool hands out addresses from the range it was given, honours
    /// a reservation of exact ports, and refuses a second reservation of the
    /// same ones.
    /// </summary>
    [Fact]
    public void AnAddressPoolHandsOutMulticastAddressesAndReservations()
    {
        using RTSPAddressPool pool = RTSPAddressPool.New();

        Assert.True(pool.AddRange("224.3.0.0", "224.3.0.10", 5000, 5010, 16));

        // A multicast range is not a unicast one.
        Assert.False(pool.HasUnicastAddresses());

        using (RTSPAddress? acquired = pool.AcquireAddress(
            RTSPAddressFlags.Ipv4 | RTSPAddressFlags.Multicast | RTSPAddressFlags.EvenPort,
            2))
        {
            Assert.NotNull(acquired);

            string? address = acquired.Address;
            Assert.NotNull(address);

            _output.WriteLine($"acquired {address}:{acquired.Port} for {acquired.NPorts} ports");

            Assert.StartsWith("224.3.0.", address, StringComparison.Ordinal);
            Assert.True(acquired.Port % 2 == 0, $"an even port was asked for and {acquired.Port} came back.");
            Assert.InRange(acquired.Port, 5000, 5010);
            Assert.Equal(2, acquired.NPorts);
            Assert.Equal(16, acquired.Ttl);
        }

        Assert.Equal(
            RTSPAddressPoolResult.Ok,
            pool.ReserveAddress("224.3.0.5", 5008, 2, 16, out RTSPAddress? reserved));
        Assert.NotNull(reserved);

        using (reserved)
        {
            Assert.Equal(5008, reserved.Port);

            // Still inside the scope of the reservation: an address hands its
            // ports back to the pool when it is released, so a second
            // reservation is only refused while the first one is alive.
            RTSPAddressPoolResult again =
                pool.ReserveAddress("224.3.0.5", 5008, 2, 16, out RTSPAddress? twice);
            _output.WriteLine($"reserving the same ports again answered {again}");

            Assert.NotEqual(RTSPAddressPoolResult.Ok, again);
            twice?.Dispose();
        }

        pool.Clear();

        using RTSPAddressPool unicast = RTSPAddressPool.New();
        Assert.True(unicast.AddRange("192.168.7.1", "192.168.7.1", 6000, 6001, 0));
        Assert.True(unicast.HasUnicastAddresses());
    }

    /// <summary>
    /// Everything a media factory is configured with before it ever builds a
    /// medium reads back off it.
    /// </summary>
    [Fact]
    public void AFactoryRoundTripsItsConfiguration()
    {
        using RTSPMediaFactory factory = RTSPMediaFactory.New();

        factory.SetLaunch(Launch);
        Assert.Equal(Launch, factory.GetLaunch());

        factory.SetShared(true);
        Assert.True(factory.IsShared());

        factory.SetLatency(500);
        Assert.Equal(500u, factory.GetLatency());

        factory.SetProtocols(RTSPLowerTrans.Tcp);
        Assert.Equal(RTSPLowerTrans.Tcp, factory.GetProtocols());

        factory.SetTransportMode(RTSPTransportMode.Record);
        Assert.Equal(RTSPTransportMode.Record, factory.GetTransportMode());

        factory.SetEosShutdown(true);
        Assert.True(factory.IsEosShutdown());

        using RTSPPermissions permissions = RTSPPermissions.New();
        nint given = permissions.Handle;

        factory.SetPermissions(permissions);

        // Permissions are a boxed type rather than a GObject, so no wrapper is
        // interned for them: the getter mints a second wrapper of the same
        // storage, which owns a reference of its own and is disposed here.
        using RTSPPermissions? read = factory.GetPermissions();
        Assert.NotNull(read);
        Assert.Equal(given, read.Handle);
    }
}
