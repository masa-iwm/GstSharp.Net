using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The reporting of <c>annotationOverrides</c> entries that describe a gir
/// which has moved on.
/// </summary>
/// <remarks>
/// <para>
/// An annotation correction states a fact about a C function, and a key that
/// matches nothing states it about a function this run never saw. Leaving that
/// silent is what lets the overlays accumulate corrections of symbols that were
/// renamed or removed, which is the same failure the array corrections are
/// already protected from by GEN0020.
/// </para>
/// <para>
/// The other half of the same promise is a key that matches and states
/// something the path it lands on does not read. An argument of a callback and
/// an argument of a signal are planned inbound by construction and take
/// nothing but <c>nullable</c>, so a direction, an array size or a scope on one
/// of those keys is reported as GEN0017 rather than dropped.
/// </para>
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

    /// <summary>
    /// The two inbound paths beside the method: a callback type that a method
    /// hands over, and a signal of the same class. Each carries one argument a
    /// key can name.
    /// </summary>
    private const string InboundBody =
        """
            <callback name="LabelFunc" c:type="GstLabelFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="labelled" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="label" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="LabelFunc" c:type="GstLabelFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    [Fact]
    public void ADirectionOnACallbackParameterIsReportedAsIgnored()
    {
        // A callback argument is inbound by construction: the trampoline is
        // handed the value the C caller passes, and there is no out projection
        // on that path for a correction to reach. Saying so is the difference
        // between a correction that was weighed and one that was dropped.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "GstLabelFunc#widget": { "direction": "out" } }
            }
            """,
            InboundBody);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal)
                && diagnostic.Message.Contains("GstLabelFunc#widget", StringComparison.Ordinal)
                && diagnostic.Message.Contains("a callback parameter", StringComparison.Ordinal));

        // The key matched, so it is not stale as well.
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal));
    }

    [Fact]
    public void AScopeOnASignalParameterIsReportedAsIgnored()
    {
        // The same rule on the signal path, with the field that has the least
        // to do with a signal argument of all of them.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Widget::labelled#label": { "scope": "forever" } }
            }
            """,
            InboundBody);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Widget::labelled#label", StringComparison.Ordinal)
                && diagnostic.Message.Contains("a signal parameter", StringComparison.Ordinal));
    }

    [Fact]
    public void ANullableOnACallbackParameterIsHonouredAndSilent()
    {
        // The four nullable callback entries of the shipped overlays are this
        // shape, so the report must not touch it: the delegate takes the
        // nullable argument and the run stays free of GEN0017.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "GstLabelFunc#widget": { "nullable": true } }
            }
            """,
            InboundBody);

        Assert.Contains(
            "delegate void LabelFunc(Gst.Widget? widget)",
            run.File("Callbacks.cs"),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal));

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal));
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
