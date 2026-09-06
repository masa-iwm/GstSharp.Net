using Gst;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The copy of the four mini objects that reach <c>gst_mini_object_copy</c>
/// through a static inline function of their own: an event, a sample, a buffer
/// list and a query.
/// </summary>
/// <remarks>
/// What every one of them promises is the same: an object of its own that
/// carries the content of the original and is writable, because it holds the
/// only reference to itself. What differs is how deep the copy goes, and the
/// assertions below say so per type: a sample shares its buffer with the
/// original, a buffer list shares its buffers, an event and a query carry a
/// structure of their own.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MiniObjectCopyTests
{
    /// <summary>
    /// An event is copied with its type and its payload, and the copy is one
    /// object of its own.
    /// </summary>
    [Fact]
    public void AnEventIsCopiedWithItsCaps()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,format=GRAY8"));
        using Event @event = Event.NewCaps(caps);

        using Event? copy = @event.Copy();

        Assert.NotNull(copy);
        Assert.NotEqual(@event.Handle, copy.Handle);
        Assert.Equal(EventType.Caps, copy.Type);
        Assert.True(copy.IsWritable);

        copy.ParseCaps(out Caps? copied);
        using (copied)
        {
            Assert.NotNull(copied);
            Assert.True(caps.IsEqual(copied));
        }
    }

    /// <summary>
    /// A sample is copied by referencing what it points at, so the copy is an
    /// object of its own that carries the very buffer of the original.
    /// </summary>
    [Fact]
    public void ASampleIsCopiedAroundTheBufferItShares()
    {
        using Buffer buffer = Buffer.New();
        using Caps caps = Assert.IsType<Caps>(Caps.FromString("video/x-raw,format=GRAY8"));
        using Sample sample = Sample.New(buffer, caps, null, null);

        using Sample? copy = sample.Copy();

        Assert.NotNull(copy);
        Assert.NotEqual(sample.Handle, copy.Handle);
        Assert.True(copy.IsWritable);

        using Buffer? original = sample.GetBuffer();
        using Buffer? copied = copy.GetBuffer();

        Assert.NotNull(original);
        Assert.NotNull(copied);
        Assert.Equal(original.Handle, copied.Handle);
    }

    /// <summary>
    /// A buffer list is copied by referencing every buffer in it, so the copy
    /// is a list of its own of the same length.
    /// </summary>
    [Fact]
    public void ABufferListIsCopiedAroundTheBuffersItShares()
    {
        using BufferList list = BufferList.New();
        list.Insert(0, Buffer.New());
        list.Insert(1, Buffer.New());

        using BufferList? copy = list.Copy();

        Assert.NotNull(copy);
        Assert.NotEqual(list.Handle, copy.Handle);
        Assert.Equal(2u, copy.Length());
        Assert.True(copy.IsWritable);
    }

    /// <summary>
    /// A query is copied with its type and a structure of its own, which is
    /// what lets it outlive the call it was handed to.
    /// </summary>
    [Fact]
    public void AQueryIsCopiedWithItsType()
    {
        using Query query = Query.NewPosition(Format.Time);

        using Query? copy = query.Copy();

        Assert.NotNull(copy);
        Assert.NotEqual(query.Handle, copy.Handle);
        Assert.Equal(QueryType.Position, copy.Type);
        Assert.True(copy.IsWritable);
    }
}
