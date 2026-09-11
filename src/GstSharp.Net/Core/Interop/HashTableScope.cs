namespace Gst.Interop;

/// <summary>
/// A transient <c>GHashTable</c> that is valid until the scope is disposed.
/// </summary>
/// <remarks>
/// <para>
/// The scope is created by <see cref="HashTableMarshal.Alloc"/> and owns the
/// one reference it was built with; the table owns every key and every value in
/// it. It is therefore only suitable for a <c>transfer-ownership="none"</c>
/// <c>in</c> parameter, where a callee that keeps the table takes a reference of
/// its own before the call returns — which is what
/// <c>gst_uri_set_query_table</c> does — and a callee that only reads it leaves
/// nothing behind at all.
/// </para>
/// <para>
/// A <see langword="null"/> dictionary answers a scope whose
/// <see cref="Handle"/> is <see cref="nint.Zero"/>, which is how C spells the
/// absence of a table; an empty dictionary answers an empty table, which is a
/// different value.
/// </para>
/// </remarks>
internal ref struct HashTableScope
{
    private nint _handle;

    internal HashTableScope(nint handle) => _handle = handle;

    /// <summary>
    /// Gets the table, or <see cref="nint.Zero"/> when the dictionary it was
    /// built from was <see langword="null"/>.
    /// </summary>
    public readonly nint Handle => _handle;

    /// <summary>
    /// Releases the reference the scope holds. Calling it a second time does
    /// nothing.
    /// </summary>
    public void Dispose()
    {
        if (_handle != nint.Zero)
        {
            HashTableMarshal.Unref(_handle);
            _handle = nint.Zero;
        }
    }
}
