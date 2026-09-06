using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The reasons the ledger reports for the members of a record whose wrapper is
/// hand written. Nothing of such a record is planned, but the skip rules still
/// have something to say about each member, and what they say is a fact about
/// the member rather than about the wrapper.
/// </summary>
public sealed class HandWrittenRecordLedgerTests
{
    /// <summary>
    /// The hand written mini object base, with one member the rules reject on
    /// its own - it is <c>introspectable="0"</c> - and one they would have let
    /// through.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <method name="init" c:identifier="gst_mini_object_init" introspectable="0">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </method>
              <method name="lock" c:identifier="gst_mini_object_lock">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
              </method>
              <method name="ref" c:identifier="gst_mini_object_ref" shadowed-by="ref_full">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
              </method>
              <method name="unref" c:identifier="gst_mini_object_unref" moved-to="MiniObject.unref_full">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </method>
            </record>
        """;

    [Fact]
    public void AMemberTheRulesRejectOnItsOwnKeepsThatReason()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal("NotIntrospectable", Bucket(run.Result.SkipReport, "gst_mini_object_init"));
    }

    [Fact]
    public void AMemberTheRulesWouldHaveLetThroughFallsBackToTheCatchAll()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal("UnsupportedSignature", Bucket(run.Result.SkipReport, "gst_mini_object_lock"));
    }

    /// <summary>
    /// A member the gir shadows or moves elsewhere falls back to the catch all
    /// reason too, because the two answers that say <em>emitted under another
    /// declaration</em> cannot be true of a record that emits nothing.
    /// </summary>
    /// <param name="symbol">The member to look up.</param>
    [Theory]
    [InlineData("gst_mini_object_ref")]
    [InlineData("gst_mini_object_unref")]
    public void AMemberTheGirShadowsOrMovesIsNotReportedAsEmittedElsewhere(string symbol)
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal("UnsupportedSignature", Bucket(run.Result.SkipReport, symbol));
    }

    /// <summary>Finds the reason heading a symbol is listed under.</summary>
    /// <param name="report">The skip report.</param>
    /// <param name="symbol">The symbol to locate.</param>
    /// <returns>The reason name.</returns>
    private static string Bucket(string report, string symbol)
    {
        string entry = "- `" + symbol + "`";
        string? reason = null;
        foreach (string line in report.Split('\n'))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                reason = line[4..].Split(' ')[0];
            }
            else if (line.TrimEnd().Equals(entry, StringComparison.Ordinal))
            {
                return reason ?? throw new InvalidOperationException(
                    $"'{symbol}' is listed before any reason heading.");
            }
        }

        throw new InvalidOperationException($"The report lists no '{symbol}'. It reads:\n{report}");
    }
}
