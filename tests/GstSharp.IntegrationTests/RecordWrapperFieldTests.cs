using Gst;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The generated accessors of the fields of a record that hold a handle,
/// measured against the library that is installed.
/// </summary>
/// <remarks>
/// <para>
/// A field that holds a handle is projected the way a <c>transfer none</c>
/// return of the same type is, so what comes back is what a call site would
/// get: a <c>GObject</c> is interned and reads as a property, while a mini
/// object and a boxed value come back owning a reference of their own and are
/// read through a <c>Get</c> method the caller disposes.
/// </para>
/// <para>
/// Every value read here was written by a native function on the other side of
/// the field, and every assertion compares the handle that comes back with the
/// one the test already holds, so a field read at the wrong offset cannot pass.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RecordWrapperFieldTests
{
    /// <summary>
    /// The allocator of a memory is set once, by <c>gst_memory_init</c>, and
    /// never replaced, which is why the accessor is not nullable. It is a
    /// <c>GObject</c>, so the property hands out the interned wrapper of the
    /// very allocator the allocation went through.
    /// </summary>
    [Fact]
    public void AMemoryNamesTheAllocatorItWasMadeBy()
    {
        using Allocator allocator = Allocator.Find(null)
            ?? throw new InvalidOperationException("The system allocator has to be registered.");

        using Memory memory = allocator.Alloc(64, null)
            ?? throw new InvalidOperationException("The system allocator has to answer 64 bytes.");

        Assert.Same(allocator, memory.Allocator);
    }

    /// <summary>
    /// The parent of a memory is the one it was shared out of, and the null
    /// pointer for one that was allocated. A mini object comes back owning a
    /// reference of its own, which is why this is a method.
    /// </summary>
    [Fact]
    public void ASharedMemoryPointsBackAtTheMemoryItWasSharedFrom()
    {
        using Allocator allocator = Allocator.Find(null)
            ?? throw new InvalidOperationException("The system allocator has to be registered.");

        using Memory memory = allocator.Alloc(64, null)
            ?? throw new InvalidOperationException("The system allocator has to answer 64 bytes.");

        Assert.Null(memory.GetParent());

        using Memory shared = memory.Share(0, 32);
        using Memory? parent = shared.GetParent();

        Assert.NotNull(parent);
        Assert.Equal(memory.Handle, parent.Handle);

        // Two reads are two wrappers over one native mini object, each owning a
        // reference of its own; that is the whole reason the member is a method.
        using Memory? again = shared.GetParent();
        Assert.NotNull(again);
        Assert.NotSame(parent, again);
        Assert.Equal(parent.Handle, again.Handle);
    }

    /// <summary>
    /// A mapping records the memory it was taken from in the map info the
    /// caller holds, which is a value projected structure: the accessor reads
    /// the storage of the caller rather than a handle.
    /// </summary>
    [Fact]
    public void AMapInfoNamesTheMemoryThatWasMapped()
    {
        using Allocator allocator = Allocator.Find(null)
            ?? throw new InvalidOperationException("The system allocator has to be registered.");

        using Memory memory = allocator.Alloc(64, null)
            ?? throw new InvalidOperationException("The system allocator has to answer 64 bytes.");

        Assert.True(memory.Map(out MapInfo info, MapFlags.Read));

        try
        {
            using Memory? mapped = info.GetMemory();

            Assert.NotNull(mapped);
            Assert.Equal(memory.Handle, mapped.Handle);

            // The address the map filled in is still there beside the accessor:
            // the raw field is public API that shipped and is left alone.
            Assert.Equal(memory.Handle, info.MemoryPtr);
        }
        finally
        {
            memory.Unmap(info);
        }
    }

    /// <summary>
    /// A video meta carries a back pointer to the buffer that owns it, and a
    /// parent buffer meta carries the buffer it references.
    /// </summary>
    [Fact]
    public void AMetaPointsBackAtTheBufferItBelongsTo()
    {
        using Buffer buffer = AbiProbeTests.NewBuffer();

        VideoMeta video = VideoGlobal.BufferAddVideoMeta(buffer, VideoFrameFlags.None, VideoFormat.I420, 320, 240)
            ?? throw new InvalidOperationException("gst_buffer_add_video_meta refused an exclusively held buffer.");

        using Buffer? owner = video.GetBuffer();

        Assert.NotNull(owner);
        Assert.Equal(buffer.Handle, owner.Handle);

        using Buffer other = AbiProbeTests.NewBuffer();
        ParentBufferMeta parent = other.AddParentBufferMeta(buffer)
            ?? throw new InvalidOperationException("gst_buffer_add_parent_buffer_meta refused the buffer.");

        using Buffer? referenced = parent.GetBuffer();

        Assert.NotNull(referenced);
        Assert.Equal(buffer.Handle, referenced.Handle);
    }

    /// <summary>
    /// A reference timestamp meta names the reference it timestamps against,
    /// which the helper takes a reference to and the free hook releases.
    /// </summary>
    [Fact]
    public void AReferenceTimestampMetaNamesTheCapsItTimestampsAgainst()
    {
        using Buffer buffer = AbiProbeTests.NewBuffer();
        using Caps reference = Caps.FromString("timestamp/x-ntp")
            ?? throw new InvalidOperationException("The reference caps have to parse.");

        ReferenceTimestampMeta meta = buffer.AddReferenceTimestampMeta(
            reference,
            new ClockTime(1_000_000),
            ClockTime.None)
            ?? throw new InvalidOperationException("gst_buffer_add_reference_timestamp_meta refused the buffer.");

        using Caps? named = meta.GetReference();

        Assert.NotNull(named);
        Assert.Equal(reference.Handle, named.Handle);

        // The structure of the meta is a field nothing answers: an accessor of
        // it would carry the name gst_reference_timestamp_meta_get_info has, so
        // the overlays hold it back and the shipped member keeps its meaning.
        Assert.IsType<Gst.MetaInfo>(ReferenceTimestampMeta.GetInfo());
    }

    /// <summary>
    /// The infos a metadata transform points at are boxed values, so the
    /// accessor hands out a copy the caller owns rather than a view of storage
    /// the library holds. Both are stated non nullable, so a zeroed structure
    /// reports itself rather than answering the null pointer.
    /// </summary>
    [Fact]
    public void AMetadataTransformCopiesTheVideoInfosItPointsAt()
    {
        using Caps caps = Caps.FromString("video/x-raw,format=I420,width=320,height=240,framerate=30/1")
            ?? throw new InvalidOperationException("The caps of an I420 frame have to parse.");

        using VideoInfo input = VideoInfo.NewFromCaps(caps)
            ?? throw new InvalidOperationException("gst_video_info_from_caps refused I420 caps.");

        VideoMetaTransform transform = default;
        transform.InInfoPtr = input.Handle;
        transform.OutInfoPtr = input.Handle;

        using VideoInfo copied = transform.GetInInfo();

        // A boxed value is copied on the way out, so this is a structure of the
        // caller with the same contents rather than the one the field
        // addresses.
        Assert.NotEqual(input.Handle, copied.Handle);
        Assert.Equal(input.Width, copied.Width);
        Assert.Equal(input.Height, copied.Height);
        Assert.Equal(VideoFormat.I420, copied.FormatInfo.Format);

        using VideoInfo output = transform.GetOutInfo();
        Assert.Equal(input.Width, output.Width);

        // A zeroed transform holds the null pointer, which the non nullable
        // accessor reports rather than hands out.
        VideoMetaTransform empty = default;
        Assert.Throws<InvalidOperationException>(() => empty.GetInInfo());
    }
}
