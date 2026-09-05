using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The gate on the one part of a class struct mirror that is computed rather
/// than transcribed: the block a <c>&lt;union&gt;</c> is laid out as.
/// </summary>
/// <remarks>
/// The union of a GES class struct is a reserved array of pointers with a
/// record of later members overlapping it, and the mirror spells the record and
/// pads it back out to the size of the reserve. Every shape the corpus does not
/// contain is a shape whose filler would be computed from an assumption, and a
/// filler that is one pointer short moves every slot of every derived class.
/// This asserts that each of those shapes stops the run instead.
/// </remarks>
public sealed class ClassStructUnionTests
{
    private const string Allowlist = """{ "subclassable": ["Gst.Widget"] }""";

    private const string PointerReserve =
        """
                <field name="_reserved" writable="1">
                  <array zero-terminated="0" fixed-size="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </array>
                </field>
        """;

    [Fact]
    public void AUnionWhoseRemainderIsNotAWholeNumberOfPointersStopsTheRun()
    {
        // A reserve of three ints is twelve bytes and the record spends eight
        // of them, which leaves four: the filler is an array of pointers and
        // there is no half of one.
        FixtureRun run = Run(
            Union(
                """
                        <field name="_reserved" writable="1">
                          <array zero-terminated="0" fixed-size="3">
                            <type name="gint" c:type="gint"/>
                          </array>
                        </field>
                """,
                Record(
                    """
                              <field name="handle" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                    """)));

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0043");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("not a whole number of pointers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnionWhoseMembersDoNotFitInTheBlockStopsTheRun()
    {
        // Three ints end four bytes past a pointer boundary and the reserve is
        // one pointer, so the block measures twelve bytes while the mirror has
        // padded its members out to sixteen. This is the branch whose message
        // used to claim the remainder was the problem.
        FixtureRun run = Run(
            Union(
                PointerReserve,
                Record(
                    """
                              <field name="first" writable="1">
                                <type name="gint" c:type="gint"/>
                              </field>
                              <field name="second" writable="1">
                                <type name="gint" c:type="gint"/>
                              </field>
                              <field name="third" writable="1">
                                <type name="gint" c:type="gint"/>
                              </field>
                    """)));

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0043");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("do not fit in the block", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordLargerThanTheReserveIsLaidOutWithNoFiller()
    {
        // The reserve is not what measures the union: the largest member is.
        // Two pointers overlapping a reserve of one leave nothing to pad, and
        // the mirror says so by writing no filler at all rather than by
        // reporting anything.
        FixtureRun run = Run(
            Union(
                PointerReserve,
                Record(
                    """
                              <field name="first" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                              <field name="second" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                    """)),
            allowErrors: false);

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0043");

        string mirror = run.File("ClassStructs/WidgetClassRaw.cs");
        Assert.Contains("internal nint First;", mirror, StringComparison.Ordinal);
        Assert.Contains("internal nint Second;", mirror, StringComparison.Ordinal);
        Assert.DoesNotContain("Filler", mirror, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRecordInsideAUnionStopsTheRun()
    {
        // Only one member of a union can be spelled in a sequential mirror.
        // Which of two records carries the offsets that have to come out right
        // is not something the gir says, so the run stops rather than picking.
        FixtureRun run = Run(
            Union(
                """
                        <field name="_reserved" writable="1">
                          <array zero-terminated="0" fixed-size="2">
                            <type name="gpointer" c:type="gpointer"/>
                          </array>
                        </field>
                """,
                Record(
                    """
                              <field name="first" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                    """)
                + "\n"
                + Record(
                    """
                              <field name="second" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                    """)));

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0043");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("declares 2 records", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AUnionThatDoesNotStartOnAPointerBoundaryStopsTheRun()
    {
        // The filler is measured in pointers from the start of the block, so a
        // block that starts four bytes into one is a block whose members the
        // mirror would place at the wrong offsets. Every union of the corpus
        // follows pointers; this one follows an int.
        FixtureRun run = Run(
            Union(
                """
                        <field name="_reserved" writable="1">
                          <array zero-terminated="0" fixed-size="2">
                            <type name="gpointer" c:type="gpointer"/>
                          </array>
                        </field>
                """,
                Record(
                    """
                              <field name="first" writable="1">
                                <type name="gpointer" c:type="gpointer"/>
                              </field>
                    """),
                """
                      <field name="stride" writable="1">
                        <type name="gint" c:type="gint"/>
                      </field>
                """));

        Diagnostic error = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0043");
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Contains("follows 4 bytes", error.Message, StringComparison.Ordinal);
        Assert.Contains("pointer boundary", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Wraps union member fields in the <c>abi</c> record GES writes.</summary>
    /// <param name="fields">The fields of the record.</param>
    /// <returns>The gir fragment.</returns>
    private static string Record(string fields) =>
        "        <record name=\"abi\" c:type=\"abi\">\n"
        + fields + "\n"
        + "        </record>";

    /// <summary>
    /// A subclassable class whose class struct carries one trailing union,
    /// which is the shape of every GES class struct that has one.
    /// </summary>
    /// <param name="reserve">The reserved array field of the union.</param>
    /// <param name="records">The records declared inside the union.</param>
    /// <param name="leading">Fields of the class struct in front of the union.</param>
    /// <returns>The members of the <c>Gst</c> namespace.</returns>
    private static string Union(string reserve, string records, string leading = "") =>
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
        """
        + "\n" + (leading.Length == 0 ? string.Empty : leading + "\n")
        + "      <union name=\"ABI\" c:type=\"ABI\">\n"
        + reserve + "\n" + records + "\n"
        + "      </union>\n"
        + "    </record>";

    /// <summary>Runs the generator over one of the fixtures above.</summary>
    /// <param name="body">The members of the <c>Gst</c> namespace.</param>
    /// <param name="allowErrors">
    /// <see langword="false"/> for the one fixture whose subject is a layout
    /// the mirror is expected to accept.
    /// </param>
    /// <returns>The run.</returns>
    private static FixtureRun Run(string body, bool allowErrors = true)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), Allowlist);
            return Fixture.Run(body, Overlays.Load(directory), allowErrors: allowErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
