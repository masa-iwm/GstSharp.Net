using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>The native layout of <c>GstColorBalanceInterface</c>.</summary>
/// <remarks>
/// The slots are <see cref="nint"/> rather than typed function pointers, the
/// way the class struct mirrors of the binding spell theirs: the runtime only
/// writes them, and it casts at the point of use. The ABI probe tests assert
/// the managed layout against the offsets of <c>colorbalance.h</c> on a 64 bit
/// platform; the running library cannot be asked, because
/// <c>g_type_query</c> answers nothing for an interface type. See
/// <c>docs/subclassing.md</c> §5.7.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GstColorBalanceInterfaceRaw
{
    /// <summary>The byte offset of the <c>list_channels</c> slot.</summary>
    internal const int ListChannelsOffset = 16;

    /// <summary>The byte offset of the <c>set_value</c> slot.</summary>
    internal const int SetValueOffset = 24;

    /// <summary>The byte offset of the <c>get_value</c> slot.</summary>
    internal const int GetValueOffset = 32;

    /// <summary>The byte offset of the <c>get_balance_type</c> slot.</summary>
    internal const int GetBalanceTypeOffset = 40;

    /// <summary>The <c>iface</c> field, the header every vtable starts with.</summary>
    internal Gst.GObject.GTypeInterfaceRaw Parent;

    /// <summary>The <c>list_channels</c> slot.</summary>
    internal nint ListChannels;

    /// <summary>The <c>set_value</c> slot.</summary>
    internal nint SetValue;

    /// <summary>The <c>get_value</c> slot.</summary>
    internal nint GetValue;

    /// <summary>The <c>get_balance_type</c> slot.</summary>
    internal nint GetBalanceType;

    /// <summary>
    /// The class handler of the <c>value-changed</c> signal, which the runtime
    /// leaves as the interface initialised it.
    /// </summary>
    internal nint ValueChanged;

    /// <summary>The <c>_gst_reserved</c> padding, <c>GST_PADDING</c> pointers.</summary>
    internal fixed long GstReserved[4];
}
