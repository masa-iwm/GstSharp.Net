using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// A <c>GSocketControlMessage</c>, the ancillary data of a socket message.
/// </summary>
/// <remarks>
/// The wrapper carries no members. GStreamer only ever passes such a message
/// through — <c>GstNetControlMessageMeta</c> holds one and hands it back — so
/// what the binding needs is a managed identity for the handle, not the C
/// surface of the type. An instance is always one of the platform specific
/// subclasses, none of which is bound; the type registry resolves them to this
/// wrapper through their ancestry.
/// </remarks>
public abstract partial class SocketControlMessage : Gst.GObject.Object
{
    /// <summary>
    /// Wraps a native <c>GSocketControlMessage</c>.
    /// </summary>
    /// <param name="handle">The object to wrap.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    internal SocketControlMessage(nint handle, Transfer transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Returns the <c>GType</c> that GObject registered <c>GSocketControlMessage</c> under.</summary>
    /// <returns>The type of the instances of this wrapper.</returns>
    [LibraryImport("Gio", EntryPoint = "g_socket_control_message_get_type")]
    internal static partial nuint GetGType();

    /// <summary>Creates the wrapper of a native instance, for the type registry.</summary>
    /// <param name="handle">The native instance.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The new wrapper.</returns>
    internal static object CreateWrapper(nint handle, Transfer transfer) => new Concrete(handle, transfer);

    /// <summary>
    /// The wrapper of a native type that derives from
    /// <c>GSocketControlMessage</c> and has no binding of its own.
    /// </summary>
    private sealed class Concrete : SocketControlMessage
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
