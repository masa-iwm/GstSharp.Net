namespace Gst.Video;

public sealed partial class VideoCodecState
{
    /// <summary>
    /// Sets the caps of the state, releasing the ones that were there.
    /// </summary>
    /// <param name="caps">
    /// The caps, whose reference the state takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is how a codec puts something into the format it negotiates that
    /// <c>gst_video_info_to_caps</c> does not carry.
    /// <c>_gst_video_codec_state_free</c> unrefs whatever is still there, so
    /// the state owns exactly one reference to it, and
    /// <c>gst_video_decoder_negotiate_default</c> ends in
    /// <c>gst_pad_set_caps</c> with the caps that are in the field.
    /// </para>
    /// <para>
    /// A decoder fills an empty field from <c>gst_video_info_to_caps</c> while
    /// it negotiates, so clearing it there only undoes what was set. An
    /// encoder does not: <c>gst_video_encoder_negotiate_default</c> trips its
    /// guard on an output state without caps, which is a critical warning and
    /// a failed negotiation. Negotiating may also make the caps writable and
    /// put a different reference in the field
    /// — that is how the mastering display info and the content light level
    /// are added — so read the field back with <see cref="GetCaps"/> rather
    /// than assuming what was set is still there.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Read the field back with <see cref="GetCaps"/>
    /// for a usable wrapper of the caps, or set a <see cref="Gst.Caps.Copy"/>
    /// of the ones to keep.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the caps, was disposed.
    /// </exception>
    public unsafe void SetCaps(Gst.Caps? caps)
    {
        nint handle = Handle;
        nint value = caps is null ? nint.Zero : caps.HandOver();
        nint previous = ((VideoCodecStateRaw*)handle)->Caps;
        ((VideoCodecStateRaw*)handle)->Caps = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
    }

    /// <summary>
    /// Sets the caps the buffer pool is negotiated with, releasing the ones
    /// that were there.
    /// </summary>
    /// <param name="caps">
    /// The allocation caps, whose reference the state takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// These are the caps the allocation query carries, which is how a codec
    /// that decodes into larger buffers than it puts out — an alignment, a
    /// padding — asks downstream for the memory it really needs.
    /// <c>_gst_video_codec_state_free</c> unrefs whatever is still there, so
    /// the state owns exactly one reference to it, and
    /// <c>gst_video_decoder_negotiate_default</c> hands the field to
    /// <c>gst_video_decoder_negotiate_pool</c>, where it becomes the caps the
    /// <c>decide_allocation</c> query is made with.
    /// </para>
    /// <para>
    /// <see langword="null"/> is the ordinary value: both the decoder and the
    /// encoder reference the caps of the state when the field is empty, so
    /// clearing it lets the allocation follow the format again.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Read the field back with
    /// <see cref="GetAllocationCaps"/> for a usable wrapper of the caps, or
    /// set a <see cref="Gst.Caps.Copy"/> of the ones to keep.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the caps, was disposed.
    /// </exception>
    public unsafe void SetAllocationCaps(Gst.Caps? caps)
    {
        nint handle = Handle;
        nint value = caps is null ? nint.Zero : caps.HandOver();
        nint previous = ((VideoCodecStateRaw*)handle)->AllocationCaps;
        ((VideoCodecStateRaw*)handle)->AllocationCaps = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
    }

    /// <summary>Takes a wrapper of this state that its caller owns.</summary>
    /// <returns>
    /// A wrapper holding its own reference to the same state, which its owner
    /// has to dispose.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>g_boxed_copy</c> of the boxed type, which for
    /// <c>GstVideoCodecState</c> is <c>gst_video_codec_state_ref</c>: the answer
    /// is not a copy of the state but a second reference to the very same one,
    /// so what is written through one wrapper is read back through the other.
    /// </para>
    /// <para>
    /// This is what a decoder or an encoder calls in <c>set_format</c> to keep
    /// the input state past the call — the conventional
    /// <c>gst_video_codec_state_ref</c> of a codec that remembers what it was
    /// configured with — since the wrapper the override is handed stops meaning
    /// anything when the slot returns.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public VideoCodecState Copy()
    {
        nint copy = Gst.Interop.GObjectNative.BoxedCopy(BoxedType.Value, Handle);
        GC.KeepAlive(this);
        return FromNative(copy, Gst.Interop.Transfer.Full)
            ?? throw new InvalidOperationException("gst_video_codec_state_ref returned no value.");
    }
}
