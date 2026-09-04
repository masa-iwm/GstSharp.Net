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

    /// <summary>Takes a wrapper of this frame that its caller owns.</summary>
    /// <returns>
    /// A wrapper holding its own reference to the same frame, which its owner
    /// has to dispose.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>g_boxed_copy</c> of the boxed type, which for
    /// <c>GstVideoCodecFrame</c> is <c>gst_video_codec_frame_ref</c>: the answer
    /// is not a copy of the frame but a second reference to the very same one,
    /// so what is written through one wrapper is read back through the other.
    /// </para>
    /// <para>
    /// This is what a slot that is lent a frame — <c>handle_frame</c> of a
    /// decoder, <c>parse</c> — calls to keep it past the call, since the wrapper
    /// the override is handed stops meaning anything when the slot returns.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public VideoCodecFrame Copy()
    {
        nint copy = Gst.Interop.GObjectNative.BoxedCopy(BoxedType.Value, Handle);
        GC.KeepAlive(this);
        return FromNative(copy, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_video_codec_frame_ref returned no value.");
    }
}
