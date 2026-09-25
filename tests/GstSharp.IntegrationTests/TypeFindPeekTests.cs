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
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TypeFindPeekTests(ITestOutputHelper output)
    {
        _output = output;

        // See Gst.Base.GstBase: the helpers below live in GstBase, and its
        // module initialiser is what puts its types into the registry.
        GstBase.Initialize();

        TypeFindProbe.EnsureRegistered();
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

        TypeFindProbe.Current = record;
        try
        {
            found = BaseGlobal.TypeFindHelperForData(null, TypeFindProbe.Payload, out probability);
        }
        finally
        {
            TypeFindProbe.Current = null;
        }

        using (found)
        {
            // A typefinder that never ran would leave every field below at its
            // default and assert nothing at all.
            Assert.True(record.Calls > 0, "The probe typefinder was never called over the data.");
            _output.WriteLine(FormattableString.Invariant(
                $"probe: calls={record.Calls}, head={record.Head?.Length}, filler={record.Filler?.Length}"));

            Assert.True(record.HeadPeeked);
            Assert.Equal(TypeFindProbe.Magic, record.Head);

            // A second peek, at an offset and with a length of its own: the
            // span is the size that was asked for and starts where it was asked
            // to start.
            Assert.True(record.FillerPeeked);
            Assert.Equal(TypeFindProbe.Payload[TypeFindProbe.Magic.Length..(TypeFindProbe.Magic.Length + 4)], record.Filler);

            // The peeks are what let the typefinder recognise the data, so the
            // caps that come back are the proof that it read the right bytes
            // through the whole round trip.
            Assert.NotNull(found);
            Assert.Equal(TypeFindProbability.Maximum, probability);
            Assert.Contains(TypeFindProbe.MediaType, found.ToString(), StringComparison.Ordinal);
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

        TypeFindProbe.Current = record;
        try
        {
            using Caps? found = BaseGlobal.TypeFindHelperForData(null, TypeFindProbe.Payload, out _);
        }
        finally
        {
            TypeFindProbe.Current = null;
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
        Assert.Equal(TypeFindProbe.Magic, record.Head);
    }
}
