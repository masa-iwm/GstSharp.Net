namespace Gst.Gio;

/// <summary>
/// A protocol identifier of a <c>GSocket</c>.
/// </summary>
/// <remarks>
/// The identifier is passed straight to the operating system, so a protocol
/// that is not listed here can still be used by casting its number to this
/// type. The numbers are the IANA protocol numbers and are the same on every
/// platform.
/// </remarks>
public enum SocketProtocol
{
    /// <summary>The protocol is unknown.</summary>
    Unknown = -1,

    /// <summary>The default protocol for the family and type.</summary>
    Default = 0,

    /// <summary>TCP over IPv4 or IPv6.</summary>
    Tcp = 6,

    /// <summary>UDP over IPv4 or IPv6.</summary>
    Udp = 17,

    /// <summary>SCTP over IPv4 or IPv6.</summary>
    Sctp = 132,
}
