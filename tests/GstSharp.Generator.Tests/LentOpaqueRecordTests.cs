using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>lentOpaqueRecords</c> overlay: the records whose wrapper the
/// trampoline of a slot detaches when the call returns. The list is stated
/// rather than derived — a run emits one module after the other, so the slots
/// that lend a record are planned long after the record is written out — and
/// both ways of it going wrong are reported.
/// </summary>
public sealed class LentOpaqueRecordTests
{
    /// <summary>
    /// One opaque record with a field to read, and one subclassable class whose
    /// slot is lent one of them.
    /// </summary>
    private const string Body =
        """
            <record name="Frame" c:type="GstFrame">
              <field name="id" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="handle">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="frame" transfer-ownership="none">
                    <type name="Frame" c:type="GstFrame*"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="handle">
                <callback name="handle">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="frame" transfer-ownership="none">
                      <type name="Frame" c:type="GstFrame*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = "\"subclassable\": [\"Gst.Widget\"], \"forceOpaque\": [\"Gst.Frame\"]";

    /// <summary>
    /// A listed record holds its pointer behind an accessor that says the call
    /// has returned, and carries the detach the trampoline calls.
    /// </summary>
    [Fact]
    public void AListedRecordCarriesTheDetach()
    {
        FixtureRun run = Run("{ " + Allowlist + ", \"lentOpaqueRecords\": [\"Gst.Frame\"] }");

        string record = run.File("Frame.cs");
        Assert.Contains("private nint _handle;", record, StringComparison.Ordinal);
        Assert.Contains(
            "ObjectDisposedException.ThrowIf(_handle == nint.Zero, this);",
            record,
            StringComparison.Ordinal);
        Assert.Contains("internal void Detach() => _handle = nint.Zero;", record, StringComparison.Ordinal);
        Assert.DoesNotContain("internal nint Handle;", record, StringComparison.Ordinal);

        // And the trampoline is what calls it, whatever the override did with
        // the wrapper.
        Assert.Contains(
            "frameValue?.Detach();",
            run.File("Subclassing/Widget.Subclass.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A record a slot lends and the list does not name would leave a wrapper
    /// reading an address that means nothing after the call, which is why that
    /// half fails the run rather than warning.
    /// </summary>
    [Fact]
    public void ALentRecordThatIsNotListedStopsTheRun()
    {
        FixtureRun run = Run("{ " + Allowlist + " }", allowErrors: true);

        Diagnostic missing = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0045");
        Assert.Equal(DiagnosticSeverity.Error, missing.Severity);
        Assert.Contains("Gst.Frame", missing.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A record no slot lends is a stale entry: the accessor it turns into costs
    /// every reader of the record a check nothing needs.
    /// </summary>
    [Fact]
    public void AListedRecordNoSlotLendsIsReported()
    {
        FixtureRun run = Run(
            "{ \"forceOpaque\": [\"Gst.Frame\"], \"lentOpaqueRecords\": [\"Gst.Frame\"] }");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0046");
        Assert.Contains("Gst.Frame", stale.Message, StringComparison.Ordinal);
    }

    private static FixtureRun Run(string fixups, bool allowErrors = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(Body, Overlays.Load(directory), allowErrors: allowErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
