using System;
using System.Buffers.Binary;
using Gst;
using Gst.Audio;
using Gst.GObject;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The audio module measured against the library that is installed: the
/// description of a raw format, the two metadata items a buffer of samples
/// carries, the converter, and the volume conversions.
/// </summary>
/// <remarks>
/// <para>
/// Apart from the <c>MakeWritable</c> regression, nothing here asserts a value
/// the binding computes. Every expectation is either what the C implementation
/// documents (a frame of stereo 16 bit audio is four bytes, the cubic volume of
/// a linear one is its cube) or a value that
/// was written through the binding a moment earlier and is read back through
/// it.
/// </para>
/// <para>
/// The canonical format of these tests is S16LE at 48000 Hz with two channels,
/// which is the shape every raw audio path in GStreamer handles, so a failure
/// is a failure of the binding rather than of the installation.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AudioFeatureTests
{
    /// <summary>The sample rate every test of this class uses.</summary>
    private const int Rate = 48000;

    /// <summary>The channel count every test of this class uses.</summary>
    private const int Channels = 2;

    /// <summary>The size of one frame of the canonical format, in bytes.</summary>
    private const int BytesPerFrame = Channels * sizeof(short);

    /// <summary>
    /// The endianness argument of <c>gst_audio_format_build_integer</c>, which
    /// is <c>G_LITTLE_ENDIAN</c>. It describes the format that is being asked
    /// for, S16<b>LE</b>, and not the machine the test runs on, so it is a
    /// constant rather than something read from the runtime.
    /// </summary>
    private const int LittleEndian = 1234;

    /// <summary>
    /// The description of a format survives the trip through caps: what
    /// <c>gst_audio_info_set_format</c> wrote is what
    /// <c>gst_audio_info_from_caps</c> reads back.
    /// </summary>
    /// <remarks>
    /// The channel positions are passed as an empty span, which is the
    /// <c>NULL</c> the C function takes to mean "the default positions for this
    /// channel count". A partial array is not an option: the C declaration
    /// sizes the buffer at 64 entries, and the binding refuses anything else.
    /// </remarks>
    [Fact]
    public void AnAudioInfoRoundTripsThroughCaps()
    {
        using AudioInfo info = AudioInfo.New();
        info.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        Assert.Equal(AudioFormat.S16le, info.Format);
        Assert.Equal(Rate, info.Rate);
        Assert.Equal(Channels, info.Channels);
        Assert.Equal(BytesPerFrame, info.Bpf);
        Assert.Equal(AudioLayout.Interleaved, info.Layout);

        // The description belongs to GStreamer, which keeps one per format for
        // the life of the process: it is borrowed, never disposed, and reading
        // through it is what proves the pointer is real.
        AudioFormatInfo description = info.FormatInfo;
        Assert.Equal(AudioFormat.S16le, description.Format);
        Assert.Equal(16, description.Width);

        using Caps caps = info.ToCaps();
        using AudioInfo? parsed = AudioInfo.NewFromCaps(caps);

        Assert.NotNull(parsed);
        Assert.Equal(AudioFormat.S16le, parsed.Format);
        Assert.Equal(Rate, parsed.Rate);
        Assert.Equal(Channels, parsed.Channels);
        Assert.Equal(BytesPerFrame, parsed.Bpf);
        Assert.Equal(AudioLayout.Interleaved, parsed.Layout);
        Assert.True(info.IsEqual(parsed));
    }

    /// <summary>
    /// A second of the canonical format is <c>4 * 48000</c> bytes, and
    /// <c>gst_audio_info_convert</c> says so in nanoseconds.
    /// </summary>
    [Fact]
    public void AnAudioInfoConvertsBytesToTime()
    {
        using AudioInfo info = AudioInfo.New();
        info.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        Assert.True(info.Convert(Format.Bytes, BytesPerFrame * Rate, Format.Time, out long time));
        Assert.Equal((long)ClockTime.NanosecondsPerSecond, time);

        // GST_FORMAT_DEFAULT is one audio frame, so a second is the rate.
        Assert.True(info.Convert(
            Format.Time,
            (long)ClockTime.NanosecondsPerSecond,
            Format.Default,
            out long frames));
        Assert.Equal(Rate, frames);
    }

    /// <summary>
    /// The caps of an audio info are a mini object like any other: the wrapper
    /// of a caps nobody else holds comes back from <c>MakeWritable</c> as
    /// itself, standing for the same native caps, and a field written through
    /// it is what the parser reads afterwards.
    /// </summary>
    /// <remarks>
    /// This is the regression of the first class <c>MakeWritable</c>: the
    /// member adopts in place instead of answering a second wrapper, so the
    /// caps the test goes on to use is the caps it wrote to.
    /// </remarks>
    [Fact]
    public void TheCapsOfAnAudioInfoAreWritableAndStillParse()
    {
        using AudioInfo info = AudioInfo.New();
        info.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        using Caps caps = info.ToCaps();
        nint before = caps.Handle;

        Caps returned = caps.MakeWritable();

        Assert.Same(caps, returned);
        Assert.Equal(before, caps.Handle);
        Assert.True(caps.IsWritable);

        using (Value rate = Value.New(GType.Int))
        {
            rate.SetInt(44100);
            caps.SetValue("rate", rate);
        }

        using AudioInfo? parsed = AudioInfo.NewFromCaps(caps);

        Assert.NotNull(parsed);
        Assert.Equal(44100, parsed.Rate);
        Assert.Equal(AudioFormat.S16le, parsed.Format);
    }

    /// <summary>
    /// The four ways of naming a raw format agree with each other: the string,
    /// the description, the integer recipe and the enumerator.
    /// </summary>
    /// <remarks>
    /// <c>ToString</c> is never asked about <see cref="AudioFormat.Unknown"/>,
    /// whose answer the C function only started to define after the version
    /// this suite takes as its floor.
    /// </remarks>
    [Fact]
    public void TheAudioFormatHelpersAgreeOnS16le()
    {
        Assert.Equal(AudioFormat.S16le, AudioFormatExtensions.FromString("S16LE"));
        Assert.Equal("S16LE", AudioFormatExtensions.ToString(AudioFormat.S16le));

        AudioFormatInfo description = AudioFormatExtensions.GetInfo(AudioFormat.S16le);
        Assert.Equal(AudioFormat.S16le, description.Format);
        Assert.Equal(16, description.Width);
        Assert.Equal(16, description.Depth);
        Assert.Equal(LittleEndian, description.Endianness);

        Assert.Equal(
            AudioFormat.S16le,
            AudioFormatExtensions.BuildInteger(sign: true, LittleEndian, width: 16, depth: 16));
    }

    /// <summary>
    /// The channel mask of the front pair is its two bit positions, the array
    /// it is built from is what reading it back produces, and a pair that names
    /// the same speaker twice is not a valid layout.
    /// </summary>
    [Fact]
    public void ChannelPositionsRoundTripThroughAMask()
    {
        ReadOnlySpan<AudioChannelPosition> front =
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight];

        Assert.True(AudioGlobal.AudioChannelPositionsToMask(front, forceOrder: true, out ulong mask));
        Assert.Equal(0x3ul, mask);

        AudioChannelPosition[] read = new AudioChannelPosition[Channels];
        Assert.True(AudioGlobal.AudioChannelPositionsFromMask(mask, read));
        Assert.Equal(AudioChannelPosition.FrontLeft, read[0]);
        Assert.Equal(AudioChannelPosition.FrontRight, read[1]);

        Assert.True(AudioGlobal.AudioCheckValidChannelPositions(front, forceOrder: true));

        ReadOnlySpan<AudioChannelPosition> twice =
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontLeft];
        Assert.False(AudioGlobal.AudioCheckValidChannelPositions(twice, forceOrder: true));
    }

    /// <summary>
    /// A clipping item attached to a writable buffer is found by the walk,
    /// reinterprets into its own wrapper, and carries the amounts it was given.
    /// </summary>
    /// <remarks>
    /// The buffer is made writable first, which is the rule for every mutation
    /// of a mini object and the shape an application uses; a buffer nobody else
    /// holds is writable already, so the wrapper comes back as itself.
    /// </remarks>
    [Fact]
    public void AnAudioClippingMetaRoundTripsOnAWritableBuffer()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 4 * BytesPerFrame, null));
        Assert.Same(buffer, buffer.MakeWritable());

        AudioClippingMeta added = AudioGlobal.BufferAddAudioClippingMeta(
            buffer,
            Format.Default,
            start: 10,
            end: 20);
        Assert.NotNull(added);

        Gst.Meta item = Assert.Single(buffer.IterateMeta(AudioGlobal.AudioClippingMetaApiGetType()));
        AudioClippingMeta? clipping = AudioClippingMeta.FromMeta(item);

        Assert.NotNull(clipping);
        Assert.Equal(10ul, clipping.Start);
        Assert.Equal(20ul, clipping.End);

        // The cast of another API answers null rather than a wrapper over the
        // wrong bytes.
        Assert.Null(AudioLevelMeta.FromMeta(item));
        Assert.Null(VideoMeta.FromMeta(item));
    }

    /// <summary>
    /// The level item is absent until it is attached, and reading it back
    /// produces what was written.
    /// </summary>
    /// <remarks>
    /// The getter answers <see langword="null"/> for a buffer that carries no
    /// level, which is the normal answer for the vast majority of buffers and
    /// not a failure: the member stays nullable.
    /// </remarks>
    [Fact]
    public void AnAudioLevelMetaIsAbsentUntilItIsAttached()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 4 * BytesPerFrame, null));
        Assert.Same(buffer, buffer.MakeWritable());

        Assert.Null(AudioGlobal.BufferGetAudioLevelMeta(buffer));

        AudioLevelMeta? added = AudioGlobal.BufferAddAudioLevelMeta(buffer, level: 42, voiceActivity: true);
        Assert.NotNull(added);
        Assert.Equal(42, added.Level);
        Assert.True(added.VoiceActivity);

        AudioLevelMeta? found = AudioGlobal.BufferGetAudioLevelMeta(buffer);
        Assert.NotNull(found);
        Assert.Equal(42, found.Level);
        Assert.True(found.VoiceActivity);

        // The cast of another API answers null rather than a wrapper over the
        // wrong bytes, in this direction as well.
        Gst.Meta item = Assert.Single(buffer.IterateMeta(AudioGlobal.AudioLevelMetaApiGetType()));
        Assert.Null(AudioClippingMeta.FromMeta(item));
    }

    /// <summary>
    /// The converter turns the canonical format into 32 bit floats: four frames
    /// in, four frames out, and a sample of half of the positive range reads as
    /// one half.
    /// </summary>
    /// <remarks>
    /// Both sides run at the same rate, so no resampler stands between them and
    /// the frame counts of the two directions are the count they are asked
    /// about. The output is read little endian, because F32LE is what the
    /// output description asked for.
    /// </remarks>
    [Fact]
    public void AnAudioConverterTurnsS16leIntoF32le()
    {
        using AudioInfo input = AudioInfo.New();
        input.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        using AudioInfo output = AudioInfo.New();
        output.SetFormat(AudioFormat.F32le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        using AudioConverter? converter = AudioConverter.New(AudioConverterFlags.None, input, output, null);

        Assert.NotNull(converter);
        Assert.False(converter.IsPassthrough());
        Assert.Equal((nuint)4, converter.GetOutFrames(4));
        Assert.Equal((nuint)4, converter.GetInFrames(4));

        const int Frames = 4;
        const short Half = 16384;
        byte[] samples = new byte[Frames * BytesPerFrame];
        for (int sample = 0; sample < Frames * Channels; sample++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(sample * sizeof(short)), Half);
        }

        Assert.True(converter.Convert(AudioConverterFlags.None, samples, out byte[]? converted));
        Assert.NotNull(converted);
        Assert.Equal(Frames * Channels * sizeof(float), converted.Length);

        for (int sample = 0; sample < Frames * Channels; sample++)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(converted.AsSpan(sample * sizeof(float)));
            Assert.Equal(0.5, value, 6);
        }

        // The configuration is answered as a copy of the boxed structure, so
        // the wrapper owns it and has to release it.
        using Structure config = converter.GetConfig(out int inRate, out int outRate);
        Assert.NotNull(config);
        Assert.Equal(Rate, inRate);
        Assert.Equal(Rate, outRate);
    }

    /// <summary>
    /// A converter whose two ends describe the same format has nothing to do
    /// and says so.
    /// </summary>
    [Fact]
    public void AnAudioConverterOfOneFormatIsPassthrough()
    {
        using AudioInfo input = AudioInfo.New();
        input.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        using AudioInfo output = AudioInfo.New();
        output.SetFormat(AudioFormat.S16le, Rate, Channels, ReadOnlySpan<AudioChannelPosition>.Empty);

        using AudioConverter? converter = AudioConverter.New(AudioConverterFlags.None, input, output, null);

        Assert.NotNull(converter);
        Assert.True(converter.IsPassthrough());
    }

    /// <summary>
    /// The volume conversions are the cube and its root: the cubic volume a
    /// slider shows is the cube of the linear factor the pipeline applies.
    /// </summary>
    /// <remarks>
    /// This is the whole of the stream volume surface these tests reach. The
    /// instance members are extensions of <c>Gst.Audio.IStreamVolume</c>, and
    /// no wrapper this binding hands out implements that interface: the
    /// elements that provide a stream volume, <c>volume</c> and <c>playbin</c>,
    /// are plugin elements with no generated class of their own, and the
    /// classes they are recognised as do not declare it. The linear factor of
    /// such an element is reachable as its <c>volume</c> property, which
    /// <see cref="ControllerModuleTests"/> already reads.
    /// </remarks>
    [Fact]
    public void TheStreamVolumeConversionsAreTheCubeAndItsRoot()
    {
        Assert.Equal(
            1.0,
            StreamVolumeExtensions.ConvertVolume(StreamVolumeFormat.Linear, StreamVolumeFormat.Cubic, 1.0),
            6);

        Assert.Equal(
            0.125,
            StreamVolumeExtensions.ConvertVolume(StreamVolumeFormat.Cubic, StreamVolumeFormat.Linear, 0.5),
            6);

        Assert.Equal(
            0.5,
            StreamVolumeExtensions.ConvertVolume(StreamVolumeFormat.Linear, StreamVolumeFormat.Cubic, 0.125),
            6);
    }
}
