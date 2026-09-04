namespace Gst.Video;

public sealed partial class VideoCodecState
{
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
