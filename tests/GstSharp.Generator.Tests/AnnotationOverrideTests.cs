using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The reporting of <c>annotationOverrides</c> entries that describe a gir
/// which has moved on.
/// </summary>
/// <remarks>
/// An annotation correction states a fact about a C function, and a key that
/// matches nothing states it about a function this run never saw. Leaving that
/// silent is what lets the overlays accumulate corrections of symbols that were
/// renamed or removed, which is the same failure the array corrections are
/// already protected from by GEN0020.
/// </remarks>
public sealed class AnnotationOverrideTests
{
    /// <summary>
    /// One method with one parameter, which is enough for a key to match or to
    /// miss on the callable, on a parameter and on the return value.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack" c:identifier="gst_widget_pack">
                <return-value transfer-ownership="none">
                  <type name="utf8" c:type="gchar*"/>
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
            </class>
        """;

    [Fact]
    public void AnEntryThatNamesNoSymbolIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_unpack#label": { "nullable": true } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "The annotation override 'gst_widget_unpack#label' matched no callable, parameter or signal argument",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryThatNamesNoParameterIsReportedAsStale()
    {
        // The callable exists and the parameter does not, which is the shape a
        // renamed argument leaves behind.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_pack#caption": { "nullable": true } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack#caption", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAppliedEntryIsNotReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "gst_widget_pack#label": { "nullable": true },
                "gst_widget_pack#return": { "nullable": true }
              }
            }
            """);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal));
    }

    private static FixtureRun RunWithOverlay(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
