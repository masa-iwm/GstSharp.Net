using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The gate on the required slots of the subclassing surface: a base class the
/// emitter knows calls a slot unguarded must find that slot on the emitted
/// surface, or the registration cannot check a subclass declares it.
/// </summary>
/// <remarks>
/// The table of required slots is keyed by the qualified name of the class, and
/// every key in it names a class of <c>GstBase</c>, <c>GstAudio</c> or
/// <c>GstVideo</c>. A fixture therefore has to declare the module the key names
/// rather than <c>Gst</c>; <c>GstBase.Aggregator</c> is the smallest of them,
/// with a single required <c>aggregate</c>.
/// </remarks>
public sealed class RequiredSlotDiagnosticTests
{
    /// <summary>
    /// <c>GstBase.Aggregator</c> without its <c>aggregate</c> slot, which is
    /// what a gir that renamed the slot or an overlay that skipped it looks
    /// like: the class is subclassable and carries a class struct, it declares
    /// an unrelated slot so that it still has a surface, and the one slot the
    /// table requires is nowhere on it.
    /// </summary>
    private const string BodyWithoutTheRequiredSlot =
        """
            <class name="Aggregator" c:type="GstAggregator" parent="GObject.Object" glib:type-name="GstAggregator" glib:get-type="gst_aggregator_get_type" glib:type-struct="AggregatorClass">
              <virtual-method name="start">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="Aggregator" c:type="GstAggregator*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="AggregatorClass" c:type="GstAggregatorClass" glib:is-gtype-struct-for="Aggregator">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="start">
                <callback name="start">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="self" transfer-ownership="none">
                      <type name="Aggregator" c:type="GstAggregator*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The same class with <c>aggregate</c> declared, which is the shape the
    /// vendored gir has.
    /// </summary>
    private const string BodyWithTheRequiredSlot =
        """
            <class name="Aggregator" c:type="GstAggregator" parent="GObject.Object" glib:type-name="GstAggregator" glib:get-type="gst_aggregator_get_type" glib:type-struct="AggregatorClass">
              <virtual-method name="aggregate">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="self" transfer-ownership="none">
                    <type name="Aggregator" c:type="GstAggregator*"/>
                  </instance-parameter>
                  <parameter name="timeout" transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="AggregatorClass" c:type="GstAggregatorClass" glib:is-gtype-struct-for="Aggregator">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="aggregate">
                <callback name="aggregate">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="self" transfer-ownership="none">
                      <type name="Aggregator" c:type="GstAggregator*"/>
                    </parameter>
                    <parameter name="timeout" transfer-ownership="none">
                      <type name="gboolean" c:type="gboolean"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = """{ "subclassable": ["GstBase.Aggregator"] }""";

    [Fact]
    public void ARequiredSlotThatIsNotOnTheEmittedSurfaceIsReported()
    {
        FixtureRun run = Run(BodyWithoutTheRequiredSlot);

        Diagnostic missing = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0034");
        Assert.Equal(DiagnosticSeverity.Warning, missing.Severity);
        Assert.Contains("GstBase.Aggregator", missing.Message, StringComparison.Ordinal);
        Assert.Contains("aggregate", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequiredSlotThatIsOnTheEmittedSurfaceIsNotReported()
    {
        FixtureRun run = Run(BodyWithTheRequiredSlot);

        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0034");
        Assert.Equal(1, run.Result.Census.EmittedCount("GstBase", "vfunc"));
    }

    private static FixtureRun Run(string body)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), Allowlist);
            return Fixture.Run(
                body,
                Overlays.Load(directory),
                namespaceName: "GstBase",
                symbolPrefixes: "gst_base");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
