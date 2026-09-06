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
    /// Sets the buffer the frame was cut from, releasing the one that was
    /// there.
    /// </summary>
    /// <param name="buffer">
    /// The input buffer, whose reference the frame takes over, or
    /// <see langword="null"/> to clear the field.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the buffer the base class handed the parser to work on.
    /// <c>gst_base_parse_frame_free</c> unrefs whatever is still there, so the
    /// frame owns exactly one reference to it, and
    /// <c>gst_base_parse_finish_frame</c> puts the output buffer back into this
    /// field.
    /// </para>
    /// <para>
    /// Clearing the field is what <c>gst_aac_parse_pre_push_frame</c> does
    /// after it has moved the buffer into the output buffer: the field is
    /// dereferenced unguarded once <c>handle_frame</c> returns having neither
    /// finished nor skipped anything, where the base class reads the DISCONT
    /// flag off it, and by <c>gst_base_parse_frame_copy</c>, while both
    /// <c>gst_base_parse_finish_frame</c> and <c>gst_base_parse_push_frame</c>
    /// refuse a frame without one. A <c>pre_push_frame</c> override that has
    /// written <see cref="SetOutBuffer"/> is therefore the one place where
    /// clearing it is safe.
    /// </para>
    /// <para>
    /// The wrapper hands its reference over and is detached by the call: using
    /// it afterwards throws. Read the field back with <see cref="GetBuffer"/>
    /// for a usable wrapper of the buffer.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">
    /// The wrapper, or the wrapper of the buffer, was disposed.
    /// </exception>
    public unsafe void SetBuffer(Gst.Buffer? buffer)
    {
        nint handle = Handle;
        nint value = buffer is null ? nint.Zero : buffer.HandOver();
        nint previous = ((BaseParseFrameRaw*)handle)->Buffer;
        ((BaseParseFrameRaw*)handle)->Buffer = value;
        GC.KeepAlive(this);

        if (previous != nint.Zero)
        {
            Gst.GstNative.MiniObjectUnref(previous);
        }
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
    /// it afterwards throws. Read the field back with <see cref="GetOutBuffer"/>
    /// for a usable wrapper of the buffer.
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
