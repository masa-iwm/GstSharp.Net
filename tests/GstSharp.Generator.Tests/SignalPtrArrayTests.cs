using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The second container a signal carries: a <c>GPtrArray</c> of GObjects the
/// emission lends the handler, read out into an array of wrappers before the
/// handler runs, and one the handler answers with, built fresh with a minted
/// reference per element. Every other element and every other transfer keeps
/// the signal off the surface.
/// </summary>
public sealed class SignalPtrArrayTests
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
    /// The shape <c>GESTimeline::select-tracks-for-object</c> has: a pointer
    /// array of objects the handler hands over.
    /// </summary>
    private const string ReturnedBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="select-widgets" when="last">
                <return-value transfer-ownership="full">
                  <doc xml:space="preserve">The widgets to use</doc>
                  <array name="GLib.PtrArray">
                    <type name="Widget"/>
                  </array>
                </return-value>
                <parameters>
                  <parameter name="item" transfer-ownership="none">
                    <type name="Widget"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same signal answering a pointer array the emission only borrows,
    /// which leaves nobody owning the container the handler allocated.
    /// </summary>
    private const string BorrowedReturnBody =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="select-widgets" when="last">
                <return-value transfer-ownership="none">
                  <array name="GLib.PtrArray">
                    <type name="Widget"/>
                  </array>
                </return-value>
                <parameters>
                  <parameter name="item" transfer-ownership="none">
                    <type name="Widget"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The interface an element of the two fixtures below is declared as. It is
    /// a real emitted type, so nothing but the element rule keeps those signals
    /// off the surface.
    /// </summary>
    private const string SizerInterface =
        """
            <interface name="Sizer" c:type="GstSizer" glib:type-name="GstSizer" glib:get-type="gst_sizer_get_type">
              <method name="get_size" c:identifier="gst_sizer_get_size">
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="sizer" transfer-ownership="none">
                    <type name="Sizer" c:type="GstSizer*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </interface>
        """;

    /// <summary>
    /// A borrowed pointer array whose element is an interface rather than a
    /// class, which the two readers cannot be instantiated over.
    /// </summary>
    private const string InterfaceElementBody =
        SizerInterface
        + """

            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="tracks-changed" when="first">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="tracks" transfer-ownership="none">
                    <array name="GLib.PtrArray">
                      <type name="Sizer"/>
                    </array>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// The same element on the side the handler answers with.
    /// </summary>
    private const string InterfaceElementReturnBody =
        SizerInterface
        + """

            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <glib:signal name="select-widgets" when="last">
                <return-value transfer-ownership="full">
                  <array name="GLib.PtrArray">
                    <type name="Sizer"/>
                  </array>
                </return-value>
                <parameters>
                  <parameter name="item" transfer-ownership="none">
                    <type name="Widget"/>
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

    /// <summary>
    /// A pointer array the handler hands over is built fresh, with one minted
    /// reference per element, and is always nullable: C spells "no objects" as
    /// the null pointer and no annotation of the corpus states it.
    /// </summary>
    [Fact]
    public void APointerArrayTheHandlerHandsOverIsPlanned()
    {
        FixtureRun run = Fixture.Run(ReturnedBody);
        string source = run.File("Widget.cs");

        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        Assert.Contains(
            "public delegate Gst.Widget[]? SelectWidgetsHandler(object? sender, Gst.Widget.SelectWidgetsSignalArgs args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static nint SelectWidgetsTrampoline(nint instance, nint item, nint userData)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return Gst.GLib.PtrArray.FromObjects<Gst.Widget>(result);",
            source,
            StringComparison.Ordinal);

        // A null or a disposed element leaves the emission with the answer a
        // handler that threw would have left it.
        Assert.Contains("/// the exception trap and the emission is answered", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pointer array the emission only borrows keeps the signal off the
    /// surface: nobody would own the container the handler allocated.
    /// </summary>
    [Fact]
    public void APointerArrayTheEmissionOnlyBorrowsIsRefused()
    {
        FixtureRun run = Fixture.Run(BorrowedReturnBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("SelectWidgets", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pointer array of an interface element keeps the signal off the
    /// surface: the two readers are written over a class, not over an
    /// interface a wrapper only implements.
    /// </summary>
    [Fact]
    public void ABorrowedPointerArrayOfInterfaceElementsIsRefused()
    {
        FixtureRun run = Fixture.Run(InterfaceElementBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("TracksChanged", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>The same element on the side the handler answers with.</summary>
    [Fact]
    public void APointerArrayOfInterfaceElementsTheHandlerHandsOverIsRefused()
    {
        FixtureRun run = Fixture.Run(InterfaceElementReturnBody);

        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain("SelectWidgets", run.File("Widget.cs"), StringComparison.Ordinal);
    }
}
