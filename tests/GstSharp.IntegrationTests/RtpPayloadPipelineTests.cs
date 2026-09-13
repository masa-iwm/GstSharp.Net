using System.Diagnostics;
using Gst;
using Gst.App;
using Gst.Rtp;
using Xunit;
using Xunit.Abstractions;
using Buffer = Gst.Buffer;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The RTP module against packets a real payloader produced: the read side of
/// <see cref="RTPBuffer"/> over buffers this process did not build, and the
/// static payload table beside it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RTPBuffer"/> is a mapping, not a wrapper: it holds the map of
/// the buffer it was opened on and has to be unmapped again, so every mapping
/// below is released in a <c>finally</c>. Reading a field of an unmapped one
/// is a read of freed memory, which is why nothing here outlives its scope.
/// </para>
/// <para>
/// The packet count is a lower bound on purpose. A payloader is free to split
/// one input buffer across several packets to stay under its MTU, so the
/// contract that can be asserted is the one the receiver depends on: the
/// sequence numbers go up by exactly one per packet, whatever the count.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtpPayloadPipelineTests
{
    private const uint Ssrc = 305419896;
    private const ushort FirstSeqnum = 1000;

    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public RtpPayloadPipelineTests(ITestOutputHelper output)
    {
        _output = output;

        // The appsink of the pipeline is only resolved as an AppSink once the
        // module initialiser of GstSharp.Net.App has run; see Gst.App.GstApp.
        GstApp.Initialize();
    }

    /// <summary>
    /// Every packet a payloader emits carries the header the payloader was
    /// configured with, and the sequence numbers of the packets follow each
    /// other without a gap.
    /// </summary>
    [RequiresElementFact("rtpL16pay", "audiotestsrc", "audioconvert", "appsink")]
    public void APayloaderNumbersItsPacketsInSequence()
    {
        using Pipeline pipeline = Assert.IsAssignableFrom<Pipeline>(Global.ParseLaunch(
            "audiotestsrc num-buffers=5 samplesperbuffer=441 ! audioconvert ! " +
            $"rtpL16pay pt=96 ssrc={Ssrc} seqnum-offset={FirstSeqnum} ! appsink name=sink sync=false"));

        using Element? element = pipeline.GetByName("sink");
        AppSink sink = Assert.IsType<AppSink>(element);

        List<ushort> sequence = [];
        List<uint> timestamps = [];

        try
        {
            Assert.NotEqual(StateChangeReturn.Failure, pipeline.SetState(State.Playing));

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < RunTimeout)
            {
                using Sample? sample = sink.TryPullSample(ClockTime.FromMilliseconds(100));

                if (sample is null)
                {
                    if (sink.IsEos())
                    {
                        break;
                    }

                    continue;
                }

                using Buffer? buffer = sample.GetBuffer();
                Assert.NotNull(buffer);

                Assert.True(RTPBuffer.MapBuffer(buffer, MapFlags.Read, out RTPBuffer rtp));

                try
                {
                    Assert.Equal((byte)2, rtp.GetVersion());
                    Assert.Equal((byte)96, rtp.GetPayloadType());
                    Assert.Equal(Ssrc, rtp.GetSsrc());
                    Assert.True(rtp.GetPayloadLen() > 0, "a payloaded packet carries no payload.");

                    sequence.Add(rtp.GetSeq());
                    timestamps.Add(rtp.GetTimestamp());
                }
                finally
                {
                    rtp.Unmap();
                }
            }
        }
        finally
        {
            pipeline.SetState(State.Null);
        }

        _output.WriteLine($"{sequence.Count} packets, sequence {string.Join(", ", sequence)}");

        Assert.True(sequence.Count >= 5, $"only {sequence.Count} packets arrived.");
        Assert.Equal(FirstSeqnum, sequence[0]);

        for (int i = 1; i < sequence.Count; i++)
        {
            Assert.Equal((ushort)(sequence[i - 1] + 1), sequence[i]);
            Assert.True(
                timestamps[i] >= timestamps[i - 1],
                $"the RTP timestamp went backwards at packet {i}.");
        }
    }

    /// <summary>
    /// The static payload table answers by name and by number, and says so
    /// where it has nothing to say.
    /// </summary>
    /// <remarks>
    /// The last assertion documents an upstream quirk rather than endorsing
    /// it. The dynamic rows of the table all carry 255 in their payload type
    /// field as a marker (gstrtppayloads.c:88-176), and
    /// <c>gst_rtp_payload_info_for_pt</c> compares that field like any other
    /// number, so 255 answers the first of those rows instead of nothing. The
    /// overlays record the same thing on
    /// <c>gst_rtp_payload_info_for_pt</c> in <c>girs/overlays/fixups.json</c>.
    /// </remarks>
    [Fact]
    public void ThePayloadTableAnswersStaticTypesByNameAndNumber()
    {
        // gstrtppayloads.c:55, {10, "audio", "L16", 44100, "2", 1411200}.
        RTPPayloadInfo? stereo = RTPPayloadInfo.ForName("audio", "L16");
        Assert.NotNull(stereo);
        Assert.Equal((byte)10, stereo.Value.PayloadType);
        Assert.Equal(44100u, stereo.Value.ClockRate);
        Assert.Equal("2", stereo.Value.EncodingParameters);

        // gstrtppayloads.c:45, {0, "audio", "PCMU", 8000, "1", 64000}.
        RTPPayloadInfo? pcmu = RTPPayloadInfo.ForPt(0);
        Assert.NotNull(pcmu);
        Assert.Equal("PCMU", pcmu.Value.EncodingName);
        Assert.Equal(8000u, pcmu.Value.ClockRate);

        // 96 is the bottom of the dynamic range and stands in no row.
        Assert.Null(RTPPayloadInfo.ForPt(96));

        // The quirk: see the remarks of this test.
        RTPPayloadInfo? marker = RTPPayloadInfo.ForPt(255);
        Assert.NotNull(marker);
        Assert.Equal("parityfec", marker.Value.EncodingName);
    }
}
