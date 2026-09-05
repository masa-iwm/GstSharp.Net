using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>vfuncSiblingArguments</c> overlay: a parameter that hands a slot a
/// second instance of the type the slot runs for, which the base class has
/// just created and may still hold floating. Nothing in the gir tells such a
/// parameter from an ordinary borrowed object, so the overlay names it, and
/// what changes is the resolution — <c>TryGetOrFabricate</c>, which settles no
/// reference, instead of <c>FromNative</c>, which sinks a floating one.
/// </summary>
public sealed class SiblingArgumentTests
{
    /// <summary>
    /// One subclassable class whose slot is handed another instance of itself,
    /// transfer none, which is the shape of <c>GESTimelineElement::deep_copy</c>.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="deep_copy">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="copy" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                  <parameter name="level" transfer-ownership="none">
                    <type name="gint" c:type="gint"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="deep_copy">
                <callback name="deep_copy">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="copy" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="level" transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The same class with a slot that is lent an opaque record before it is
    /// handed the sibling, which is the shape the resolution has to be written
    /// ahead of.
    /// </summary>
    private const string LentBody =
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
                  <parameter name="copy" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
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
                    <parameter name="copy" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = "\"subclassable\": [\"Gst.Widget\"]";

    /// <summary>
    /// Without the entry the parameter is an ordinary borrowed object, and the
    /// trampoline settles whatever reference it was handed.
    /// </summary>
    [Fact]
    public void WithoutTheEntryTheParameterIsBorrowedTheOrdinaryWay()
    {
        FixtureRun run = Run("{ " + Allowlist + " }");

        Assert.Contains(
            "Gst.Widget? copyValue = Gst.GObject.Object.FromNative<Gst.Widget>(copy, Gst.Interop.Transfer.None);",
            run.File("Subclassing/Widget.Subclass.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// With it the copy is resolved the way the instance of the slot is, and a
    /// copy that resolves to nothing — a type registered without a wrapper
    /// factory has no fabrication to offer — leaves the slot to the
    /// implementation below the trampoline.
    /// </summary>
    [Fact]
    public void TheEntrySelectsTheSiblingResolution()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"vfuncSiblingArguments\": [\"Gst.Widget::deep_copy#copy\"] }");

        string file = run.File("Subclassing/Widget.Subclass.cs");
        Assert.Contains(
            "if (Gst.GObject.Object.TryGetOrFabricate(copy) is not Gst.Widget copyValue)",
            file,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FromNative<Gst.Widget>(copy", file, StringComparison.Ordinal);

        // The same fallback the trampoline takes for an instance it cannot
        // resolve: the C implementation still runs, and the copy stays valid.
        Assert.Contains(
            """
                        if (Gst.GObject.Object.TryGetOrFabricate(copy) is not Gst.Widget copyValue)
                        {
                            ChainUpDeepCopy(widget, copy, level);
                            return;
                        }
            """.TrimEnd(),
            file,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A key that names no parameter of a slot at all is stale, and saying so
    /// is what keeps a misspelling from silently leaving the parameter on the
    /// borrowing bucket.
    /// </summary>
    [Fact]
    public void AKeyThatNamesNoParameterIsReported()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"vfuncSiblingArguments\": [\"Gst.Widget::deep_copy#other\"] }");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0044");
        Assert.Contains("Gst.Widget::deep_copy#other", stale.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// So is a key whose parameter is of another shape. The entry describes an
    /// object the slot is lent, and the scalar the slot is also handed is no
    /// object: the key names a parameter of the slot and still says nothing the
    /// resolution could act on, which is a different mistake from a misspelling
    /// and reported as the same staleness.
    /// </summary>
    [Fact]
    public void AKeyOnAParameterOfAnotherShapeIsReported()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"vfuncSiblingArguments\": [\"Gst.Widget::deep_copy#level\"] }");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0044");
        Assert.Contains("Gst.Widget::deep_copy#level", stale.Message, StringComparison.Ordinal);

        // The scalar is still handed to the override the ordinary way.
        Assert.Contains(
            "managed.OnDeepCopy(copyValue!, level);",
            run.File("Subclassing/Widget.Subclass.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One opaque record the slot is lent, and one sibling argument planned
    /// after it. The fallback the sibling resolution writes returns from the
    /// middle of the trampoline, before the <c>finally</c> that forgets the
    /// pointer of the record, so the resolution is emitted first: the order of
    /// the parameters is not what the two are written in.
    /// </summary>
    [Fact]
    public void ASiblingIsResolvedBeforeTheWrapperOfALentRecordIsBuilt()
    {
        FixtureRun run = Run(
            "{ \"subclassable\": [\"Gst.Widget\"], \"forceOpaque\": [\"Gst.Frame\"], "
                + "\"lentOpaqueRecords\": [\"Gst.Frame\"], "
                + "\"vfuncSiblingArguments\": [\"Gst.Widget::handle#copy\"] }",
            LentBody);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static d => d.Code.StartsWith("GEN004", StringComparison.Ordinal));

        string file = run.File("Subclassing/Widget.Subclass.cs");
        int sibling = file.IndexOf(
            "if (Gst.GObject.Object.TryGetOrFabricate(copy) is not Gst.Widget copyValue)",
            StringComparison.Ordinal);
        int borrowed = file.IndexOf("Gst.Frame? frameValue = Gst.Frame.FromNative(frame);", StringComparison.Ordinal);

        Assert.True(sibling >= 0, "the sibling resolution was not emitted at all.");
        Assert.True(borrowed >= 0, "the lent record was not borrowed at all.");
        Assert.True(sibling < borrowed, "the fallback can return before the record is detached.");
        Assert.Contains("frameValue?.Detach();", file, StringComparison.Ordinal);
    }

    private static FixtureRun Run(string fixups) => Run(fixups, Body);

    private static FixtureRun Run(string fixups, string body)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
