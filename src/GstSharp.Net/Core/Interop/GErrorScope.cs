namespace Gst.Interop;

/// <summary>
/// A transient <c>GError</c> that is valid until the scope is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The scope is created by <see cref="GMarshal.AllocError"/> and owns the one
/// allocation it holds. It is therefore only suitable for
/// <c>transfer-ownership="none"</c> <c>in</c> parameters, where the callee
/// reads the error while the call runs and copies whatever it keeps -
/// <c>gst_message_new_error</c> copies it into the message with
/// <c>g_error_copy</c>, and <c>gst_object_default_error</c> only prints it.
/// </para>
/// <para>
/// A scope built from <see langword="null"/> holds no error at all, and
/// disposing it does nothing: <c>g_error_free</c> is not <c>NULL</c> tolerant,
/// unlike the free of a plain block, so the pointer is tested before it is
/// released.
/// </para>
/// </remarks>
public ref struct GErrorScope
{
    private nint _error;

    internal GErrorScope(nint error) => _error = error;

    /// <summary>
    /// Gets the pointer to the <c>GError</c>, or <see cref="nint.Zero"/> when
    /// the encoded value was <see langword="null"/>.
    /// </summary>
    public readonly nint Pointer => _error;

    /// <summary>
    /// Releases the error. Calling it a second time does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_error != nint.Zero)
        {
            GLibNative.ErrorFree(_error);
        }

        _error = nint.Zero;
    }
}
