using System.Runtime.InteropServices;

namespace Gst;

/// <summary>The native layout of <c>GstURIHandlerInterface</c>.</summary>
/// <remarks>
/// The slots are <see cref="nint"/> rather than typed function pointers, the
/// way the class struct mirrors of the binding spell theirs: the runtime only
/// writes them, and it casts at the point of use. The offsets are asserted
/// against the running library by the ABI probe tests. See
/// <c>docs/subclassing.md</c> §5.7.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GstURIHandlerInterfaceRaw
{
    /// <summary>The byte offset of the <c>get_type</c> slot.</summary>
    internal const int GetUriTypeOffset = 16;

    /// <summary>The byte offset of the <c>get_protocols</c> slot.</summary>
    internal const int GetProtocolsOffset = 24;

    /// <summary>The byte offset of the <c>get_uri</c> slot.</summary>
    internal const int GetUriOffset = 32;

    /// <summary>The byte offset of the <c>set_uri</c> slot.</summary>
    internal const int SetUriOffset = 40;

    /// <summary>The <c>parent</c> field, the header every vtable starts with.</summary>
    internal Gst.GObject.GTypeInterfaceRaw Parent;

    /// <summary>The <c>get_type</c> slot, which answers for a type rather than an instance.</summary>
    internal nint GetUriType;

    /// <summary>The <c>get_protocols</c> slot, which answers for a type rather than an instance.</summary>
    internal nint GetProtocols;

    /// <summary>The <c>get_uri</c> slot.</summary>
    internal nint GetUri;

    /// <summary>The <c>set_uri</c> slot.</summary>
    internal nint SetUri;
}
