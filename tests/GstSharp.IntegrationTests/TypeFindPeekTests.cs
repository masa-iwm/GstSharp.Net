using Gst;
using Gst.Base;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Exercises <see cref="TypeFind.TryPeek(long, uint, out ReadOnlySpan{byte})"/>
/// from where it is meant to be called: inside a typefind function that
/// GStreamer itself is running.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_type_find_peek</c> is on the skip list of
/// <c>girs/overlays/fixups.json</c> — its gir carries no length for the pointer
/// it returns, which is <c>size</c> bytes long — so <c>TryPeek</c> is the only
/// way a typefinder on this binding can read the stream it is asked to
/// identify. What is measured here is that the borrowed pointer is read with
/// the right length, and that a range the reader cannot serve comes back as
/// <see langword="false"/> rather than as an exception or as a span over
/// nothing.
/// </para>
/// <para>
/// The typefinder is driven by <c>gst_type_find_helper_for_data</c>, which
/// walks the factories of the registry over a block of bytes the test owns.
/// That is the shortest path to a real <c>GstTypeFind</c>: no pipeline, no
/// temporary file, and the data the peeks are compared against is the data that
/// was handed in.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TypeFindPeekTests
{
    /// <summary>
    /// The bytes the probe typefinder is run over: the magic it looks for,
    /// followed by a filler that the second peek reads.
    /// </summary>
    private static readonly byte[] Payload = BuildPayload();

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TypeFindPeekTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.Base.GstBase: the helpers below live in GstBase, and its
        // module initialiser is what puts its types into the registry.
        GstBase.Initialize();

        Probe.EnsureRegistered();
    }

    /// <summary>
    /// A peek of a range the reader holds hands back exactly those bytes, and
    /// the typefinder that read them is the one that identifies the data.
    /// </summary>
    [Fact]
    public void APeekInsideTheDataReadsTheBytesTheTypefinderWasGiven()
    {
        PeekRecord record = new();
        Caps? found;
        TypeFindProbability probability;

        Probe.Current = record;
        try
        {
            found = BaseGlobal.TypeFindHelperForData(null, Payload, out probability);
        }
        finally
        {
            Probe.Current = null;
        }

        using (found)
        {
            // A typefinder that never ran would leave every field below at its
            // default and assert nothing at all.
            Assert.True(record.Calls > 0, "The probe typefinder was never called over the data.");
            _output.WriteLine(FormattableString.Invariant(
                $"probe: calls={record.Calls}, head={record.Head?.Length}, filler={record.Filler?.Length}"));

            Assert.True(record.HeadPeeked);
            Assert.Equal(Probe.Magic, record.Head);

            // A second peek, at an offset and with a length of its own: the
            // span is the size that was asked for and starts where it was asked
            // to start.
            Assert.True(record.FillerPeeked);
            Assert.Equal(Payload[Probe.Magic.Length..(Probe.Magic.Length + 4)], record.Filler);

            // The peeks are what let the typefinder recognise the data, so the
            // caps that come back are the proof that it read the right bytes
            // through the whole round trip.
            Assert.NotNull(found);
            Assert.Equal(TypeFindProbability.Maximum, probability);
            Assert.Contains(Probe.MediaType, found.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A peek of a range the reader cannot serve is refused: that is how a
    /// typefinder finds the end of the data, so it is a <see langword="false"/>
    /// and an empty span rather than an exception.
    /// </summary>
    [Fact]
    public void APeekBeyondTheDataIsRefusedWithAnEmptySpan()
    {
        PeekRecord record = new();

        Probe.Current = record;
        try
        {
            using Caps? found = BaseGlobal.TypeFindHelperForData(null, Payload, out _);
        }
        finally
        {
            Probe.Current = null;
        }

        Assert.True(record.Calls > 0, "The probe typefinder was never called over the data.");

        // Far past the end of a block of 64 bytes.
        Assert.False(record.FarPeeked);
        Assert.Equal(0, record.FarLength);

        // And one byte past it, which is the range a typefinder actually walks
        // into when it reads until a peek fails.
        Assert.False(record.PastEndPeeked);
        Assert.Equal(0, record.PastEndLength);

        // The refusals leave the reader usable: the peek that follows them
        // still reads the head of the data.
        Assert.True(record.HeadPeeked);
        Assert.Equal(Probe.Magic, record.Head);
    }

    /// <summary>
    /// Builds the block the typefinder is run over.
    /// </summary>
    /// <returns>The magic, followed by 56 bytes of filler.</returns>
    private static byte[] BuildPayload()
    {
        byte[] payload = new byte[64];
        Probe.Magic.CopyTo(payload, 0);

        for (int index = Probe.Magic.Length; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 7);
        }

        return payload;
    }

    /// <summary>
    /// What one run of the probe typefinder saw. The spans it peeked cannot be
    /// stored — they are borrowed for the length of the call — so what is kept
    /// is a copy of the bytes and the answer of each call.
    /// </summary>
    private sealed class PeekRecord
    {
        /// <summary>Gets or sets how often the typefinder ran.</summary>
        public int Calls { get; set; }

        /// <summary>Gets or sets whether the peek of the magic succeeded.</summary>
        public bool HeadPeeked { get; set; }

        /// <summary>Gets or sets the bytes that peek read.</summary>
        public byte[]? Head { get; set; }

        /// <summary>Gets or sets whether the peek of the filler succeeded.</summary>
        public bool FillerPeeked { get; set; }

        /// <summary>Gets or sets the bytes that peek read.</summary>
        public byte[]? Filler { get; set; }

        /// <summary>Gets or sets whether the peek far past the end succeeded.</summary>
        public bool FarPeeked { get; set; }

        /// <summary>Gets or sets the length of the span that peek handed out.</summary>
        public int FarLength { get; set; } = -1;

        /// <summary>
        /// Gets or sets whether the peek of one byte more than there is
        /// succeeded.
        /// </summary>
        public bool PastEndPeeked { get; set; }

        /// <summary>Gets or sets the length of the span that peek handed out.</summary>
        public int PastEndLength { get; set; } = -1;
    }

    /// <summary>
    /// The typefinder of this suite. It is registered into the process wide
    /// registry once and reports what its peeks saw into
    /// <see cref="Current"/>.
    /// </summary>
    /// <remarks>
    /// <c>gst_type_find_register</c> with no plugin adds a factory to the
    /// default registry for the rest of the process, and nothing takes it back
    /// out again. The typefinder therefore has to be harmless to every later
    /// test: it records only while a test has put a record in place, and it only
    /// ever suggests caps for data that starts with a magic no real format uses.
    /// </remarks>
    private static class Probe
    {
        /// <summary>The name the factory is registered under.</summary>
        internal const string Name = "gstsharp-typefind-peek-probe";

        /// <summary>The media type the typefinder suggests.</summary>
        internal const string MediaType = "application/x-gstsharp-typefind-peek";

        /// <summary>The bytes the typefinder recognises.</summary>
        internal static readonly byte[] Magic = [0xAB, 0x1F, 0x7C, 0x03, 0xD5, 0x42, 0x9E, 0x60];

        private static readonly object Gate = new();

        private static bool _registered;

        /// <summary>
        /// Gets or sets the record of the test that is currently driving the
        /// typefinder, or <see langword="null"/> when none is.
        /// </summary>
        internal static PeekRecord? Current { get; set; }

        /// <summary>
        /// Registers the typefinder, once for the whole process.
        /// </summary>
        internal static void EnsureRegistered()
        {
            lock (Gate)
            {
                if (_registered)
                {
                    return;
                }

                using Caps possible = Caps.NewEmptySimple(MediaType);

                // Above every typefinder the installation ships, so that the
                // helper reaches this one before another factory can claim the
                // data with the maximum probability and end the walk early.
                // gst_type_find_register takes no ownership of the caps.
                Assert.True(TypeFind.Register(null, Name, (uint)Rank.Primary + 100u, Run, null, possible));

                _registered = true;
            }
        }

        /// <summary>
        /// The typefind function itself: everything this suite measures happens
        /// here, because a <c>GstTypeFind</c> only exists for the length of this
        /// call.
        /// </summary>
        /// <param name="find">The reader of the stream being identified.</param>
        private static void Run(TypeFind find)
        {
            PeekRecord? record = Current;

            // The refused ranges are asked for first, so that the peek of the
            // magic below also shows that a refusal leaves the reader usable.
            bool farPeeked = find.TryPeek(1_000_000_000, 4096, out ReadOnlySpan<byte> far);
            bool pastEndPeeked = find.TryPeek(0, (uint)Payload.Length + 1, out ReadOnlySpan<byte> pastEnd);

            if (!find.TryPeek(0, (uint)Magic.Length, out ReadOnlySpan<byte> head))
            {
                // Every other typefinding of the test run ends up here: this
                // typefinder is in the registry for good and sees data that is
                // shorter than the magic as well.
                if (record is not null)
                {
                    record.Calls++;
                    record.HeadPeeked = false;
                }

                return;
            }

            // The span is borrowed for the length of this call, so what leaves
            // it is a copy.
            bool isMagic = head.SequenceEqual(Magic);

            if (record is not null)
            {
                record.Calls++;

                record.FarPeeked = farPeeked;
                record.FarLength = far.Length;
                record.PastEndPeeked = pastEndPeeked;
                record.PastEndLength = pastEnd.Length;

                record.HeadPeeked = true;
                record.Head = head.ToArray();

                record.FillerPeeked = find.TryPeek(Magic.Length, 4, out ReadOnlySpan<byte> filler);
                record.Filler = record.FillerPeeked ? filler.ToArray() : null;
            }

            if (!isMagic)
            {
                return;
            }

            using Caps caps = Caps.NewEmptySimple(MediaType);
            find.Suggest((uint)TypeFindProbability.Maximum, caps);
        }
    }
}
