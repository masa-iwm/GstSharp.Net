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
/// A list the callee takes over is built here as well, by the very same spine
/// builder, and is not released at all: the head and one value minted per
/// element belong to the callee from the moment the call is made. The builder
/// takes no side in that, which is why it answers a bare head and leaves the
/// ownership to whichever factory of <see cref="GMarshal"/> asked for it — the
/// borrowed direction wraps the head in a <see cref="GListScope"/> that frees
/// it, the consumed direction hands it straight to the call.
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
/// <c>{ gpointer data; GList *next; GList *prev; }</c> (<c>glib/glist.h</c>),
/// so <c>data</c> sits at offset zero and <c>next</c> one pointer further in. A
/// <c>GSList</c> is <c>{ gpointer data; GSList *next; }</c>
/// (<c>glib/gslist.h</c>), which puts the same two fields at the same two
/// offsets and only leaves the backward link out, so one walk reads both and
/// the list type is told apart where the spine is released rather than where it
/// is read. Only the forward links are read, and the walk starts at the head
/// the call returned; a list that native code corrupted into a cycle would spin
/// here, which is a bug that belongs to whoever built the list.
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
    /// Copies the element pointers of a list whose spine the caller owns, and
    /// releases the spine with the function that belongs to its list type.
    /// </summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/> for an empty list.</param>
    /// <param name="singly">
    /// <see langword="true"/> for a <c>GSList</c>, <see langword="false"/> for a
    /// <c>GList</c>.
    /// </param>
    /// <returns>The <c>data</c> pointer of every node, in list order.</returns>
    /// <remarks>
    /// The walk is the one of <see cref="CollectAndFreeSpine(nint)"/>, because
    /// the two node layouts agree on everything it reads; only the release
    /// differs, and mixing the two would hand a <c>GSList</c> node to the slice
    /// allocator of the wrong size.
    /// </remarks>
    internal static nint[] CollectAndFreeSpine(nint head, bool singly)
    {
        if (head == nint.Zero)
        {
            return [];
        }

        nint[] items = Collect(head);
        FreeSpine(head, singly);
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
    /// <see cref="CollectAndFreeSpine(nint)"/> already establishes, so nothing walks
    /// memory that was released a line earlier.
    /// </remarks>
    internal static void FreeStringList(nint head)
    {
        foreach (nint item in CollectAndFreeSpine(head))
        {
            GMarshal.Free(item);
        }
    }

    /// <summary>
    /// Builds a spine over element pointers the caller already produced.
    /// </summary>
    /// <param name="items">The <c>data</c> of every node, in list order.</param>
    /// <param name="singly">
    /// <see langword="true"/> for a <c>GSList</c>, <see langword="false"/> for a
    /// <c>GList</c>.
    /// </param>
    /// <returns>
    /// The first node, or <see cref="nint.Zero"/> when <paramref name="items"/>
    /// is empty: <c>NULL</c> is how C spells the empty list.
    /// </returns>
    /// <remarks>
    /// The nodes are prepended from the back, so the list comes out in the
    /// order of <paramref name="items"/> without a second walk. Nothing here
    /// owns the elements — they were minted or read by the caller, which is
    /// also the only party that knows whether they have to be released — so a
    /// throw takes the half built spine down and leaves the elements to the
    /// caller's own <c>catch</c>.
    /// </remarks>
    internal static nint BuildSpine(ReadOnlySpan<nint> items, bool singly)
    {
        nint head = nint.Zero;

        try
        {
            for (int i = items.Length - 1; i >= 0; i--)
            {
                head = singly ? SListPrepend(head, items[i]) : ListPrepend(head, items[i]);
            }
        }
        catch
        {
            FreeSpine(head, singly);
            throw;
        }

        return head;
    }

    /// <summary>
    /// Builds the list a virtual method override hands to a C caller that takes
    /// over the list and one reference per element (<c>transfer full</c>).
    /// </summary>
    /// <param name="items">
    /// The objects to answer, in list order, or <see langword="null"/> for none.
    /// </param>
    /// <param name="member">
    /// The managed member that produced the list, for the message of a refusal.
    /// </param>
    /// <param name="slot">
    /// The C slot the list is answered to, for the message of a refusal.
    /// </param>
    /// <returns>
    /// The first node, or <see cref="nint.Zero"/> when there is nothing to
    /// answer: <c>NULL</c> is how C spells the empty list.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The list is walked twice on purpose. The first pass only reads the
    /// handles, so nothing is referenced until every entry has been read and a
    /// bad entry leaves no reference behind; the second pass mints the one
    /// reference per element the caller is about to take over. A failure while
    /// the spine is being built releases exactly the references the second pass
    /// took and rethrows, so the answer is either whole or nothing at all.
    /// </para>
    /// <para>
    /// The wrappers keep the reference they own. What the caller receives is an
    /// added one per element, which is what <c>transfer full</c> on the elements
    /// of the list means, so every object answered here stays usable afterwards
    /// and may be answered again. The same object may appear more than once:
    /// each occurrence mints a reference of its own.
    /// </para>
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">
    /// One of <paramref name="items"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.ObjectDisposedException">
    /// One of <paramref name="items"/> was disposed.
    /// </exception>
    internal static nint BuildOwnedObjectList(
        System.Collections.Generic.IReadOnlyList<Gst.GObject.Object>? items,
        string member,
        string slot)
    {
        if (items is not { Count: > 0 })
        {
            return nint.Zero;
        }

        nint[] handles = new nint[items.Count];
        for (int i = 0; i < handles.Length; i++)
        {
            Gst.GObject.Object? item = items[i];
            if (item is null)
            {
                throw new InvalidOperationException(
                    $"{member} answered a list with an empty entry, which {slot} does not allow.");
            }

            handles[i] = item.Handle;
        }

        int referenced = 0;
        try
        {
            for (; referenced < handles.Length; referenced++)
            {
                _ = GObjectNative.ObjectRef(handles[referenced]);
            }

            nint head = BuildSpine(handles, singly: false);
            GC.KeepAlive(items);
            return head;
        }
        catch
        {
            for (int i = 0; i < referenced; i++)
            {
                GObjectNative.ObjectUnref(handles[i]);
            }

            throw;
        }
    }

    /// <summary>
    /// Releases the nodes of a list, and nothing else.
    /// </summary>
    /// <param name="head">The first node, or <see cref="nint.Zero"/> for an empty list.</param>
    /// <param name="singly">
    /// <see langword="true"/> for a <c>GSList</c>, <see langword="false"/> for a
    /// <c>GList</c>.
    /// </param>
    internal static void FreeSpine(nint head, bool singly)
    {
        if (singly)
        {
            SListFree(head);
            return;
        }

        ListFree(head);
    }

    [LibraryImport("GLib", EntryPoint = "g_list_free")]
    private static partial void ListFree(nint list);

    [LibraryImport("GLib", EntryPoint = "g_list_prepend")]
    private static partial nint ListPrepend(nint list, nint data);

    [LibraryImport("GLib", EntryPoint = "g_slist_free")]
    private static partial void SListFree(nint list);

    [LibraryImport("GLib", EntryPoint = "g_slist_prepend")]
    private static partial nint SListPrepend(nint list, nint data);
}
