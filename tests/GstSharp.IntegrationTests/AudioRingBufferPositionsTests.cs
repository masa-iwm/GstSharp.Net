using System;
using Gst;
using Gst.Audio;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The rules of the hand written
/// <see cref="AudioRingBuffer.SetChannelPositions"/>, and the instance layout
/// the count it measures against is read out of.
/// </summary>
/// <remarks>
/// The number of positions the library reads is <c>spec.info.channels</c>, a
/// public field of the ring buffer that carries no accessor. The wrapper reads
/// it through a mirror of the instance head, so the layout of that mirror is
/// what everything here rests on: it is asserted against the offsets of the
/// 1.28 headers, and it is proven against the installed library by acquiring a
/// ring buffer for a two channel format and watching the wrapper answer with
/// the count the acquire path wrote.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class AudioRingBufferPositionsTests
{
    /// <summary>The latency of the spec the probe acquires with, in microseconds.</summary>
    private const ulong LatencyTime = 10000;

    /// <summary>The buffer size of the spec the probe acquires with, in microseconds.</summary>
    private const ulong BufferTime = 200000;

    /// <summary>
    /// How long an acquire that sets the channel order from the sink's
    /// <c>prepare</c> is waited for. It takes microseconds when it works and
    /// never returns when it deadlocks, so the value only decides how long a
    /// failing run takes.
    /// </summary>
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The mirror of the instance head lays its two fields where the C headers
    /// put them on a 64 bit platform.
    /// </summary>
    /// <remarks>
    /// The derivation is written out on
    /// <c>Gst.Audio.AudioRingBufferHeadRaw</c>. The numbers are repeated here
    /// on purpose: the C ABI is the ground truth, so a mirror that drifts has
    /// to fail here rather than quietly agree with itself.
    /// </remarks>
    [Fact]
    public unsafe void TheInstanceHeadIsMirroredWhereTheHeadersPutIt()
    {
        AudioRingBufferHeadRaw raw = default;

        Assert.Equal(88L, Offset(&raw, &raw.Cond));
        Assert.Equal(104L, Offset(&raw, &raw.Open));
        Assert.Equal(108L, Offset(&raw, &raw.Acquired));
        Assert.Equal(112L, Offset(&raw, &raw.Memory));
        Assert.Equal(120L, Offset(&raw, &raw.Size));
        Assert.Equal(128L, Offset(&raw, &raw.Timestamps));
        Assert.Equal(136L, Offset(&raw, &raw.Spec));
        Assert.Equal(172L, Offset(&raw, &raw.Spec.Info.Channels));
    }

    /// <summary>A channel order of no channels at all is refused.</summary>
    [Fact]
    public void AnEmptyChannelOrderIsRefused()
    {
        using ProbeAudioSink sink = new();
        using AudioRingBuffer ringBuffer = sink.CreateRingbuffer()
            ?? throw new InvalidOperationException("An audio sink has to be able to create a ring buffer.");

        _ = Assert.Throws<ArgumentOutOfRangeException>(
            () => ringBuffer.SetChannelPositions(ReadOnlySpan<AudioChannelPosition>.Empty));
    }

    /// <summary>
    /// A channel order longer than the sixty four positions a
    /// <c>GstAudioInfo</c> holds is refused, which is what keeps the padded
    /// buffer the call is made over from being overrun.
    /// </summary>
    [Fact]
    public void AChannelOrderOfMoreThanSixtyFourPositionsIsRefused()
    {
        using ProbeAudioSink sink = new();
        using AudioRingBuffer ringBuffer = sink.CreateRingbuffer()
            ?? throw new InvalidOperationException("An audio sink has to be able to create a ring buffer.");

        AudioChannelPosition[] tooMany = new AudioChannelPosition[65];
        Array.Fill(tooMany, AudioChannelPosition.None);

        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ringBuffer.SetChannelPositions(tooMany));
    }

    /// <summary>
    /// A ring buffer that was never acquired has no format and therefore no
    /// channel order; the call the C answers with a critical is refused here
    /// instead.
    /// </summary>
    [Fact]
    public void AnUnacquiredRingBufferHasNoChannelOrder()
    {
        using ProbeAudioSink sink = new();
        using AudioRingBuffer ringBuffer = sink.CreateRingbuffer()
            ?? throw new InvalidOperationException("An audio sink has to be able to create a ring buffer.");

        Assert.False(ringBuffer.IsAcquired());

        _ = Assert.Throws<InvalidOperationException>(() => ringBuffer.SetChannelPositions(
            [AudioChannelPosition.FrontLeft, AudioChannelPosition.FrontRight]));
    }

    /// <summary>
    /// The count the wrapper measures a channel order against is the one the
    /// acquire path of the installed library wrote into the instance.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the proof of the mirror against the running library rather than
    /// against the headers: the spec the ring buffer is acquired with is its
    /// own <c>spec</c> field, reached through the mirror, so an offset that is
    /// wrong leaves the real field zeroed and
    /// <c>gst_audio_ring_buffer_acquire</c> fails on a <c>bpf</c> of zero. A
    /// wrong <c>channels</c> offset shows up in the same call, as a count that
    /// refuses the two positions the stereo format has.
    /// </para>
    /// <para>
    /// Nothing here starts the thread of the ring buffer: the sink is never
    /// activated, so open, acquire, release and close all run on this thread.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAcquiredRingBufferMeasuresTheOrderAgainstItsOwnChannelCount()
    {
        using ProbeAudioSink sink = new();
        using AudioRingBuffer ringBuffer = sink.CreateRingbuffer()
            ?? throw new InvalidOperationException("An audio sink has to be able to create a ring buffer.");

        // The spec the ring buffer is acquired with has to be the one it
        // carries: the acquire path reads bpf, segsize and the type back out
        // of that field and nowhere else.
        AudioRingBufferSpec spec = SpecOf(ringBuffer);

        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)44100, channels=(int)2")
            ?? throw new InvalidOperationException("The acquire caps could not be parsed.");

        Assert.True(AudioRingBuffer.ParseCaps(spec, caps));
        Assert.True(ringBuffer.OpenDevice());

        try
        {
            Assert.True(ringBuffer.Acquire(spec));
            Assert.True(ringBuffer.IsAcquired());

            using (AudioInfo info = spec.GetInfo())
            {
                Assert.Equal(2, info.Channels);
            }

            // The two positions of the acquired format, swapped: a permutation
            // the library computes a reorder map for rather than a set it
            // answers with a critical.
            IReadOnlyList<string> logged = InitializeLogProbe.CaptureWhile(
                () => ringBuffer.SetChannelPositions(
                    [AudioChannelPosition.FrontRight, AudioChannelPosition.FrontLeft]));

            if (InitializeLogProbe.IsInstalled)
            {
                Assert.DoesNotContain(
                    logged,
                    message => message.Contains("CRITICAL", StringComparison.Ordinal));
            }

            // One position for a format of two channels is the overread the
            // wrapper exists to refuse.
            _ = Assert.Throws<ArgumentException>(() => ringBuffer.SetChannelPositions(
                [AudioChannelPosition.FrontLeft]));
        }
        finally
        {
            _ = ringBuffer.Release();
            _ = ringBuffer.CloseDevice();
        }
    }

    /// <summary>
    /// The channel order can be set from the <c>prepare</c> of a sink, which
    /// runs while the library holds the object lock of the ring buffer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gst_audio_ring_buffer_acquire</c> takes the object lock, sets
    /// <c>acquired</c> and then calls the acquire vfunc, which calls the
    /// sink's <c>prepare</c>; <c>alsasink</c> sets its channel order exactly
    /// there. A wrapper that read the acquired flag through
    /// <c>gst_audio_ring_buffer_is_acquired</c> would take the same
    /// non-recursive lock a second time and hang the pipeline, which is why
    /// both fields are read raw.
    /// </para>
    /// <para>
    /// The acquire runs on another thread with a bounded wait, so that the
    /// deadlock this pins fails the test rather than stopping the run. The
    /// ring buffer is only released when the wait succeeded: releasing one
    /// whose lock is held would hang the same way.
    /// </para>
    /// </remarks>
    [Fact]
    public async System.Threading.Tasks.Task TheChannelOrderCanBeSetFromThePrepareOfASink()
    {
        using ChannelOrderAudioSink sink = new();
        using AudioRingBuffer ringBuffer = sink.CreateRingbuffer()
            ?? throw new InvalidOperationException("An audio sink has to be able to create a ring buffer.");

        sink.OrderTarget = ringBuffer;

        AudioRingBufferSpec spec = SpecOf(ringBuffer);

        using Caps caps = Caps.FromString(
            "audio/x-raw, format=(string)S16LE, layout=(string)interleaved, "
            + "rate=(int)44100, channels=(int)2")
            ?? throw new InvalidOperationException("The acquire caps could not be parsed.");

        Assert.True(AudioRingBuffer.ParseCaps(spec, caps));
        Assert.True(ringBuffer.OpenDevice());

        System.Threading.Tasks.Task<bool> acquire =
            System.Threading.Tasks.Task.Run(() => ringBuffer.Acquire(spec));

        System.Threading.Tasks.Task finished = await System.Threading.Tasks.Task.WhenAny(
            acquire,
            System.Threading.Tasks.Task.Delay(AcquireTimeout));

        Assert.True(
            ReferenceEquals(finished, acquire),
            "The acquire did not return: setting the channel order deadlocked on the object lock.");
        Assert.True(await acquire);

        try
        {
            Assert.Null(sink.OrderFailure);
            Assert.True(sink.OrderWasSet);
        }
        finally
        {
            _ = ringBuffer.Release();
            _ = ringBuffer.CloseDevice();
        }
    }

    /// <summary>
    /// Points a specification wrapper at the <c>spec</c> field of a ring
    /// buffer and gives it the two times <c>ParseCaps</c> needs.
    /// </summary>
    /// <param name="ringBuffer">The ring buffer whose field is wrapped.</param>
    /// <returns>A wrapper over that field, which nothing frees.</returns>
    /// <remarks>
    /// The acquire path reads <c>bpf</c>, <c>segsize</c> and the type back out
    /// of that field and nowhere else, so the specification an acquire is
    /// handed has to be this one.
    /// </remarks>
    private static unsafe AudioRingBufferSpec SpecOf(AudioRingBuffer ringBuffer)
    {
        AudioRingBufferSpecRaw* specRaw = &((AudioRingBufferHeadRaw*)ringBuffer.Handle)->Spec;
        specRaw->LatencyTime = LatencyTime;
        specRaw->BufferTime = BufferTime;

        return new AudioRingBufferSpec((nint)specRaw);
    }

    /// <summary>Measures where a field of a mirror sits.</summary>
    /// <param name="start">The address of the mirror.</param>
    /// <param name="field">The address of the field.</param>
    /// <returns>The offset of the field, in bytes.</returns>
    private static unsafe long Offset(void* start, void* field) => (byte*)field - (byte*)start;
}
