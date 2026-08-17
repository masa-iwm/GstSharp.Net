using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The managed wrapper of a boxed type, that is of a plain structure that
/// GObject copies and frees through its registered functions.
/// </summary>
public abstract class Boxed : IDisposable
{
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
    /// Releases the boxed value.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
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
        if (handle != nint.Zero)
        {
            GObjectNative.BoxedFree(BoxedType.Value, handle);
        }
    }
}
