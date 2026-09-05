using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The gate on a slot that answers a handle nobody references on the way out.
/// </summary>
/// <remarks>
/// The note the emitter writes for such a slot says the base class takes a
/// reference of its own and points at the remarks for how. That is a promise
/// about C code, and only the <c>vfuncDocNotes</c> entry of the slot keeps it:
/// it names the call site, the function that references the answer and the
/// state the answer has to be in. A gir refresh that adds a slot of this shape
/// would otherwise ship the note over remarks that say nothing, so the run
/// stops until the sentence is written.
/// </remarks>
public sealed class BorrowedReturnNoteTests
{
    /// <summary>
    /// One subclassable class with a slot that answers an object the gir
    /// declares <c>transfer none</c>, which is the shape
    /// <c>create_ringbuffer</c> and <c>create_new_pad</c> have.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="create_part">
                <return-value transfer-ownership="none" nullable="1">
                  <doc xml:space="preserve">the new part of @widget.</doc>
                  <type name="Widget" c:type="GstWidget*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="create_part">
                <callback name="create_part">
                  <return-value transfer-ownership="none" nullable="1">
                    <type name="Widget" c:type="GstWidget*"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = "\"subclassable\": [\"Gst.Widget\"]";

    private const string Note =
        "The widget parents the answer and takes a reference of its own "
        + "(gst_object_set_parent sinks it).";

    [Fact]
    public void ASlotWithNoNoteStopsTheRun()
    {
        FixtureRun run = Run("{ " + Allowlist + " }", allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0047");

        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::create_part", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlotWithANoteIsAcceptedAndTheNoteCarriesTheOwnership()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"vfuncDocNotes\": { \"Gst.Widget::create_part\": \"" + Note + "\" } }",
            allowErrors: false);

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0047");

        string source = run.File("Subclassing/Widget.Subclass.cs");

        // The generated sentence states the rule and hands the reader to the
        // remarks, where the entry says which call site keeps it.
        Assert.Contains(
            "No reference is added on the way out: the base class takes one of its own",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "The widget parents the answer and takes a reference of its own",
            source,
            StringComparison.Ordinal);
    }

    private static FixtureRun Run(string fixups, bool allowErrors)
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
