using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The one container a signal handler is handed: a NULL terminated vector of
/// strings the emission lends it. Every other array shape keeps the signal off
/// the surface, because reading it needs a length the trampoline has no way of
/// seeing or an element the runtime has no reader for.
/// </summary>
public sealed class SignalStringArrayArgumentTests
{
    /// <summary>
    /// The shape <c>GstRTSPClient::check-requirements</c> has: a borrowed
    /// vector of strings beside a plain argument, and a string the handler
    /// hands over.
    /// </summary>
    private const string VectorBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="check-names" when="last">
                <return-value transfer-ownership="full">
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <parameter name="names" transfer-ownership="none">
                    <doc xml:space="preserve">a NULL-terminated array of strings</doc>
                    <array c:type="gchar**">
                      <type name="utf8" c:type="gchar*"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal with the vector counted by a second argument instead of
    /// terminated, which is the shape a method binds as a span and a signal
    /// binds not at all.
    /// </summary>
    private const string CountedBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="check-names" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="names" transfer-ownership="none">
                    <array c:type="gchar**" length="1">
                      <type name="utf8" c:type="gchar*"/>
                    </array>
                  </parameter>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal with a vector the emission hands over. A handler owns
    /// nothing it is passed and no place in an emission would free it, so the
    /// signal stays off the surface.
    /// </summary>
    private const string OwnedBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="check-names" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="names" transfer-ownership="full">
                    <array c:type="gchar**">
                      <type name="utf8" c:type="gchar*"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A NULL terminated vector of strings reaches the handler as an array of
    /// its own, read out of the vector while the emission still holds it.
    /// </summary>
    [Fact]
    public void AZeroTerminatedStringVectorIsPlanned()
    {
        FixtureRun run = Fixture.Run(VectorBody);
        string source = run.File("Widget.cs");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        Assert.Contains("public string[] Names { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static nint CheckNamesTrampoline(nint instance, nint* names, nint userData)",
            source,
            StringComparison.Ordinal);

        // The vector is borrowed for the length of the emission, so nothing of
        // it is freed here; the helper is the one an inbound vector of a
        // callable is read with.
        Assert.Contains(
            "string[] namesValue = Gst.Interop.GMarshal.StrvToArray((nint)names, free: false) ?? [];",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>A length counted vector keeps the signal off the surface.</summary>
    [Fact]
    public void ALengthCountedArrayIsRefused()
    {
        FixtureRun run = Fixture.Run(CountedBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("CheckNames", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>A vector the emission hands over keeps the signal off the surface.</summary>
    [Fact]
    public void AVectorTheEmissionHandsOverIsRefused()
    {
        FixtureRun run = Fixture.Run(OwnedBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("CheckNames", run.File("Widget.cs"), StringComparison.Ordinal);
    }
}
