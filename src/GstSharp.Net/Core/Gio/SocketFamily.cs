namespace Gst.Gio;

/// <summary>
/// The protocol family of a <c>GSocket</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>GSocketFamily</c> is defined from the <c>AF_*</c> constants of the
/// platform, so its native values differ per operating system: <c>AF_INET6</c>
/// is 23 on Windows, 10 on Linux and Android, and 30 on the Apple platforms.
/// This enumeration keeps one set of values — the ones of the vendored
/// introspection data — and the binding translates at the native boundary, so
/// a member here means the same family everywhere and never has to be compared
/// against a platform number.
/// </para>
/// </remarks>
public enum SocketFamily
{
    /// <summary>No address family.</summary>
    Invalid = 0,

    /// <summary>The UNIX domain family, <c>AF_UNIX</c>.</summary>
    Unix = 1,

    /// <summary>The IPv4 family, <c>AF_INET</c>.</summary>
    Ipv4 = 2,

    /// <summary>
    /// The IPv6 family, <c>AF_INET6</c>. The number is nominal — see the
    /// remarks on the type.
    /// </summary>
    Ipv6 = 23,
}

/// <summary>
/// Translates between <see cref="SocketFamily"/> and the platform values of
/// <c>GSocketFamily</c>.
/// </summary>
/// <remarks>
/// Only <see cref="SocketFamily.Ipv6"/> differs between platforms;
/// <c>AF_UNSPEC</c>, <c>AF_UNIX</c> and <c>AF_INET</c> agree everywhere the
/// binding runs. This is the same shape as
/// <see cref="System.Net.Sockets.AddressFamily"/>: one managed enumeration
/// over differing platform numbers, reconciled where the native call is made.
/// </remarks>
internal static class SocketFamilyNative
{
    /// <summary>The <c>AF_INET6</c> constant of the running platform.</summary>
    private static readonly int Ipv6 =
        OperatingSystem.IsWindows() ? 23
        : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() ? 30
        : 10;

    /// <summary>Turns a managed family into the value of the platform.</summary>
    /// <param name="family">The managed family.</param>
    /// <returns>What the native call expects.</returns>
    internal static int ToNative(SocketFamily family) =>
        family == SocketFamily.Ipv6 ? Ipv6 : (int)family;

    /// <summary>Turns a value of the platform into the managed family.</summary>
    /// <param name="native">What the native call produced.</param>
    /// <returns>
    /// The managed family. A family this enumeration does not name comes
    /// through as its raw platform number.
    /// </returns>
    internal static SocketFamily FromNative(int native) =>
        native == Ipv6 ? SocketFamily.Ipv6 : (SocketFamily)native;
}
