namespace Gst;

/// <content>
/// The copy of a sample, which the generator cannot emit.
/// </content>
/// <remarks>
/// <c>gst_sample_copy</c> is a static inline function of the C header and the
/// gir marks it <c>introspectable="0"</c>, so no overlay can bring it back;
/// the exported <c>gst_mini_object_copy</c> it forwards to is what the member
/// below calls.
/// </remarks>
public sealed partial class Sample
{
    /// <summary>
    /// Creates a copy of this sample.
    /// </summary>
    /// <returns>
    /// The copy, which the caller owns, or <see langword="null"/> when the type
    /// of the object has no copy function.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_sample_copy</c>, hand written for the reason
    /// <see cref="Gst.Buffer.Copy"/> is: the gir marks the function
    /// <c>introspectable="0"</c>, so the generator skips it and no overlay can
    /// bring it back. For C consumers it is a static inline function of
    /// <c>gst/gstsample.h</c>, and the entry point called here is the
    /// <c>gst_mini_object_copy</c> it forwards to, which the library exports as
    /// a real symbol. That call answers NULL for a type that installed no copy
    /// function, which a sample never is; the nullable return states what the C
    /// promises rather than a narrower promise this binding cannot take back.
    /// </para>
    /// <para>
    /// The copy carries the buffer, the caps and the buffer list of the
    /// original by reference and its segment by value, and the structure of
    /// extra information is copied with it (<c>_gst_sample_copy</c>,
    /// gstsample.c:62-77, over <c>gst_sample_new</c>, gstsample.c:126-174).
    /// Neither the buffer, the caps nor the buffer list is duplicated, so the
    /// copy is cheap however large the frame is; the copy holds the only
    /// reference to itself and is therefore writable as a mini object, while
    /// the buffer it shares with the original is not.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.Sample? Copy()
    {
        nint nativeResult = GstNative.MiniObjectCopy(Handle);

        // The sample has to outlive the call that reads it: reading Handle is
        // the last use of this wrapper, and a finalizer that runs in between
        // would release the sample being copied.
        GC.KeepAlive(this);
        return Gst.Sample.FromNative(nativeResult, Gst.Interop.Transfer.Full);
    }
}
