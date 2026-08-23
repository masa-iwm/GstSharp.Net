namespace Gst.Interop;

/// <summary>
/// A transient <c>GList</c> or <c>GSList</c> that is valid until the scope is
/// disposed.
/// </summary>
/// <remarks>
/// <para>
/// The scope is created by <see cref="GMarshal.AllocList(System.Collections.Generic.IEnumerable{Gst.GObject.Object}, bool)"/>
/// and its sibling for strings, and it owns everything it holds: the spine and,
/// for a list of strings, every UTF-8 copy in it. It is therefore only suitable
/// for <c>transfer-ownership="none"</c> <c>in</c> parameters, where the callee
/// reads the list while the call runs and copies whatever it keeps. A list the
/// callee takes over is built by <see cref="GMarshal.ConsumeList(System.Collections.Generic.IEnumerable{string}, bool)"/>
/// instead, which hands out a bare head and no scope at all.
/// </para>
/// <para>
/// The element wrappers are held in a field on purpose. The spine carries bare
/// handles, and nothing else in the generated body mentions the sequence the
/// caller passed once the handles have been read, so the wrappers would be
/// collectable — and a GObject wrapper releases its toggle reference from its
/// finalizer — while the call is still walking the list. The scope is a
/// <c>using</c> local, so it is live across the call and its array roots every
/// element until <see cref="Dispose"/> runs. That is why no
/// <c>GC.KeepAlive</c> is emitted for a list argument: a barrier on the
/// <c>IEnumerable</c> would not root a wrapper a lazy sequence produced anyway.
/// </para>
/// <para>
/// An empty sequence and <see langword="null"/> both answer a scope whose
/// <see cref="Head"/> is <see cref="nint.Zero"/>, because <c>NULL</c> is how C
/// spells the empty list.
/// </para>
/// </remarks>
public unsafe ref struct GListScope
{
    private nint _head;
    private Gst.GObject.Object[]? _elements;
    private nint[]? _strings;
    private readonly bool _singly;

    internal GListScope(Gst.GObject.Object[]? elements, nint[]? strings, bool singly)
    {
        _head = nint.Zero;
        _elements = elements;
        _strings = strings;
        _singly = singly;
    }

    /// <summary>
    /// Gets the first node of the list, or <see cref="nint.Zero"/> when the
    /// encoded sequence was <see langword="null"/> or empty.
    /// </summary>
    public readonly nint Head => _head;

    /// <summary>
    /// Releases the spine and every string that was allocated for it. Calling
    /// it a second time does nothing.
    /// </summary>
    public void Dispose()
    {
        // The spine goes first and the strings after it, the order
        // GListMarshal.FreeStringList already establishes: nothing walks
        // memory a line above released.
        if (_head != nint.Zero)
        {
            GListMarshal.FreeSpine(_head, _singly);
            _head = nint.Zero;
        }

        if (_strings is { } strings)
        {
            foreach (nint item in strings)
            {
                GMarshal.Free(item);
            }
        }

        _strings = null;

        // The last use of the element array, and the reason it is a field: the
        // wrappers stay reachable until the scope is disposed, which is after
        // the call the list was built for.
        System.GC.KeepAlive(_elements);
        _elements = null;
    }

    /// <summary>Takes over the spine the factory built.</summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/>.</param>
    internal void Adopt(nint head) => _head = head;
}
