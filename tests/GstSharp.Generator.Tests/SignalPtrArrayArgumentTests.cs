using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The second container a signal handler is handed: a <c>GPtrArray</c> of
/// GObjects the emission lends it, read out into an array of wrappers before
/// the handler runs. Every other element and every other transfer keeps the
/// signal off the surface.
/// </summary>
public sealed class SignalPtrArrayArgumentTests
{
    /// <summary>
    /// The shape <c>GESLayer::active-changed</c> has: a borrowed pointer array
    /// of objects beside a plain argument.
    /// </summary>
    private const string BorrowedBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="tracks-changed" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="active" transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </parameter>
                  <parameter name="tracks" transfer-ownership="none">
                    <doc xml:space="preserve">A list of widgets</doc>
                    <array name="GLib.PtrArray">
                      <type name="Widget"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal carrying a pointer array of a boxed element, which the
    /// runtime has no reader for.
    /// </summary>
    private const string BoxedElementBody =
        """
            <record name="Thing" c:type="GstThing" glib:type-name="GstThing" glib:get-type="gst_thing_get_type">
              <field name="value" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="tracks-changed" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="tracks" transfer-ownership="none">
                    <array name="GLib.PtrArray">
                      <type name="Thing"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal handing the container over. A handler owns nothing it is
    /// passed and no place in an emission would free the container, so the
    /// signal stays off the surface.
    /// </summary>
    private const string ContainerBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="tracks-changed" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="tracks" transfer-ownership="container">
                    <array name="GLib.PtrArray">
                      <type name="Widget"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A borrowed pointer array of objects reaches the handler as an array of
    /// its own, read out of the container while the emission still holds it.
    /// </summary>
    [Fact]
    public void ABorrowedPointerArrayOfObjectsIsPlanned()
    {
        FixtureRun run = Fixture.Run(BorrowedBody);
        string source = run.File("Widget.cs");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        Assert.Contains("public Gst.Widget[] Tracks { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "private static void TracksChangedTrampoline(nint instance, int active, nint tracks, nint userData)",
            source,
            StringComparison.Ordinal);

        // The container is borrowed for the length of the emission, so nothing
        // of it is freed here and nothing of it is retained: every element is
        // read out into the wrapper the interning table hands over.
        Assert.Contains(
            "Gst.Widget[] tracksValue = Gst.GLib.PtrArray.ToArray<Gst.Widget>(tracks);",
            source,
            StringComparison.Ordinal);

        // The array outlives the emission, which the documentation says,
        // because the emitting library frees its own the moment it ends.
        Assert.Contains("/// A snapshot: the elements are read out of the array", source, StringComparison.Ordinal);
    }

    /// <summary>A pointer array of a boxed element keeps the signal off the surface.</summary>
    [Fact]
    public void APointerArrayOfBoxedElementsIsRefused()
    {
        FixtureRun run = Fixture.Run(BoxedElementBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("TracksChanged", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>A container the emission hands over keeps the signal off the surface.</summary>
    [Fact]
    public void APointerArrayTheEmissionHandsOverIsRefused()
    {
        FixtureRun run = Fixture.Run(ContainerBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("TracksChanged", run.File("Widget.cs"), StringComparison.Ordinal);
    }
}
