using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The managed wrapper of a boxed type, that is of a plain structure that
/// GObject copies and frees through its registered functions.
/// </summary>
public abstract class Boxed : IDisposable
{
    private readonly bool _borrowed;
    private nint _handle;

    /// <summary>
    /// Wraps a boxed value.
    /// </summary>
    /// <param name="handle">The value to wrap.</param>
    /// <param name="boxedType">The boxed type of the value.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands the value over,
    /// <see cref="Transfer.None"/> to adopt a copy of it.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is the constructor a wrapper class chains to, including one in a
    /// binding module outside this repository; see
    /// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>.
    /// Boxed wrappers are not interned either, and unlike a mini object a boxed
    /// value is not reference counted at all: <see cref="Transfer.None"/> makes
    /// this take a <c>g_boxed_copy</c>, so the wrapper always owns a value that
    /// is nobody else's, and disposing it frees that value.
    /// </para>
    /// <para>
    /// <b>The type has to be the boxed type of the value.</b> It is not read
    /// back from the value — a boxed value is a plain structure whose first word
    /// is a field rather than a pointer to a class — so it is what the copy and
    /// the free are dispatched through, and a wrong one corrupts memory rather
    /// than failing. It comes from the <c>get_type</c> function of the type the
    /// module binds.
    /// </para>
    /// <para>
    /// <b>Its owner has to dispose it</b>, exactly as for a mini object, and for
    /// the same reason a module must not expose one from a property.
    /// </para>
    /// </remarks>
    protected Boxed(nint handle, GType boxedType, Transfer transfer)
    {
        BoxedType = boxedType;

        _handle = handle == nint.Zero || transfer == Transfer.Full
            ? handle
            : GObjectNative.BoxedCopy(boxedType.Value, handle);
    }

    /// <summary>
    /// Wraps a boxed value that the caller keeps owning, for the length of one
    /// call.
    /// </summary>
    /// <param name="borrowed">The value that is lent to managed code.</param>
    /// <param name="boxedType">The boxed type of the value.</param>
    /// <remarks>
    /// <para>
    /// This is the reverse direction only: the override of a virtual method is
    /// handed the very instance the caller of the slot holds, writes to it, and
    /// never keeps it. A <c>g_boxed_copy</c> would hand the override a copy the
    /// caller never reads back, which is why a lent boxed value is wrapped
    /// without one. See <see cref="Gst.Interop.Borrowed"/>.
    /// </para>
    /// <para>
    /// The wrapper owns nothing, so disposing it only detaches it: a wrapper
    /// used after the call it was made for throws
    /// <see cref="ObjectDisposedException"/> instead of freeing a value it does
    /// not own.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lent handle is <see cref="nint.Zero"/>.
    /// </exception>
    internal Boxed(Borrowed borrowed, GType boxedType)
    {
        if (borrowed.Handle == nint.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(borrowed), "A boxed handle must not be null.");
        }

        BoxedType = boxedType;
        _borrowed = true;
        _handle = borrowed.Handle;

        // There is nothing to free, so there is nothing for the finalizer to do.
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the boxed value if the wrapper was not disposed.
    /// </summary>
    ~Boxed() => Dispose(disposing: false);

    /// <summary>
    /// Gets the boxed type of the value.
    /// </summary>
    public GType BoxedType { get; }

    /// <summary>
    /// Gets the native value.
    /// </summary>
    public nint Handle
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);
            return _handle;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the wrapper still holds a value.
    /// </summary>
    public bool IsDisposed => _handle == nint.Zero;

    /// <summary>
    /// Gives the value of the wrapper up to a call that takes it over, and
    /// returns the handle to hand that call.
    /// </summary>
    /// <returns>The value the call is given.</returns>
    /// <remarks>
    /// This is the boxed half of the hand over a mini object wrapper performs:
    /// the wrapper hands its own value over rather than copying it, and is left
    /// detached, so using it afterwards throws. A wrapper that only borrows its
    /// value owns nothing to hand over and gets a <c>g_boxed_copy</c> made for
    /// the call instead, which for a boxed type whose copy raises a reference
    /// count — the shape every codec frame and codec state has — is the very
    /// reference the call expects.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    internal nint HandOver()
    {
        nint handle = Handle;
        if (_borrowed)
        {
            return GObjectNative.BoxedCopy(BoxedType.Value, handle);
        }

        _ = Interlocked.Exchange(ref _handle, nint.Zero);
        GC.SuppressFinalize(this);
        return handle;
    }

    /// <summary>
    /// Releases the boxed value.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gives the value of the wrapper up to a call that takes it over, and
    /// returns the handle to hand that call.
    /// </summary>
    /// <returns>The value the call is given.</returns>
    /// <remarks>
    /// <para>
    /// This is the boxed half of the adopt in place shape that
    /// <c>gst_uri_make_writable</c> has: the call consumes what it is given and
    /// answers a value of the same type, which the wrapper adopts through
    /// <see cref="AdoptWritable(nint)"/>. The wrapper keeps holding the handle
    /// until then, which is what keeps the value alive across the call.
    /// </para>
    /// <para>
    /// It is only correct for a boxed type whose copy raises a reference count
    /// rather than duplicating the value, which is what
    /// <c>GST_DEFINE_MINI_OBJECT_TYPE</c> registers: the wrapper owns one
    /// reference and hands exactly that one over. The generator emits the
    /// members that use it for any boxed type whose <c>*_make_writable</c>
    /// consumes the instance it is called on; today the only such type is
    /// <c>Gst.Uri</c>, whose boxed copy is <c>gst_mini_object_ref</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    protected nint BeginMakeWritable() => Handle;

    /// <summary>
    /// Adopts the value a call that consumed the value of the wrapper answered.
    /// </summary>
    /// <param name="writable">The answer of the call.</param>
    /// <remarks>
    /// What the wrapper held is gone whichever value comes back: the call took
    /// it over, whether it copied or not. A zero is a copy the C function could
    /// not make, and it took the value over all the same, so the wrapper is
    /// left disposed rather than holding a handle that stands for nothing.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The writable copy could not be made.</exception>
    protected void AdoptWritable(nint writable)
    {
        Interlocked.Exchange(ref _handle, writable);

        if (writable == nint.Zero)
        {
            throw new InvalidOperationException(
                "The boxed value could not be made writable: it is shared and the copy failed. The call " +
                "released the value of this wrapper all the same, so the wrapper is now disposed.");
        }
    }

    /// <summary>
    /// Releases the boxed value.
    /// </summary>
    /// <param name="disposing">
    /// <see langword="true"/> when the call comes from <see cref="Dispose()"/>.
    /// </param>
    protected virtual void Dispose(bool disposing)
    {
        nint handle = Interlocked.Exchange(ref _handle, nint.Zero);

        // A borrowed wrapper owns no value: disposing it invalidates it and
        // leaves what it pointed at to the caller of the slot that lent it.
        if (handle != nint.Zero && !_borrowed)
        {
            GObjectNative.BoxedFree(BoxedType.Value, handle);
        }
    }
}
