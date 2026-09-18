using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>docStrip</c> overlay, which takes a substring out of the gir
/// documentation of a callable before it is split into paragraphs.
/// </summary>
/// <remarks>
/// It is the one overlay that removes upstream text rather than adding to it,
/// so what holds it honest is the requirement that the substring stand in the
/// documentation exactly once: an upstream rewording then reports itself
/// instead of stripping nothing, and a key nothing read reports itself as well.
/// </remarks>
public sealed class DocStripTests
{
    /// <summary>
    /// One method whose documentation carries a sentence of its own in a
    /// paragraph of its own, and one whose documentation carries a sentence in
    /// the middle of a paragraph: the two positions a removal leaves different
    /// whitespace behind in.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack" c:identifier="gst_widget_pack">
                <doc xml:space="preserve">Packs the @label into the @widget.

        Ownership is taken of @label.</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="label" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="unpack" c:identifier="gst_widget_unpack">
                <doc xml:space="preserve">Unpacks the @widget. Ownership is taken of nothing. The widget stays packable.</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
        """;

    [Fact]
    public void ASentenceThatIsAParagraphOfItsOwnLeavesNoParagraphBehind()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_pack": ["\n\nOwnership is taken of @label."]
              }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.DoesNotContain("Ownership is taken of @label.", source, StringComparison.Ordinal);

        // What stood above it is untouched, and the paragraph the removal
        // emptied is gone rather than written out blank.
        Assert.Contains("Packs the @label into the @widget.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/// <para></para>", source, StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0052" or "GEN0053");
    }

    [Fact]
    public void ASentenceInsideAParagraphLeavesOneSpaceBehind()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_unpack": [" Ownership is taken of nothing."]
              }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.DoesNotContain("Ownership is taken of nothing.", source, StringComparison.Ordinal);
        Assert.Contains(
            "Unpacks the @widget. The widget stays packable.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASubstringThatStandsNowhereIsReportedAndNothingIsRemoved()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_pack": ["Ownership is taken of the @label."]
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0052", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));

        // The documentation is rendered as the gir carries it: a rewording that
        // is reported is not also half applied.
        Assert.Contains("Ownership is taken of @label.", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASubstringThatStandsTwiceIsReportedAndNothingIsRemoved()
    {
        // "Ownership is taken of" stands in both sentences of the fixture, so
        // an entry that names it could not say which one it means.
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_unpack": ["Unpacks the @widget."],
                "gst_widget_pack": ["@label"]
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0052", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal)
                && diagnostic.Message.Contains("more than once", StringComparison.Ordinal));

        Assert.Contains("Ownership is taken of @label.", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OneSubstringOfAnEntryStandingNowhereLeavesTheOthersRemoved()
    {
        // The entry is a list, and each substring is judged on its own: the
        // one that still stands is taken out, and the one an upstream
        // rewording moved is reported rather than silently forgiven.
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_pack": [
                  "\n\nOwnership is taken of @label.",
                  "The widget stays unpacked."
                ]
              }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.DoesNotContain("Ownership is taken of @label.", source, StringComparison.Ordinal);
        Assert.Contains("Packs the @label into the @widget.", source, StringComparison.Ordinal);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0052", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));

        // The key was read, so it is not also reported as naming nothing.
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0053", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryThatNamesNoCallableIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "docStrip": {
                "gst_widget_repack": ["Ownership is taken of @label."]
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0053", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_repack", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryOnASkippedCallableIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": ["gst_widget_pack"],
              "docStrip": {
                "gst_widget_pack": ["\n\nOwnership is taken of @label."]
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0053", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryWithoutASubstringIsRefusedWhileTheOverlaysLoad()
    {
        // An entry that takes nothing out is consumed by the very member it
        // leaves unchanged, so the stale key report cannot catch it.
        Assert.Contains("gst_widget_pack", Refused("""{ "docStrip": { "gst_widget_pack": [] } }"""), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithABlankSubstringIsRefusedWhileTheOverlaysLoad()
    {
        // A blank substring stands in every documentation there is, and taking
        // it out changes nothing that a reader would see.
        Assert.Contains(
            "gst_widget_pack",
            Refused("""{ "docStrip": { "gst_widget_pack": ["  "] } }"""),
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

    private static FixtureRun RunWithOverlay(string fixups, string? body = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body ?? Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
