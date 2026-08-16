using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GTlsInteraction</c>, the object a TLS connection asks when it needs a
/// password or a client certificate from the user.
/// </summary>
/// <remarks>
/// The wrapper carries no members, and unlike the other TLS types it is not
/// abstract in C either: an interaction is implemented by subclassing it, which
/// is a C exercise, and managed code only ever passes an existing one to and
/// from the GStreamer type that uses it.
/// </remarks>
public partial class TlsInteraction : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GTlsInteraction</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal TlsInteraction(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GTlsInteraction</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_tls_interaction_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new TlsInteraction(handle, transfer);
}
