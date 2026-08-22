using Gst;
using Gst.Gio;
using Gst.Net;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstNet</c> binding against the library that is installed: a network
/// clock is built through the narrowed factory, and a socket of the hand
/// written Gio layer is handed to a GStreamer entry point.
/// </summary>
/// <remarks>
/// <para>
/// The second test is what this module was bound for. Every generated module
/// before it stayed inside the types the generator emits itself;
/// <see cref="NetGlobal.NetUtilsSetSocketTos"/> is the first generated call
/// that takes a wrapper of <c>src/GstSharp.Net/Core/Gio</c> and passes its
/// handle to a native library. That the call exists at all, and takes a
/// <see cref="Gst.Gio.Socket"/> rather than an <c>nint</c> or nothing, is
/// already half of what the test measures — the planner used to skip every
/// member that named a Gio type. The other half is that the handle the wrapper
/// carries is one <c>libgstnet</c> accepts.
/// </para>
/// <para>
/// PTP is deliberately absent. <c>gst_ptp_init</c> starts a privileged helper
/// process because PTP listens below port 1024, so whether
/// <see cref="PtpClock"/> works is a property of the machine rather than of
/// the binding.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GstNetTests
{
    /// <summary>
    /// The clock factories are spelled in the gir as returning a
    /// <see cref="Clock"/>, and fixups.json narrows each of them onto the class
    /// that declares it. What the narrowing rests on is that the wrapper is
    /// built from the real type of the instance and not from the declared one,
    /// which is what the type test here reads back.
    /// </summary>
    /// <remarks>
    /// Constructing the clock starts the synchronisation in the background, and
    /// nothing answers on the port it is pointed at. That is the same as in C:
    /// the clock stays unsynchronised, reports its failure through its bus if
    /// one is set, and constructing it is not an error.
    /// </remarks>
    [Fact]
    public void TheNarrowedFactoryHandsBackTheClockItDeclares()
    {
        using NtpClock clock = NtpClock.New("gstsharp-ntp", "127.0.0.1", 5678, ClockTime.Zero);

        // The static type is already NtpClock; the type test is about the
        // instance the registry built, which the declared GstClock* return
        // would have made a plain SystemClock.
        Assert.IsType<NtpClock>(clock);
        Assert.IsAssignableFrom<NetClientClock>(clock);
        Assert.IsAssignableFrom<SystemClock>(clock);
        Assert.IsAssignableFrom<Clock>(clock);

        Assert.Equal("gstsharp-ntp", clock.Name);
    }

    /// <summary>
    /// A UDP socket built by the Gio layer is handed to
    /// <c>gst_net_utils_set_socket_tos</c>, which sets <c>IP_TOS</c> on the
    /// descriptor behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DSCP value is 46, expedited forwarding, which is what the RTP
    /// elements of GStreamer ask for on an audio stream. Only the low six bits
    /// are used; the library shifts them into the type of service byte itself.
    /// </para>
    /// <para>
    /// The answer is platform dependent, and asserting the platform is what
    /// makes the test say something: Windows does not let a process set
    /// <c>IP_TOS</c> on a socket, so <c>setsockopt</c> refuses it there and the
    /// library reports the refusal, while every other platform sets it. What is
    /// measured on all of them is that the call reaches the library with this
    /// socket rather than throwing, warning or crashing on the way in.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASocketOfTheGioLayerReachesTheNetworkUtilities()
    {
        using Socket socket = Socket.New(SocketFamily.Ipv4, SocketType.Datagram, SocketProtocol.Udp);

        Assert.Equal(!OperatingSystem.IsWindows(), NetGlobal.NetUtilsSetSocketTos(socket, 46));

        socket.Close();
    }

    /// <summary>
    /// The wire form of a time packet is sixteen bytes: two big endian
    /// <c>GstClockTime</c> values. Both halves of it are bound off an
    /// <c>&lt;array fixed-size="16"&gt;</c> that carries no count of its own,
    /// so the length is part of the projection rather than of the call.
    /// </summary>
    /// <remarks>
    /// The constructor accepts an empty span as well, which pins to the
    /// <c>NULL</c> the C function answers with a zeroed packet; a span of any
    /// other length is refused, because the C function reads sixteen bytes
    /// whenever the pointer is not <c>NULL</c>.
    /// </remarks>
    [Fact]
    public void ATimePacketRoundTripsThroughItsSixteenByteWireForm()
    {
        using NetTimePacket packet = NetTimePacket.New([]);

        byte[]? wire = packet.Serialize();

        Assert.NotNull(wire);
        Assert.Equal(16, wire.Length);

        using NetTimePacket restored = NetTimePacket.New(wire);

        Assert.Equal(packet.LocalTime, restored.LocalTime);
        Assert.Equal(packet.RemoteTime, restored.RemoteTime);

        ArgumentException error = Assert.Throws<ArgumentException>(() => NetTimePacket.New(new byte[8]));

        Assert.Equal("buffer", error.ParamName);
    }
}
