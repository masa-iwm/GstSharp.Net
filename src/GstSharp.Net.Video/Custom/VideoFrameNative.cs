using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// The C layout of <c>GstVideoFrame</c>, which the hand written mapping scope
/// declares as its own storage.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_video_frame_map</c> fills a structure the caller declares, so the
/// binding has to declare one of exactly the size and the layout the library
/// writes: 664 bytes on a 64 bit platform, of which the leading 152 are the
/// <c>GstVideoInfo</c> the mapping copied. Getting that wrong writes past the
/// end of a stack frame, which is why the size and every offset that this glue
/// reads are pinned by the ABI probe tests, and why the fields the library
/// fills are read back out of a live mapping by the caller allocated storage
/// tests.
/// </para>
/// <para>
/// Only the fields the scope reads are named. The <c>GstVideoInfo</c> at the
/// front is kept as opaque storage rather than mirrored field by field: the
/// scope hands it out by taking a boxed copy of it, and the copy is what a
/// caller reads it through.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct VideoFrameRaw
{
    /// <summary>The number of planes a <c>GstVideoFrame</c> can hold.</summary>
    internal const int MaxPlanes = 4;

    /// <summary>The size of a <c>GstVideoInfo</c> on a 64 bit platform.</summary>
    internal const int VideoInfoSize = 152;

    /// <summary>The <c>info</c> field, a <c>GstVideoInfo</c> the mapping copied.</summary>
    internal VideoInfoStorage Info;

    /// <summary>The <c>flags</c> field, a <c>GstVideoFrameFlags</c>.</summary>
    internal int Flags;

    /// <summary>The <c>buffer</c> field, the buffer that was mapped.</summary>
    internal nint BufferPtr;

    /// <summary>The <c>meta</c> field, the <c>GstVideoMeta</c> the mapping used, or <c>0</c>.</summary>
    internal nint MetaPtr;

    /// <summary>The <c>id</c> field, the frame identifier of the meta.</summary>
    internal int Id;

    /// <summary>The <c>data</c> field, where each plane starts.</summary>
    internal PlaneDataArray Data;

    /// <summary>The <c>map</c> field, the mapping each plane was taken through.</summary>
    internal PlaneMapArray Map;

    /// <summary>The <c>_gst_reserved</c> field of <c>GstVideoFrame</c>.</summary>
    private GstReservedArray _gstReserved;

    /// <summary>Inline storage of the <c>GstVideoInfo</c> a frame carries.</summary>
    [InlineArray(VideoInfoSize)]
    internal struct VideoInfoStorage
    {
        private byte _element0;
    }

    /// <summary>Inline storage of the 4 plane pointers of a <c>GstVideoFrame</c>.</summary>
    [InlineArray(MaxPlanes)]
    internal struct PlaneDataArray
    {
        private nint _element0;
    }

    /// <summary>Inline storage of the 4 plane mappings of a <c>GstVideoFrame</c>.</summary>
    [InlineArray(MaxPlanes)]
    internal struct PlaneMapArray
    {
        private Gst.MapInfo _element0;
    }

    /// <summary>Inline storage of the 4 reserved pointers of a <c>GstVideoFrame</c>.</summary>
    [InlineArray(4)]
    private struct GstReservedArray
    {
        private nint _element0;
    }
}

/// <summary>
/// Raw entry points of <c>libgstvideo-1.0</c> that the hand written frame
/// mapping needs.
/// </summary>
/// <remarks>
/// The frame is passed as a bare address rather than as a typed pointer, so
/// that nothing about the layout of <see cref="VideoFrameRaw"/> has to cross an
/// assembly boundary in an interop signature. All three entry points are on the
/// skip list of <c>girs/overlays/fixups.json</c>: the mapping belongs to
/// <see cref="VideoFrame.MapScope"/> and the release is one way.
/// </remarks>
internal static partial class VideoFrameNative
{
    /// <summary>Fills a frame with the planes of a buffer.</summary>
    /// <param name="frame">The storage to fill.</param>
    /// <param name="info">The video info the buffer holds.</param>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="flags">The access the caller needs.</param>
    /// <returns>Non zero when the frame was mapped.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_frame_map")]
    internal static partial int Map(nint frame, nint info, nint buffer, int flags);

    /// <summary>Fills a frame with the planes of one frame of a buffer.</summary>
    /// <param name="frame">The storage to fill.</param>
    /// <param name="info">The video info the buffer holds.</param>
    /// <param name="buffer">The buffer to map.</param>
    /// <param name="id">The frame identifier, or <c>-1</c> for the first one.</param>
    /// <param name="flags">The access the caller needs.</param>
    /// <returns>Non zero when the frame was mapped.</returns>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_frame_map_id")]
    internal static partial int MapId(nint frame, nint info, nint buffer, int id, int flags);

    /// <summary>Releases the planes a frame was mapped to.</summary>
    /// <param name="frame">The frame to release.</param>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_frame_unmap")]
    internal static partial void Unmap(nint frame);
}
