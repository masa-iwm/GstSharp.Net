using Gst.Interop;

namespace Gst.Gio;

/// <summary>
/// The type table of the hand written Gio layer.
/// </summary>
/// <remarks>
/// <para>
/// The Gio wrappers live in the runtime library rather than in a generated
/// module, so nothing emits a <c>_Module.cs</c> for them and the table is
/// written out here. It is registered from
/// <c>Gst.GstModule.Initialize</c>, together with the table of the core
/// module, because both belong to this assembly.
/// </para>
/// <para>
/// Registration is what makes <c>Gst.GObject.Object.FromNative</c> able to
/// build these wrappers at all: without an entry the lookup finds no factory
/// for the type, walks past it to <c>GObject</c>, and hands out a plain
/// <c>Gst.GObject.Object</c> that no cast to a Gio type survives. Nothing in
/// the table touches native code until the registry is frozen.
/// </para>
/// </remarks>
internal static unsafe class GioModule
{
    /// <summary>Builds the type table of the module.</summary>
    /// <returns>One entry per Gio wrapper of the runtime.</returns>
    internal static ModuleTypeEntry[] CreateEntries() =>
    [
        new ModuleTypeEntry(&Cancellable.GetGType, &Cancellable.CreateWrapper),
        new ModuleTypeEntry(&Socket.GetGType, &Socket.CreateWrapper),
        new ModuleTypeEntry(&SocketAddress.GetGType, &SocketAddress.CreateWrapper),
        new ModuleTypeEntry(&SocketControlMessage.GetGType, &SocketControlMessage.CreateWrapper),
        new ModuleTypeEntry(&TlsCertificate.GetGType, &TlsCertificate.CreateWrapper),
        new ModuleTypeEntry(&TlsConnection.GetGType, &TlsConnection.CreateWrapper),
        new ModuleTypeEntry(&TlsDatabase.GetGType, &TlsDatabase.CreateWrapper),
        new ModuleTypeEntry(&TlsInteraction.GetGType, &TlsInteraction.CreateWrapper),
    ];
}
