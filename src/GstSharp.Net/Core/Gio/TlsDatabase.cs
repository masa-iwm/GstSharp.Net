using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GTlsDatabase</c>, the store of certificates a TLS connection verifies
/// against.
/// </summary>
/// <remarks>
/// The wrapper carries no members. A database is produced by the TLS backend or
/// by an application subclass in C, and is only ever handed to and read back
/// from the GStreamer type that uses it, so the wrapper exists to give the
/// handle a managed identity and nothing else.
/// </remarks>
public abstract partial class TlsDatabase : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GTlsDatabase</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal TlsDatabase(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GTlsDatabase</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_tls_database_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

    /// <summary>
    /// The wrapper of a native type that derives from <c>GTlsDatabase</c> and
    /// has no binding of its own, which is every database a TLS backend
    /// provides.
    /// </summary>
    private sealed class Concrete : TlsDatabase
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
