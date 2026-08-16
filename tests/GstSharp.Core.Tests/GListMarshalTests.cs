using System.Runtime.InteropServices;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The <c>GList</c> walk, checked against nodes this test builds itself.
/// </summary>
/// <remarks>
/// Only <see cref="GListMarshal.Collect"/> is exercised here, which is the half
/// of the helper that calls nothing native; the other half adds a single
/// <c>g_list_free</c> on top of it and needs a GLib to call. Splitting the two
/// is what lets the walk itself — the part that reads raw memory and that a bug
/// in would corrupt the heap — be tested without a GStreamer installation.
/// </remarks>
public sealed class GListMarshalTests
{
    /// <summary>
    /// <c>NULL</c> is how C spells the empty list, and every caller of this
    /// helper turns what it returns into a list, never into a null reference.
    /// </summary>
    [Fact]
    public void ANullHeadIsTheEmptyList()
    {
        Assert.Empty(GListMarshal.Collect(nint.Zero));
    }

    /// <summary>
    /// One node yields its <c>data</c> pointer, which sits at offset zero of
    /// the node.
    /// </summary>
    [Fact]
    public void ASingleNodeYieldsItsData()
    {
        nint head = Build(0x1234);

        try
        {
            Assert.Equal(new nint[] { 0x1234 }, GListMarshal.Collect(head));
        }
        finally
        {
            Release(head);
        }
    }

    /// <summary>
    /// Several nodes are walked through their <c>next</c> links, in list order.
    /// </summary>
    [Fact]
    public void EveryNodeIsWalkedInOrder()
    {
        nint head = Build(11, 22, 33, 44);

        try
        {
            Assert.Equal(new nint[] { 11, 22, 33, 44 }, GListMarshal.Collect(head));
        }
        finally
        {
            Release(head);
        }
    }

    /// <summary>
    /// A node whose <c>data</c> is <c>NULL</c> is reported as it is. Dropping it
    /// is the job of the generated member, which cannot wrap a null pointer, and
    /// keeping the two apart is what lets this test see the whole spine.
    /// </summary>
    [Fact]
    public void ANullElementIsReportedRatherThanSkipped()
    {
        nint head = Build(1, 0, 2);

        try
        {
            Assert.Equal(new nint[] { 1, 0, 2 }, GListMarshal.Collect(head));
        }
        finally
        {
            Release(head);
        }
    }

    /// <summary>
    /// The <c>prev</c> links are written the way GLib writes them, and the walk
    /// follows the forward ones only: starting halfway through a list yields its
    /// tail rather than the whole of it.
    /// </summary>
    [Fact]
    public void OnlyTheForwardLinksAreFollowed()
    {
        nint head = Build(7, 8);

        try
        {
            nint second = Marshal.ReadIntPtr(head, IntPtr.Size);

            Assert.Equal(head, Marshal.ReadIntPtr(second, 2 * IntPtr.Size));
            Assert.Equal(new nint[] { 7, 8 }, GListMarshal.Collect(head));
            Assert.Equal(new nint[] { 8 }, GListMarshal.Collect(second));
        }
        finally
        {
            Release(head);
        }
    }

    /// <summary>
    /// Builds a <c>GList</c> by hand: <c>{ gpointer data; GList *next; GList
    /// *prev; }</c>, three pointers per node.
    /// </summary>
    /// <param name="values">The <c>data</c> pointer of each node, in order.</param>
    /// <returns>The head of the list.</returns>
    private static nint Build(params nint[] values)
    {
        nint head = nint.Zero;
        nint previous = nint.Zero;

        foreach (nint value in values)
        {
            nint node = Marshal.AllocHGlobal(3 * IntPtr.Size);
            Marshal.WriteIntPtr(node, 0, value);
            Marshal.WriteIntPtr(node, IntPtr.Size, nint.Zero);
            Marshal.WriteIntPtr(node, 2 * IntPtr.Size, previous);

            if (previous == nint.Zero)
            {
                head = node;
            }
            else
            {
                Marshal.WriteIntPtr(previous, IntPtr.Size, node);
            }

            previous = node;
        }

        return head;
    }

    /// <summary>Releases the nodes that <see cref="Build"/> allocated.</summary>
    /// <param name="head">The head of the list.</param>
    private static void Release(nint head)
    {
        nint node = head;
        while (node != nint.Zero)
        {
            nint next = Marshal.ReadIntPtr(node, IntPtr.Size);
            Marshal.FreeHGlobal(node);
            node = next;
        }
    }
}
