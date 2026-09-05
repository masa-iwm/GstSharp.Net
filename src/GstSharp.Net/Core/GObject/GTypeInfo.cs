using System.Runtime.InteropServices;

namespace Gst.GObject;

/// <summary>
/// The native layout of <c>GTypeInfo</c>, the description one static type is
/// registered from.
/// </summary>
/// <remarks>
/// <para>
/// The mirror exists because <c>g_type_register_static_simple</c> has no
/// <c>class_data</c> member, and a single shared <c>class_init</c> — which is
/// what NativeAOT forces, there being no per-type code generation — needs a
/// discriminator to tell the managed subclasses apart. See
/// <c>docs/subclassing.md</c> §3.1.
/// </para>
/// <para>
/// <c>class_size</c>, <c>instance_size</c> and <c>n_preallocs</c> are
/// <c>guint16</c> in C, which is a real limit rather than a spelling choice: a
/// class struct larger than 64 KiB cannot be registered at all.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GTypeInfo
{
    /// <summary>The <c>class_size</c> field, in bytes.</summary>
    internal ushort ClassSize;

    /// <summary>The <c>base_init</c> field, always null for managed types.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> BaseInit;

    /// <summary>The <c>base_finalize</c> field, always null for managed types.</summary>
    internal delegate* unmanaged[Cdecl]<nint, void> BaseFinalize;

    /// <summary>The <c>class_init</c> field, called as <c>(g_class, class_data)</c>.</summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, void> ClassInit;

    /// <summary>
    /// The <c>class_finalize</c> field, always null: managed types are static
    /// types and their classes are never finalized.
    /// </summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, void> ClassFinalize;

    /// <summary>
    /// The <c>class_data</c> field, handed back as the second argument of
    /// <see cref="ClassInit"/>.
    /// </summary>
    internal nint ClassData;

    /// <summary>The <c>instance_size</c> field, in bytes.</summary>
    internal ushort InstanceSize;

    /// <summary>The <c>n_preallocs</c> field, ignored by GLib since 2.10.</summary>
    internal ushort NPreallocs;

    /// <summary>The <c>instance_init</c> field, called as <c>(instance, g_class)</c>.</summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, void> InstanceInit;

    /// <summary>The <c>value_table</c> field, always null for managed types.</summary>
    internal nint ValueTable;
}

/// <summary>
/// The native layout of <c>GTypeQuery</c>, the answer of <c>g_type_query</c>.
/// </summary>
/// <remarks>
/// The sizes a managed subclass registers with are read out of the running
/// library rather than taken from the gir or from a compile time constant, so
/// that registration keeps working when a newer GStreamer grows a class within
/// its padding. See <c>docs/subclassing.md</c> §3.2.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GTypeQuery
{
    /// <summary>The <c>type</c> field, zero when the type has no class.</summary>
    internal nuint Type;

    /// <summary>The <c>type_name</c> field, owned by GObject.</summary>
    internal nint TypeName;

    /// <summary>The <c>class_size</c> field, in bytes.</summary>
    internal uint ClassSize;

    /// <summary>The <c>instance_size</c> field, in bytes.</summary>
    internal uint InstanceSize;
}

/// <summary>
/// The native layout of <c>GInterfaceInfo</c>, the description one interface is
/// attached to a type with.
/// </summary>
/// <remarks>
/// GLib copies the struct with <c>g_memdup2</c> while
/// <c>g_type_add_interface_static</c> runs, so a caller may hand it a stack
/// value. See <c>docs/subclassing.md</c> §5.7.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GInterfaceInfo
{
    /// <summary>
    /// The <c>interface_init</c> field, called as <c>(g_iface, iface_data)</c>
    /// once the class of the implementing type is initialised.
    /// </summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, void> InterfaceInit;

    /// <summary>
    /// The <c>interface_finalize</c> field, always null: an interface vtable of
    /// a static type is never finalized.
    /// </summary>
    internal delegate* unmanaged[Cdecl]<nint, nint, void> InterfaceFinalize;

    /// <summary>
    /// The <c>interface_data</c> field, handed back as the second argument of
    /// <see cref="InterfaceInit"/>. The runtime passes none: the trampoline
    /// reads the two types out of the vtable header instead.
    /// </summary>
    internal nint InterfaceData;
}
