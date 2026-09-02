using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>fieldAnnotations</c> key: the nullability of a record field, which no
/// gir carries, and the entries that are reported as stale.
/// </summary>
public sealed class FieldAnnotationTests
{
    /// <summary>
    /// One opaque record carrying the shapes an entry is read on: two string
    /// fields, a field the gir keeps to the C implementation, and a string
    /// field behind a reserved ABI union.
    /// </summary>
    private const string Body =
        """
            <record name="Widget" c:type="GstWidget" opaque="1">
              <field name="name" writable="1">
                <type name="utf8" c:type="const gchar*"/>
              </field>
              <field name="title" writable="1">
                <type name="utf8" c:type="gchar*"/>
              </field>
              <field name="priv" readable="0" private="1">
                <type name="utf8" c:type="gchar*"/>
              </field>
              <union name="ABI" c:type="ABI">
                <record name="abi" c:type="abi">
                  <field name="nick" writable="1">
                    <type name="utf8" c:type="const gchar*"/>
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
    public void AStringFieldIsCopiedOnReadAndIsNullableByDefault()
    {
        // No gir spells nullable on a field, so a string field is nullable
        // until an entry says otherwise, and the read copies rather than
        // borrows: the storage belongs to the C structure.
        FixtureRun run = RunWithOverlay("{}");
        string source = run.File("Widget.cs");

        Assert.Contains("public string? Name\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "string? value = Gst.Interop.GMarshal.PtrToStringUtf8(((WidgetRaw*)Handle)->Name);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("- `Widget.name` — Pointer", run.Result.SkipReport, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNullableEntryDropsTheQuestionMarkAndThrowsOnTheNullPointer()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.name": { "nullable": false, "$comment": "gstwidget.c:1" } } }
            """);
        string source = run.File("Widget.cs");

        Assert.Contains("public string Name\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "throw new System.InvalidOperationException(\"The 'name' field of GstWidget is null.\")",
            source,
            StringComparison.Ordinal);

        // The other string field is untouched, which is what makes the entry a
        // statement about one field rather than about the record.
        Assert.Contains("public string? Title\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0026");
    }

    [Fact]
    public void AMemberOfAReservedAbiUnionIsAddressedByTheFieldAlone()
    {
        // The union and the structure inside it are transparent in the key, the
        // same way they are in the name of the accessor.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.nick": { "nullable": false, "$comment": "gstwidget.c:2" } } }
            """);

        Assert.Contains("public string Nick\n", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0026");
    }

    [Fact]
    public void AnEntryThatMatchesNoFieldIsReported()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.colour": { "nullable": false, "$comment": "gstwidget.c:3" } } }
            """);

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
        Assert.Contains("GstWidget.colour", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryOnAFieldTheGirKeepsToTheImplementationIsReported()
    {
        // Nothing reads such a field, so there is no accessor for the entry to
        // correct and the claim it carries about the C implementation would sit
        // in the overlays unchecked.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.priv": { "nullable": false, "$comment": "gstwidget.c:4" } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
    }

    [Fact]
    public void AnEntryThatStatesTheDefaultIsReportedAndChangesNothing()
    {
        // Nullable is what every string field already is, so an entry that
        // states it corrects nothing and reads as a decision that was never
        // taken.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.name": { "nullable": true } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
        Assert.Contains("public string? Name\n", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatStatesNothingIsReportedAndChangesNothing()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.name": { } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
        Assert.Contains("public string? Name\n", run.File("Widget.cs"), StringComparison.Ordinal);
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
