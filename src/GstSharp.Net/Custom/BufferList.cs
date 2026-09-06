namespace Gst;

/// <content>
/// The shallow copy of a buffer list, which the generator cannot emit.
/// </content>
/// <remarks>
/// <c>gst_buffer_list_copy</c> is a static inline function of the C header and
/// the gir marks it <c>introspectable="0"</c>, so no overlay can bring it
/// back; the exported <c>gst_mini_object_copy</c> it forwards to is what the
/// member below calls. Its deep counterpart, <c>gst_buffer_list_copy_deep</c>,
/// is a real symbol and is generated as <see cref="BufferList.CopyDeep"/>.
/// </remarks>
public sealed partial class BufferList
{
    /// <summary>
    /// Creates a copy of this buffer list.
    /// </summary>
    /// <returns>
    /// The copy, which the caller owns, or <see langword="null"/> when the type
    /// of the object has no copy function.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_buffer_list_copy</c>, hand written for the reason
    /// <see cref="Gst.Buffer.Copy"/> is: the gir marks the function
    /// <c>introspectable="0"</c>, so the generator skips it and no overlay can
    /// bring it back. For C consumers it is a static inline function of
    /// <c>gst/gstbufferlist.h</c>, and the entry point called here is the
    /// <c>gst_mini_object_copy</c> it forwards to, which the library exports as
    /// a real symbol. That call answers NULL for a type that installed no copy
    /// function, which a buffer list never is; the nullable return states what the C
    /// promises rather than a narrower promise this binding cannot take back.
    /// </para>
    /// <para>
    /// The copy is a list of the same length whose buffers are the buffers of
    /// the original, referenced rather than copied
    /// (<c>_gst_buffer_list_copy</c>, gstbufferlist.c:80-99). It is the list
    /// itself that a copy makes writable — it holds the only reference to
    /// itself, so buffers may be inserted into it and removed from it — while
    /// the buffers inside it stay shared with the original. Use
    /// <see cref="CopyDeep"/> when the buffers have to be copies too.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.BufferList? Copy()
    {
        nint nativeResult = GstNative.MiniObjectCopy(Handle);

        // The buffer list has to outlive the call that reads it: reading Handle is
        // the last use of this wrapper, and a finalizer that runs in between
        // would release the buffer list being copied.
        GC.KeepAlive(this);
        return Gst.BufferList.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }
}
