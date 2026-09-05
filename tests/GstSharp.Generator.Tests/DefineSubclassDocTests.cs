using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the generated registration overloads say they raise.
/// </summary>
/// <remarks>
/// The interfaces a subclass implements are declared through the options and
/// nowhere else, so the two conditions that the validation of that list raises
/// - an interface declared twice, and one the parent type implements already -
/// are conditions of the overload that takes the options. The hand written
/// <c>SubclassType.Define</c> has documented them all along; the overload that
/// takes no options cannot reach them.
/// </remarks>
public sealed class DefineSubclassDocTests
{
    /// <summary>One subclassable class with a single slot to take over.</summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="start">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
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
              <field name="start">
                <callback name="start">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
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

    [Fact]
    public void TheOverloadThatTakesTheOptionsNamesTheInterfaceConditions()
    {
        Assert.Contains(
            """
                /// <exception cref="System.ArgumentException">
                /// The type name is not a legal <c>GType</c> name, a declared slot belongs to a
                /// class that <c>GstWidget</c> does not derive from, an interface is declared
                /// twice, or the parent type implements it already.
                /// </exception>
            """,
            Source(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheOverloadThatTakesNoOptionsNamesOnlyWhatItCanRaise()
    {
        Assert.Contains(
            """
                /// <exception cref="System.ArgumentException">
                /// The type name is not a legal <c>GType</c> name, or a declared slot belongs to a
                /// class that <c>GstWidget</c> does not derive from.
                /// </exception>
            """,
            Source(),
            StringComparison.Ordinal);
    }

    private static string Source()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """{ "subclassable": ["Gst.Widget"] }""");
            return Fixture.Run(Body, Overlays.Load(directory)).File("Subclassing/Widget.Subclass.cs");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
