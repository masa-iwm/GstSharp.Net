namespace Gst.Gio;

/// <summary>
/// The communication style of a <c>GSocket</c>.
/// </summary>
/// <remarks>
/// These are the values of <c>GSocketType</c>, which GLib defines itself rather
/// than taking them from the platform, so they are the same everywhere.
/// </remarks>
public enum SocketType
{
    /// <summary>Type unknown or wrong.</summary>
    Invalid = 0,

    /// <summary>Reliable connection based byte streams, <c>SOCK_STREAM</c>.</summary>
    Stream = 1,

    /// <summary>Connectionless, unreliable datagram passing, <c>SOCK_DGRAM</c>.</summary>
    Datagram = 2,

    /// <summary>
    /// Reliable connection based passing of datagrams of fixed maximum length,
    /// <c>SOCK_SEQPACKET</c>.
    /// </summary>
    Seqpacket = 3,
}
