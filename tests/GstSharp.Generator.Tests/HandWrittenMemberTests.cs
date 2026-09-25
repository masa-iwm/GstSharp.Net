using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>handWrittenMembers</c> overlay, which tells the hiding rules about a
/// member the <c>Custom/</c> partial of a class declares by hand.
/// </summary>
/// <remarks>
/// The generator never reads <c>Custom/</c>, so a generated member of a
/// descendant with the name of a hand written one is otherwise emitted without
/// <c>new</c>, and the build fails on CS0108. <c>GstBaseSink</c> is the case the
/// overlay exists for: <c>BaseSink.CanActivatePull</c> is written by hand, and
/// <c>GstAudioBaseSink</c> installs a <c>can-activate-pull</c> property with no
/// C accessor, which is emitted as a value backed property of the same name.
/// </remarks>
public sealed class HandWrittenMemberTests
{
    /// <summary>
    /// A base class and a descendant that installs a property with no C
    /// accessor, which is what takes the value backed path.
    /// </summary>
    private const string Body =
        """
            <class name="Sink" c:type="GstSink" parent="GObject.Object" glib:type-name="GstSink" glib:get-type="gst_sink_get_type">
            </class>
            <class name="PullSink" c:type="GstPullSink" parent="Sink" glib:type-name="GstPullSink" glib:get-type="gst_pull_sink_get_type">
              <property name="can-activate-pull" writable="1" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
            </class>
        """;

    [Fact]
    public void AValueBackedPropertyOverAHandWrittenMemberIsEmittedWithNew()
    {
        FixtureRun run = RunWithOverlay("""{ "handWrittenMembers": { "GstSink": ["CanActivatePull"] } }""");

        string source = run.File("PullSink.cs");
        Assert.Contains("public new bool CanActivatePull\n", source, StringComparison.Ordinal);

        // It is emitted rather than dropped the way it is over a generated member.
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void WithoutTheEntryThePropertyCarriesNoNew()
    {
        FixtureRun run = RunWithOverlay("""{ }""");

        string source = run.File("PullSink.cs");
        Assert.Contains("public bool CanActivatePull\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new bool CanActivatePull", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatNamesNoRenderedClassIsReported()
    {
        FixtureRun run = RunWithOverlay("""{ "handWrittenMembers": { "GstNowhere": ["CanActivatePull"] } }""");

        Diagnostic error = Assert.Single(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0065", StringComparison.Ordinal));

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("GstNowhere", error.Message, StringComparison.Ordinal);
        Assert.Contains("stale", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatNamesNoMemberIsRefusedWhileTheOverlaysLoad()
    {
        Assert.Contains(
            "GstSink",
            Refused("""{ "handWrittenMembers": { "GstSink": [] } }"""),
            StringComparison.Ordinal);
        Assert.Contains(
            "GstSink",
            Refused("""{ "handWrittenMembers": { "GstSink": [" "] } }"""),
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

            // The stale key test is about an error, so the run has to reach the
            // test rather than fail inside the fixture.
            return Fixture.Run(Body, Overlays.Load(directory), allowErrors: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
