using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The stale reports of the two overlay keys that had none: the <c>skip</c>
/// list in the three spellings the signal loop does not read, and the
/// <c>rename</c> table.
/// </summary>
/// <remarks>
/// An entry of either kind that matches nothing keeps nothing off the surface
/// and renames nothing, while it still reads as a decision that is in force.
/// The whole point of both reports is that the run says so, so each spelling is
/// asserted from both sides: an entry that does its work is silent, and one
/// that names something the gir does not declare is reported.
/// </remarks>
public sealed class StaleOverlayEntryTests
{
    /// <summary>
    /// One class carrying a method and a property, and one enumeration beside
    /// it, which is every shape a skip entry other than a signal addresses.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <property name="label" writable="1" transfer-ownership="none">
                <type name="utf8" c:type="gchar*"/>
              </property>
              <method name="poke" c:identifier="gst_widget_poke">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
            <enumeration name="Colour" c:type="GstColour">
              <member name="red" value="0" c:identifier="GST_COLOUR_RED"/>
            </enumeration>
        """;

    [Fact]
    public void ASkippedCallableIsNotReported()
    {
        FixtureRun run = RunWithOverlay("""{ "skip": ["gst_widget_poke"] }""");

        Assert.DoesNotContain("Poke(", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0056");
    }

    [Fact]
    public void ASkipEntryOnACallableTheGirDoesNotDeclareIsReported()
    {
        // The entry that outlives the function it was written for: nothing is
        // kept off the surface, and the next reader of the overlays reads a
        // decision about a symbol that is not there.
        FixtureRun run = RunWithOverlay("""{ "skip": ["gst_widget_prod"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0056");
        Assert.Contains("gst_widget_prod", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkippedTypeIsNotReported()
    {
        FixtureRun run = RunWithOverlay("""{ "skip": ["Gst.Widget"] }""");

        Assert.False(run.HasFile("Widget.cs"));
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0056");
    }

    [Fact]
    public void ASkipEntryOnATypeTheGirDoesNotDeclareIsReported()
    {
        FixtureRun run = RunWithOverlay("""{ "skip": ["Gst.Gadget"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0056");
        Assert.Contains("Gst.Gadget", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkippedEnumerationIsNotReported()
    {
        // The enumeration emitter has a loop of its own, so the shape is
        // asserted on it as well as on the class emitter above.
        FixtureRun run = RunWithOverlay("""{ "skip": ["Gst.Colour"] }""");

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0056");
    }

    [Fact]
    public void ASkippedPropertyIsNotReported()
    {
        FixtureRun run = RunWithOverlay("""{ "skip": ["Gst.Widget:label"] }""");

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0056");
    }

    [Fact]
    public void ASkipEntryOnAPropertyTheGirDoesNotDeclareIsReported()
    {
        FixtureRun run = RunWithOverlay("""{ "skip": ["Gst.Widget:colour"] }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0056");
        Assert.Contains("Gst.Widget:colour", stale.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkipEntryThatIsHandBoundIsNotReported()
    {
        // The hand bound ledger rewrites the reason the skip is counted under,
        // which says nothing about whether the skip entry matched. It did, so
        // the pair is silent from both sides.
        FixtureRun run = RunWithOverlay(
            """
            { "skip": ["gst_widget_poke"], "handBound": ["gst_widget_poke"] }
            """);

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0056");
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0023");
    }

    [Fact]
    public void ARenameThatNamedSomethingIsNotReported()
    {
        FixtureRun run = RunWithOverlay("""{ "rename": { "gst_widget_poke": "Prod" } }""");

        Assert.Contains("public void Prod(", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0057");
    }

    [Fact]
    public void ARenameThatNamedNothingIsReported()
    {
        FixtureRun run = RunWithOverlay("""{ "rename": { "gst_widget_prod": "Prod" } }""");

        Diagnostic stale = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0057");
        Assert.Contains("gst_widget_prod", stale.Message, StringComparison.Ordinal);
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
