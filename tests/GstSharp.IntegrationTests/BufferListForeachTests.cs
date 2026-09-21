using System.Runtime.InteropServices;
using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>Gst.BufferList.Foreach</c>: the walk whose function is lent one reference
/// through a slot it may keep, clear or replace.
/// </summary>
/// <remarks>
/// Every list here is built by the test and therefore writable, which is the
/// state the library requires before it honours a change of a slot.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class BufferListForeachTests
{
    /// <summary>Every buffer is shown once, with the index it has in the list.</summary>
    [Fact]
    public void EveryBufferIsShownWithItsIndex()
    {
        using BufferList list = NewList();
        List<(uint Index, ulong Pts)> seen = [];

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            seen.Add((idx, buffer!.Pts.Nanoseconds));
            return true;
        }));

        Assert.Equal([(0u, 10ul), (1u, 20ul), (2u, 30ul)], seen);
    }

    /// <summary>
    /// A walk that leaves every slot alone leaves every reference count where
    /// it was, and the wrapper it lent is dead once the invocation is over.
    /// </summary>
    [Fact]
    public void AnUntouchedWalkLeavesEveryReferenceCountWhereItWas()
    {
        using BufferList list = NewList();
        using Buffer held = list.Get(1);
        nint handle = held.Handle;
        uint before = RefCountOf(handle);
        Buffer? captured = null;

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (idx == 1)
            {
                captured = buffer;
            }

            return true;
        }));

        Assert.Equal(before, RefCountOf(handle));
        Assert.NotNull(captured);
        Assert.True(captured.IsDisposed);
    }

    /// <summary>
    /// Clearing the slot removes the buffer from the list and releases the
    /// reference the list held.
    /// </summary>
    [Fact]
    public void ClearingTheSlotRemovesTheBuffer()
    {
        using BufferList list = NewList();
        using Buffer held = list.Get(1);
        nint handle = held.Handle;

        Assert.Equal(2u, RefCountOf(handle));

        // The removal is keyed on the buffer rather than on the index, because
        // a removal moves the following buffers down and the next one is shown
        // under the index the removed one had.
        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (buffer!.Pts.Nanoseconds == 20ul)
            {
                buffer = null;
            }

            return true;
        }));

        Assert.Equal(2u, list.Length());
        Assert.Equal(1u, RefCountOf(handle));

        using Buffer first = list.Get(0);
        using Buffer second = list.Get(1);

        Assert.Equal(10ul, first.Pts.Nanoseconds);
        Assert.Equal(30ul, second.Pts.Nanoseconds);
    }

    /// <summary>
    /// Assigning another buffer replaces the entry: the new buffer is handed
    /// over to the library and the old one loses the reference the list held.
    /// </summary>
    [Fact]
    public void AssigningAnotherBufferReplacesIt()
    {
        using BufferList list = NewList();
        using Buffer held = list.Get(1);
        nint handle = held.Handle;
        Buffer? replacement = null;

        Assert.Equal(2u, RefCountOf(handle));

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (idx != 1)
            {
                return true;
            }

            replacement = Buffer.New();
            replacement.SetPts(ClockTime.FromNanoseconds(99));
            buffer = replacement;
            return true;
        }));

        Assert.NotNull(replacement);
        Assert.True(replacement.IsDisposed);
        Assert.Equal(1u, RefCountOf(handle));
        Assert.Equal(3u, list.Length());

        using Buffer stored = list.Get(1);

        Assert.Equal(99ul, stored.Pts.Nanoseconds);
    }

    /// <summary>
    /// Disposing the wrapper without clearing the slot removes the buffer as
    /// well, because the wrapper owns nothing any more.
    /// </summary>
    /// <remarks>
    /// Nothing else holds the buffer here, so the library lends the only
    /// reference the list has and removes the entry without releasing a
    /// reference of its own.
    /// </remarks>
    [Fact]
    public void DisposingTheWrapperRemovesTheEntry()
    {
        using BufferList list = NewList();

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (buffer!.Pts.Nanoseconds == 20ul)
            {
                buffer.Dispose();
            }

            return true;
        }));

        Assert.Equal(2u, list.Length());

        using Buffer first = list.Get(0);
        using Buffer second = list.Get(1);

        Assert.Equal(10ul, first.Pts.Nanoseconds);
        Assert.Equal(30ul, second.Pts.Nanoseconds);
    }

    /// <summary>
    /// Clearing the slot of a buffer nothing else holds removes it, on the path
    /// where the reference the slot lends is the only one the list has.
    /// </summary>
    [Fact]
    public void ClearingTheSlotOfAWritableBufferRemovesIt()
    {
        using BufferList list = NewList();

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (buffer!.Pts.Nanoseconds == 20ul)
            {
                buffer = null;
            }

            return true;
        }));

        Assert.Equal(2u, list.Length());

        using Buffer first = list.Get(0);
        using Buffer second = list.Get(1);

        Assert.Equal(10ul, first.Pts.Nanoseconds);
        Assert.Equal(30ul, second.Pts.Nanoseconds);
    }

    /// <summary>
    /// Assigning another buffer over a buffer nothing else holds replaces the
    /// entry, on the same path: the buffer that was there is released by the
    /// binding rather than by the library.
    /// </summary>
    [Fact]
    public void AssigningAnotherBufferOverAWritableBufferReplacesIt()
    {
        using BufferList list = NewList();
        Buffer? replacement = null;

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (idx != 1)
            {
                return true;
            }

            replacement = Buffer.New();
            replacement.SetPts(ClockTime.FromNanoseconds(99));
            buffer = replacement;
            return true;
        }));

        Assert.NotNull(replacement);
        Assert.True(replacement.IsDisposed);
        Assert.Equal(3u, list.Length());

        using Buffer first = list.Get(0);
        using Buffer second = list.Get(1);
        using Buffer third = list.Get(2);

        Assert.Equal(10ul, first.Pts.Nanoseconds);
        Assert.Equal(99ul, second.Pts.Nanoseconds);
        Assert.Equal(30ul, third.Pts.Nanoseconds);
    }

    /// <summary>
    /// The buffer of a writable list that nothing else holds is writable inside
    /// the function, so it can be edited in place without replacing the entry.
    /// </summary>
    /// <remarks>
    /// This is what the wrapper adopting the lent reference buys: a second
    /// reference of its own would leave every buffer of the walk unwritable.
    /// </remarks>
    [Fact]
    public void AWritableBufferCanBeEditedInPlace()
    {
        using BufferList list = NewList();

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            Assert.True(buffer!.IsWritable);
            buffer.SetPts(ClockTime.FromNanoseconds(idx + 1));
            return true;
        }));

        using Buffer first = list.Get(0);
        using Buffer second = list.Get(1);
        using Buffer third = list.Get(2);

        Assert.Equal(1ul, first.Pts.Nanoseconds);
        Assert.Equal(2ul, second.Pts.Nanoseconds);
        Assert.Equal(3ul, third.Pts.Nanoseconds);
    }

    /// <summary>
    /// A buffer somebody else holds is made writable by copying it, and the
    /// copy the wrapper adopted becomes the entry of the list.
    /// </summary>
    [Fact]
    public void MakeWritableInPlaceOnASharedBufferReplacesTheEntry()
    {
        using BufferList list = NewList();
        using Buffer held = list.Get(0);
        nint handle = held.Handle;

        Assert.Equal(2u, RefCountOf(handle));

        Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            if (idx != 0)
            {
                return true;
            }

            Assert.False(buffer!.IsWritable);
            buffer.MakeWritable();
            buffer.SetPts(ClockTime.FromNanoseconds(77));
            return true;
        }));

        using Buffer stored = list.Get(0);

        Assert.NotEqual(handle, stored.Handle);
        Assert.Equal(77ul, stored.Pts.Nanoseconds);
        Assert.Equal(10ul, held.Pts.Nanoseconds);
        Assert.Equal(1u, RefCountOf(handle));
    }

    /// <summary>
    /// A function that answers false is not called again, and the walk answers
    /// false as well.
    /// </summary>
    [Fact]
    public void ReturningFalseEndsTheWalkAndAnswersFalse()
    {
        using BufferList list = NewList();
        int calls = 0;

        Assert.False(list.Foreach((ref Buffer? buffer, uint idx) =>
        {
            calls++;
            return false;
        }));

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// A list that is not writable refuses every change a walk makes: the
    /// library keeps the entry it had, releases whatever the function left
    /// behind and says so once with a GLib critical
    /// (<c>gstbufferlist.c:283-290</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The binding has nothing to decide here — the slot is settled the same
    /// way for a writable list and for this one — but the walk of a shared list
    /// is a path a caller reaches by holding a second reference, and what it
    /// does is worth pinning: the list is unchanged, the walk still answers
    /// true, and the one outward sign is the critical, which the log probe is
    /// what makes visible.
    /// </para>
    /// <para>
    /// The clearing happens once, and the test would hang if it did not: the
    /// walk advances only when the function left a buffer behind
    /// (<c>gstbufferlist.c:318-319</c>), and on a list that refused the removal
    /// the same entry comes back under the same index for as long as the
    /// function keeps clearing it. The critical is written once as well
    /// (<c>first_warning</c>), so a spinning walk would be silent.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWalkThatChangesANonWritableListIsRefusedWithACritical()
    {
        using BufferList list = NewList();
        using Buffer held = list.Get(1);
        nint handle = held.Handle;

        // The second reference is what makes the list non-writable; it is the
        // state a list is in whenever something else still holds it.
        _ = MiniObjectRef(list.Handle);

        try
        {
            bool cleared = false;
            int calls = 0;

            IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(() =>
                Assert.True(list.Foreach((ref Buffer? buffer, uint idx) =>
                {
                    calls++;
                    if (!cleared && buffer!.Pts.Nanoseconds == 20ul)
                    {
                        cleared = true;
                        buffer = null;
                    }

                    return true;
                })));

            // Three entries and the one repetition of the entry the refused
            // removal left where it was.
            Assert.Equal(4, calls);

            // Nothing moved: the entry is still there with the reference the
            // list holds for it, beside the one the test holds.
            Assert.Equal(3u, list.Length());
            Assert.Equal(2u, RefCountOf(handle));

            using Buffer second = list.Get(1);
            Assert.Equal(20ul, second.Pts.Nanoseconds);

            if (InitializeLogProbe.IsInstalled)
            {
                Assert.Contains(
                    logged,
                    message => message.Contains("non-writable list", StringComparison.Ordinal));
            }
        }
        finally
        {
            MiniObjectUnref(list.Handle);
        }
    }

    /// <summary>Builds a list of three buffers with distinct timestamps.</summary>
    /// <returns>The list, which the caller disposes.</returns>
    private static BufferList NewList()
    {
        BufferList list = BufferList.New();
        try
        {
            for (int index = 0; index < 3; index++)
            {
                Buffer buffer = Buffer.New();
                buffer.SetPts(ClockTime.FromNanoseconds((ulong)((index + 1) * 10)));

                // The insert consumes the wrapper, so the list holds the only
                // reference of each buffer it carries.
                list.Insert(-1, buffer);
            }

            return list;
        }
        catch
        {
            list.Dispose();
            throw;
        }
    }

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));

    /// <summary>Takes a reference of a mini object, which the test gives back.</summary>
    /// <param name="miniObject">The instance.</param>
    /// <returns>The instance.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_ref")]
    private static partial nint MiniObjectRef(nint miniObject);

    /// <summary>Releases one reference of a mini object.</summary>
    /// <param name="miniObject">The instance.</param>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_unref")]
    private static partial void MiniObjectUnref(nint miniObject);
}
