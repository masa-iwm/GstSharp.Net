namespace Gst.Interop;

/// <summary>
/// A transient, null terminated UTF-8 encoding of a managed string that is
/// valid until the scope is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The scope is created by <see cref="GMarshal.StackUtf8"/>. Short strings are
/// encoded into the caller supplied buffer, longer ones into a native
/// allocation that <see cref="Dispose"/> releases again. It is therefore only
/// suitable for <c>in</c> parameters that the callee does not keep; use
/// <see cref="GMarshal.StringToUtf8Ptr"/> when native code takes ownership.
/// </para>
/// <para>
/// The buffer handed to <see cref="GMarshal.StackUtf8"/> must be stack
/// allocated (or otherwise pinned), because the scope keeps a raw pointer into
/// it.
/// </para>
/// </remarks>
public unsafe ref struct Utf8Scope
{
    private byte* _pointer;
    private readonly bool _owned;

    internal Utf8Scope(byte* pointer, bool owned)
    {
        _pointer = pointer;
        _owned = owned;
    }

    /// <summary>
    /// Gets the pointer to the null terminated UTF-8 bytes, or
    /// <see langword="null"/> when the encoded string was
    /// <see langword="null"/>.
    /// </summary>
    public readonly byte* Pointer => _pointer;

    /// <summary>
    /// Gets <see cref="Pointer"/> as an integer, for entry points that are
    /// declared with <see cref="nint"/> parameters.
    /// </summary>
    public readonly nint Address => (nint)_pointer;

    /// <summary>
    /// Releases the native allocation, if the string did not fit into the
    /// caller supplied buffer.
    /// </summary>
    public void Dispose()
    {
        if (_owned && _pointer is not null)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_pointer);
        }

        _pointer = null;
    }
}
