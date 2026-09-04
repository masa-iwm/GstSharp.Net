namespace Gst.Base;

public sealed partial class BaseParseFrame
{
    /// <summary>
    /// Adds flags to the ones the frame carries.
    /// </summary>
    /// <param name="flags">The flags to add.</param>
    /// <remarks>
    /// The generated <see cref="Flags"/> reads the field; the field is
    /// writable, and setting a flag on the frame it is handed is how a parser
    /// tells the base class what to do with it —
    /// <see cref="BaseParseFrameFlags.Drop"/> to drop it,
    /// <see cref="BaseParseFrameFlags.Queue"/> to hold it back and
    /// <see cref="BaseParseFrameFlags.Clip"/> to let the ordinary segment
    /// clipping happen, which is what <c>pre_push_frame</c> does when no
    /// subclass overrides it.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public unsafe void AddFlags(BaseParseFrameFlags flags)
    {
        ((BaseParseFrameRaw*)Handle)->Flags |= (uint)flags;
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Sets the buffer the parser produced for the frame, releasing the one
    /// that was there.
    /// </summary>
    /// <param name="buffer">
    /// The output buffer, whose reference the frame takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the field a parser writes when the data it hands downstream is
    /// not the input it was given — a header it rewrote, a payload it
    /// unwrapped. <c>gst_base_parse_finish_frame</c> takes the buffer out of
    /// the frame and pushes it, and <c>gst_base_parse_frame_free</c> unrefs
    /// whatever is still there, so the frame owns exactly one reference to it.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Reference the buffer first to keep a usable
    /// wrapper of it.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the buffer, was disposed.
    /// </exception>
    public unsafe void SetOutBuffer(Gst.Buffer? buffer)
    {
        nint handle = Handle;
        nint value = buffer is null ? nint.Zero : buffer.HandOver();
        nint previous = ((BaseParseFrameRaw*)handle)->OutBuffer;
        ((BaseParseFrameRaw*)handle)->OutBuffer = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
    }
}
