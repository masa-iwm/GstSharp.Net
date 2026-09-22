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
/// The table of required slots is keyed by the qualified name of the class:
/// <c>Gst.DeviceProvider</c> and classes of <c>GstBase</c>, <c>GstAudio</c> and
/// <c>GstVideo</c>. A fixture therefore has to declare the module the key names;
/// <c>GstBase.Aggregator</c> is the smallest of them with a pad template beside
/// it, and it is the one this gate is written against, with a single required
/// <c>aggregate</c>. <c>GstAudio.AudioEncoder</c> is the one rule that names two
/// required slots, so it carries the fixture for the order they are checked in.
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

    /// <summary>
    /// <c>Gst.DeviceProvider</c> with its <c>start</c> slot, which is the one
    /// class whose rule names a hand-written alternative: a provider may declare
    /// <c>probe</c> instead, and that slot carries no emitted surface at all.
    /// </summary>
    private const string DeviceProviderBody =
        """
            <class name="DeviceProvider" c:type="GstDeviceProvider" parent="GObject.Object" glib:type-name="GstDeviceProvider" glib:get-type="gst_device_provider_get_type" glib:type-struct="DeviceProviderClass">
              <virtual-method name="start">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="provider" transfer-ownership="none">
                    <type name="DeviceProvider" c:type="GstDeviceProvider*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="DeviceProviderClass" c:type="GstDeviceProviderClass" glib:is-gtype-struct-for="DeviceProvider">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="start">
                <callback name="start">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="provider" transfer-ownership="none">
                      <type name="DeviceProvider" c:type="GstDeviceProvider*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// <c>GstAudio.AudioEncoder</c> with both slots its rule requires, which is
    /// the only rule of the table that names two of them. The signatures are
    /// trivial on purpose: the rule looks the slots up by name, the way the
    /// <c>Aggregator</c> fixture already fakes <c>aggregate</c>.
    /// </summary>
    private const string AudioEncoderBody =
        """
            <class name="AudioEncoder" c:type="GstAudioEncoder" parent="GObject.Object" glib:type-name="GstAudioEncoder" glib:get-type="gst_audio_encoder_get_type" glib:type-struct="AudioEncoderClass">
              <virtual-method name="handle_frame">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="enc" transfer-ownership="none">
                    <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
              <virtual-method name="set_format">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="enc" transfer-ownership="none">
                    <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="AudioEncoderClass" c:type="GstAudioEncoderClass" glib:is-gtype-struct-for="AudioEncoder">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="handle_frame">
                <callback name="handle_frame">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="enc" transfer-ownership="none">
                      <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
              <field name="set_format">
                <callback name="set_format">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="enc" transfer-ownership="none">
                      <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The same class with only the first of the two required slots, which is
    /// what a gir that renamed <c>set_format</c> looks like.
    /// </summary>
    private const string AudioEncoderBodyWithoutSetFormat =
        """
            <class name="AudioEncoder" c:type="GstAudioEncoder" parent="GObject.Object" glib:type-name="GstAudioEncoder" glib:get-type="gst_audio_encoder_get_type" glib:type-struct="AudioEncoderClass">
              <virtual-method name="handle_frame">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="enc" transfer-ownership="none">
                    <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="AudioEncoderClass" c:type="GstAudioEncoderClass" glib:is-gtype-struct-for="AudioEncoder">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="handle_frame">
                <callback name="handle_frame">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="enc" transfer-ownership="none">
                      <type name="AudioEncoder" c:type="GstAudioEncoder*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    private const string Allowlist = """{ "subclassable": ["GstBase.Aggregator"] }""";

    private const string AudioEncoderAllowlist = """{ "subclassable": ["GstAudio.AudioEncoder"] }""";

    private const string DeviceProviderAllowlist = """{ "subclassable": ["Gst.DeviceProvider"] }""";

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

    [Fact]
    public void ARuleWithoutAlternativesChecksTheOneOverride()
    {
        FixtureRun run = Run(BodyWithTheRequiredSlot);
        string body = run.Member(
            "Subclassing/Aggregator.Subclass.cs",
            "private static Gst.GObject.SubclassType DefineSubclassCore(",
            project: "GstSharp.Net.Base");

        Assert.Contains("if (candidate.Function == AggregateOverride.Function)", body, StringComparison.Ordinal);
        Assert.Contains("has to declare AggregateOverride: ", body, StringComparison.Ordinal);
        Assert.DoesNotContain(" or ", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ARuleWithAlternativesChecksEitherOverride()
    {
        FixtureRun run = RunDeviceProvider();
        string body = run.Member(
            "Subclassing/DeviceProvider.Subclass.cs",
            "private static Gst.GObject.SubclassType DefineSubclassCore(");

        Assert.Contains(
            "if (candidate.Function == StartOverride.Function || candidate.Function == ProbeOverride.Function)",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "A managed GstDeviceProvider has to declare StartOverride or ProbeOverride: ",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AHandWrittenAlternativeIsNotLookedUpOnTheEmittedSurface()
    {
        FixtureRun run = RunDeviceProvider();

        // The alternative names a slot the gir marks introspectable="0", so it
        // is nowhere on the emitted surface; only the required slot itself goes
        // through the GEN0034 lookup.
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0034");
        Assert.False(run.File("Subclassing/DeviceProvider.Subclass.cs").Contains("OnProbe", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rule with two required slots checks both, in the order the table lists
    /// them, which is what keeps the first refusal the one a reader expects.
    /// </summary>
    [Fact]
    public void ATwoSlotRuleChecksBothInTableOrder()
    {
        FixtureRun run = RunAudioEncoder(AudioEncoderBody);
        string body = run.Member(
            "Subclassing/AudioEncoder.Subclass.cs",
            "private static Gst.GObject.SubclassType DefineSubclassCore(",
            project: "GstSharp.Net.Audio");

        Assert.Contains("bool declaredHandleFrame = false;", body, StringComparison.Ordinal);
        Assert.Contains("bool declaredSetFormat = false;", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("declaredHandleFrame", StringComparison.Ordinal)
            < body.IndexOf("declaredSetFormat", StringComparison.Ordinal));
        Assert.Contains(
            "has to declare SetFormatOverride: gst_audio_encoder_sink_setcaps",
            body,
            StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0034");
    }

    /// <summary>
    /// The second slot of a two slot rule goes through the same lookup as the
    /// first: a gir without it is reported once, and the check for the slot that
    /// is there is still emitted.
    /// </summary>
    [Fact]
    public void ARequiredSlotMissingFromATwoSlotRuleIsReported()
    {
        FixtureRun run = RunAudioEncoder(AudioEncoderBodyWithoutSetFormat);

        Diagnostic missing = Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0034");
        Assert.Contains("GstAudio.AudioEncoder", missing.Message, StringComparison.Ordinal);
        Assert.Contains("set_format", missing.Message, StringComparison.Ordinal);

        string body = run.Member(
            "Subclassing/AudioEncoder.Subclass.cs",
            "private static Gst.GObject.SubclassType DefineSubclassCore(",
            project: "GstSharp.Net.Audio");

        Assert.Contains("bool declaredHandleFrame = false;", body, StringComparison.Ordinal);
        Assert.DoesNotContain("declaredSetFormat", body, StringComparison.Ordinal);
    }

    private static FixtureRun Run(string body) =>
        RunWith(body, Allowlist, "GstBase");

    private static FixtureRun RunAudioEncoder(string body) =>
        RunWith(body, AudioEncoderAllowlist, "GstAudio");

    private static FixtureRun RunDeviceProvider() =>
        RunWith(DeviceProviderBody, DeviceProviderAllowlist, "Gst");

    private static FixtureRun RunWith(string body, string fixups, string namespaceName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(
                body,
                Overlays.Load(directory),
                namespaceName: namespaceName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
