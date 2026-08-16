using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GSocket</c>: the low level socket object that the GStreamer networking
/// elements hand out and take in.
/// </summary>
/// <remarks>
/// <para>
/// The binding covers the members a GStreamer application needs to create a
/// socket, give it a local address and read that address back; the roughly
/// sixty remaining members of the C type stay unbound until something asks for
/// them.
/// </para>
/// <para>
/// <c>GSocket</c> implements <c>GInitable</c>, and both constructors below run
/// its initialisation themselves, so nothing has to call <c>g_initable_init</c>
/// and the interface is not bound.
/// </para>
/// <para>
/// There is deliberately no bridge to
/// <see cref="System.Net.Sockets.Socket"/>. Building one means handing a file
/// descriptor across two ownership models — see the remarks on
/// <see cref="NewFromFd(int)"/> — and that belongs in its own change.
/// </para>
/// </remarks>
public unsafe partial class Socket : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GSocket</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal Socket(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the underlying operating system socket: a file descriptor on UNIX
    /// and a Winsock2 <c>SOCKET</c> on Windows.
    /// </summary>
    /// <remarks>
    /// The socket belongs to the <c>GSocket</c>, which closes it when its last
    /// reference goes away. The value is a <c>gint</c> in C on both platforms;
    /// Windows guarantees that a socket handle fits into 32 bits.
    /// </remarks>
    public int Fd
    {
        get
        {
            int result = GSocketGetFd(Handle);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>
    /// Creates a socket of a family, type and protocol.
    /// </summary>
    /// <param name="family">The address family, for example <see cref="SocketFamily.Ipv4"/>.</param>
    /// <param name="type">The communication style, for example <see cref="SocketType.Datagram"/>.</param>
    /// <param name="protocol">
    /// The protocol, or <see cref="SocketProtocol.Default"/> for the default
    /// protocol of the family and type. Any number the platform knows can be
    /// used by casting it to <see cref="SocketProtocol"/>.
    /// </param>
    /// <returns>The new socket.</returns>
    /// <exception cref="GException">The socket could not be created.</exception>
    public static Socket New(SocketFamily family, SocketType type, SocketProtocol protocol)
    {
        nint errorNative = 0;
        nint nativeResult = GSocketNew(
            SocketFamilyNative.ToNative(family), (int)type, (int)protocol, &errorNative);
        GException.ThrowIfSet(ref errorNative);
        return FromNative<Socket>(nativeResult, Transfer.Full)
            ?? throw new InvalidOperationException("g_socket_new returned no value.");
    }

    /// <summary>
    /// Creates a socket around an operating system socket that already exists.
    /// </summary>
    /// <param name="fd">
    /// A native socket file descriptor on UNIX, or a Winsock2 <c>SOCKET</c> on
    /// Windows.
    /// </param>
    /// <returns>The new socket.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Ownership</strong>: on success the <c>GSocket</c> takes
    /// <paramref name="fd"/> over and closes it when its last reference goes
    /// away, so the caller must not close it as well. On failure — that is,
    /// when this throws — <paramref name="fd"/> is still the caller's and has
    /// to be closed by them. A descriptor that a
    /// <see cref="System.Net.Sockets.Socket"/> owns must therefore be released
    /// by that socket first, which is what
    /// <c>System.Net.Sockets.Socket.DuplicateAndClose</c> and
    /// <c>SafeHandle</c> ownership are for; handing over a descriptor that .NET
    /// still owns closes it twice.
    /// </para>
    /// <para>
    /// The descriptor is put into non-blocking mode, whatever the blocking mode
    /// of the resulting <c>GSocket</c> is.
    /// </para>
    /// </remarks>
    /// <exception cref="GException">The socket could not be created.</exception>
    public static Socket NewFromFd(int fd)
    {
        nint errorNative = 0;
        nint nativeResult = GSocketNewFromFd(fd, &errorNative);
        GException.ThrowIfSet(ref errorNative);
        return FromNative<Socket>(nativeResult, Transfer.Full)
            ?? throw new InvalidOperationException("g_socket_new_from_fd returned no value.");
    }

    /// <summary>
    /// Gives the socket a local address.
    /// </summary>
    /// <param name="address">The address to bind to.</param>
    /// <param name="allowReuse">
    /// <see langword="true"/> to set <c>SO_REUSEADDR</c>, which a server socket
    /// normally wants and a client socket normally does not. On a UDP socket it
    /// decides whether other sockets may bind the same address as well.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    /// <exception cref="GException">The socket could not be bound.</exception>
    public void Bind(SocketAddress address, bool allowReuse)
    {
        ArgumentNullException.ThrowIfNull(address);

        nint errorNative = 0;
        int nativeResult = GSocketBind(Handle, address.Handle, allowReuse ? 1 : 0, &errorNative);
        GC.KeepAlive(this);
        GC.KeepAlive(address);
        Check(nativeResult, ref errorNative, "g_socket_bind");
    }

    /// <summary>
    /// Closes the socket, shutting any active connection down.
    /// </summary>
    /// <remarks>
    /// A socket is closed anyway when its last reference goes away; closing it
    /// here releases the operating system resources earlier. Closing an already
    /// closed socket is not an error.
    /// </remarks>
    /// <exception cref="GException">The socket could not be closed.</exception>
    public void Close()
    {
        nint errorNative = 0;
        int nativeResult = GSocketClose(Handle, &errorNative);
        GC.KeepAlive(this);
        Check(nativeResult, ref errorNative, "g_socket_close");
    }

    /// <summary>
    /// Reads the local address of a bound socket back.
    /// </summary>
    /// <returns>
    /// The address the socket is bound to, which is the one the operating
    /// system chose when <see cref="Bind(SocketAddress, bool)"/> asked for port
    /// zero.
    /// </returns>
    /// <exception cref="GException">The socket has no local address.</exception>
    public SocketAddress GetLocalAddress()
    {
        nint errorNative = 0;
        nint nativeResult = GSocketGetLocalAddress(Handle, &errorNative);
        GC.KeepAlive(this);
        GException.ThrowIfSet(ref errorNative);
        return FromNative<SocketAddress>(nativeResult, Transfer.Full)
            ?? throw new InvalidOperationException("g_socket_get_local_address returned no value.");
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GSocket</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_socket_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Socket(handle, transfer);

    /// <summary>
    /// Turns the <c>gboolean</c> plus <c>GError</c> contract of Gio into an
    /// exception.
    /// </summary>
    /// <param name="result">What the call returned.</param>
    /// <param name="error">The <c>GError*</c> the call produced, if any.</param>
    /// <param name="entryPoint">The C entry point, for the fallback message.</param>
    private static void Check(int result, ref nint error, string entryPoint)
    {
        GException.ThrowIfSet(ref error);

        if (result == 0)
        {
            throw new GException($"{entryPoint} failed, and did not report why.");
        }
    }

    /// <summary>The <c>g_socket_new</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_new")]
    private static partial nint GSocketNew(int family, int type, int protocol, nint* error);

    /// <summary>The <c>g_socket_new_from_fd</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_new_from_fd")]
    private static partial nint GSocketNewFromFd(int fd, nint* error);

    /// <summary>The <c>g_socket_bind</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_bind")]
    private static partial int GSocketBind(nint socket, nint address, int allowReuse, nint* error);

    /// <summary>The <c>g_socket_close</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_close")]
    private static partial int GSocketClose(nint socket, nint* error);

    /// <summary>The <c>g_socket_get_fd</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_get_fd")]
    private static partial int GSocketGetFd(nint socket);

    /// <summary>The <c>g_socket_get_local_address</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_get_local_address")]
    private static partial nint GSocketGetLocalAddress(nint socket, nint* error);
}
