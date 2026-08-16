using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GTlsCertificate</c>.
/// </summary>
/// <remarks>
/// The wrapper carries no members. A certificate reaches managed code as the
/// subject of a decision — the peer certificate an accept-certificate callback
/// is asked about — and the decision itself is taken through the API of the
/// GStreamer type that raised it, so the wrapper only has to give the handle a
/// managed identity. Reading the certificate itself (<c>new_from_pem</c>, the
/// <c>certificate-pem</c> property) is a later extension.
/// </remarks>
public abstract partial class TlsCertificate : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GTlsCertificate</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal TlsCertificate(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GTlsCertificate</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_tls_certificate_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

    /// <summary>
    /// The wrapper of a native type that derives from <c>GTlsCertificate</c>
    /// and has no binding of its own, which is every certificate a TLS backend
    /// creates.
    /// </summary>
    private sealed class Concrete : TlsCertificate
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
