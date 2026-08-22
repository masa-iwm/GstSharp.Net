namespace Gst.Interop;

/// <summary>
/// A transient, <c>NULL</c> terminated vector of UTF-8 strings that is valid
/// until the scope is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The scope is created by <see cref="GMarshal.AllocStrv"/> and owns both
/// allocations it holds: the vector itself and every string in it. It is
/// therefore only suitable for <c>transfer-ownership="none"</c> <c>in</c>
/// parameters, where the callee reads the vector while the call runs and copies
/// whatever it keeps.
/// </para>
/// <para>
/// The strings are released from the private list the scope built, never by
/// walking the vector: a callee is free to filter the vector in place - the
/// option parser behind <c>gst_init</c> does - and the entries it drops are
/// still the caller's to free.
/// </para>
/// </remarks>
public unsafe ref struct StrvScope
{
    private nint* _vector;
    private nint[]? _owned;

    internal StrvScope(nint* vector, nint[]? owned)
    {
        _vector = vector;
        _owned = owned;
    }

    /// <summary>
    /// Gets the pointer to the <c>NULL</c> terminated vector, or
    /// <see langword="null"/> when the encoded array was
    /// <see langword="null"/>.
    /// </summary>
    public readonly nint* Pointer => _vector;

    /// <summary>
    /// Gets <see cref="Pointer"/> as an integer, for entry points that are
    /// declared with <see cref="nint"/> parameters.
    /// </summary>
    public readonly nint Address => (nint)_vector;

    /// <summary>
    /// Releases the strings and the vector. Calling it a second time does
    /// nothing.
    /// </summary>
    public void Dispose()
    {
        if (_owned is { } owned)
        {
            foreach (nint item in owned)
            {
                GMarshal.Free(item);
            }
        }

        if (_vector is not null)
        {
            System.Runtime.InteropServices.NativeMemory.Free(_vector);
        }

        _owned = null;
        _vector = null;
    }
}
