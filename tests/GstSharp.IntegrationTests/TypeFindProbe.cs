using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The typefinder the typefind suites run the helpers of GstBase over. It is
/// registered into the process wide registry once and reports what its peeks
/// saw into <see cref="Current"/>.
/// </summary>
/// <remarks>
/// <c>gst_type_find_register</c> with no plugin adds a factory to the default
/// registry for the rest of the process, and nothing takes it back out again.
/// The typefinder therefore has to be harmless to every later test: it records
/// only while a test has put a record in place, and it only ever suggests caps
/// for data that starts with a magic no real format uses.
/// </remarks>
internal static class TypeFindProbe
{
    /// <summary>The name the factory is registered under.</summary>
    internal const string Name = "gstsharp-typefind-peek-probe";

    /// <summary>The media type the typefinder suggests.</summary>
    internal const string MediaType = "application/x-gstsharp-typefind-peek";

    /// <summary>The bytes the typefinder recognises.</summary>
    internal static readonly byte[] Magic = [0xAB, 0x1F, 0x7C, 0x03, 0xD5, 0x42, 0x9E, 0x60];

    /// <summary>
    /// The bytes the probe typefinder is run over: the magic it looks for,
    /// followed by a filler that the second peek reads.
    /// </summary>
    internal static readonly byte[] Payload = BuildPayload();

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
    /// Builds the block the typefinder is run over.
    /// </summary>
    /// <returns>The magic, followed by 56 bytes of filler.</returns>
    private static byte[] BuildPayload()
    {
        byte[] payload = new byte[64];
        Magic.CopyTo(payload, 0);

        for (int index = Magic.Length; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 7);
        }

        return payload;
    }

    /// <summary>
    /// The typefind function itself: everything the suites measure happens
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

/// <summary>
/// What one run of the probe typefinder saw. The spans it peeked cannot be
/// stored — they are borrowed for the length of the call — so what is kept is
/// a copy of the bytes and the answer of each call.
/// </summary>
internal sealed class PeekRecord
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
