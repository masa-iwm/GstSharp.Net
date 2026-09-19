using System.Runtime.InteropServices;

namespace Gst.GObject;

/// <summary>
/// The head of a <c>GObject</c> instance, which is what every instance of a
/// <c>GObject</c> derived type starts with.
/// </summary>
/// <remarks>
/// <c>GTypeInstance</c> is one pointer, the reference count is a
/// <c>guint</c>, and the alignment of the <c>GData</c> pointer behind it is
/// what the runtime pads to, exactly as the C compiler does.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct GObjectInstanceRaw
{
    /// <summary>The <c>g_class</c> field of the <c>GTypeInstance</c>.</summary>
    internal nint TypeClass;

    /// <summary>The <c>ref_count</c> field.</summary>
    internal uint RefCount;

    /// <summary>The <c>qdata</c> field.</summary>
    internal nint QData;
}
