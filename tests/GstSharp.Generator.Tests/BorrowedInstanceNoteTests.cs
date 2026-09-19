using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The note an adopt in place member on a mini object closes its remarks with.
/// </summary>
/// <remarks>
/// The note is prose the renderer owns, and it is a promise about C code: a
/// base transform running in passthrough calls <c>transform_ip</c> on a buffer
/// it did not make writable, so the note may not claim that what an in place
/// vfunc receives is writable by construction, and it has to name the escape
/// hatch. The escape hatch is <c>Copy</c> rather than <c>MakeWritable()</c>,
/// because the note sits on <c>MakeWritable()</c> itself, which is the call
/// that refuses on a borrowed wrapper. It is named without parentheses because
/// the carriers do not agree on a signature: <c>Gst.Memory.Copy</c> takes an
/// offset and a size, while the other nine take nothing. Nothing pinned that
/// wording before, so
/// it drifted away from <c>docs/ownership.md</c>; these tests hold the two
/// together.
/// </remarks>
public sealed class BorrowedInstanceNoteTests
{
    /// <summary>
    /// The note as it is rendered into a documentation comment, wrapped the way
    /// the renderer wraps it.
    /// </summary>
    private const string Note =
        "    /// <para>\n"
        + "    /// A wrapper that borrows the object for the length of one call has no reference\n"
        + "    /// to give and refuses instead. What is lent is writable only where whoever lends\n"
        + "    /// it holds the only reference: an in place vfunc lends the object of its caller\n"
        + "    /// and normally promises exactly that, a base transform running in passthrough\n"
        + "    /// being the exception, as it calls <c>transform_ip</c> on a buffer it did not make\n"
        + "    /// writable; <c>Copy</c> the object to get one that is yours to write.\n"
        + "    /// </para>\n";

    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Fact]
    public void TheNoteNamesThePassthroughExceptionAndTheCopy()
    {
        Assert.Contains(Note, Source("GstSharp.Net/Generated/Caps.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoteDoesNotSendTheReaderBackToMakeWritable()
    {
        // The member the note is written onto is MakeWritable itself, so
        // advising the call would be circular: on a borrowed wrapper it is the
        // one call that raises.
        Assert.DoesNotContain("MakeWritable()</c> the object", Note, StringComparison.Ordinal);
        Assert.Contains("<c>Copy</c>", Note, StringComparison.Ordinal);
        Assert.DoesNotContain("<c>Copy()</c>", Note, StringComparison.Ordinal);
    }

    [Fact]
    public void NoGeneratedFileClaimsABorrowedObjectIsWritableAlready()
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.DoesNotContain("is writable already", file.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryMiniObjectThatInlinesMakeWritableCarriesTheNote()
    {
        // The nine mini objects whose make_writable the renderer inlines, plus
        // the one video record bound through the same path. Gst.Buffer is not
        // among them: its MakeWritable is hand written.
        string[] expected =
        [
            "GstSharp.Net/Generated/BufferList.cs",
            "GstSharp.Net/Generated/Caps.cs",
            "GstSharp.Net/Generated/Context.cs",
            "GstSharp.Net/Generated/Event.cs",
            "GstSharp.Net/Generated/Memory.cs",
            "GstSharp.Net/Generated/Message.cs",
            "GstSharp.Net/Generated/Query.cs",
            "GstSharp.Net/Generated/Sample.cs",
            "GstSharp.Net/Generated/TagList.cs",
            "GstSharp.Net.Video/Generated/VideoOverlayComposition.cs",
        ];

        List<string> carriers = [];
        foreach (GeneratedFile file in Generated.Files)
        {
            if (file.Content.Contains(Note, StringComparison.Ordinal))
            {
                carriers.Add(file.RelativePath);
            }
        }

        carriers.Sort(StringComparer.Ordinal);
        Assert.Equal(expected.Order(StringComparer.Ordinal), carriers);
    }

    private static string Source(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no '" + path + "'.");
    }
}
