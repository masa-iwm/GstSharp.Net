using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>fieldSkips</c> key: what it takes off the field ledgers of the skip
/// report, the accessor it keeps the generator from emitting, and the entries
/// it reports as stale. The ledger of the instance fields of a class is read
/// here too, because the entries address one the way they address a record
/// field and the rules that leave a field out are what they are read behind.
/// </summary>
public sealed class FieldSkipTests
{
    /// <summary>
    /// One opaque record carrying the shapes an entry is read on: a scalar
    /// field that gets an accessor of its own, a pointer field that the ledger
    /// counts as unbound, a field the gir keeps to the C implementation and
    /// that the ledger never counted, and a field behind a reserved ABI union.
    /// </summary>
    private const string Body =
        """
            <record name="Widget" c:type="GstWidget" opaque="1">
              <field name="width" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="data" writable="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="priv" readable="0" private="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <union name="ABI" c:type="ABI">
                <record name="abi" c:type="abi">
                  <field name="depth" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                </record>
                <field name="_gst_reserved" readable="0" private="1">
                  <array zero-terminated="0" fixed-size="4">
                    <type name="gpointer" c:type="gpointer"/>
                  </array>
                </field>
              </union>
            </record>
        """;

    [Fact]
    public void WithoutAnEntryEveryFieldIsBoundOrLedgered()
    {
        // The baseline the entries move away from: two accessors, the pointer
        // field on the ledger, and a section for the answered fields that is
        // empty rather than absent.
        FixtureRun run = RunWithOverlay("{}");

        Assert.Contains("public int Width\n", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("public int Depth\n", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("- `Widget.data` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Contains("## Fields exposed elsewhere (0)\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AnExposedFieldLosesItsAccessorAndIsListedWithTheMemberThatAnswersIt()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.width": { "exposedBy": "GetWidth" } } }
            """);

        Assert.DoesNotContain("public int Width\n", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            "- `Widget.width` — GetWidth\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AHandBoundFieldLeavesTheLedgerOfTheFieldsNothingBinds()
    {
        // The pointer field is the one the ledger counts, so this is where the
        // entry changes a number rather than only a section.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.data": { "handBound": true } } }
            """);

        Assert.DoesNotContain("- `Widget.data` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Contains("- `Widget.data` — hand written\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.DroppedFieldCount("Gst"));
    }

    [Fact]
    public void AMemberOfAReservedAbiUnionIsAddressedByTheFieldAlone()
    {
        // The union and the structure inside it are transparent in the key, the
        // same way they are in the name of the accessor. With nothing left to
        // read them, the mirror declares the reserved space and no more.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.depth": { "exposedBy": "GetDepth" } } }
            """);

        string source = run.File("Widget.cs");
        Assert.DoesNotContain("public int Depth\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ABIMembers", source, StringComparison.Ordinal);
        Assert.Contains("internal ABIArray ABI;\n", source, StringComparison.Ordinal);
        Assert.Contains("- `Widget.depth` — GetDepth\n", run.Result.SkipReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatMatchesNoFieldIsReported()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.colour": { "exposedBy": "GetColour" } } }
            """);

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0025");
        Assert.Contains("GstWidget.colour", stale.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AnEntryOnAFieldTheLedgerNeverCountedIsReported()
    {
        // What the gir keeps to the C implementation carries no API in C
        // either, so it is not on the ledger and there is nothing for an entry
        // to take off it. Accepting one would claim a binding for reserved
        // space; the entry is reported as stale instead.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.priv": { "handBound": true } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0025");
        Assert.DoesNotContain("- `Widget.priv`", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AnEntryThatStatesBothHalvesIsReportedAndChangesNothing()
    {
        // Two different answers to who hands the field out. Neither is applied,
        // because the ledger would go quiet on the strength of a claim that
        // contradicts itself.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.data": { "exposedBy": "GetData", "handBound": true } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0025");
        Assert.Contains("- `Widget.data` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AnEntryThatStatesNothingIsReportedAndChangesNothing()
    {
        // An entry with neither half says nothing about the field, so the
        // ledger keeps counting it and the entry is reported rather than
        // quietly taking a field off the measurement.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldSkips": { "GstWidget.data": { } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0025");
        Assert.Contains("- `Widget.data` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
    }

    /// <summary>
    /// One class carrying every shape the class field ledger reads: the
    /// instance structure of the base class under a name that says nothing, a
    /// scalar, a pointer, a structure laid into the instance, a union laid into
    /// it the same way, a function pointer slot, the two shapes the ledger
    /// leaves out, and a union the structure grew by.
    /// </summary>
    private const string ClassBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <field name="head">
                <type name="GObject.Object" c:type="GObject"/>
              </field>
              <field name="width">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="data">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="shape">
                <type name="Shape" c:type="GstShape"/>
              </field>
              <field name="mutex">
                <type name="Latch" c:type="GstLatch"/>
              </field>
              <field name="render">
                <callback name="render" c:type="render">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                </callback>
              </field>
              <field name="priv" readable="0" private="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="_gst_reserved">
                <array zero-terminated="0" fixed-size="4">
                  <type name="gpointer" c:type="gpointer"/>
                </array>
              </field>
              <union name="ABI" c:type="ABI">
                <record name="abi" c:type="abi">
                  <field name="depth">
                    <type name="gint" c:type="gint"/>
                  </field>
                </record>
              </union>
            </class>
            <record name="Shape" c:type="GstShape">
              <field name="sides" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <union name="Latch" c:type="GstLatch">
              <field name="held" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </union>
        """;

    [Fact]
    public void TheInstanceFieldsOfAClassAreLedgeredUnderTheirShape()
    {
        // Nothing of a class is projected, so the ledger counts every public
        // field of one and the shape is all it has to say. The union is one
        // line under its own name: there is no mirror to choose a member of.
        FixtureRun run = RunClassWithOverlay("{}");
        string report = run.Result.SkipReport;

        Assert.Equal(6, run.Result.Census.ClassFieldCount("Gst"));
        Assert.Contains("## Class fields (6)\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Widget.width` — Scalar\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Widget.data` — Pointer\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Widget.shape` — EmbeddedStruct\n", report, StringComparison.Ordinal);

        // A union laid into the instance by value is the same shape as a record
        // laid into it: the catch all keeps neither.
        Assert.Contains("- `Widget.mutex` — EmbeddedStruct\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget.mutex` — Other\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Widget.render` — Callback\n", report, StringComparison.Ordinal);
        Assert.Contains("- `Widget.ABI` — Union\n", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBaseInstanceThePaddingAndThePrivateFieldsAreLeftOut()
    {
        // The instance structure of the base class is named nothing in
        // particular here, which is the point: what says it is the chain is the
        // shape of the field and not its name or its position.
        FixtureRun run = RunClassWithOverlay("{}");
        string report = run.Result.SkipReport;

        Assert.DoesNotContain("- `Widget.head`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget.priv`", report, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget._gst_reserved`", report, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExposedClassFieldIsListedWithTheMemberThatAnswersIt()
    {
        // The overlays address a field of a class the way they address one of a
        // record, and the entry moves it into the same section.
        FixtureRun run = RunClassWithOverlay(
            """
            { "fieldSkips": { "GstWidget.width": { "exposedBy": "Width" } } }
            """);

        Assert.Equal(5, run.Result.Census.ClassFieldCount("Gst"));
        Assert.Contains("- `Widget.width` — Width\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.ExposedFieldCount());
    }

    [Fact]
    public void AnEntryOnAClassFieldThatDoesNotExistIsReported()
    {
        FixtureRun run = RunClassWithOverlay(
            """
            { "fieldSkips": { "GstWidget.colour": { "exposedBy": "GetColour" } } }
            """);

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0025");
        Assert.Contains("GstWidget.colour", stale.Message, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());

        // The entry takes nothing off the ledger either: a name that matches no
        // field of the class leaves every field of it counted.
        Assert.Equal(6, run.Result.Census.ClassFieldCount("Gst"));
        Assert.Contains("- `Widget.width` — Scalar\n", run.Result.SkipReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryOnAClassFieldTheLedgerNeverCountedIsReported()
    {
        // The overlays are asked behind every rule that leaves a field out, so
        // an entry on the base instance claims a binding for the inheritance
        // chain, and one on what the gir keeps to the C implementation or on
        // padding claims a binding for reserved space. All three are reported
        // instead, and none of them moves a field into the section above.
        FixtureRun run = RunClassWithOverlay(
            """
            {
              "fieldSkips": {
                "GstWidget.head": { "handBound": true },
                "GstWidget.priv": { "handBound": true },
                "GstWidget._gst_reserved": { "handBound": true }
              }
            }
            """);

        Assert.Equal(3, run.Result.Diagnostics.Count(static d => d.Code == "GEN0025"));
        Assert.DoesNotContain("- `Widget.head`", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget.priv`", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget._gst_reserved`", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.ExposedFieldCount());
    }

    private static FixtureRun RunClassWithOverlay(string fixups) => RunWithOverlay(fixups, ClassBody);

    private static FixtureRun RunWithOverlay(string fixups, string? body = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body ?? Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
