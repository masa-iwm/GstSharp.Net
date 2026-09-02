using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The causes the field ledger reports in place of its catch all, and the
/// accessor of an enumeration that another module of the run declares.
/// </summary>
public sealed class FieldLedgerReasonTests
{
    /// <summary>
    /// A record whose mirror collapses, one whose enumeration the run does not
    /// emit, and one whose enumeration another module of the run does.
    /// </summary>
    private const string Body =
        """
            <enumeration name="Format" c:type="GstFormat">
              <member name="undefined" value="0" c:identifier="GST_FORMAT_UNDEFINED"/>
              <member name="bytes" value="2" c:identifier="GST_FORMAT_BYTES"/>
            </enumeration>
            <record name="Widget" c:type="GstWidget" opaque="1">
              <field name="width" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="shape" writable="1">
                <type name="GLib.Shape" c:type="GShape"/>
              </field>
            </record>
            <record name="Gadget" c:type="GstGadget" opaque="1">
              <field name="spec" writable="1">
                <type name="GObject.ParamSpec" c:type="GParamSpec"/>
              </field>
              <field name="height" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
        """;

    /// <summary>
    /// A second module of the run, whose enumeration a field of it names. The
    /// mirror keeps the integer, because the mirror is interop storage, and the
    /// accessor hands the enumeration out.
    /// </summary>
    private const string ExtraNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <enumeration name="Shape" c:type="GShape">
              <member name="round" value="0" c:identifier="G_SHAPE_ROUND"/>
            </enumeration>
          </namespace>
          <namespace name="GstVideo" version="1.0" c:identifier-prefixes="GstVideo" c:symbol-prefixes="gst_video">
            <record name="Frame" c:type="GstVideoFrame" opaque="1">
              <field name="format" writable="1">
                <type name="Gst.Format" c:type="GstFormat"/>
              </field>
            </record>
          </namespace>
        """;

    [Fact]
    public void AnEnumerationOfAnotherGeneratedModuleIsHandedOutTyped()
    {
        FixtureRun run = Fixture.Run(Body, extraNamespaces: ExtraNamespace);
        string source = run.File("Frame.cs", "GstSharp.Net.Video");

        Assert.Contains("public Gst.Format Format\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Format value = (Gst.Format)((FrameRaw*)Handle)->Format;",
            source,
            StringComparison.Ordinal);

        // The mirror keeps the underlying integer, which is what crosses.
        Assert.Contains("internal int Format;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Frame.format`", run.Result.SkipReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumerationOfAModuleTheRunDoesNotEmitIsReportedAsSuch()
    {
        // The GLib stack has a hand written runtime layer and no generated
        // enumerations, so there is no name to hand the field out under. The
        // ledger says that rather than filing it under the catch all.
        FixtureRun run = Fixture.Run(Body, extraNamespaces: ExtraNamespace);

        Assert.Contains(
            "- `Widget.shape` — CrossNamespaceEnum\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldOfARecordWithNoMirrorIsReportedAsSuch()
    {
        // The first field of the record is a GParamSpec by value, which has no
        // layout the run can project, so the mirror collapses to nothing and
        // the field behind it has no storage to be read out of.
        FixtureRun run = Fixture.Run(Body, extraNamespaces: ExtraNamespace);

        Assert.Contains("- `Gadget.height` — NoLayout\n", run.Result.SkipReport, StringComparison.Ordinal);
    }
}
