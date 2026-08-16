using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Reads the <c>GList</c> that a native call returned, and releases its spine.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GList</c> never reaches managed code as a list. The generated members
/// that return one call into this class first, so that the singly used native
/// spine is turned into a plain array of element pointers and is gone before
/// anything else runs; the caller then materializes an
/// <see cref="System.Collections.Generic.IReadOnlyList{T}"/> out of it. No
/// managed type ever holds a <c>GList*</c>, so no wrapper can outlive the
/// memory it points at, and no second reader can free a spine that was freed
/// already.
/// </para>
/// <para>
/// The order matters and is the reason this is one call rather than an
/// enumerator: every element pointer is copied out first, the spine is released
/// next, and the elements are adopted last. Wrapping an element can throw — a
/// registry lookup, a boxed copy and an interned lookup all can — and by then
/// the spine is neither reachable nor half freed, so a failed adoption can leak
/// a reference but can never free the same node twice or walk freed memory.
/// </para>
/// <para>
/// The layout this walks is the one <c>GList</c> has had since GLib 1.2:
/// <c>{ gpointer data; GList *next; GList *prev; }</c>, so <c>data</c> sits at
/// offset zero and <c>next</c> one pointer further in. Only the forward links
/// are read, and the walk starts at the head the call returned; a list that
/// native code corrupted into a cycle would spin here, which is a bug that
/// belongs to whoever built the list.
/// </para>
/// </remarks>
internal static partial class GListMarshal
{
    /// <summary>The offset of the <c>data</c> field of a <c>GList</c> node.</summary>
    private const int DataOffset = 0;

    /// <summary>The offset of the <c>next</c> field of a <c>GList</c> node.</summary>
    private static readonly int NextOffset = IntPtr.Size;

    /// <summary>
    /// Copies the element pointers of a list that the caller does not own.
    /// </summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/> for an empty list.</param>
    /// <returns>
    /// The <c>data</c> pointer of every node, in list order. A null head yields
    /// an empty array: <c>NULL</c> is how C spells the empty list.
    /// </returns>
    /// <remarks>
    /// This is the <c>transfer-ownership="none"</c> case, where the list belongs
    /// to the library and only its contents are read. It is also the seam the
    /// unit tests walk hand built nodes through, because it calls nothing
    /// native.
    /// </remarks>
    internal static nint[] Collect(nint head)
    {
        if (head == nint.Zero)
        {
            return [];
        }

        List<nint> items = [];
        for (nint node = head; node != nint.Zero; node = Marshal.ReadIntPtr(node, NextOffset))
        {
            items.Add(Marshal.ReadIntPtr(node, DataOffset));
        }

        return [.. items];
    }

    /// <summary>
    /// Copies the element pointers of a list whose spine the caller owns, and
    /// releases the spine.
    /// </summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/> for an empty list.</param>
    /// <returns>The <c>data</c> pointer of every node, in list order.</returns>
    /// <remarks>
    /// This is the <c>transfer-ownership="full"</c> and
    /// <c>transfer-ownership="container"</c> case. Only the nodes are freed;
    /// what the elements need is decided by the caller, which owns them under
    /// <c>full</c> and borrows them under <c>container</c>.
    /// </remarks>
    internal static nint[] CollectAndFreeSpine(nint head)
    {
        if (head == nint.Zero)
        {
            return [];
        }

        nint[] items = Collect(head);
        ListFree(head);
        return items;
    }

    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);
}
