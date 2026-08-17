using Gst.Interop;

namespace Gst;

/// <summary>
/// The managed wrapper of a <c>GstMiniObject</c>, the reference counted base of
/// buffers, events, messages, caps and their relatives.
/// </summary>
/// <remarks>
/// <para>
/// A mini object is not a <c>GObject</c>: it has a plain atomic reference
/// count, no properties, no signals and no toggle references. The wrapper
/// therefore owns exactly one reference and releases it when it is disposed,
/// and unlike <see cref="Gst.GObject.Object"/> it does not have to queue the
/// release for a thread that is allowed to call native code. Nothing about
/// <c>gst_mini_object_unref</c> is thread affine, so the finalizer can perform
/// it directly.
/// </para>
/// <para>
/// The one thing to keep in mind is that the free function of a mini object can
/// run managed code again, for example the destroy notification of a wrapped
/// buffer. That code then runs on the finalizer thread, so it must not block on
/// anything the finalizer thread is needed for.
/// </para>
/// <para>
/// Because one wrapper owns one reference, wrappers are not interned the way
/// GObject wrappers are: two wrappers of the same native mini object are two
/// independent references to it.
/// </para>
/// </remarks>
public abstract class MiniObject : IDisposable
{
    private readonly bool _borrowed;

    private nint _handle;

    /// <summary>
    /// Wraps a native mini object.
    /// </summary>
    /// <param name="handle">The mini object to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands a reference over,
    /// anything else when the wrapper has to take its own.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="handle"/> is <see cref="nint.Zero"/>.
    /// </exception>
    protected MiniObject(nint handle, Transfer transfer)
    {
        if (handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handle), "A mini object handle must not be null.");
        }

        if (transfer != Transfer.Full)
        {
            GstNative.MiniObjectRef(handle);
        }

        _handle = handle;

        // Adopting a mini object is the other thing an application does often
        // on a thread that is allowed to call native code, so it is the second
        // place where the releases that GObject finalizers queued are
        // performed. An application that pulls samples in a loop and never
        // looks a GObject wrapper up would otherwise let that queue grow
        // without bound. The call costs two volatile reads when the queue is
        // empty, which it normally is.
        Gst.GObject.Object.DrainPendingReleases();
    }

    /// <summary>
    /// Wraps a native mini object that the caller keeps owning, for the length
    /// of one call.
    /// </summary>
    /// <param name="borrowed">The mini object that is lent to managed code.</param>
    /// <remarks>
    /// <para>
    /// See <see cref="Borrowed"/> for why the borrow is a real one rather than
    /// a reference of its own: an in-place transform receives a buffer that
    /// GStreamer has already made writable, and a second reference would take
    /// that away.
    /// </para>
    /// <para>
    /// Nothing is queued and nothing is drained here. This constructor runs per
    /// buffer on a streaming thread, and the releases of collected wrappers are
    /// drained by every other path that touches a wrapper; performing them from
    /// inside a vfunc would put an unbounded amount of unrelated work into the
    /// data path.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lent handle is <see cref="nint.Zero"/>.
    /// </exception>
    internal MiniObject(Borrowed borrowed)
    {
        if (borrowed.Handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(borrowed), "A mini object handle must not be null.");
        }

        _borrowed = true;
        _handle = borrowed.Handle;

        // There is no reference to release, so there is nothing for the
        // finalizer to do and no reason to put the wrapper on its queue.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the reference if the wrapper was not disposed.
    /// </summary>
    ~MiniObject() => Dispose(disposing: false);

    /// <summary>
    /// Gets the native mini object.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the wrapper has released its reference.
    /// </summary>
    public bool IsDisposed => _handle == nint.Zero;

    /// <summary>
    /// Gets a value indicating whether this is the only reference to the mini
    /// object, so that it may be modified in place.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public bool IsWritable
    {
        get
        {
            int writable = GstNative.MiniObjectIsWritable(Handle);

            // Reading Handle is the last use of this wrapper, so without this
            // the collector may finalize it while the call is still running,
            // which unrefs the object the call is reading.
            GC.KeepAlive(this);
            return writable != 0;
        }
    }

    /// <summary>
    /// Releases the reference of the wrapper.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Makes the mini object writable and returns the handle to use from then
    /// on.
    /// </summary>
    /// <returns>The writable mini object, which may be a copy.</returns>
    /// <remarks>
    /// <para>
    /// <c>gst_mini_object_make_writable</c> consumes the reference of the
    /// wrapper and returns one that is either the same object, when nobody else
    /// held it, or a fresh copy of it. The wrapper adopts whatever comes back,
    /// so <see cref="Handle"/> can change and every handle that was read before
    /// the call has to be considered stale.
    /// </para>
    /// <para>
    /// This is single owner surgery: it is only correct when no other wrapper
    /// and no other thread is using this wrapper at the same time, which is the
    /// same rule the C API imposes on <c>gst_buffer_make_writable</c> and its
    /// relatives.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected nint MakeWritableHandle()
    {
        nint current = Handle;

        if (_borrowed)
        {
            // gst_mini_object_make_writable consumes the reference it is given,
            // and a borrowed wrapper has none: on a shared object it would copy
            // and then release the reference of whoever lent the object. A
            // borrowed object is handed out writable when the vfunc it belongs
            // to promises that, and needs no call at all.
            throw new InvalidOperationException(
                "This wrapper borrows a mini object for the length of one call, so it cannot make it writable: " +
                "the call would release a reference the wrapper does not own. A buffer that an in place vfunc " +
                "receives is writable already; copy the object to keep one.");
        }

        nint writable = GstNative.MiniObjectMakeWritable(current);

        if (writable != current)
        {
            // The old reference is gone: it was consumed by the call, whether a
            // copy was made or not.
            Interlocked.Exchange(ref _handle, writable);
        }

        // On the path where the object was writable already, nothing touches
        // this wrapper after the handle is read, so the collector would be free
        // to finalize it while the native call runs.
        GC.KeepAlive(this);
        return writable;
    }

    /// <summary>
    /// Releases the reference of the wrapper.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <see cref="Dispose()"/>,
    /// <see langword="false"/> when it comes from the finalizer.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);

        // A borrowed wrapper owns nothing, so disposing it only detaches it.
        // That is what makes a stray using declaration around a vfunc argument
        // harmless, and what makes a wrapper that outlives the call throw
        // instead of touching an object somebody else owns.
        if (handle != nint.Zero && !_borrowed)
        {
            GstNative.MiniObjectUnref(handle);
        }
    }
}
