using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>rename</c> overlay reaches the name of a slot, not only the names of
/// its parameters. A slot whose derived name is one an inherited member of
/// another return type already carries has no managed spelling of its own, and
/// the entry keyed <c>Ns.Class::vfunc</c> is what gives it one.
/// </summary>
public sealed class VirtualMethodRenameTests
{
    /// <summary>
    /// A class and a subclass of it declaring a slot of the same name and the
    /// same parameters answering different types, which is
    /// <c>GstAudioSink::stop</c> against <c>GstBaseSink::stop</c> in miniature.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="polish">
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
              <field name="polish">
                <callback name="polish">
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
            <class name="Gadget" c:type="GstGadget" parent="Widget" glib:type-name="GstGadget" glib:get-type="gst_gadget_get_type" glib:type-struct="GadgetClass">
              <virtual-method name="polish">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="gadget" transfer-ownership="none">
                    <type name="Gadget" c:type="GstGadget*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="GadgetClass" c:type="GstGadgetClass" glib:is-gtype-struct-for="Gadget">
              <field name="parent_class">
                <type name="WidgetClass" c:type="GstWidgetClass"/>
              </field>
              <field name="polish">
                <callback name="polish">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                  <parameters>
                    <parameter name="gadget" transfer-ownership="none">
                      <type name="Gadget" c:type="GstGadget*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = "\"subclassable\": [\"Gst.Widget\", \"Gst.Gadget\"]";

    /// <summary>
    /// Without the entry the pair is the one C# accepts and nothing below the
    /// generator catches, so the run stops on GEN0040.
    /// </summary>
    [Fact]
    public void ACollidingSlotWithoutARenameStopsTheRun()
    {
        FixtureRun run = Run("{ " + Allowlist + " }", allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0040");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Gadget::polish", error.Message, StringComparison.Ordinal);

        // The diagnostic is not advice the emitter then ignores: the slot it
        // names is left out of the file it would have hidden a member in, so
        // the in-memory result a test reads holds what the command line would
        // have been willing to write.
        string gadget = run.File("Subclassing/Gadget.Subclass.cs");
        Assert.DoesNotContain("OnPolish", gadget, StringComparison.Ordinal);
        Assert.DoesNotContain("ChainUpPolish", gadget, StringComparison.Ordinal);
        Assert.DoesNotContain("PolishOverride", gadget, StringComparison.Ordinal);

        // The member it would have hidden is the one that stays.
        Assert.Contains(
            "OnPolish",
            run.File("Subclassing/Widget.Subclass.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// With it the slot is emitted under the name the overlay gives, the
    /// collision is gone with the derived name, and the member no longer hides
    /// anything.
    /// </summary>
    [Fact]
    public void ARenamedSlotTakesTheNameTheOverlayGives()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"rename\": { \"Gst.Gadget::polish\": \"PolishSurface\" } }");

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0040");
        Assert.Equal(
            """
            protected virtual void OnPolishSurface() =>
                ChainUpPolishSurface();
            """,
            run.Member("Subclassing/Gadget.Subclass.cs", "protected virtual void OnPolishSurface()"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The chain-up and the override declaration are concatenations of the same
    /// stem, so all three members move together and nothing keeps the derived
    /// spelling.
    /// </summary>
    [Fact]
    public void TheChainUpAndTheOverrideFollowTheRenamedStem()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"rename\": { \"Gst.Gadget::polish\": \"PolishSurface\" } }");

        string file = run.File("Subclassing/Gadget.Subclass.cs");
        Assert.Contains("protected void ChainUpPolishSurface()", file, StringComparison.Ordinal);
        Assert.Contains(
            "public static Gst.GObject.VfuncOverride PolishSurfaceOverride { get; }",
            file,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OnPolish(", file, StringComparison.Ordinal);
        Assert.DoesNotContain("ChainUpPolish(", file, StringComparison.Ordinal);
        Assert.DoesNotContain("PolishOverride", file, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slot of the base class is untouched by the entry, which names the
    /// class the slot is declared on.
    /// </summary>
    [Fact]
    public void TheEntryReachesOnlyTheClassItNames()
    {
        FixtureRun run = Run(
            "{ " + Allowlist + ", \"rename\": { \"Gst.Gadget::polish\": \"PolishSurface\" } }");

        Assert.Contains(
            "protected virtual bool OnPolish()",
            run.File("Subclassing/Widget.Subclass.cs"),
            StringComparison.Ordinal);
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
