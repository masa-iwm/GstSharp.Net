using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GTlsConnection</c>, the TLS layer over a stream.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper carries no members. Everything a GStreamer application sets on a
/// TLS connection — the certificate database, the interaction, the validation
/// flags, the accept-certificate decision — is reached through the API of the
/// GStreamer type that owns the connection, so the wrapper only has to exist,
/// so that the handle such a call hands back has a managed identity.
/// </para>
/// <para>
/// <strong>The base class is deliberately wrong.</strong> In C the parent of
/// <c>GTlsConnection</c> is <c>GIOStream</c>, and this wrapper derives from
/// <see cref="Gst.GObject.Object"/> instead: binding <c>GIOStream</c> pulls in
/// the whole stream hierarchy of Gio for no member anything here uses. The
/// choice is recorded rather than hidden because it is source breaking to
/// revisit: inserting an <c>IOStream</c> base later changes the public type
/// hierarchy of this class, which is a breaking change for anything that
/// declared a variable of it. It costs nothing at run time — the type registry
/// walks the native ancestry, not the managed one, so a
/// <c>GTlsClientConnectionGnutls</c> still resolves to this wrapper.
/// </para>
/// </remarks>
public abstract partial class TlsConnection : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GTlsConnection</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal TlsConnection(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GTlsConnection</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_tls_connection_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

    /// <summary>
    /// The wrapper of a native type that derives from <c>GTlsConnection</c> and
    /// has no binding of its own, which is every connection a TLS backend
    /// creates.
    /// </summary>
    private sealed class Concrete : TlsConnection
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
