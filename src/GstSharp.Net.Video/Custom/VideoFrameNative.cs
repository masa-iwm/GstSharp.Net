using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// Raw entry points of <c>libgstvideo-1.0</c> that the hand written frame
/// mapping needs.
/// </summary>
/// <remarks>
/// The frame is passed as a bare address rather than as a typed pointer, so
/// that nothing about the layout of <c>GstVideoFrame</c> has to cross an
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
