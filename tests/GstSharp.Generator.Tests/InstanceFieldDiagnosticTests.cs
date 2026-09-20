using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Every way an <c>instanceFields</c> entry is refused, read off a gir the test
/// wrote by hand.
/// </summary>
/// <remarks>
/// An entry is the one overlay key that adds public surface out of a structure
/// the generator otherwise never looks at, so each refusal is an error and each
/// of them is the only thing standing between a wrong entry and an accessor that
/// reads the wrong bytes. The real girs exercise the accepted path alone, which
/// leaves the refusals to a fixture: the classes below carry one field per shape
/// the allowlist turns down, and one class per way a mirror fails to lay its own
/// fields out.
/// </remarks>
public sealed class InstanceFieldDiagnosticTests
{
    /// <summary>The overlay key of the allowlist, opening the object of a case.</summary>
    private const string Entries = "\"instanceFields\": ";

    /// <summary>The three statements every well formed entry makes.</summary>
    private const string Window =
        "\"lock\": \"STREAM_LOCK\", \"overrides\": [\"render\"], \"$comment\": \"gstwidget.h:1\"";

    /// <summary>The allowlist of the subclassing surface the fixture needs.</summary>
    /// <remarks>
    /// Every fixture class is on it, because an entry states a window of
    /// overrides and a class with no subclassing surface emits none.
    /// </remarks>
    private const string Subclassable =
        "\"subclassable\": [\"GstBase.Widget\", \"GstBase.Gadget\", \"GstBase.Gizmo\", "
        + "\"GstBase.Doodad\", \"GstBase.Thing\"]";

    /// <summary>
    /// The fields every fixture class carries: the instance structure of the base
    /// class, the one shape the allowlist admits, and the shapes it refuses that
    /// a mirror can still lay out.
    /// </summary>
    private const string CommonFields =
        """
              <field name="head">
                <type name="GObject.Object" c:type="GObject"/>
              </field>
              <field name="segment">
                <type name="Gst.Segment" c:type="GstSegment"/>
              </field>
              <field name="data">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="width">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="mutex">
                <type name="GLib.Mutex" c:type="GMutex"/>
              </field>
              <field name="priv" readable="0" private="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
        """;

    /// <summary>
    /// The shapes that are refused before a mirror is ever written: another
    /// embedded structure, and a field of a version above the support floor.
    /// </summary>
    private const string RefusedFields =
        """
              <field name="spec">
                <type name="Gst.Spec" c:type="GstSpec"/>
              </field>
              <field name="cursor" version="1.99">
                <type name="Gst.Segment" c:type="GstSegment"/>
              </field>
        """;

    /// <summary>A member whose name an accessor would take, with a parameter so that it stays a method.</summary>
    private const string ShapeMethod =
        """
              <method name="get_shape" c:identifier="gst_widget_get_shape">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="index" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </method>
        """;

    /// <summary>The types a field of the fixture names, in the modules that declare them.</summary>
    private const string ExtraNamespaces =
        """
          <namespace name="Gst" version="1.0" c:identifier-prefixes="Gst" c:symbol-prefixes="gst">
            <record name="Segment" c:type="GstSegment" glib:type-name="GstSegment" glib:get-type="gst_segment_get_type">
              <field name="rate" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
            <record name="Spec" c:type="GstSpec">
              <field name="depth" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
          </namespace>
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <union name="Mutex" c:type="GMutex">
              <field name="p" writable="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
            </union>
          </namespace>
        """;

    [Theory]

    // What the entry itself says, read without asking what the run made of it.
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { \"lock\": \"STREAM_LOCK\", \"$comment\": \"gstwidget.h:1\" } } }",
        "GEN0060",
        "states no 'overrides'")]
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { " + Window + ", \"name\": \"\" } } }",
        "GEN0060",
        "states an empty 'name'")]
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { " + Window + ", \"name\": \"Get Segment\" } } }",
        "GEN0060",
        "which is no C# identifier")]

    // What the key names.
    [InlineData(
        Entries + "{ \"GstWidget.head\": { " + Window + " } } }",
        "GEN0060",
        "names the instance structure of the base class")]
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { " + Window + " } }, "
        + "\"fieldSkips\": { \"GstWidget.segment\": { \"handBound\": true } } }",
        "GEN0060",
        "is also registered under 'fieldSkips'")]
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { \"lock\": \"STREAM_LOCK\", \"overrides\": [\"polish\"], "
        + "\"$comment\": \"gstwidget.h:1\" } } }",
        "GEN0060",
        "names the override 'polish'")]

    // The shapes wave 1 turns down.
    [InlineData(
        Entries + "{ \"GstWidget.priv\": { " + Window + " } } }",
        "GEN0061",
        "is marked private or unreadable in the gir")]
    [InlineData(
        Entries + "{ \"GstWidget.data\": { " + Window + " } } }",
        "GEN0061",
        "Pointer instance fields are not exposed")]
    [InlineData(
        Entries + "{ \"GstWidget.width\": { " + Window + " } } }",
        "GEN0061",
        "Scalar instance fields are not exposed")]
    [InlineData(
        Entries + "{ \"GstWidget.mutex\": { " + Window + " } } }",
        "GEN0061",
        "Embedded GMutex instance fields are not exposed")]
    [InlineData(
        Entries + "{ \"GstWidget.spec\": { " + Window + " } } }",
        "GEN0061",
        "Embedded GstSpec instance fields are not exposed")]
    [InlineData(
        Entries + "{ \"GstWidget.cursor\": { " + Window + " } } }",
        "GEN0061",
        "which is above the support floor")]

    // The name of the accessor against the surface the class already carries.
    [InlineData(
        Entries + "{ \"GstWidget.segment\": { " + Window + ", \"name\": \"Shape\" } } }",
        "GEN0063",
        "The accessor 'GetShape' of the instance field 'GstWidget.segment' collides")]

    // The mirror the offset is measured from.
    [InlineData(
        Entries + "{ \"GstGizmo.segment\": { " + Window + " } } }",
        "GEN0062",
        "The instance field 'stamp' has the type 'guint16'")]
    [InlineData(
        Entries + "{ \"GstDoodad.segment\": { " + Window + " } } }",
        "GEN0062",
        "occupies 1 bits of a word it shares with its neighbours")]
    [InlineData(
        Entries + "{ \"GstThing.segment\": { " + Window + " } } }",
        "GEN0062",
        "declares the nested union 'ABI'")]
    public void ARefusedEntryStopsTheRun(string entries, string code, string fragment)
    {
        FixtureRun run = Run(entries);

        Diagnostic error = Assert.Single(run.Result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains(fragment, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A class that emits no override at all is refused for that rather than for
    /// the slot the entry names, which exists.
    /// </summary>
    [Fact]
    public void AnEntryOnAClassThatIsNotSubclassableIsRefusedForTheClass()
    {
        FixtureRun run = Run(
            Entries + "{ \"GstGadget.segment\": { " + Window + " } } }",
            subclassable: "\"subclassable\": [\"GstBase.Widget\"]");

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0060");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("is not on 'subclassable'", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The accepted path of the same fixture, so that a refusal above is a
    /// refusal of what the entry says and not of the gir it is read against.
    /// </summary>
    [Fact]
    public void AnAcceptedEntryEmitsAnAccessorAndAMirror()
    {
        FixtureRun run = Run(
            Entries + "{ \"GstGadget.segment\": { " + Window + " } } }",
            allowErrors: false);

        Assert.Contains(
            "public Gst.Segment GetSegment()",
            run.File("Gadget.cs", "GstSharp.Net.Base"),
            StringComparison.Ordinal);
        Assert.Contains(
            "SegmentOffset = Gst.GObject.InstanceLayout.OffsetOf(ref probe, ref probe.Segment);",
            run.File("InstanceFields/GadgetOwnFieldsRaw.cs", "GstSharp.Net.Base"),
            StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.EmittedCount("GstBase", "instance field"));
    }

    /// <summary>One class of the fixture, with its class struct.</summary>
    /// <param name="name">The gir name of the class.</param>
    /// <param name="fields">The instance fields it declares after the ones every class carries.</param>
    /// <param name="members">The methods it declares, if any.</param>
    /// <returns>The gir fragment.</returns>
    private static string Class(string name, string fields = "", string members = "") =>
        $"""
            <class name="{name}" c:type="Gst{name}" parent="GObject.Object" glib:type-name="Gst{name}" glib:get-type="gst_{name.ToLowerInvariant()}_get_type" glib:type-struct="{name}Class">
              <virtual-method name="render">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="{name}" c:type="Gst{name}*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
        {CommonFields}
        {fields}
        {members}
            </class>
            <record name="{name}Class" c:type="Gst{name}Class" glib:is-gtype-struct-for="{name}">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="render">
                <callback name="render">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="self" transfer-ownership="none">
                      <type name="{name}" c:type="Gst{name}*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>Runs the generator over the fixture with one overlay.</summary>
    /// <param name="entries">
    /// The overlay keys of the case, as JSON, closing the object the allowlist of
    /// the subclassing surface opened.
    /// </param>
    /// <param name="subclassable">The allowlist of the subclassing surface.</param>
    /// <param name="allowErrors">
    /// <see langword="false"/> for the one case whose subject is an entry the
    /// allowlist accepts.
    /// </param>
    /// <returns>The run.</returns>
    private static FixtureRun Run(string entries, string? subclassable = null, bool allowErrors = true)
    {
        // Widget carries every shape a refusal is read off; the other classes
        // carry one layout fault each, so that the entry of a case is the only
        // thing the run has to report.
        string body =
            Class("Widget", RefusedFields, ShapeMethod)
            + "\n"
            + Class("Gadget")
            + "\n"
            + Class(
                "Gizmo",
                """
                      <field name="stamp">
                        <type name="guint16" c:type="guint16"/>
                      </field>
                """)
            + "\n"
            + Class(
                "Doodad",
                """
                      <field name="flags" bits="1">
                        <type name="guint" c:type="guint"/>
                      </field>
                """)
            + "\n"
            + Class(
                "Thing",
                """
                      <union name="ABI" c:type="ABI">
                        <field name="_gst_reserved" readable="0" private="1">
                          <array zero-terminated="0" fixed-size="4">
                            <type name="gpointer" c:type="gpointer"/>
                          </array>
                        </field>
                      </union>
                """);

        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                "{ " + (subclassable ?? Subclassable) + ", " + entries);
            return Fixture.Run(
                body,
                Overlays.Load(directory),
                extraNamespaces: ExtraNamespaces,
                allowErrors: allowErrors,
                namespaceName: "GstBase");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
