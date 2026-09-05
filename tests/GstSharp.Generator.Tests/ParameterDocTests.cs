using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the <c>&lt;param&gt;</c> of a generated member says: the sentence the
/// gir wrote for the parameter, whatever the parameter is marshalled as.
/// </summary>
/// <remarks>
/// The shapes that plan a block or a vector of their own always carried the
/// gir parameter with them, so their documentation arrived on its own. Every
/// other kind - a number, an enumeration member, a string, an object - was
/// planned without it and was documented by naming the argument back at the
/// reader. The documentation is attached at the one point every parameter of a
/// callable passes through, so these tests read one of each kind out of the
/// emitted member, one parameter the gir documents over several lines, which
/// comes out as the block form of the element, and one parameter whose gir
/// says nothing, which keeps the sentence the generator writes.
/// </remarks>
public sealed class ParameterDocTests
{
    /// <summary>
    /// One class with a method that takes a number, an enumeration member, an
    /// object, a string the gir documents over two lines and a fifth parameter
    /// the gir documents nowhere.
    /// </summary>
    private const string Body =
        """
            <enumeration name="Mode" c:type="GstMode">
              <member name="idle" value="0" c:identifier="GST_MODE_IDLE"/>
              <member name="busy" value="1" c:identifier="GST_MODE_BUSY"/>
            </enumeration>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="configure" c:identifier="gst_widget_configure">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="latency" transfer-ownership="none">
                    <doc xml:space="preserve">the latency to run with, in nanoseconds</doc>
                    <type name="guint64" c:type="guint64"/>
                  </parameter>
                  <parameter name="mode" transfer-ownership="none">
                    <doc xml:space="preserve">the #GstMode to switch to</doc>
                    <type name="Mode" c:type="GstMode"/>
                  </parameter>
                  <parameter name="peer" transfer-ownership="none">
                    <doc xml:space="preserve">the #GstWidget to configure against</doc>
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                  <parameter name="title" transfer-ownership="none">
                    <doc xml:space="preserve">the title to show, which the widget
              copies</doc>
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                  <parameter name="tag" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    [Fact]
    public void AScalarParameterCarriesWhatTheGirWroteForIt()
    {
        string source = Fixture.Run(Body).File("Widget.cs");

        Assert.Contains(
            "<param name=\"latency\">the latency to run with, in nanoseconds</param>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumerationParameterCarriesWhatTheGirWroteForIt()
    {
        string source = Fixture.Run(Body).File("Widget.cs");

        // The gir idioms are the ones the summaries already carry, so the
        // reference to the enumeration stands as the gir spelled it.
        Assert.Contains(
            "<param name=\"mode\">the #GstMode to switch to</param>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AHandleParameterCarriesWhatTheGirWroteForIt()
    {
        string source = Fixture.Run(Body).File("Widget.cs");

        Assert.Contains(
            "<param name=\"peer\">the #GstWidget to configure against</param>",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterDocumentedOverSeveralLinesKeepsTheLinesTheGirWrote()
    {
        string source = Fixture.Run(Body).File("Widget.cs");

        // The gir wrote two lines and indented the second one; both are the
        // lines that ship, so the element is opened and closed on lines of
        // their own.
        Assert.Contains(
            """
                /// <param name="title">
                /// the title to show, which the widget
                ///       copies
                /// </param>
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AParameterTheGirDocumentsNowhereKeepsTheSentenceTheGeneratorWrites()
    {
        string source = Fixture.Run(Body).File("Widget.cs");

        Assert.Contains(
            "<param name=\"tag\">The <c>tag</c> argument.</param>",
            source,
            StringComparison.Ordinal);
    }
}
