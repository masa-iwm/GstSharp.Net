using Gst;
using Gst.Audio;
using Gst.Pbutils;
using Gst.Video;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The members the array annotation corrections unlocked, against the library
/// that is installed.
/// </summary>
/// <remarks>
/// The two Opus builders are what the <c>arrayOverrides</c> key exists for.
/// Their gir spells <c>channel_mapping</c> as a bare <c>(array)</c> with no
/// length on it, while the C function reads <c>channel_mapping[i]</c> for
/// <c>i &lt; channels</c>; the correction names <c>channels</c> as the length,
/// which both hides that parameter and makes the span the only thing that
/// decides how many bytes the call reads.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ArrayAnnotationTests
{
    /// <summary>
    /// A mapping family above zero is the shape the C function reads the span
    /// for: the caps carry the mapping back, and the channel count is the
    /// length of the span that was passed.
    /// </summary>
    [Fact]
    public void TheOpusCapsCarryTheChannelMappingOfTheSpan()
    {
        ReadOnlySpan<byte> mapping = [0, 4, 1, 2, 3, 5];

        using Caps? caps = PbutilsGlobal.CodecUtilsOpusCreateCaps(
            48000,
            channelMappingFamily: 1,
            streamCount: 4,
            coupledCount: 2,
            mapping);

        Assert.NotNull(caps);

        using Structure structure = caps.GetStructure(0);

        Assert.True(structure.GetInt("channels", out int channels));
        Assert.Equal(mapping.Length, channels);
        Assert.True(structure.GetInt("channel-mapping-family", out int family));
        Assert.Equal(1, family);
        Assert.True(structure.HasField("channel-mapping"));
    }

    /// <summary>
    /// Family zero returns before it reads the mapping at all, so the span is
    /// only there to state the channel count. That is the case the correction
    /// had to keep expressible: a two byte span is stereo.
    /// </summary>
    [Fact]
    public void TheOpusCapsOfFamilyZeroCountTheSpanWithoutReadingIt()
    {
        ReadOnlySpan<byte> stereo = [0, 0];

        using Caps? caps = PbutilsGlobal.CodecUtilsOpusCreateCaps(
            48000,
            channelMappingFamily: 0,
            streamCount: 1,
            coupledCount: 1,
            stereo);

        Assert.NotNull(caps);

        using Structure structure = caps.GetStructure(0);

        Assert.True(structure.GetInt("channels", out int channels));
        Assert.Equal(2, channels);
    }

    /// <summary>
    /// The header builder reads the same span and writes it into the
    /// <c>OpusHead</c> it hands back, so the buffer is the fixed 19 byte
    /// header plus one byte per channel.
    /// </summary>
    [Fact]
    public void TheOpusHeaderCarriesOneMappingBytePerChannel()
    {
        ReadOnlySpan<byte> mapping = [0, 4, 1, 2, 3, 5];

        using Gst.Buffer? header = PbutilsGlobal.CodecUtilsOpusCreateHeader(
            48000,
            channelMappingFamily: 1,
            streamCount: 4,
            coupledCount: 2,
            mapping,
            preSkip: 0,
            outputGain: 0);

        Assert.NotNull(header);
        Assert.Equal((nuint)(21 + mapping.Length), header.GetSize());
    }

    /// <summary>
    /// The parse pair reads the header back out of the caps into storage the
    /// caller declares. Its gir calls the 256 byte buffer
    /// <c>transfer=full caller-allocates=0</c>, which the overlays correct, so
    /// what comes back is the buffer that was passed rather than a pointer
    /// read out of it.
    /// </summary>
    [Fact]
    public void TheOpusCapsRoundTripThroughTheParser()
    {
        ReadOnlySpan<byte> mapping = [0, 4, 1, 2, 3, 5];

        using Caps? caps = PbutilsGlobal.CodecUtilsOpusCreateCaps(
            48000,
            channelMappingFamily: 1,
            streamCount: 4,
            coupledCount: 2,
            mapping);

        Assert.NotNull(caps);
        Assert.True(PbutilsGlobal.CodecUtilsOpusParseCaps(
            caps,
            out uint rate,
            out byte channels,
            out byte family,
            out byte streamCount,
            out byte coupledCount,
            out PbutilsGlobal.ChannelMappingArray parsed));

        Assert.Equal(48000u, rate);
        Assert.Equal(mapping.Length, channels);
        Assert.Equal(1, family);
        Assert.Equal(4, streamCount);
        Assert.Equal(2, coupledCount);

        for (int i = 0; i < mapping.Length; i++)
        {
            Assert.Equal(mapping[i], parsed[i]);
        }
    }

    /// <summary>
    /// <c>from</c> and <c>to</c> are counted by one C argument, and the call
    /// site reads it off <c>to</c>. A shorter <c>from</c> would have the C
    /// function read past its end, which is what the guard answers.
    /// </summary>
    [Fact]
    public void ASharedLengthRefusesSpansOfDifferentLengths()
    {
        byte[] data = new byte[8];

        ArgumentException error = Assert.Throws<ArgumentException>(() => AudioGlobal.AudioReorderChannels(
            data,
            AudioFormat.S16le,
            [AudioChannelPosition.FrontLeft],
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight]));

        Assert.Equal("from", error.ParamName);
    }

    /// <summary>
    /// The positions are sized at 64 by the C declaration, and NULL is how the
    /// mono and stereo defaults are asked for. An empty span pins to that
    /// NULL, which is why the count stays a visible argument.
    /// </summary>
    [Fact]
    public void AnEmptyPositionSpanAsksForTheDefaultLayout()
    {
        using AudioInfo info = AudioInfo.New();

        info.SetFormat(AudioFormat.S16le, 44100, 1, []);

        Assert.Equal(1, info.Channels);
        Assert.Equal(44100, info.Rate);

        // The positions are not read back through a member of their own, and
        // the caps are where the default shows: audio-info.c:396-411 leaves the
        // channel-mask off exactly one layout, a single channel whose position
        // is GST_AUDIO_CHANNEL_POSITION_MONO. Had the empty span reached the C
        // function as anything but NULL, the position would be the 0xff fill of
        // audio-info.c:152 and gst_audio_info_to_caps would fail on it instead.
        using Caps caps = info.ToCaps();
        using Structure structure = caps.GetStructure(0);

        Assert.Equal("audio/x-raw", structure.GetName());
        Assert.True(structure.GetInt("channels", out int channels));
        Assert.Equal(1, channels);
        Assert.False(structure.HasField("channel-mask"));
    }

    /// <summary>
    /// The stereo half of the same default: two channels the caller states
    /// nothing about are front left and front right, which the caps carry as
    /// the mask of those two positions.
    /// </summary>
    [Fact]
    public void AnEmptyPositionSpanAsksForTheStereoDefaultAsWell()
    {
        using AudioInfo info = AudioInfo.New();

        info.SetFormat(AudioFormat.S16le, 48000, 2, []);

        Assert.Equal(2, info.Channels);

        using Caps caps = info.ToCaps();
        using Structure structure = caps.GetStructure(0);

        Assert.True(structure.HasField("channel-mask"));
        Assert.True(AudioGlobal.AudioChannelPositionsToMask(
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight],
            forceOrder: true,
            out ulong expected));

        using Gst.GObject.Value mask = structure.GetValue("channel-mask");

        Assert.Equal(expected, Gst.Global.ValueGetBitmask(mask));
    }

    /// <summary>
    /// The downmix meta getter answers <c>NULL</c> for a buffer that carries no
    /// matching meta - gstaudiometa.c:114 - which its gir does not say and
    /// <c>fixups.json</c> corrects. Without the correction the member would be
    /// non-null and a plain miss would throw.
    /// </summary>
    [Fact]
    public void TheDownmixMetaGetterAnswersNullWhenThereIsNone()
    {
        using Gst.Buffer buffer = Gst.Buffer.New();

        Assert.Null(AudioGlobal.BufferGetAudioDownmixMetaForChannels(
            buffer,
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight]));
    }

    /// <summary>
    /// A six channel layout is stated position by position; the span is padded
    /// to the 64 the declaration sizes it at.
    /// </summary>
    [Fact]
    public void AnExplicitPositionSpanIsAcceptedAtTheDeclaredSize()
    {
        AudioChannelPosition[] positions = new AudioChannelPosition[64];
        AudioChannelPosition[] layout =
        [
            AudioChannelPosition.FrontLeft,
            AudioChannelPosition.FrontRight,
            AudioChannelPosition.FrontCenter,
            AudioChannelPosition.Lfe1,
            AudioChannelPosition.RearLeft,
            AudioChannelPosition.RearRight,
        ];

        layout.CopyTo(positions, 0);

        using AudioInfo info = AudioInfo.New();

        info.SetFormat(AudioFormat.S16le, 48000, layout.Length, positions);

        Assert.Equal(6, info.Channels);
    }

    /// <summary>A span of a length the declaration does not allow is refused.</summary>
    [Fact]
    public void APositionSpanOfTheWrongLengthIsRefused()
    {
        using AudioInfo info = AudioInfo.New();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => info.SetFormat(AudioFormat.S16le, 48000, 2, new AudioChannelPosition[2]));

        Assert.Equal("position", error.ParamName);
    }

    /// <summary>
    /// The mask is computed off the span, and the valid order call reorders
    /// the caller's own storage in place: the gir spells the parameter as a
    /// plain in array, and only a const C type would have made it read only.
    /// </summary>
    [Fact]
    public void AWritablePositionSpanIsReorderedInPlace()
    {
        AudioChannelPosition[] positions =
        [
            AudioChannelPosition.FrontRight,
            AudioChannelPosition.FrontLeft,
        ];

        Assert.True(AudioGlobal.AudioChannelPositionsToValidOrder(positions));
        Assert.Equal(AudioChannelPosition.FrontLeft, positions[0]);
        Assert.Equal(AudioChannelPosition.FrontRight, positions[1]);

        Assert.True(AudioGlobal.AudioChannelPositionsToMask(positions, forceOrder: false, out ulong mask));
        Assert.NotEqual(0ul, mask);
    }

    /// <summary>
    /// A returned block of enumerations is copied out under the enumeration
    /// rather than as the ints it is stored as.
    /// </summary>
    [Fact]
    public void TheRawVideoFormatsComeBackAsTheEnumeration()
    {
        VideoFormat[]? formats = VideoGlobal.VideoFormatsRaw();

        Assert.NotNull(formats);
        Assert.NotEmpty(formats);
        Assert.Contains(VideoFormat.I420, formats);
    }

    /// <summary>
    /// The plane sizes come back through storage the caller declares, whose
    /// length is part of its type: the C function writes four values through a
    /// parameter the gir sizes at four.
    /// </summary>
    [Fact]
    public void ThePlaneSizesComeBackThroughInlineStorage()
    {
        using Gst.Buffer buffer = Gst.Buffer.New();

        VideoMeta? meta = VideoGlobal.BufferAddVideoMeta(
            buffer,
            VideoFrameFlags.None,
            VideoFormat.I420,
            width: 320,
            height: 240);

        // The return is nullable since the overlay of item E3-13: the C
        // function answers NULL on an invalid format and on a buffer that is
        // not writable, and this buffer is neither.
        Assert.NotNull(meta);

        Assert.True(meta.GetPlaneSize(out VideoMeta.PlaneSizeArray sizes));

        Assert.Equal((nuint)(320 * 240), sizes[0]);
        Assert.Equal((nuint)(160 * 120), sizes[1]);
        Assert.Equal((nuint)(160 * 120), sizes[2]);
        Assert.Equal((nuint)0, sizes[3]);
    }
}
