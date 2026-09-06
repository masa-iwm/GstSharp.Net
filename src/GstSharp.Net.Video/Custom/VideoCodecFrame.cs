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

    /// <summary>
    /// Sets the buffer the codec produced for the frame, releasing the one
    /// that was there.
    /// </summary>
    /// <param name="buffer">
    /// The output buffer, whose reference the frame takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the field a decoder or an encoder writes when it produced the
    /// output itself rather than asking the base class for it.
    /// <c>_gst_video_codec_frame_free</c> unrefs whatever is still there, so
    /// the frame owns exactly one reference to it, and
    /// <c>gst_video_decoder_finish_frame</c> reads a frame without an output
    /// buffer as one that was skipped, which makes
    /// <see langword="null"/> an ordinary value here.
    /// </para>
    /// <para>
    /// Both <c>gst_video_decoder_allocate_output_frame</c> and
    /// <c>gst_video_encoder_allocate_output_frame</c> refuse a frame whose
    /// output buffer is already set and answer <c>GST_FLOW_ERROR</c>, so this
    /// is the alternative to those calls and not a step before them. Finishing
    /// the frame may make the buffer writable and put a different one in the
    /// field, and <c>gst_video_encoder_finish_subframe</c> clears it, so read
    /// the field back with <see cref="GetOutputBuffer"/> rather than assuming
    /// what was set is still there; in subframe mode every subframe of a frame
    /// shares the one output buffer.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Read the field back with
    /// <see cref="GetOutputBuffer"/> for a usable wrapper of the buffer.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the buffer, was disposed.
    /// </exception>
    public unsafe void SetOutputBuffer(Gst.Buffer? buffer)
    {
        nint handle = Handle;
        nint value = buffer is null ? nint.Zero : buffer.HandOver();
        nint previous = ((VideoCodecFrameRaw*)handle)->OutputBuffer;
        ((VideoCodecFrameRaw*)handle)->OutputBuffer = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
    }

    /// <summary>
    /// Sets the buffer the frame was decoded or encoded from, releasing the
    /// one that was there.
    /// </summary>
    /// <param name="buffer">
    /// The input buffer, whose reference the frame takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// The base class assigns this field before it hands the frame to the
    /// subclass — through <c>gst_video_decoder_replace_input_buffer</c>, which
    /// unrefs what was there, and in <c>gst_video_encoder_new_frame</c> — and
    /// in subframe mode it assigns it again for every subframe it delivers.
    /// <c>_gst_video_codec_frame_free</c> unrefs whatever is still there, so
    /// the frame owns exactly one reference to it.
    /// </para>
    /// <para>
    /// This member is for replacing that buffer, not for clearing the field:
    /// <c>gst_video_decoder_finish_frame</c> hands the input buffer to
    /// <c>gst_buffer_foreach_meta</c> to copy the metas across, which is
    /// enabled by the default <c>transform_meta</c>, and leaving the field
    /// empty trips that call's guard with a critical warning. The encoder
    /// checks the field before it goes the same way.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Read the field back with
    /// <see cref="GetInputBuffer"/> for a usable wrapper of the buffer.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the buffer, was disposed.
    /// </exception>
    public unsafe void SetInputBuffer(Gst.Buffer? buffer)
    {
        nint handle = Handle;
        nint value = buffer is null ? nint.Zero : buffer.HandOver();
        nint previous = ((VideoCodecFrameRaw*)handle)->InputBuffer;
        ((VideoCodecFrameRaw*)handle)->InputBuffer = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
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
