using System.Runtime.InteropServices;

namespace Gst.Video;

/// <summary>
/// Raw entry points of <c>libgstvideo-1.0</c> that the hand written codec frame
/// glue needs.
/// </summary>
/// <remarks>
/// <c>gst_video_codec_frame_set_user_data</c> is imported by hand because the
/// notification it installs is run synchronously when the slot is written
/// again, which no generated shape expresses: the state of the previous call
/// is released by the next one rather than by the call that allocated it.
/// </remarks>
internal static unsafe partial class VideoCodecFrameNative
{
    /// <summary>
    /// Stores a pointer on the frame together with the notification that
    /// releases it.
    /// </summary>
    /// <param name="frame">The frame to write to.</param>
    /// <param name="userData">The pointer to store, or <c>0</c> to clear the slot.</param>
    /// <param name="notify">The notification, or <c>0</c>.</param>
    [LibraryImport("GstVideo", EntryPoint = "gst_video_codec_frame_set_user_data")]
    internal static partial void SetUserData(nint frame, nint userData, nint notify);
}
