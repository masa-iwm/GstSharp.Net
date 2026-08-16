using Gst.GObject;
using Gst.Gio;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written Gio layer against the library that is installed: the
/// cancellation token, the socket surface with its bridge to
/// <c>System.Net</c>, and the proof that <c>libgio</c> comes out of the same
/// installation as the rest of the binding.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in the generated output references Gio yet. What these tests
/// measure is therefore the runtime half of the layer on its own: the imports
/// resolve, the ownership rules of the constructors hold, and — the part that
/// is silent when it breaks — the type registry entries make
/// <c>Object.FromNative</c> produce a Gio wrapper instead of a plain
/// <c>GObject</c>. <see cref="Socket.GetLocalAddress"/> is where the last one
/// is measured: without the <c>GSocketAddress</c> entry the lookup walks past
/// it, the cast to <see cref="SocketAddress"/> fails and the call throws.
/// </para>
/// <para>
/// Every wrapper here is a GObject wrapper and is disposed like one, which is
/// what the rest of this suite does with an element or a bus.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GioTests
{
    /// <summary>
    /// A token starts unused, reports its cancellation, and can be put back
    /// into its unused state.
    /// </summary>
    [Fact]
    public void ACancellableCancelsAndResets()
    {
        using Cancellable cancellable = Cancellable.New();

        Assert.False(cancellable.IsCancelled);

        cancellable.Cancel();
        Assert.True(cancellable.IsCancelled);

        // Cancelling twice is documented to do nothing rather than to fail.
        cancellable.Cancel();
        Assert.True(cancellable.IsCancelled);

        cancellable.Reset();
        Assert.False(cancellable.IsCancelled);
    }

    /// <summary>
    /// A UDP socket bound to the loopback interface on port zero reports back
    /// the address the operating system gave it, through the
    /// <c>System.Net</c> bridge and without a single <c>GInetAddress</c>
    /// wrapper.
    /// </summary>
    /// <remarks>
    /// Port zero is what makes the read back mean something: the port in the
    /// answer is one the binding never wrote, so it can only have come out of
    /// the native <c>struct sockaddr</c> through
    /// <see cref="SocketAddress.ToSystemAddress"/>.
    /// </remarks>
    [Fact]
    public void ABoundSocketReportsTheAddressTheSystemGaveIt()
    {
        using Socket socket = Socket.New(SocketFamily.Ipv4, SocketType.Datagram, SocketProtocol.Udp);

        // -1 is the invalid descriptor on both platforms; anything else is a
        // socket the operating system handed out.
        Assert.NotEqual(-1, socket.Fd);

        System.Net.SocketAddress wanted =
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0).Serialize();

        SocketAddress? requested = SocketAddress.FromSystemAddress(wanted);
        Assert.NotNull(requested);

        using (requested)
        {
            Assert.Equal(SocketFamily.Ipv4, requested.Family);
            Assert.Equal(wanted.Size, (int)requested.NativeSize);

            socket.Bind(requested, allowReuse: false);
        }

        using SocketAddress local = socket.GetLocalAddress();

        Assert.Equal(SocketFamily.Ipv4, local.Family);

        System.Net.IPEndPoint bound = (System.Net.IPEndPoint)new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0)
            .Create(local.ToSystemAddress());

        Assert.Equal(System.Net.IPAddress.Loopback, bound.Address);
        Assert.NotEqual(0, bound.Port);

        // Closing is explicit here rather than left to the last reference, so
        // that the error path of the call is exercised at least once.
        socket.Close();
    }

    /// <summary>
    /// The IPv6 walk of the same round trip. What it measures beyond the IPv4
    /// case is the family translation: <c>AF_INET6</c> is 23 on Windows, 10 on
    /// Linux and 30 on macOS, so this test only passes on every CI leg when
    /// <see cref="SocketFamily.Ipv6"/> is translated at the boundary rather
    /// than passed through.
    /// </summary>
    [Fact]
    public void AnIpv6SocketBindsAndReportsItsFamily()
    {
        using Socket socket = Socket.New(SocketFamily.Ipv6, SocketType.Datagram, SocketProtocol.Udp);

        System.Net.SocketAddress wanted =
            new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Loopback, 0).Serialize();

        SocketAddress? requested = SocketAddress.FromSystemAddress(wanted);
        Assert.NotNull(requested);

        using (requested)
        {
            Assert.Equal(SocketFamily.Ipv6, requested.Family);

            socket.Bind(requested, allowReuse: false);
        }

        using SocketAddress local = socket.GetLocalAddress();

        Assert.Equal(SocketFamily.Ipv6, local.Family);

        System.Net.IPEndPoint bound = (System.Net.IPEndPoint)new System.Net.IPEndPoint(System.Net.IPAddress.IPv6Any, 0)
            .Create(local.ToSystemAddress());

        Assert.Equal(System.Net.IPAddress.IPv6Loopback, bound.Address);
        Assert.NotEqual(0, bound.Port);

        socket.Close();
    }

    /// <summary>
    /// <c>libgio</c> is registered as a module of the binding and is loaded out
    /// of the installation the anchor library came from, which is what keeps
    /// one process on one GLib.
    /// </summary>
    /// <remarks>
    /// Nothing here loads the library on purpose: freezing the type registry
    /// resolves <c>g_socket_get_type</c> and its siblings, and
    /// <c>GstSharp.Initialize</c> did that before the first test ran.
    /// <c>Load</c> therefore hands the cached handle back.
    /// </remarks>
    [Fact]
    public void GioIsLoadedFromTheInstallationOfTheAnchorLibrary()
    {
        Assert.Contains("Gio", TypeRegistry.RegisteredModules);
        Assert.NotEqual(nint.Zero, NativeLoader.Load("Gio"));

        // Only Windows can turn a module handle back into a path.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string? anchor = NativeLoader.GetLoadedModulePath("Gst");
        string? gio = NativeLoader.GetLoadedModulePath("Gio");

        Assert.NotNull(anchor);
        Assert.NotNull(gio);
        Assert.Equal(
            Path.GetDirectoryName(anchor),
            Path.GetDirectoryName(gio),
            StringComparer.OrdinalIgnoreCase);
    }
}
