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
                  <parameter name="buf" direction="out" caller-allocates="0" transfer-ownership="full">
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
                    <parameter name="buf" direction="out" caller-allocates="0" transfer-ownership="full">
                      <type name="Gst.Buffer" c:type="GstBuffer**"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// A subclassable class whose parent is mirrored without being
    /// subclassable: the parent is a chain-only mirror, which lays its slots out
    /// and gives none of them a managed surface.
    /// </summary>
    private const string ChainOnlyBody =
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
              <virtual-method name="wind">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
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
              <field name="wind">
                <callback name="wind">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
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

    /// <summary>
    /// A class and a subclass of it that declare a slot of the same name and
    /// the same parameters answering different types: the shape the managed
    /// <c>new</c> member must never be written for.
    /// </summary>
    private const string BodyWithAReturnTypeCollision =
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

    /// <summary>
    /// The same slot with the buffer passed <c>inout</c> and transfer full: the
    /// caller gives its reference up on entry and takes over what the slot
    /// leaves, which is the third inout shape.
    /// </summary>
    private static readonly string BodyWithAnInOutHandOver =
        Body.Replace("direction=\"out\"", "direction=\"inout\"", StringComparison.Ordinal);

    /// <summary>
    /// The same class struct carrying a field of a type the mirror has no
    /// layout for, which is what an unknown record embedded by value looks
    /// like.
    /// </summary>
    private const string BodyWithAnUnmappableField =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="palette" writable="1">
                <type name="Gadgetry" c:type="GstGadgetry"/>
              </field>
            </record>
        """;

    /// <summary>
    /// A slot that answers an instance of a class the gir spells
    /// <c>transfer-ownership="none"</c>, which is the shape a floating hand out
    /// arrives in: no gir transfer kind says "floating", so the annotation of
    /// <c>GstDeviceClass::create_element</c> reads as a borrow.
    /// </summary>
    private const string BodyWithAnObjectReturn =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="mint">
                <return-value transfer-ownership="none" nullable="1">
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
              <field name="mint">
                <callback name="mint">
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

    /// <summary>
    /// The same slot answering a <c>GList</c>, which is the shape the planner
    /// refuses outright — <c>GstDeviceProvider::probe</c> is the one in the
    /// corpus. No bucket is chosen at all, and the slot leaves the surface as
    /// <c>UnsupportedSignature</c>.
    /// </summary>
    private const string BodyWithAnUnprojectableReturn =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="mint">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="GLib.List" c:type="GList*"/>
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
              <field name="mint">
                <callback name="mint">
                  <return-value transfer-ownership="none" nullable="1">
                    <type name="GLib.List" c:type="GList*"/>
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

    /// <summary>
    /// The object answering slot with a <c>GError</c> out of band, which the
    /// planner refuses before it ever looks at the answer. The mode would apply
    /// to the answer perfectly well, so nothing about the return value says the
    /// entry lands nowhere.
    /// </summary>
    private const string BodyWithAThrowingObjectReturn =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="mint" throws="1">
                <return-value transfer-ownership="none" nullable="1">
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
              <field name="mint">
                <callback name="mint" throws="1">
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
    public void AChainOnlySlotWithALedgerEntryRecordsTheReasonItStates()
    {
        FixtureRun run = Run(
            ChainOnlyBody,
            """{ "subclassable": ["Gst.Gadget"], "skipVirtuals": { "Gst.Widget::polish": "hand bound for the classes below it" } }""");

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0029");
        Assert.Equal(
            "hand bound for the classes below it",
            run.Result.Census.SkippedVirtuals("Gst")["Gst.Widget::polish"]);
    }

    [Fact]
    public void AChainOnlySlotWithoutALedgerEntryRecordsTheFixedReason()
    {
        FixtureRun run = Run(ChainOnlyBody, """{ "subclassable": ["Gst.Gadget"] }""");

        Assert.Contains(
            "NotSubclassable",
            run.Result.Census.SkippedVirtuals("Gst")["Gst.Widget::polish"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void AKeyThatNamesNoMirroredSlotIsStillReported()
    {
        // The chain-only mirror lays out one slot and the key names another, so
        // the wider set the ledger is checked against does not cover it either.
        FixtureRun run = Run(
            ChainOnlyBody,
            """{ "subclassable": ["Gst.Gadget"], "skipVirtuals": { "Gst.Widget::wind": "not a slot of that class" } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0029");
        Assert.Contains("Gst.Widget::wind", stale.Message, StringComparison.Ordinal);
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
    public void AFloatingReturnThatNamesNoSlotIsReported()
    {
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncFloatingReturns": ["Gst.Widget::polish"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0058");
        Assert.Contains("Gst.Widget::polish", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatingReturnMintsTheReferenceAndFloatsIt()
    {
        // The overlay is the only thing that selects the mode: the gir of the
        // slot is a plain transfer-none object return, which every other slot of
        // that shape is emitted as a borrow.
        FixtureRun run = Run(
            BodyWithAnObjectReturn,
            """{ "subclassable": ["Gst.Widget"], "vfuncFloatingReturns": ["Gst.Widget::mint"] }""");

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code != "GEN0005");

        string source = run.File("Subclassing/Widget.Subclass.cs");

        // The three steps, in order: mint, float, and a barrier before the
        // handle leaves the trampoline.
        Assert.Contains(
            "nint resultHandle = Gst.Interop.GObjectNative.ObjectRef(result.Handle);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Interop.GObjectNative.ObjectForceFloating(resultHandle);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("GC.KeepAlive(result);", source, StringComparison.Ordinal);
        Assert.Contains("return resultHandle;", source, StringComparison.Ordinal);

        // A null answer crosses untouched, and the chain-up settles the floating
        // reference the parent slot handed over rather than claiming a second
        // one.
        Assert.Contains("return nint.Zero;", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.GObject.Object.FromNative<Gst.Widget>(resultNative, Gst.Interop.Transfer.None)",
            source,
            StringComparison.Ordinal);

        // The contract of the mode is on the managed member, which is what the
        // borrowed handle the same gir would otherwise select needs a
        // vfuncDocNotes entry for.
        Assert.Contains("may drop it without ever", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnObjectReturnWithoutTheFloatingKeyStaysAnUndocumentedBorrow()
    {
        // The same gir without the overlay entry: the slot is a borrow, and a
        // borrow with no note about who references the answer stops the run.
        // This is the error the new mode is exempt from - it says who owns the
        // reference itself.
        FixtureRun run = Run(BodyWithAnObjectReturn, "{ " + Allowlist + " }", allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0047");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::mint", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatingReturnOnASlotThatAnswersNoObjectStopsTheRun()
    {
        // The mode mints a reference and sets a flag on the handle, neither of
        // which a gboolean carries. An entry on such a slot is a mistake in the
        // overlay rather than a mode the emitter can fall back from.
        FixtureRun run = Run(
            Body,
            """{ "subclassable": ["Gst.Widget"], "vfuncFloatingReturns": ["Gst.Widget::prepare"] }""",
            allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0059");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::prepare", error.Message, StringComparison.Ordinal);
        Assert.Contains("bool", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFloatingReturnOnASlotTheBindingDoesNotProjectStopsTheRun()
    {
        // The other way the key can be consumed in silence: the planner refuses
        // the answer outright, so no bucket is chosen and nothing checks the
        // entry against one - and the key names a real field of a real class
        // struct, so the stale report does not catch it either.
        FixtureRun run = Run(
            BodyWithAnUnprojectableReturn,
            """{ "subclassable": ["Gst.Widget"], "vfuncFloatingReturns": ["Gst.Widget::mint"] }""",
            allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0059");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::mint", error.Message, StringComparison.Ordinal);

        // And the slot really is off the surface, which is what makes the
        // refusal the only report there is.
        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0058");
    }

    [Fact]
    public void AFloatingReturnOnASlotThatThrowsStopsTheRun()
    {
        // The answer of this slot is exactly the shape the mode applies to, and
        // the entry still lands nowhere: a slot with a GError out of band is
        // refused before the answer is planned at all, so no bucket is chosen
        // and the key names a real field of a real class struct either way.
        FixtureRun run = Run(
            BodyWithAThrowingObjectReturn,
            """{ "subclassable": ["Gst.Widget"], "vfuncFloatingReturns": ["Gst.Widget::mint"] }""",
            allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0059");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::mint", error.Message, StringComparison.Ordinal);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0058");
    }

    [Fact]
    public void AFloatingReturnOnASkippedSlotStopsTheRun()
    {
        // The third way out, and the one that never reaches the planner: a slot
        // the overlays take out of the surface by hand. Two entries that
        // contradict each other are a mistake in the overlay whichever of them
        // was meant.
        FixtureRun run = Run(
            BodyWithAnObjectReturn,
            """
            {
              "subclassable": ["Gst.Widget"],
              "skipVirtuals": { "Gst.Widget::mint": "UnsupportedSignature" },
              "vfuncFloatingReturns": ["Gst.Widget::mint"]
            }
            """,
            allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0059");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Widget::mint", error.Message, StringComparison.Ordinal);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0058");
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

    [Fact]
    public void AnInOutHandleThatHandsOwnershipOverIsEmitted()
    {
        // The caller gives its reference up on entry and takes over whatever
        // the slot leaves behind, which is the adopt on entry, hand over on
        // exit projection: the trampoline owns what it was handed and the
        // wrapper the override left is handed on without a second reference.
        FixtureRun run = Run(BodyWithAnInOutHandOver, "{ " + Allowlist + " }");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "vfunc"));
        Assert.Empty(run.Result.Census.SkippedVirtuals("Gst"));

        string source = run.File("Subclassing/Widget.Subclass.cs");
        Assert.Contains("OnPrepare(ref ", source, StringComparison.Ordinal);
        Assert.Contains("HandOver()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AClassStructFieldTheMirrorCannotLayOutIsReported()
    {
        // A warning and not an error: the field is mirrored as a pointer, which
        // is the right width for most of what reaches here, and the ABI probes
        // measure the offsets that follow it against the running library. What
        // it must not do is pass unnoticed.
        FixtureRun run = Run(BodyWithAnUnmappableField, "{ " + Allowlist + " }");

        Diagnostic unmappable = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0032");
        Assert.Equal(DiagnosticSeverity.Warning, unmappable.Severity);
        Assert.Contains("palette", unmappable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASlotThatHidesAnInheritedMemberWithAnotherReturnTypeStopsTheRun()
    {
        // GstAudioSink::stop against GstBaseSink::stop, in miniature. C# would
        // compile the pair, so nothing below the generator catches it: the
        // subclass member would answer nothing where the one it hides answers a
        // bool, and which of the two runs depends on the static type the caller
        // holds. The run stops until the slot is skipped or renamed.
        FixtureRun run = Run(
            BodyWithAReturnTypeCollision,
            """{ "subclassable": ["Gst.Widget", "Gst.Gadget"] }""",
            allowErrors: true);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0040");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("Gst.Gadget::polish", error.Message, StringComparison.Ordinal);
        Assert.Contains("bool", error.Message, StringComparison.Ordinal);

        // And the slot is dropped rather than written and complained about: the
        // result the test holds is the one the command line would have
        // persisted, had it been willing to persist anything at all.
        Assert.DoesNotContain(
            "OnPolish",
            run.File("Subclassing/Gadget.Subclass.cs"),
            StringComparison.Ordinal);
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
