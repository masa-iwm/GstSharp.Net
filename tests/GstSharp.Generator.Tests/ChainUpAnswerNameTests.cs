using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The names the body of a generated chain-up gives its own locals, and what a
/// gir parameter of one of those names does to the slot.
/// </summary>
/// <remarks>
/// A chain-up that answers a handle reads the answer of the parent slot into a
/// local and converts it into a second one, <c>resultNative</c> beside
/// <c>result</c>, and the arguments of the chain-up are the parameters of the
/// slot themselves. A parameter the gir calls <c>result_native</c> would
/// therefore be redeclared by the body, which is a file that does not compile
/// rather than a diagnostic. The planner reserves the name the way it reserves
/// <c>result</c>, <c>parent</c> and <c>slot</c> beside it: the slot is refused
/// as an unsupported signature and the class keeps everything else. Nothing in
/// the vendored girs is called either, so this fixture is what says the rule is
/// there at all.
/// </remarks>
public sealed class ChainUpAnswerNameTests
{
    /// <summary>
    /// Two subclassable classes with the same handle answering slot: the one of
    /// <c>Widget</c> has a string parameter the gir calls <c>result_native</c>,
    /// which is the name the raw answer of the chain-up takes, and the one of
    /// <c>Gadget</c> is the same slot with the parameter called something else.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="adopt">
                <return-value transfer-ownership="full">
                  <type name="GObject.Object" c:type="GObject*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="result_native" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="adopt">
                <callback name="adopt">
                  <return-value transfer-ownership="full">
                    <type name="GObject.Object" c:type="GObject*"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="result_native" transfer-ownership="none">
                      <type name="utf8" c:type="const gchar*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
            <class name="Gadget" c:type="GstGadget" parent="GObject.Object" glib:type-name="GstGadget" glib:get-type="gst_gadget_get_type" glib:type-struct="GadgetClass">
              <virtual-method name="adopt">
                <return-value transfer-ownership="full">
                  <type name="GObject.Object" c:type="GObject*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="gadget" transfer-ownership="none">
                    <type name="Gadget" c:type="GstGadget*"/>
                  </instance-parameter>
                  <parameter name="name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="GadgetClass" c:type="GstGadgetClass" glib:is-gtype-struct-for="Gadget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="adopt">
                <callback name="adopt">
                  <return-value transfer-ownership="full">
                    <type name="GObject.Object" c:type="GObject*"/>
                  </return-value>
                  <parameters>
                    <parameter name="gadget" transfer-ownership="none">
                      <type name="Gadget" c:type="GstGadget*"/>
                    </parameter>
                    <parameter name="name" transfer-ownership="none">
                      <type name="utf8" c:type="const gchar*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Fixups =
        """
        {
          "subclassable": ["Gst.Widget", "Gst.Gadget"]
        }
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Generate(Fixups), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static string Widget => Run.File("Subclassing/Widget.Subclass.cs");

    private static string Gadget => Run.File("Subclassing/Gadget.Subclass.cs");

    /// <summary>
    /// The slot with the ordinary parameter is emitted, and its chain-up is
    /// where the two reserved names are read off: the raw answer is
    /// <c>resultNative</c> and the wrapper of it is <c>result</c>.
    /// </summary>
    [Fact]
    public void TheChainUpReadsTheAnswerIntoResultNative()
    {
        Assert.Contains("nint resultNative = ChainUpAdopt(", Gadget, StringComparison.Ordinal);
        Assert.Contains("resultNative, Gst.Interop.Transfer.Full)", Gadget, StringComparison.Ordinal);
        Assert.Contains("return result;", Gadget, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same slot with a parameter of that name is refused rather than
    /// emitted with a local declared twice.
    /// </summary>
    [Fact]
    public void ASlotWithAParameterOfThatNameIsRefused()
    {
        Assert.DoesNotContain("ChainUpAdopt", Widget, StringComparison.Ordinal);
        Assert.DoesNotContain("OnAdopt", Widget, StringComparison.Ordinal);

        // The class itself is untouched: only the slot is out.
        Assert.Contains("public static Gst.GObject.SubclassType DefineSubclass(", Widget, StringComparison.Ordinal);
    }

    private static FixtureRun Generate(string fixups)
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
