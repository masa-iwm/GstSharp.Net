using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>fieldSkips</c> key: what it takes off the field ledger of the skip
/// report, the accessor it keeps the generator from emitting, and the entries
/// it reports as stale.
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

    private static FixtureRun RunWithOverlay(string fixups)
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
