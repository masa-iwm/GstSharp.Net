using System;
using System.Linq;
using Gst;
using Gst.Audio;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written half of the metadata surface: enumerating, casting,
/// removing and serialising the items a buffer carries, plus the two overlay
/// corrections that came with it.
/// </summary>
/// <remarks>
/// The convention these tests pin down is that a <see cref="Gst.Meta"/> wrapper
/// whose handle is zero is dead, and that the handle is zeroed exactly where the
/// library frees the item: <see cref="Gst.Buffer.RemoveMeta"/> on
/// <see langword="true"/>, and a <see cref="Gst.Buffer.ForeachMeta"/> removal
/// the walk honoured.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BufferMetaTests
{
    /// <summary>
    /// The whole round trip on a buffer nobody else holds: attach, find,
    /// reinterpret, remove, and read through the wrapper afterwards.
    /// </summary>
    [Fact]
    public void AVideoMetaIsEnumeratedCastRemovedAndInvalidated()
    {
        using Buffer buffer = Buffer.New();
        Gst.GObject.GType videoApi = VideoGlobal.VideoMetaApiGetType();

        VideoMeta? added = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            width: 320,
            height: 240);
        Assert.NotNull(added);

        Gst.Meta item = Assert.Single(buffer.IterateMeta());
        Assert.Equal(videoApi, item.ApiType);
        Assert.Equal(videoApi, item.Info.Api);

        // The cast is a reinterpretation of the same address, and the cast of
        // another API answers null rather than a wrapper over the wrong bytes.
        Assert.NotNull(VideoMeta.FromMeta(item));
        Assert.Null(AudioLevelMeta.FromMeta(item));

        Assert.Equal(1u, buffer.GetNMeta(videoApi));
        Assert.True(buffer.RemoveMeta(item));
        Assert.Equal(0u, buffer.GetNMeta(videoApi));

        // The item was freed before RemoveMeta returned, so the wrapper is dead
        // and every hand written member that reads it says so.
        Assert.Throws<ObjectDisposedException>(() => buffer.RemoveMeta(item));
        Assert.Throws<ObjectDisposedException>(() => item.ApiType);
        Assert.Throws<ObjectDisposedException>(() => VideoMeta.FromMeta(item));
    }

    /// <summary>
    /// The <c>GType</c> overload filters, and the unfiltered one does not.
    /// </summary>
    [Fact]
    public void IterateMetaOfOneApiSkipsTheOtherApis()
    {
        using Buffer buffer = Buffer.New();
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        Gst.GObject.GType videoApi = VideoGlobal.VideoMetaApiGetType();

        Assert.NotNull(VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            width: 32,
            height: 24));
        Assert.NotNull(buffer.AddReferenceTimestampMeta(reference, new ClockTime(11), new ClockTime(3)));

        Assert.Equal(2, buffer.IterateMeta().Count());

        Gst.Meta only = Assert.Single(buffer.IterateMeta(videoApi));
        Assert.Equal(videoApi, only.ApiType);

        Gst.Meta timestamp = Assert.Single(
            buffer.IterateMeta(Gst.Global.ReferenceTimestampMetaApiGetType()));
        Assert.NotNull(ReferenceTimestampMeta.FromMeta(timestamp));
    }

    /// <summary>
    /// A walk that keeps everything visits every item, answers true and changes
    /// nothing.
    /// </summary>
    [Fact]
    public void ForeachMetaKeepsEveryItemAndRunsToTheEnd()
    {
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        using Buffer buffer = NewBufferWithThreeTimestamps(reference);
        Gst.GObject.GType api = Gst.Global.ReferenceTimestampMetaApiGetType();

        int visits = 0;
        bool completed = buffer.ForeachMeta((walked, meta) =>
        {
            Assert.Same(buffer, walked);
            visits++;
            return MetaForeachAction.Keep;
        });

        Assert.True(completed);
        Assert.Equal(3, visits);
        Assert.Equal(3u, buffer.GetNMeta(api));
    }

    /// <summary>
    /// A walk that removes the item it is shown second drops that one, runs to
    /// the end and leaves the wrapper of the removed item dead.
    /// </summary>
    [Fact]
    public void ForeachMetaRemovesTheItemTheFunctionRejects()
    {
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        using Buffer buffer = NewBufferWithThreeTimestamps(reference);
        Gst.GObject.GType api = Gst.Global.ReferenceTimestampMetaApiGetType();

        int visits = 0;
        Gst.Meta? removed = null;

        bool completed = buffer.ForeachMeta((walked, meta) =>
        {
            if (visits++ != 1)
            {
                return MetaForeachAction.Keep;
            }

            removed = meta;
            return MetaForeachAction.Remove;
        });

        Assert.True(completed);
        Assert.Equal(3, visits);
        Assert.Equal(2u, buffer.GetNMeta(api));

        Assert.NotNull(removed);
        Assert.Throws<ObjectDisposedException>(() => removed.ApiType);
    }

    /// <summary>
    /// A removal the library refuses leaves the item attached and its wrapper
    /// alive.
    /// </summary>
    /// <remarks>
    /// <c>gst_buffer_foreach_meta</c> decides about a removal after the function
    /// has answered: on a buffer somebody else holds, and on an item flagged
    /// <c>GST_META_FLAG_LOCKED</c>, it answers <see langword="false"/> and aborts
    /// the walk before it frees anything. The wrapper of that item therefore has
    /// to survive, which is what the binding restores. Enumerating a buffer that
    /// is not writable is allowed, so the function is still shown the first item.
    /// </remarks>
    [Fact]
    public void ForeachMetaKeepsTheWrapperOfARemovalTheLibraryRefuses()
    {
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        using Buffer buffer = NewBufferWithThreeTimestamps(reference);
        Gst.GObject.GType api = Gst.Global.ReferenceTimestampMetaApiGetType();

        // Somebody else takes a reference, the way a queue or a pad probe does,
        // which is what makes the buffer refuse every modification.
        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        try
        {
            Assert.False(buffer.IsWritable);

            int visits = 0;
            Gst.Meta? refused = null;

            bool completed = buffer.ForeachMeta((walked, meta) =>
            {
                visits++;
                refused = meta;
                return MetaForeachAction.Remove;
            });

            Assert.False(completed);
            Assert.Equal(1, visits);
            Assert.Equal(3u, buffer.GetNMeta(api));

            // Nothing was freed, so the wrapper still addresses a live item.
            Assert.NotNull(refused);
            Assert.Equal(api, refused.Info.Api);
            Assert.Equal(api, refused.ApiType);
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }
    }

    /// <summary>
    /// A walk that stops at the first item ends there, answers false and removes
    /// nothing.
    /// </summary>
    [Fact]
    public void ForeachMetaStopsWhereTheFunctionAsksItTo()
    {
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        using Buffer buffer = NewBufferWithThreeTimestamps(reference);
        Gst.GObject.GType api = Gst.Global.ReferenceTimestampMetaApiGetType();

        int visits = 0;
        bool completed = buffer.ForeachMeta((walked, meta) =>
        {
            visits++;
            return MetaForeachAction.Stop;
        });

        Assert.False(completed);
        Assert.Equal(1, visits);
        Assert.Equal(3u, buffer.GetNMeta(api));
    }

    /// <summary>
    /// An exception thrown by the function ends the walk and reaches the caller
    /// of <see cref="Gst.Buffer.ForeachMeta"/> once the library has returned.
    /// </summary>
    [Fact]
    public void ForeachMetaRethrowsWhatTheFunctionThrew()
    {
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));
        using Buffer buffer = NewBufferWithThreeTimestamps(reference);
        Gst.GObject.GType api = Gst.Global.ReferenceTimestampMetaApiGetType();

        int visits = 0;

        InvalidTimeZoneException thrown = Assert.Throws<InvalidTimeZoneException>(() =>
            buffer.ForeachMeta((walked, meta) =>
            {
                visits++;
                throw new InvalidTimeZoneException("the walk is over");
            }));

        Assert.Equal("the walk is over", thrown.Message);
        Assert.Equal(1, visits);
        Assert.Equal(3u, buffer.GetNMeta(api));
    }

    /// <summary>
    /// The overlay regression test: a buffer somebody else holds refuses the
    /// metadata, and the adder answers null where it used to throw an
    /// <see cref="InvalidOperationException"/> that named the wrong cause.
    /// </summary>
    [Fact]
    public void AddVideoMetaAnswersNullOnANonWritableBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        // Somebody else takes a reference, the way a queue or a pad probe does.
        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        try
        {
            Assert.False(buffer.IsWritable);
            Assert.Null(VideoGlobal.BufferAddVideoMeta(
                buffer,
                VideoFrameFlags.None,
                VideoFormat.I420,
                width: 32,
                height: 24));
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }
    }

    /// <summary>
    /// The corruption regression test: the aggregated parameters come back
    /// through the out parameter and both inputs are untouched.
    /// </summary>
    /// <remarks>
    /// <c>gst_meta_api_type_aggregate_params</c> arrived in GStreamer 1.26, so
    /// the gate is the version rather than the 1.28 symbol probe.
    /// </remarks>
    [RequiresGStreamerFact(26)]
    public void AggregateParamsAnswersAFreshStructureAndLeavesItsInputsAlone()
    {
        Gst.GObject.GType api = VideoGlobal.VideoMetaApiGetType();

        using Structure params0 = Structure.NewEmpty("video-meta");
        VideoAlignment align0 = default;
        align0.PaddingTop = 2;
        VideoGlobal.BufferPoolConfigSetVideoAlignment(params0, align0);

        using Structure params1 = Structure.NewEmpty("video-meta");
        VideoAlignment align1 = default;
        align1.PaddingLeft = 4;
        VideoGlobal.BufferPoolConfigSetVideoAlignment(params1, align1);

        string before0 = params0.ToString();
        string before1 = params1.ToString();

        Assert.True(Gst.Meta.ApiTypeAggregateParams(api, out Structure? aggregated, params0, params1));
        Assert.NotNull(aggregated);

        using (aggregated)
        {
            Assert.True(VideoGlobal.BufferPoolConfigGetVideoAlignment(aggregated, out VideoAlignment merged));
            Assert.Equal(2u, merged.PaddingTop);
            Assert.Equal(4u, merged.PaddingLeft);
        }

        // The shipped overload wrote the pointer of the copy over the header of
        // this structure. Nothing of either input may have moved.
        Assert.Equal(before0, params0.ToString());
        Assert.Equal(before1, params1.ToString());
    }

    /// <summary>
    /// The overload that shipped in 1.28.2 is kept for source and binary
    /// compatibility and answers nothing at all.
    /// </summary>
    [Fact]
    public void TheShippedAggregateParamsOverloadThrows()
    {
        using Structure aggregated = Structure.NewEmpty("video-meta");
        using Structure params0 = Structure.NewEmpty("video-meta");
        using Structure params1 = Structure.NewEmpty("video-meta");

        // The bridge is deliberately obsolete: it is the shape that shipped in
        // 1.28.2, which corrupted the structure it was handed, and item E3-13
        // keeps it only so that code compiled against 1.28.2 still links.
#pragma warning disable CS0618 // Type or member is obsolete
        Assert.Throws<NotSupportedException>(() => Gst.Meta.ApiTypeAggregateParams(
            VideoGlobal.VideoMetaApiGetType(),
            aggregated,
            params0,
            params1));
#pragma warning restore CS0618
    }

    /// <summary>
    /// A metadata item goes out through <see cref="Gst.Meta.Serialize"/> and
    /// comes back on another buffer through <c>Meta.Deserialize</c>.
    /// </summary>
    [Fact]
    public void AReferenceTimestampMetaSurvivesASerializationRoundTrip()
    {
        using Buffer source = Buffer.New();
        using Caps reference = Assert.IsType<Caps>(Caps.FromString("timestamp/x-gstsharp-test"));

        Assert.NotNull(source.AddReferenceTimestampMeta(reference, new ClockTime(1234), new ClockTime(56)));

        Gst.Meta item = Assert.Single(source.IterateMeta());
        byte[]? bytes = item.Serialize();
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);

        using Buffer target = Buffer.New();
        Gst.Meta? restored = Gst.Meta.Deserialize(target, bytes, out uint consumed);

        Assert.NotNull(restored);
        Assert.Equal((uint)bytes.Length, consumed);
        Assert.Equal(Gst.Global.ReferenceTimestampMetaApiGetType(), restored.ApiType);

        ReferenceTimestampMeta? typed = ReferenceTimestampMeta.FromMeta(restored);
        Assert.NotNull(typed);
        Assert.Equal(new ClockTime(1234), typed.Timestamp);
        Assert.Equal(new ClockTime(56), typed.Duration);
    }

    /// <summary>
    /// The hand written audio adder, whose offsets array the gir gives no length
    /// because the length is a field of another argument.
    /// </summary>
    [Fact]
    public void AudioMetaTakesTheOffsetsOfANonInterleavedLayout()
    {
        using AudioInfo info = NewNonInterleavedInfo();
        Assert.Equal(AudioLayout.NonInterleaved, info.Layout);
        Assert.Equal(2, info.Channels);

        // Two planes of 100 S16 samples each: 200 bytes per plane.
        using Buffer explicitOffsets = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 400, null));
        AudioMeta? withOffsets = AudioGlobal.BufferAddAudioMeta(
            explicitOffsets,
            info,
            samples: 100,
            new nuint[] { 0, 200 });
        Assert.NotNull(withOffsets);
        Assert.Equal((nuint)100, withOffsets.Samples);

        using Buffer computedOffsets = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 400, null));
        AudioMeta? withoutOffsets = AudioGlobal.BufferAddAudioMeta(computedOffsets, info, samples: 100);
        Assert.NotNull(withoutOffsets);
        Assert.Equal((nuint)100, withoutOffsets.Samples);

        Gst.Meta item = Assert.Single(computedOffsets.IterateMeta());
        Assert.Equal(AudioGlobal.AudioMetaApiGetType(), item.ApiType);
    }

    /// <summary>
    /// A span that is neither empty nor one entry per channel is refused before
    /// anything is called.
    /// </summary>
    [Fact]
    public void AudioMetaRefusesAnOffsetsSpanOfTheWrongLength()
    {
        using AudioInfo info = NewNonInterleavedInfo();
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 400, null));

        ArgumentException thrown = Assert.Throws<ArgumentException>(
            () => AudioGlobal.BufferAddAudioMeta(buffer, info, samples: 100, new nuint[] { 0 }));

        Assert.Equal("offsets", thrown.ParamName);
    }

    /// <summary>
    /// A buffer somebody else holds is refused before the call, because the C
    /// function dereferences the NULL that <c>gst_buffer_add_meta</c> answers.
    /// </summary>
    [Fact]
    public void AudioMetaRefusesASharedBuffer()
    {
        using AudioInfo info = NewNonInterleavedInfo();
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 400, null));

        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        try
        {
            Assert.False(buffer.IsWritable);
            Assert.Throws<InvalidOperationException>(
                () => AudioGlobal.BufferAddAudioMeta(buffer, info, samples: 100));
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }
    }

    /// <summary>
    /// The embedded info of an audio meta, whose accessor is named after the
    /// type it hands out because <c>GetInfo</c> is the metadata registration.
    /// </summary>
    [Fact]
    public void AudioMetaCopiesOutTheInfoItWasAddedWith()
    {
        using AudioInfo info = NewNonInterleavedInfo();
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 400, null));

        AudioMeta? meta = AudioGlobal.BufferAddAudioMeta(buffer, info, samples: 100);
        Assert.NotNull(meta);

        using AudioInfo copied = meta.GetAudioInfo();

        Assert.Equal(info.Format, copied.Format);
        Assert.Equal(info.Rate, copied.Rate);
        Assert.Equal(info.Channels, copied.Channels);
        Assert.Equal(info.Layout, copied.Layout);

        // The copy is the caller's: disposing it leaves the meta reading the
        // same values, because what it holds is a structure of its own.
        copied.Dispose();

        using AudioInfo again = meta.GetAudioInfo();
        Assert.Equal(info.Rate, again.Rate);
    }

    /// <summary>
    /// The structure of a protection meta, under the name of its type for the
    /// same reason.
    /// </summary>
    [Fact]
    public void ProtectionMetaCopiesOutTheStructureItWasAddedWith()
    {
        using Buffer buffer = Buffer.New();
        Structure info = Structure.NewEmpty("application/x-cenc");

        using (Gst.GObject.Value value = Gst.GObject.Value.New(Gst.GObject.GType.UInt))
        {
            value.SetUInt(16u);
            info.SetValue("iv_size", value);
        }

        ProtectionMeta meta = buffer.AddProtectionMeta(info);

        using Structure? copied = meta.GetStructure();
        Assert.NotNull(copied);
        Assert.Equal("application/x-cenc", copied.GetName());
        Assert.True(copied.GetUint("iv_size", out uint size));
        Assert.Equal(16u, size);

        // Disposing the copy frees a structure of the caller's own, so the one
        // the meta owns is still there to be read again.
        copied.Dispose();

        using Structure? second = meta.GetStructure();
        Assert.NotNull(second);
        Assert.Equal("application/x-cenc", second.GetName());
    }

    private static Buffer NewBufferWithThreeTimestamps(Caps reference)
    {
        Buffer buffer = Buffer.New();

        for (ulong i = 1; i <= 3; i++)
        {
            Assert.NotNull(buffer.AddReferenceTimestampMeta(reference, new ClockTime(i), new ClockTime(i * 10)));
        }

        return buffer;
    }

    private static AudioInfo NewNonInterleavedInfo()
    {
        using Caps caps = Assert.IsType<Caps>(Caps.FromString(
            "audio/x-raw,format=S16LE,rate=44100,channels=2,layout=non-interleaved"));
        return Assert.IsType<AudioInfo>(AudioInfo.NewFromCaps(caps));
    }
}
