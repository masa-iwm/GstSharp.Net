using System;
using Gst;
using Gst.Audio;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written attach half of <see cref="Gst.Audio.AudioDownmixMeta"/>:
/// <see cref="Gst.Audio.AudioGlobal.BufferAddAudioDownmixMeta"/> and what the
/// generated finder makes of what it attached.
/// </summary>
/// <remarks>
/// The record exposes only <c>FromChannels</c> and <c>ToChannels</c>, so the
/// coefficients and the two position blocks are read back through the internal
/// mirror of the C structure, which is what the deep copy the library makes has
/// to be measured against.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AudioDownmixMetaTests
{
    /// <summary>The stereo layout the tests downmix from.</summary>
    private static ReadOnlySpan<AudioChannelPosition> Stereo =>
        [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight];

    /// <summary>The one channel layout the tests downmix to.</summary>
    private static ReadOnlySpan<AudioChannelPosition> Mono =>
        [AudioChannelPosition.Mono];

    /// <summary>
    /// A two by one matrix is attached, deep copied and read back through the
    /// row table the library built, and the finder answers for its layout only.
    /// </summary>
    [Fact]
    public unsafe void ADownmixMatrixIsAttachedDeepCopiedAndFoundByItsDestinationLayout()
    {
        using Buffer buffer = Buffer.New();

        float[] matrix = [0.25f, 0.75f];
        AudioDownmixMeta added = AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, Mono, matrix);

        Assert.Equal(2, added.FromChannels);
        Assert.Equal(1, added.ToChannels);

        // The library owns one row table of to_channels pointers into one flat
        // block of to_channels * from_channels floats, and both position blocks
        // are copies of their own; the arrays above may be forgotten.
        AudioDownmixMetaRaw* raw = (AudioDownmixMetaRaw*)added.Handle;
        float** rows = (float**)raw->Matrix;
        Assert.Equal(0.25f, rows[0][0]);
        Assert.Equal(0.75f, rows[0][1]);
        Assert.NotEqual((nint)0, raw->Matrix);

        AudioChannelPosition* from = (AudioChannelPosition*)raw->FromPosition;
        Assert.Equal(AudioChannelPosition.FrontLeft, from[0]);
        Assert.Equal(AudioChannelPosition.FrontRight, from[1]);
        Assert.Equal(AudioChannelPosition.Mono, ((AudioChannelPosition*)raw->ToPosition)[0]);

        matrix[0] = 0f;
        Assert.Equal(0.25f, rows[0][0]);

        // The finder matches on the destination positions, so the layout it was
        // attached for is found and another one is not.
        AudioDownmixMeta? found = AudioGlobal.BufferGetAudioDownmixMetaForChannels(buffer, Mono);
        Assert.NotNull(found);
        Assert.Equal(added.Handle, found.Handle);
        Assert.Null(AudioGlobal.BufferGetAudioDownmixMetaForChannels(buffer, Stereo));
    }

    /// <summary>
    /// A copy of the buffer carries a downmix matrix of its own.
    /// </summary>
    /// <remarks>
    /// The transform of the item re-attaches it through the same call, so what
    /// the copy carries is a second deep copy rather than a shared one.
    /// </remarks>
    [Fact]
    public unsafe void ACopyOfTheBufferCarriesTheDownmixMatrix()
    {
        using Buffer buffer = Buffer.New();
        AudioDownmixMeta added = AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, Mono, [0.25f, 0.75f]);

        Buffer? copied = buffer.Copy();
        Assert.NotNull(copied);
        using Buffer copy = copied;
        AudioDownmixMeta? carried = AudioGlobal.BufferGetAudioDownmixMetaForChannels(copy, Mono);

        Assert.NotNull(carried);
        Assert.NotEqual(added.Handle, carried.Handle);
        Assert.Equal(2, carried.FromChannels);
        Assert.Equal(1, carried.ToChannels);

        float** rows = (float**)((AudioDownmixMetaRaw*)carried.Handle)->Matrix;
        Assert.Equal(0.25f, rows[0][0]);
        Assert.Equal(0.75f, rows[0][1]);
    }

    /// <summary>
    /// A buffer somebody else holds is refused before the library is called.
    /// </summary>
    /// <remarks>
    /// <c>gst_buffer_add_audio_downmix_meta</c> does not check what
    /// <c>gst_buffer_add_meta</c> answered (<c>gstaudiometa.c:151-156</c>), so
    /// the call on a shared buffer is a NULL dereference inside the library;
    /// the pre-check is what turns it into an exception.
    /// </remarks>
    [Fact]
    public void ASharedBufferIsRefusedBeforeTheLibraryIsCalled()
    {
        using Buffer buffer = Buffer.New();

        nint shared = buffer.Handle;
        TestNatives.MiniObjectRef(shared);

        try
        {
            Assert.False(buffer.IsWritable);
            Assert.Throws<InvalidOperationException>(
                () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, Mono, [0.25f, 0.75f]));
        }
        finally
        {
            TestNatives.MiniObjectUnref(shared);
        }

        Assert.Empty(buffer.IterateMeta());
    }

    /// <summary>
    /// The shape of the matrix and of the two layouts is checked by the caller.
    /// </summary>
    [Fact]
    public void TheShapeOfTheMatrixAndOfTheLayoutsIsChecked()
    {
        using Buffer buffer = Buffer.New();

        // One coefficient per source channel per destination channel, and the
        // rows are the destination.
        Assert.Throws<ArgumentException>(
            "matrix",
            () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, Mono, [0.25f]));
        Assert.Throws<ArgumentException>(
            "matrix",
            () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, Mono, [0.25f, 0.5f, 0.75f]));

        Assert.Throws<ArgumentException>(
            "fromPosition",
            () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, [], Mono, []));
        Assert.Throws<ArgumentException>(
            "toPosition",
            () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, [], []));

        Assert.Throws<ArgumentNullException>(
            "buffer",
            () => AudioGlobal.BufferAddAudioDownmixMeta(null!, Stereo, Mono, [0.25f, 0.75f]));

        Assert.Empty(buffer.IterateMeta());
    }

    /// <summary>
    /// More destination channels than the row table is built for are refused.
    /// </summary>
    [Fact]
    public void MoreThanSixtyFourDestinationChannelsAreRefused()
    {
        using Buffer buffer = Buffer.New();

        AudioChannelPosition[] wide = new AudioChannelPosition[65];
        Array.Fill(wide, AudioChannelPosition.None);
        float[] matrix = new float[wide.Length * 2];

        Assert.Throws<ArgumentOutOfRangeException>(
            "toPosition",
            () => AudioGlobal.BufferAddAudioDownmixMeta(buffer, Stereo, wide, matrix));
    }
}
