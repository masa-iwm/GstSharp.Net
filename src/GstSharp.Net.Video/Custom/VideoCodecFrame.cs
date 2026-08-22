namespace Gst.Video;

public sealed partial class VideoCodecFrame
{
    /// <summary>
    /// Attaches a notification to the frame that runs when the frame is
    /// released, replacing the one that is attached.
    /// </summary>
    /// <param name="notify">
    /// What to run when the frame is released, or <see langword="null"/> to
    /// clear the slot.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_video_codec_frame_set_user_data</c>. The C function pairs
    /// a pointer with a <c>GDestroyNotify</c>; the binding stores the state of
    /// <paramref name="notify"/> in that pointer, so there is nothing else to
    /// pass and nothing to read back. <see cref="GetUserData"/> answers the
    /// binding's own handle for a frame this member was called on, and that
    /// value must not be dereferenced.
    /// </para>
    /// <para>
    /// A notification that is already attached runs <em>synchronously</em>, on
    /// the calling thread, before this call returns: replacing a notification
    /// is how the previous one is released. Otherwise the notification runs
    /// when the frame is released, on an arbitrary streaming thread — whichever
    /// one drops the last reference of the frame.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe void SetUserData(Action? notify)
    {
        nint handle = Handle;
        Gst.Interop.CallbackHandle state = notify is null
            ? default
            : Gst.Interop.CallbackHandle.Alloc(notify);

        VideoCodecFrameNative.SetUserData(
            handle,
            state.UserData,
            notify is null ? 0 : (nint)Gst.Interop.CallbackHandle.InvokeAndFreeNotify);

        GC.KeepAlive(this);
    }
}
