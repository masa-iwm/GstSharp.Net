using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Reads the <c>GList</c> that a native call returned and releases its spine,
/// and builds the temporary list that a native call is given.
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
/// The same holds in the other direction. A list that is passed to a native
/// call is built here, lives for the length of that one call, and is released
/// by the caller in a <c>finally</c>; nothing managed keeps the head, so there
/// is no list to free twice and none to forget.
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

    /// <summary>
    /// Builds a list of freshly allocated UTF-8 strings for the length of a
    /// single native call.
    /// </summary>
    /// <param name="values">The strings to copy into the list, in list order.</param>
    /// <returns>
    /// The first node, or <see cref="nint.Zero"/> when <paramref name="values"/>
    /// is empty. The caller owns the nodes and the strings and has to release
    /// both with <see cref="FreeStringList"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is the <c>transfer-ownership="none"</c> parameter case, the only
    /// direction that is bound at all: the callee reads the list while the call
    /// runs and copies whatever it keeps, so the whole allocation is the
    /// caller's from beginning to end. The call site is expected to free it in a
    /// <c>finally</c>, which is why nothing here hands the head to anyone else.
    /// </para>
    /// <para>
    /// The nodes are prepended from the back, so the list comes out in the order
    /// of <paramref name="values"/> without a second walk, and a string that
    /// cannot be encoded takes the half built list down with it rather than
    /// leaking it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// One of <paramref name="values"/> contains a null character.
    /// </exception>
    internal static nint BuildStringList(IReadOnlyList<string> values)
    {
        nint head = nint.Zero;

        try
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                head = ListPrepend(head, GMarshal.StringToUtf8Ptr(values[i]));
            }
        }
        catch
        {
            FreeStringList(head);
            throw;
        }

        return head;
    }

    /// <summary>
    /// Releases a list of strings that the caller owns, the strings as well as
    /// the spine.
    /// </summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/> for an empty list.</param>
    /// <remarks>
    /// This is <c>g_list_free_full</c> with <c>g_free</c>, spelled out rather
    /// than imported so that no function pointer to a native deallocator has to
    /// be handed back to GLib. The pointers are copied out and the spine is
    /// released before the first string is freed, in the order
    /// <see cref="CollectAndFreeSpine"/> already establishes, so nothing walks
    /// memory that was released a line earlier.
    /// </remarks>
    internal static void FreeStringList(nint head)
    {
        foreach (nint item in CollectAndFreeSpine(head))
        {
            GMarshal.Free(item);
        }
    }

    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);

    [LibraryImport("GLib", EntryPoint = "g_list_prepend")]
    private static partial nint ListPrepend(nint list, nint data);
}
