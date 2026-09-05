using System.Buffers.Binary;
using Gst;
using Gst.GLib;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>Discoverer.TryDiscoverUri</c>, the non throwing face of
/// <c>gst_discoverer_discover_uri</c>: the C sets its error and returns its
/// information object independently of one another, and this overload hands
/// both out.
/// </summary>
/// <remarks>
/// The WAV of the successful half is built here rather than shared: the one
/// other test that writes one keeps its builder private, and a fixture file
/// would have to be committed.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DiscovererTryDiscoverUriTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public DiscovererTryDiscoverUriTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A file that does not exist fails the discovery, and both halves of the
    /// answer arrive: the information object that says what the result was and
    /// the error that says why.
    /// </summary>
    [Fact]
    public void ADiscoveryThatFailsAnswersBothTheInfoAndTheError()
    {
        GstPbutils.Initialize();

        using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(10));

        using DiscovererInfo? info = discoverer.TryDiscoverUri(
            "file:///gstsharp/does-not-exist.mkv",
            out GException? error);

        Assert.NotNull(info);
        Assert.NotNull(error);

        _output.WriteLine(FormattableString.Invariant(
            $"result={info.GetResult()} domain={error.Domain} code={error.Code} message={error.Message}"));

        Assert.NotEqual(DiscovererResult.Ok, info.GetResult());
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>
    /// A file the installation can read is discovered without an error at all.
    /// </summary>
    [RequiresElementFact("wavparse")]
    public void ADiscoveryThatSucceedsAnswersNoError()
    {
        GstPbutils.Initialize();

        string path = Path.Combine(
            Path.GetTempPath(),
            FormattableString.Invariant($"gstsharp-try-discover-{Guid.NewGuid():N}.wav"));

        File.WriteAllBytes(path, SilentWave(sampleCount: 8000));
        try
        {
            using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(10));

            using DiscovererInfo? info = discoverer.TryDiscoverUri(
                new System.Uri(path).AbsoluteUri,
                out GException? error);

            Assert.NotNull(info);
            Assert.Null(error);
            Assert.Equal(DiscovererResult.Ok, info.GetResult());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Builds a silent mono WAV file of the given length.</summary>
    /// <param name="sampleCount">How many samples the file carries.</param>
    /// <returns>The bytes of the file.</returns>
    private static byte[] SilentWave(int sampleCount)
    {
        const int SampleRate = 8000;
        const int BitsPerSample = 16;
        const int Channels = 1;

        int dataBytes = sampleCount * Channels * (BitsPerSample / 8);
        byte[] file = new byte[44 + dataBytes];
        Span<byte> span = file;

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * Channels * (BitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], Channels * (BitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        return file;
    }
}
