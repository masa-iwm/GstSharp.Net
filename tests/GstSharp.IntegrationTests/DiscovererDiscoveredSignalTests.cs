using System.Buffers.Binary;
using System.Diagnostics;
using Gst;
using Gst.GLib;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstDiscoverer::discovered</c> signal. Its <c>GError</c> argument is
/// registered <c>G_SIGNAL_TYPE_STATIC_SCOPE</c>, so it is borrowed: the
/// handler is handed the very <c>GError</c> the discoverer keeps in its
/// private data and frees once the emission returns. The projection therefore
/// copies it inside the trampoline and frees nothing.
/// </summary>
/// <remarks>
/// <para>
/// The signal is emitted on the main context that was thread default when
/// <c>gst_discoverer_start</c> ran, not when the discoverer was constructed:
/// the start is what reads the thread default, attaches the bus watch to it
/// and keeps a reference (gstdiscoverer.c:2515-2526). Both tests therefore
/// push a context of their own before they call <c>Start()</c> and iterate it
/// afterwards. That keeps the emission on the test thread and needs no loop of
/// its own.
/// </para>
/// <para>
/// Every member here is 1.24 or older, so no availability gate is needed. The
/// successful discovery does need an element to parse the file it writes, and
/// gates on that instead.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class DiscovererDiscoveredSignalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public DiscovererDiscoveredSignalTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A URI nothing can open fails the discovery, and the handler is handed
    /// the error that failed it.
    /// </summary>
    [Fact]
    public void ADiscoveryThatFailsHandsTheHandlerTheError()
    {
        GstPbutils.Initialize();

        (bool discovered, GException? error, DiscovererResult result) =
            Discover("gstsharp-no-such-scheme:///nothing");

        Assert.True(discovered, "the discoverer never emitted its discovered signal");
        Assert.NotNull(error);

        _output.WriteLine(FormattableString.Invariant(
            $"result={result} domain={error.Domain} code={error.Code} message={error.Message}"));

        Assert.NotEqual(Quark.Zero, error.Domain);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    /// <summary>
    /// A file the installation can read is discovered without an error, and
    /// the handler sees <see langword="null"/> - the nullable annotation of
    /// the argument is the fact this half pins.
    /// </summary>
    [RequiresElementFact("wavparse")]
    public void ADiscoveryThatSucceedsHandsTheHandlerNoError()
    {
        GstPbutils.Initialize();

        string path = Path.Combine(
            Path.GetTempPath(),
            FormattableString.Invariant($"gstsharp-discover-{Guid.NewGuid():N}.wav"));

        File.WriteAllBytes(path, SilentWave(sampleCount: 8000));
        try
        {
            (bool discovered, GException? error, DiscovererResult result) =
                Discover(new System.Uri(path).AbsoluteUri);

            Assert.True(discovered, "the discoverer never emitted its discovered signal");
            _output.WriteLine(FormattableString.Invariant($"result={result} error={error?.Message ?? "<null>"}"));

            Assert.Equal(DiscovererResult.Ok, result);
            Assert.Null(error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Runs one asynchronous discovery on a context of the calling thread and
    /// reports what the handler saw.
    /// </summary>
    /// <param name="uri">The URI to discover.</param>
    /// <returns>Whether the signal arrived, the error it carried and the result.</returns>
    private static (bool Discovered, GException? Error, DiscovererResult Result) Discover(string uri)
    {
        bool discovered = false;
        GException? error = null;
        DiscovererResult result = DiscovererResult.Error;

        using MainContext context = MainContext.New();
        context.PushThreadDefault();
        try
        {
            using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(10));

            void OnDiscovered(object? sender, Discoverer.DiscoveredSignalArgs args)
            {
                discovered = true;
                error = args.Error;
                result = args.Info.GetResult();
            }

            discoverer.Discovered += OnDiscovered;
            try
            {
                // Start() is the call that has to run under the pushed
                // context: it takes the thread default and delivers the
                // emission on it.
                discoverer.Start();
                Assert.True(discoverer.DiscoverUriAsync(uri));

                Stopwatch clock = Stopwatch.StartNew();
                while (!discovered && clock.Elapsed < Patience)
                {
                    if (!context.Iteration(mayBlock: false))
                    {
                        Thread.Sleep(5);
                    }
                }

                discoverer.Stop();
            }
            finally
            {
                discoverer.Discovered -= OnDiscovered;
            }
        }
        finally
        {
            context.PopThreadDefault();
        }

        return (discovered, error, result);
    }

    /// <summary>
    /// Builds a mono 16 bit RIFF/WAVE file of silence, which is the cheapest
    /// media file an installation with the good plugins can describe.
    /// </summary>
    /// <param name="sampleCount">How many samples the data chunk holds.</param>
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
