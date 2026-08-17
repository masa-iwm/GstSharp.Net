namespace Gst.Interop;

/// <summary>
/// A native mini object that is lent to managed code for the length of one
/// call, and that the caller keeps owning.
/// </summary>
/// <remarks>
/// <para>
/// This is the one exception to the rule of <c>docs/ownership.md</c> that every
/// mini object wrapper owns a reference of its own, and it exists for the
/// reverse direction only: a vfunc override receives the buffer or the caps
/// that GStreamer is holding, uses it while the call runs, and never keeps it.
/// A wrapper built from this takes no reference and releases none; disposing it
/// only detaches it, so a wrapper that outlives the call throws
/// <see cref="System.ObjectDisposedException"/> instead of touching an object
/// it does not own.
/// </para>
/// <para>
/// Taking a reference for the duration of the call — which is what the borrow
/// of a signal argument does — is not usable here: it makes the object
/// non-writable, and <c>gst_buffer_map</c> refuses a write mapping on a buffer
/// that somebody else holds. An in-place transform receives a buffer that
/// GStreamer has already made writable, so the wrapper must not be the second
/// holder that takes that away. See <c>docs/subclassing.md</c> §4.3.
/// </para>
/// </remarks>
internal readonly struct Borrowed
{
    /// <summary>Lends a native mini object to managed code.</summary>
    /// <param name="handle">The mini object the caller keeps owning.</param>
    internal Borrowed(nint handle) => Handle = handle;

    /// <summary>Gets the lent mini object.</summary>
    internal nint Handle { get; }
}
