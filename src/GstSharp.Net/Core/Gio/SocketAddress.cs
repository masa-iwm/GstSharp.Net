using System.Runtime.InteropServices;
using Gst.GLib;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GSocketAddress</c>, the Gio equivalent of a <c>struct sockaddr</c>.
/// </summary>
/// <remarks>
/// <para>
/// The class is abstract in C as well: an instance is always a
/// <c>GInetSocketAddress</c>, a <c>GUnixSocketAddress</c> or something else
/// that a Gio module provides. None of those subclasses is bound. What replaces
/// them is the pair of native conversions that every subclass implements —
/// <see cref="NewFromNative(nint, nuint)"/> and
/// <see cref="ToNative(nint, nuint)"/> — plus the bridge to
/// <see cref="System.Net.SocketAddress"/>, which is itself nothing but a
/// <c>struct sockaddr</c> buffer. Together they turn any address into a
/// <see cref="System.Net.IPEndPoint"/> and back without a single subclass
/// wrapper.
/// </para>
/// <para>
/// An address that arrives from native code is wrapped as this type, whatever
/// its exact subclass is, because that is the closest registered ancestor the
/// type registry finds.
/// </para>
/// </remarks>
public abstract unsafe partial class SocketAddress : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GSocketAddress</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal SocketAddress(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>
    /// Gets the address family of the address.
    /// </summary>
    /// <remarks>
    /// The native call answers the <c>AF_*</c> number of the platform, which
    /// <see cref="SocketFamilyNative"/> translates onto the one value a member
    /// of <see cref="SocketFamily"/> has everywhere.
    /// </remarks>
    public SocketFamily Family
    {
        get
        {
            int result = GSocketAddressGetFamily(Handle);
            GC.KeepAlive(this);
            return SocketFamilyNative.FromNative(result);
        }
    }

    /// <summary>
    /// Gets the size of the <c>struct sockaddr</c> this address converts into.
    /// </summary>
    /// <remarks>
    /// The C function returns a <c>gssize</c>, so the value is signed and
    /// pointer sized. It is what <see cref="ToNative(nint, nuint)"/> needs at
    /// least.
    /// </remarks>
    public nint NativeSize
    {
        get
        {
            nint result = GSocketAddressGetNativeSize(Handle);
            GC.KeepAlive(this);
            return result;
        }
    }

    /// <summary>
    /// Creates the address that a <c>struct sockaddr</c> describes.
    /// </summary>
    /// <param name="sockaddr">A pointer to a <c>struct sockaddr</c>.</param>
    /// <param name="length">The size of the memory <paramref name="sockaddr"/> points at.</param>
    /// <returns>
    /// The address, or <see langword="null"/> when Gio knows no subclass for
    /// the address family in the buffer.
    /// </returns>
    /// <remarks>
    /// The buffer is read during the call and not retained, so it may live on
    /// the stack of the caller.
    /// </remarks>
    public static SocketAddress? NewFromNative(nint sockaddr, nuint length)
    {
        nint nativeResult = GSocketAddressNewFromNative(sockaddr, length);
        return FromNative<SocketAddress>(nativeResult, Transfer.Full);
    }

    /// <summary>
    /// Creates the address that a <see cref="System.Net.SocketAddress"/>
    /// describes.
    /// </summary>
    /// <param name="address">
    /// The address to convert. <see cref="System.Net.IPEndPoint.Serialize"/>
    /// produces one from an endpoint.
    /// </param>
    /// <returns>
    /// The address, or <see langword="null"/> when Gio knows no subclass for
    /// the address family of <paramref name="address"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public static SocketAddress? FromSystemAddress(System.Net.SocketAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // The managed type is a struct sockaddr buffer and nothing else, so the
        // conversion is the buffer itself. Size is what is filled in; the array
        // behind it may be larger.
        Span<byte> buffer = address.Buffer.Span;
        int size = Math.Min(address.Size, buffer.Length);

        fixed (byte* pointer = buffer)
        {
            return NewFromNative((nint)pointer, (nuint)size);
        }
    }

    /// <summary>
    /// Writes the address into a <c>struct sockaddr</c>.
    /// </summary>
    /// <param name="dest">
    /// The memory to fill, which has to be at least
    /// <see cref="NativeSize"/> bytes long.
    /// </param>
    /// <param name="destlen">The size of <paramref name="dest"/>.</param>
    /// <exception cref="GException">
    /// The buffer is too small, or the platform does not know the address
    /// family.
    /// </exception>
    public void ToNative(nint dest, nuint destlen)
    {
        nint errorNative = 0;
        int nativeResult = GSocketAddressToNative(Handle, dest, destlen, &errorNative);
        GC.KeepAlive(this);
        Check(nativeResult, ref errorNative, "g_socket_address_to_native");
    }

    /// <summary>
    /// Converts the address into a <see cref="System.Net.SocketAddress"/>.
    /// </summary>
    /// <returns>
    /// The managed address. <see cref="System.Net.IPEndPoint.Create"/> turns it
    /// into an endpoint:
    /// <c>(IPEndPoint)new IPEndPoint(IPAddress.Any, 0).Create(converted)</c>.
    /// </returns>
    /// <exception cref="GException">
    /// The address cannot be written as a <c>struct sockaddr</c>.
    /// </exception>
    public System.Net.SocketAddress ToSystemAddress()
    {
        nint size = NativeSize;
        if (size <= 0)
        {
            throw new InvalidOperationException(
                "g_socket_address_get_native_size reported no native size for this address.");
        }

        // The family is written by the conversion below, together with the rest
        // of the sockaddr, so the one the buffer is created with is irrelevant.
        System.Net.SocketAddress address = new(System.Net.Sockets.AddressFamily.Unspecified, checked((int)size));
        Span<byte> buffer = address.Buffer.Span;

        if (buffer.Length < size)
        {
            throw new InvalidOperationException(
                "The managed socket address is smaller than the native one it has to hold.");
        }

        fixed (byte* pointer = buffer)
        {
            ToNative((nint)pointer, (nuint)size);
        }

        return address;
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GSocketAddress</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_socket_address_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

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

    /// <summary>The <c>g_socket_address_new_from_native</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_address_new_from_native")]
    private static partial nint GSocketAddressNewFromNative(nint native, nuint length);

    /// <summary>The <c>g_socket_address_get_family</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_address_get_family")]
    private static partial int GSocketAddressGetFamily(nint address);

    /// <summary>The <c>g_socket_address_get_native_size</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_address_get_native_size")]
    private static partial nint GSocketAddressGetNativeSize(nint address);

    /// <summary>The <c>g_socket_address_to_native</c> entry point.</summary>
    [LibraryImport("Gio", EntryPoint = "g_socket_address_to_native")]
    private static partial int GSocketAddressToNative(nint address, nint dest, nuint destlen, nint* error);

    /// <summary>
    /// The wrapper of a native type that derives from <c>GSocketAddress</c> and
    /// has no binding of its own, which is every one of them.
    /// </summary>
    private sealed class Concrete : SocketAddress
    {
        /// <summary>Wraps a native instance.</summary>
        /// <param name="handle">The native instance.</param>
        /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
        internal Concrete(nint handle, Transfer transfer)
            : base(handle, transfer)
        {
        }
    }
}
