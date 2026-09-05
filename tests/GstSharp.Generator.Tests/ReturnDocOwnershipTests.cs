using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What a return documentation says once an <c>annotationOverrides</c> entry
/// has corrected the ownership the gir stated.
/// </summary>
/// <remarks>
/// A correction to <c>transfer none</c> says the gir was wrong about who owns
/// the answer, and the emitted note under the documentation then says that no
/// reference is added on the way out. The gir sentence that tells the caller
/// to release it would stand right above that note and contradict it, so it
/// comes out with the annotation it belongs to. A gir that already said
/// <c>none</c> is left alone: nothing corrected it, and the sentence is then
/// the gir's own business.
/// </remarks>
public sealed class ReturnDocOwnershipTests
{
    /// <summary>
    /// One subclassable class with a slot that hands back an object the gir
    /// declares <c>transfer full</c>, documented the way the gir of
    /// <c>GstElement::request_new_pad</c> documents its answer.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="borrow">
                <return-value transfer-ownership="full" nullable="1">
                  <doc xml:space="preserve">the widget if found, otherwise %NULL. Release after usage.</doc>
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
              <field name="borrow">
                <callback name="borrow">
                  <return-value transfer-ownership="full" nullable="1">
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

    /// <summary>
    /// The ownership note the slot needs once the answer is borrowed: a slot
    /// that hands a handle back without a reference has to say who does
    /// reference it, or the run stops on GEN0047.
    /// </summary>
    private const string BorrowNote =
        ", \"vfuncDocNotes\": { \"Gst.Widget::borrow\": "
        + "\"The widget references the answer itself (gst_object_ref).\" }";

    [Fact]
    public void ACorrectedReturnLosesTheSentenceThatHandsItOver()
    {
        FixtureRun run = Run(
            Body,
            "{ " + Allowlist + BorrowNote + ", \"annotationOverrides\": "
            + """{ "Gst.Widget::borrow#return": { "transfer": "none" } } }""");

        string source = run.File("Subclassing/Widget.Subclass.cs");

        Assert.DoesNotContain("Release after usage.", source, StringComparison.Ordinal);

        // What the value is stays the gir's to say; only the ownership sentence
        // goes, and the note the correction earned takes its place.
        Assert.Contains("the widget if found, otherwise %NULL.", source, StringComparison.Ordinal);
        Assert.Contains("No reference is added on the way out", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUncorrectedReturnKeepsWhatTheGirSays()
    {
        FixtureRun run = Run(Body, "{ " + Allowlist + " }");

        string source = run.File("Subclassing/Widget.Subclass.cs");

        Assert.Contains(
            "the widget if found, otherwise %NULL. Release after usage.",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same class, but with a gir that already declares the returned
    /// widget <c>transfer none</c>.
    /// </summary>
    private static readonly string BorrowedBody =
        Body.Replace("transfer-ownership=\"full\"", "transfer-ownership=\"none\"", StringComparison.Ordinal);

    [Fact]
    public void AReturnTheGirItselfCallsBorrowedKeepsWhatItSays()
    {
        FixtureRun run = Run(BorrowedBody, "{ " + Allowlist + BorrowNote + " }");

        string source = run.File("Subclassing/Widget.Subclass.cs");

        // Nothing corrected this gir, so the sentence about releasing the
        // value is the gir's own business and stands as written.
        Assert.Contains(
            "the widget if found, otherwise %NULL. Release after usage.",
            source,
            StringComparison.Ordinal);
    }

    private static FixtureRun Run(string body, string fixups)
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
