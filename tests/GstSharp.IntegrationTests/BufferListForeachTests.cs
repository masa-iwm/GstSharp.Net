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
}
