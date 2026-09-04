using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The gate on the overlay keys of the subclassing surface: every entry that
/// names nothing is reported rather than silently ignored, and a class struct
/// field the mirror has no type for stops the run.
/// </summary>
/// <remarks>
/// A stale key here is worse than elsewhere. A <c>vfuncDefaults</c> entry that
/// lands nowhere turns a slot with a documented default into one whose chain-up
/// throws, and a <c>skipVirtuals</c> entry that lands nowhere emits a slot the
/// ledger claims was left out; neither shows up in a count.
/// </remarks>
public sealed class VirtualOverlayDiagnosticTests
{
    /// <summary>
    /// One subclassable class with a class struct of its own: a slot that takes
    /// a mini object and hands one back, which is enough shape for every key
    /// the overlays address a virtual method or one of its parameters by.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Buffer" c:type="GstBuffer" glib:type-name="GstBuffer" glib:get-type="gst_buffer_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="prepare">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="buf" direction="inout" caller-allocates="0" transfer-ownership="full">
                    <type name="Gst.Buffer" c:type="GstBuffer**"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="prepare">
                <callback name="prepare">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="buf" direction="inout" caller-allocates="0" transfer-ownership="full">
                      <type name="Gst.Buffer" c:type="GstBuffer**"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The same class with no <c>glib:type-struct</c>, which is what a
    /// misspelled allowlist entry or a gir that moved on looks like.
    /// </summary>
    private const string BodyWithoutClassStruct =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
            </class>
        """;

    /// <summary>
    /// The same class struct carrying a C long, whose width differs between
    /// Windows and the other targets of this binding.
    /// </summary>
    private const string BodyWithACLong =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="stride" writable="1">
                <type name="glong" c:type="glong"/>
              </field>
            </record>
        """;

    private const string Allowlist = "\"subclassable\": [\"Gst.Widget\"]";

    [Fact]
    public void TheBaselineFixtureReportsNothing()
    {
        // What every case below moves away from: one mirror, one slot, and
        // nothing reported. GEN0005 is the fixture itself - a hand written gir
        // declares one namespace where the run expects a module per gir - and
        // every case below carries it too.
        FixtureRun run = Run(Body, "{ " + Allowlist + " }");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "class struct"));
        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code != "GEN0005");
    }

    [Fact]
    public void AnAllowlistEntryThatMatchesNoClassIsReported()
    {
        FixtureRun run = Run(Body, """{ "subclassable": ["Gst.Widget", "Gst.Gadget"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0027");
        Assert.Contains("Gst.Gadget", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAllowlistedClassWithNoClassStructIsReported()
    {
        FixtureRun run = Run(BodyWithoutClassStruct, "{ " + Allowlist + " }");

        Diagnostic missing = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0028");
        Assert.Contains("Gst.Widget", missing.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "class struct"));
    }

    [Fact]
    public void ASkipThatNamesNoSlotIsReported()
    {
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "skipVirtuals": { "Gst.Widget::polish": "not a slot" } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0029");
        Assert.Contains("Gst.Widget::polish", stale.Message, StringComparison.Ordinal);

        // The slot the entry did not name keeps its member, and the ledger
        // keeps the slot the entry claimed off it.
        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.Equal(0, run.Result.Census.SkippedVirtualCount("Gst"));
    }

    [Fact]
    public void ADefaultThatNamesNoSlotIsReported()
    {
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncDefaults": { "Gst.Widget::polish": "true" } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0030");
        Assert.Contains("Gst.Widget::polish", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdentityBufferThatNamesNoParameterIsReported()
    {
        // The slot exists and the key is a parameter key, so only the parameter
        // half of it is wrong: the entry names 'outbuf' where the slot declares
        // 'buf'.
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncIdentityBuffers": ["Gst.Widget::prepare#outbuf"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0031");
        Assert.Contains("Gst.Widget::prepare#outbuf", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNullReturnThatNamesNoSlotIsReported()
    {
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncNonNullReturns": { "Gst.Widget::polish": "true" } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0036");
        Assert.Contains("Gst.Widget::polish", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocNoteThatNamesNoSlotIsReported()
    {
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncDocNotes": { "Gst.Widget::polish": "A note." } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0037");
        Assert.Contains("Gst.Widget::polish", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACLongInAClassStructStopsTheRun()
    {
        // An error rather than a warning: no managed type mirrors a C long, so
        // laying the field out at all would shift every field behind it on one
        // of the two target families.
        FixtureRun run = Run(BodyWithACLong, "{ " + Allowlist + " }", allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0035");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("stride", error.Message, StringComparison.Ordinal);
        Assert.Contains("glong", error.Message, StringComparison.Ordinal);
    }

    private static FixtureRun Run(string body, string fixups, bool allowErrors = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory), allowErrors: allowErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
