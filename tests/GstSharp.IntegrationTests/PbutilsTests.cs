using System;
using System.Collections.Generic;
using Gst;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The feature surface of the base utils library measured against the
/// installation: the serialised encoding profile, the encoding targets on
/// disk, the description strings, and the codec header readers whose answers
/// are fixed tables in the C source.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here asserts a value the binding computes. Every expectation is
/// either a row of a table in <c>descriptions.c</c> or <c>codec-utils.c</c> —
/// read out of the 1.24 source, which is the floor of the CI matrix, and
/// checked to be unchanged in 1.28 — or the shape of a string the C source
/// builds with a format the running library fills in.
/// </para>
/// <para>
/// <b>Locale.</b> Most description strings go through <c>gettext</c>, and a
/// catalogue may reorder them — the French one turns <c>"%s decoder"</c> into
/// <c>"Décodeur %s"</c> — so only two answers are compared for equality: the
/// codec description of Vorbis, whose table entry is marked for no translation
/// and is in no <c>.po</c> file, and the sink description, which the C source
/// builds without <c>_()</c> at all. Everything else is asserted by
/// containment, which survives any translation because the part that is
/// substituted into the format is the part that is never translated.
/// </para>
/// <para>
/// <b>Gates.</b> <c>gst_encoding_profile_from_string</c> arrived in 1.26, and
/// parsing a serialisation walks the registry of muxers and encoders
/// (<c>encoding-profile.c</c>, <c>create_encoding_profile_from_caps</c>), so
/// the one test of it needs both a new enough library and the two elements it
/// resolves the media types against. Everything else here is 1.20 or older and
/// needs no element at all.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PbutilsTests
{
    /// <summary>
    /// An AAC <c>AudioSpecificConfig</c>: audio object type 2 (Low Complexity),
    /// sampling frequency index 3 (48000 Hz), channel configuration 2 (stereo).
    /// </summary>
    /// <remarks>
    /// Five bits of object type, four of frequency index and four of channel
    /// configuration, packed most significant bit first:
    /// <c>00010 0011 0010 000</c>.
    /// </remarks>
    private static ReadOnlySpan<byte> AacLowComplexity48000Stereo => [0x11, 0x90];

    /// <summary>
    /// The first three bytes of an H.264 sequence parameter set: profile_idc
    /// 100 (High), no constraint set flag, level_idc 40 (level 4).
    /// </summary>
    /// <remarks>
    /// <c>gst_codec_utils_h264_get_profile</c> reads <c>sps[0]</c> and the
    /// constraint flags out of <c>sps[1]</c>, and
    /// <c>gst_codec_utils_h264_get_level</c> reads <c>sps[2]</c>. Nothing else
    /// of a real sequence parameter set is looked at, which is why three bytes
    /// are a whole input rather than a fragment.
    /// </remarks>
    private static ReadOnlySpan<byte> H264HighLevel4Sps => [100, 0x00, 40];

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public PbutilsTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The serialisation format of an encoding profile is read back into the
    /// container and the stream profile it names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>parse_encoding_profile</c> splits the string on unescaped colons and
    /// turns the first element into the profile and every later one into a
    /// stream profile added to it. Which of the three kinds each element
    /// becomes is decided by <c>create_encoding_profile_from_caps</c> against
    /// the list of installed muxers and encoders, which is why this test is
    /// gated on <c>oggmux</c> and <c>vorbisenc</c> rather than on the library
    /// alone: without them the very same string parses to nothing.
    /// </para>
    /// <para>
    /// The call arrived in 1.26 and the floor of the matrix is 1.24, where the
    /// symbol is not exported at all. That is the documented behaviour of the
    /// binding rather than a defect, so the older half is asserted instead of
    /// skipped — a fact gated twice cannot say which gate stopped it.
    /// </para>
    /// </remarks>
    [RequiresElementFact("oggmux", "vorbisenc")]
    public void ASerialisedProfileParsesIntoAContainerAndItsStream()
    {
        GstPbutils.Initialize();

        const string Serialized = "application/ogg:audio/x-vorbis";

        if (!NativeAvailability.Has126)
        {
            Assert.Throws<EntryPointNotFoundException>(() => EncodingProfile.FromString(Serialized));
            return;
        }

        using EncodingProfile? profile = EncodingProfile.FromString(Serialized);

        Assert.NotNull(profile);

        EncodingContainerProfile container = Assert.IsType<EncodingContainerProfile>(profile);

        Assert.Equal("container", container.GetTypeNick());

        using (Caps format = container.GetFormat())
        {
            Assert.Equal("application/ogg", format.GetStructure(0).GetName());
        }

        EncodingProfile stream = Assert.Single(container.GetProfiles());

        Assert.IsType<EncodingAudioProfile>(stream);
        Assert.Equal("audio", stream.GetTypeNick());

        using Caps streamFormat = stream.GetFormat();

        Assert.Equal("audio/x-vorbis", streamFormat.GetStructure(0).GetName());

        _output.WriteLine($"{Serialized} parsed into a {container.GetTypeNick()} holding one {stream.GetTypeNick()}");
    }

    /// <summary>
    /// The encoding targets of the installation are listed rather than
    /// searched for, and an installation with none of them is a legitimate
    /// answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gst_encoding_list_all_targets</c> walks
    /// <c>$GST_ENCODING_TARGET_PATH</c>, the user data directory and the system
    /// data directory, and each of those is opened with the failure folded into
    /// an empty list — a machine with no <c>.gep</c> file anywhere gets an
    /// empty list and no error. So what is asserted is the shape of the answer
    /// and not its size: the lists exist, every target in one carries a name
    /// and a category, and its profiles can be read.
    /// </para>
    /// <para>
    /// <c>gst_encoding_profile_find</c> is the lookup half, and it is the one
    /// answer that is fixed on every machine: it loads the target with a
    /// <see langword="null"/> error and returns <see langword="null"/> when
    /// there is none, so a name no installation can carry has exactly one
    /// answer everywhere.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheEncodingTargetsOnDiskAreListedWhetherOrNotThereAreAny()
    {
        GstPbutils.Initialize();

        IReadOnlyList<string> categories = PbutilsGlobal.EncodingListAvailableCategories();
        IReadOnlyList<EncodingTarget> all = PbutilsGlobal.EncodingListAllTargets(null);

        _output.WriteLine(FormattableString.Invariant(
            $"{categories.Count} categories and {all.Count} targets are installed"));

        foreach (string category in categories)
        {
            Assert.False(string.IsNullOrEmpty(category));

            foreach (EncodingTarget target in PbutilsGlobal.EncodingListAllTargets(category))
            {
                Assert.False(string.IsNullOrEmpty(target.GetName()));
                Assert.False(string.IsNullOrEmpty(target.GetCategory()));

                // The profiles are what a target is for, and reading them is
                // what proves the object behind the wrapper is real.
                _output.WriteLine(FormattableString.Invariant(
                    $"  {category}/{target.GetName()}: {target.GetProfiles().Count} profiles"));
            }
        }

        foreach (EncodingTarget target in all)
        {
            Assert.False(string.IsNullOrEmpty(target.GetName()));
        }

        // A name no target can answer to: the loader lower-cases and validates
        // it, finds no file and no listed target, and hands back nothing.
        Assert.Null(EncodingProfile.Find("gstsharp-no-such-target", null, null));
    }

    /// <summary>
    /// The codec description of a format in the table is the table's own
    /// string, and the flags and the file extension beside it are the same row.
    /// </summary>
    /// <remarks>
    /// <c>{"audio/x-vorbis", "Vorbis", FLAG_AUDIO, ""}</c> and
    /// <c>{"application/ogg", "Ogg", AVIS_CONTAINER, "ogg"}</c> are two rows of
    /// the format table of <c>descriptions.c</c>, unchanged between 1.24 and
    /// 1.28. Neither description is in a catalogue, so <c>_()</c> hands the
    /// msgid straight back and these are the answers in every locale.
    /// </remarks>
    [Fact]
    public void TheCodecDescriptionOfAKnownFormatIsItsRowInTheTable()
    {
        GstPbutils.Initialize();

        using Caps vorbis = NewCaps("audio/x-vorbis");
        using Caps ogg = NewCaps("application/ogg");

        Assert.Equal("Vorbis", PbutilsGlobal.PbUtilsGetCodecDescription(vorbis));
        Assert.Equal("Ogg", PbutilsGlobal.PbUtilsGetCodecDescription(ogg));

        // The third column of the same two rows: an audio codec, and a
        // container that is allowed to carry more than one kind of stream.
        Assert.Equal(PbUtilsCapsDescriptionFlags.Audio, PbutilsGlobal.PbUtilsGetCapsDescriptionFlags(vorbis));
        Assert.True(PbutilsGlobal.PbUtilsGetCapsDescriptionFlags(ogg)
            .HasFlag(PbUtilsCapsDescriptionFlags.Container));

        // And the fourth. Vorbis has no extension of its own, which is the
        // empty string in the table and null here.
        Assert.Equal("ogg", PbutilsGlobal.PbUtilsGetFileExtensionFromCaps(ogg));
        Assert.Null(PbutilsGlobal.PbUtilsGetFileExtensionFromCaps(vorbis));
    }

    /// <summary>
    /// The decoder and encoder descriptions quote the codec description, and
    /// the word they wrap it in says whether the format is a container.
    /// </summary>
    /// <remarks>
    /// <c>gst_pb_utils_get_decoder_description</c> asks
    /// <c>gst_pb_utils_get_codec_description</c> first and then formats
    /// <c>"%s demuxer"</c> for a container and <c>"%s decoder"</c> for anything
    /// else; the encoder half is <c>"%s muxer"</c> and <c>"%s encoder"</c>.
    /// Those four formats are translated and the description substituted into
    /// them is not, so containment is the assertion that holds in every locale.
    /// </remarks>
    [Fact]
    public void TheDecoderAndEncoderDescriptionsQuoteTheCodecDescription()
    {
        GstPbutils.Initialize();

        using Caps vorbis = NewCaps("audio/x-vorbis");
        using Caps ogg = NewCaps("application/ogg");

        string decoder = PbutilsGlobal.PbUtilsGetDecoderDescription(vorbis);
        string encoder = PbutilsGlobal.PbUtilsGetEncoderDescription(vorbis);
        string demuxer = PbutilsGlobal.PbUtilsGetDecoderDescription(ogg);
        string muxer = PbutilsGlobal.PbUtilsGetEncoderDescription(ogg);

        _output.WriteLine($"{decoder} / {encoder} / {demuxer} / {muxer}");

        Assert.Contains("Vorbis", decoder, StringComparison.Ordinal);
        Assert.Contains("Vorbis", encoder, StringComparison.Ordinal);
        Assert.Contains("Ogg", demuxer, StringComparison.Ordinal);
        Assert.Contains("Ogg", muxer, StringComparison.Ordinal);

        // A codec and a container are described differently, which is the one
        // thing these four strings say beyond the codec description itself.
        Assert.NotEqual(decoder, demuxer);
        Assert.NotEqual(encoder, muxer);
    }

    /// <summary>
    /// The sink description of a protocol upper-cases it, and is the one
    /// string of the family that no catalogue can change.
    /// </summary>
    /// <remarks>
    /// <c>gst_pb_utils_get_sink_description</c> builds
    /// <c>"%s protocol sink"</c> with a plain <c>g_strdup_printf</c> and no
    /// <c>_()</c> around it, in 1.24 and still in 1.28, so it is the same
    /// sentence in every locale. Its source twin is translated, and
    /// <c>"rtsp"</c> is one of four protocols it answers with a sentence of its
    /// own instead of with the format, so only the upper-cased protocol is
    /// asserted there.
    /// </remarks>
    [Fact]
    public void TheSinkDescriptionOfAProtocolIsTheOneThatIsNotTranslated()
    {
        GstPbutils.Initialize();

        Assert.Equal("HTTP protocol sink", PbutilsGlobal.PbUtilsGetSinkDescription("http"));

        // Nothing asks the registry whether the protocol is handled, which is
        // the point of the family: it describes what is missing.
        Assert.Equal("GSTSHARP protocol sink", PbutilsGlobal.PbUtilsGetSinkDescription("gstsharp"));

        string source = PbutilsGlobal.PbUtilsGetSourceDescription("http");
        string rtsp = PbutilsGlobal.PbUtilsGetSourceDescription("rtsp");

        _output.WriteLine($"{source} / {rtsp}");

        Assert.Contains("HTTP", source, StringComparison.Ordinal);
        Assert.Contains("RTSP", rtsp, StringComparison.Ordinal);

        // The element description is the third of the family. It formats
        // "GStreamer element %s", which is translated, around a factory name
        // that is not, and it does not ask the registry whether the factory
        // exists either.
        string element = PbutilsGlobal.PbUtilsGetElementDescription("gstsharp-no-such-factory");

        Assert.Contains("gstsharp-no-such-factory", element, StringComparison.Ordinal);
    }

    /// <summary>
    /// The AAC sample rate table is read in both directions, and a two byte
    /// <c>AudioSpecificConfig</c> is decoded into the three things it holds.
    /// </summary>
    /// <remarks>
    /// The table of <c>codec-utils.c</c> is
    /// <c>{ 96000, 88200, 64000, 48000, ... }</c> with thirteen entries, so
    /// index 3 is 48000 Hz; <c>gst_codec_utils_aac_get_channels</c> is
    /// <c>(config[1] &amp; 0x7f) >> 3</c>; and audio object type 2 is
    /// <c>"lc"</c> in the profile switch. An index outside the table answers 0
    /// and a rate outside it answers -1, which are the two documented failures.
    /// </remarks>
    [Fact]
    public void TheAacHeaderReadersAnswerWhatTheCTablesHold()
    {
        GstPbutils.Initialize();

        Assert.Equal(3, PbutilsGlobal.CodecUtilsAacGetIndexFromSampleRate(48000));
        Assert.Equal(48000u, PbutilsGlobal.CodecUtilsAacGetSampleRateFromIndex(3));

        Assert.Equal(0u, PbutilsGlobal.CodecUtilsAacGetSampleRateFromIndex(13));
        Assert.Equal(-1, PbutilsGlobal.CodecUtilsAacGetIndexFromSampleRate(48001));

        Assert.Equal(48000u, PbutilsGlobal.CodecUtilsAacGetSampleRate(AacLowComplexity48000Stereo));
        Assert.Equal(2u, PbutilsGlobal.CodecUtilsAacGetChannels(AacLowComplexity48000Stereo));
        Assert.Equal("lc", PbutilsGlobal.CodecUtilsAacGetProfile(AacLowComplexity48000Stereo));

        // Fewer than two bytes is not a configuration, and the C source says so
        // by answering zero before it reads anything.
        Assert.Equal(0u, PbutilsGlobal.CodecUtilsAacGetSampleRate(AacLowComplexity48000Stereo[..1]));
        Assert.Equal(0u, PbutilsGlobal.CodecUtilsAacGetChannels(AacLowComplexity48000Stereo[..1]));
    }

    /// <summary>
    /// The H.264 profile and level readers answer the rows of their switches,
    /// and the level string goes back to the number it came from.
    /// </summary>
    /// <remarks>
    /// profile_idc 100 without the constraint set 4 flag is <c>"high"</c>, and
    /// level_idc 40 divides by ten into <c>"4"</c>, which
    /// <c>gst_codec_utils_h264_get_level_idc</c> turns back into 40. The caps
    /// setter is those same two answers written into a structure, which is how
    /// a parser hands them on.
    /// </remarks>
    [Fact]
    public void TheH264SpsIsReadTheWayTheCSwitchesReadIt()
    {
        GstPbutils.Initialize();

        Assert.Equal("high", PbutilsGlobal.CodecUtilsH264GetProfile(H264HighLevel4Sps));
        Assert.Equal("4", PbutilsGlobal.CodecUtilsH264GetLevel(H264HighLevel4Sps));
        Assert.Equal(40, PbutilsGlobal.CodecUtilsH264GetLevelIdc("4"));

        // Two bytes are enough for a profile and not for a level, and a
        // profile_idc in no row of the switch has no name at all.
        Assert.Null(PbutilsGlobal.CodecUtilsH264GetLevel(H264HighLevel4Sps[..2]));
        Assert.Null(PbutilsGlobal.CodecUtilsH264GetProfile([0, 0, 0]));

        using Caps caps = NewCaps("video/x-h264");

        Assert.True(PbutilsGlobal.CodecUtilsH264CapsSetLevelAndProfile(caps, H264HighLevel4Sps));

        Structure structure = caps.GetStructure(0);

        Assert.Equal("high", structure.GetString("profile"));
        Assert.Equal("4", structure.GetString("level"));
    }

    /// <summary>
    /// The MIME codec string of a caps is the RFC 6381 name of the same
    /// format, and it reads back into caps of that media type.
    /// </summary>
    /// <remarks>
    /// <c>gst_codec_utils_caps_get_mime_codec</c> answers <c>"opus"</c> for
    /// Opus and <c>"vp09"</c>-shaped strings for VP9; the way back is the
    /// four character code switch of
    /// <c>gst_codec_utils_caps_from_mime_codec_single</c>, whose <c>opus</c>
    /// row builds <c>audio/x-opus</c> again. The pair is what a DASH or HLS
    /// manifest is written and read with. Neither string is translated.
    /// </remarks>
    [Fact]
    public void TheMimeCodecStringOfACapsRoundTrips()
    {
        GstPbutils.Initialize();

        using Caps opus = NewCaps("audio/x-opus");

        Assert.Equal("opus", PbutilsGlobal.CodecUtilsCapsGetMimeCodec(opus));

        using Caps? back = PbutilsGlobal.CodecUtilsCapsFromMimeCodec("opus");

        Assert.NotNull(back);
        Assert.Equal("audio/x-opus", back.GetStructure(0).GetName());

        // Only the formats RFC 6381 names have one of these, and the media
        // types the codec-utils switch does not list answer with nothing —
        // Vorbis, which the description table above does know, is one of them.
        using Caps vorbis = NewCaps("audio/x-vorbis");

        Assert.Null(PbutilsGlobal.CodecUtilsCapsGetMimeCodec(vorbis));

        // Fewer than four characters is not a four character code, and the C
        // source refuses it before it looks anything up.
        Assert.Null(PbutilsGlobal.CodecUtilsCapsFromMimeCodec("ops"));
    }

    /// <summary>
    /// Builds fixed caps of one media type and nothing else.
    /// </summary>
    /// <param name="mediaType">The media type, for example <c>audio/x-vorbis</c>.</param>
    /// <returns>The caps, which the caller disposes.</returns>
    private static Caps NewCaps(string mediaType)
    {
        Caps? caps = Caps.FromString(mediaType);

        Assert.NotNull(caps);

        return caps;
    }
}
