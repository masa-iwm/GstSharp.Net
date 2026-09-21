using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>typeDocReplace</c> overlay, which writes part of the gir
/// documentation of a class differently before it is rendered.
/// </summary>
/// <remarks>
/// It is the counterpart of <c>docStrip</c> one level up, for the upstream
/// sentence that documents a string grammar the library itself does not write
/// that way. What holds it honest is that every failure is an error: a
/// substring that no longer stands in the documentation exactly once, and a key
/// that named no rendered class, both stop the run rather than leaving the
/// upstream sentence in the generated text with nothing to say so.
/// </remarks>
public sealed class TypeDocReplaceTests
{
    /// <summary>
    /// One class whose documentation carries a written out identifier once, and
    /// one that carries the same text twice: the two occurrences a replacement
    /// has to tell apart.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <doc xml:space="preserve">A widget.

        Its identifier is written "audio bin || video bin".</doc>
            </class>
            <class name="Gadget" c:type="GstGadget" parent="GObject.Object" glib:type-name="GstGadget" glib:get-type="gst_gadget_get_type">
              <doc xml:space="preserve">A gadget.

        Its identifier is written "audio bin || video bin", and the identifier of
        its parts is written "audio bin || video bin" as well.</doc>
            </class>
        """;

    [Fact]
    public void AReplacedSubstringStandsAsTheEntryWroteIt()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "typeDocReplace": {
                "Gst.Widget": [{ "old": "|| video", "new": "||video" }]
              }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("\"audio bin ||video bin\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("|| video", source, StringComparison.Ordinal);

        // What stood around it is untouched: the entry replaces a substring,
        // not the paragraph it stands in.
        Assert.Contains("A widget.", source, StringComparison.Ordinal);
        Assert.Contains("Its identifier is written", source, StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0064", StringComparison.Ordinal));
    }

    [Fact]
    public void ASubstringThatStandsNowhereIsReported()
    {
        // The gir refresh that corrected the sentence upstream: the key is still
        // read, so no stale report follows it, and this is what says so.
        FixtureRun run = RunWithOverlay(
            """
            {
              "typeDocReplace": {
                "Gst.Widget": [{ "old": "|| audio", "new": "||audio" }]
              }
            }
            """);

        Diagnostic error = Assert.Single(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0064", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget", error.Message, StringComparison.Ordinal);
        Assert.Contains("nowhere", error.Message, StringComparison.Ordinal);

        // And the documentation is rendered as the gir carries it, rather than
        // half rewritten.
        Assert.Contains("\"audio bin || video bin\"", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASubstringThatStandsTwiceIsReported()
    {
        // An upstream rewording that repeated the sentence would otherwise leave
        // one of the two occurrences reading the way the entry exists to stop it
        // reading.
        FixtureRun run = RunWithOverlay(
            """
            {
              "typeDocReplace": {
                "Gst.Gadget": [{ "old": "|| video", "new": "||video" }]
              }
            }
            """);

        Diagnostic error = Assert.Single(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0064", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Gadget", error.Message, StringComparison.Ordinal);
        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("||video", run.File("Gadget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatNamesNoRenderedClassIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "typeDocReplace": {
                "Gst.Doohickey": [{ "old": "|| video", "new": "||video" }]
              }
            }
            """);

        Diagnostic error = Assert.Single(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0064", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Doohickey", error.Message, StringComparison.Ordinal);
        Assert.Contains("stale", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithoutAReplacementIsRefusedWhileTheOverlaysLoad()
    {
        // An entry that replaces nothing is consumed by the very class it
        // leaves unchanged, so the stale key report cannot catch it.
        Assert.Contains(
            "Gst.Widget",
            Refused("""{ "typeDocReplace": { "Gst.Widget": [] } }"""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithABlankOldIsRefusedWhileTheOverlaysLoad()
    {
        // A blank substring stands in every documentation there is, and the
        // first position of it is no statement about the text at all.
        Assert.Contains(
            "Gst.Widget",
            Refused("""{ "typeDocReplace": { "Gst.Widget": [{ "old": "  ", "new": "x" }] } }"""),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatWritesItsOwnTextBackIsRefusedWhileTheOverlaysLoad()
    {
        Assert.Contains(
            "Gst.Widget",
            Refused("""{ "typeDocReplace": { "Gst.Widget": [{ "old": "video", "new": "video" }] } }"""),
            StringComparison.Ordinal);
    }

    /// <summary>Loads an overlay file that the load has to refuse.</summary>
    /// <param name="fixups">The overlay text.</param>
    /// <returns>The message of the refusal.</returns>
    private static string Refused(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Assert.Throws<InvalidDataException>(() => Overlays.Load(directory)).Message;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FixtureRun RunWithOverlay(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);

            // Every failure of this overlay is an error, and three of the tests
            // below are about one, so the run has to reach the test rather than
            // fail inside the fixture.
            return Fixture.Run(Body, Overlays.Load(directory), allowErrors: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
