using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.Base;

/// <summary>
/// The implementations <c>GstBaseSrc</c> installs on its own class, reached
/// from a subclass of a class below it whose own slot is NULL.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_push_src_alloc</c> is the <c>alloc</c> of <c>GstBaseSrcClass</c> for
/// every push source. It calls the <c>alloc</c> of <c>GstPushSrcClass</c> when
/// that slot is set and falls back to
/// <c>GST_BASE_SRC_CLASS (parent_class)-&gt;alloc</c> - which is
/// <c>gst_base_src_default_alloc</c>, the pooled allocation - when it is not.
/// The generated chain-up of <c>PushSrc.OnAlloc</c> reads the
/// <c>GstPushSrcClass</c> slot, which no class of the library sets, so without
/// this it would answer <c>NotSupported</c> where C allocates a buffer.
/// </para>
/// <para>
/// The two arguments C forwards are not on the <c>GstPushSrcClass</c> slot,
/// which takes the source and the out buffer alone, so they are reconstructed
/// the way the push path passes them: <c>GST_BUFFER_OFFSET_NONE</c>, which
/// <c>gst_base_src_default_alloc</c> never reads, and the <c>blocksize</c> of
/// the source, which is what <c>gst_base_src_loop</c> asks a buffer for.
/// </para>
/// </remarks>
internal static unsafe partial class BaseSrcDefaults
{
    /// <summary>
    /// The <c>GstBaseSrcClass</c> of the library, cached after the first
    /// lookup. A class is registered once and lives for the process, so the
    /// pointer never changes; a torn read of a machine word cannot happen and
    /// two threads racing here would peek the same address.
    /// </summary>
    private static nint _baseSrcClass;

    /// <summary>
    /// Allocates a buffer the way <c>gst_base_src_default_alloc</c> does.
    /// </summary>
    /// <param name="src">The native <c>GstPushSrc</c>, which is a <c>GstBaseSrc</c>.</param>
    /// <param name="buf">Where the new buffer is stored, owned by the caller.</param>
    /// <returns>
    /// What the base class answers, or <see cref="Gst.FlowReturn.NotSupported"/>
    /// when the library installs no <c>alloc</c> at all.
    /// </returns>
    internal static Gst.FlowReturn Alloc(nint src, nint* buf)
    {
        nint klass = _baseSrcClass;
        if (klass == 0)
        {
            klass = GObjectNative.TypeClassPeek(Gst.Base.BaseSrc.GetGType());
            _baseSrcClass = klass;
        }

        delegate* unmanaged[Cdecl]<nint, ulong, uint, nint*, int> slot = klass == 0
            ? null
            : (delegate* unmanaged[Cdecl]<nint, ulong, uint, nint*, int>)((BaseSrcClassRaw*)klass)->Alloc;

        if (slot is null)
        {
            *buf = default;
            return Gst.FlowReturn.NotSupported;
        }

        return (Gst.FlowReturn)slot(src, BufferOffsetNone, GetBlocksize(src), buf);
    }

    /// <summary>The <c>GST_BUFFER_OFFSET_NONE</c> of <c>gstbuffer.h</c>.</summary>
    private const ulong BufferOffsetNone = ulong.MaxValue;

    /// <summary>Reads the <c>blocksize</c> property of a source.</summary>
    /// <param name="src">The native <c>GstBaseSrc</c>.</param>
    /// <returns>The number of bytes a buffer is asked for.</returns>
    [LibraryImport("GstBase", EntryPoint = "gst_base_src_get_blocksize")]
    private static partial uint GetBlocksize(nint src);
}
