using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GLib.Date</c> projection: a borrowed date crosses as a temporary the
/// member builds and frees, a produced one is read out of the pointer the call
/// allocated and released again, and every other shape stays refused.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise the feature through six members — three on
/// <c>Gst</c> and three on <c>GES</c> — whose counts the census tests freeze.
/// The fixtures here are the definition of it: they name each shape once and
/// pin the emitted text, including the refusals, which fail closed and would
/// widen silently if these were deleted.
/// </para>
/// <para>
/// Nothing of the binding stands for a <c>GDate</c>. The public type is
/// <c>System.DateOnly</c> in both directions, nullable on the way out because a
/// call may answer <c>true</c> and leave no date behind.
/// </para>
/// </remarks>
public sealed class GDateTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixtures refer to. It
    /// stands in for the vendored <c>GLib-2.0.gir</c>; the type map answers a
    /// projection for it by name, so no attribute of the declaration decides
    /// anything.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="Date" c:type="GDate" glib:type-name="GDate" glib:get-type="g_date_get_type">
              <field name="julian_days" writable="1" bits="32">
                <type name="guint" c:type="guint"/>
              </field>
            </record>
          </namespace>
        """;

    /// <summary>
    /// One class carrying every shape: a borrowed date in, a produced one out,
    /// a transferred one in, a nullable one in, a <c>ref</c> one, a caller
    /// allocated one, a returned one, a signal that carries one and a callback
    /// that receives one.
    /// </summary>
    private const string Body =
        """
            <callback name="DateFunc" c:type="GstDateFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="date" transfer-ownership="none">
                  <type name="GLib.Date" c:type="const GDate*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="set_date" c:identifier="gst_widget_set_date">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" transfer-ownership="none">
                    <doc xml:space="preserve">the date to store</doc>
                    <type name="GLib.Date" c:type="const GDate*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_date" c:identifier="gst_widget_get_date">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" direction="out" caller-allocates="0" transfer-ownership="full">
                    <doc xml:space="preserve">the date that was stored</doc>
                    <type name="GLib.Date" c:type="GDate**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_date" c:identifier="gst_widget_take_date">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" transfer-ownership="full">
                    <type name="GLib.Date" c:type="GDate*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="maybe_date" c:identifier="gst_widget_maybe_date">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" transfer-ownership="none" nullable="1">
                    <type name="GLib.Date" c:type="const GDate*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="update_date" c:identifier="gst_widget_update_date">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" direction="inout" transfer-ownership="full">
                    <type name="GLib.Date" c:type="GDate**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="fill_date" c:identifier="gst_widget_fill_date">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="date" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="GLib.Date" c:type="GDate*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="dup_date" c:identifier="gst_widget_dup_date">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GLib.Date" c:type="GDate*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_date_function" c:identifier="gst_widget_set_date_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="DateFunc" c:type="GstDateFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="dated" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="date" transfer-ownership="none">
                    <type name="GLib.Date" c:type="const GDate*"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A borrowed date parameter is built into a temporary that the scope
    /// releases when the call returns and when it throws.
    /// </summary>
    [Fact]
    public void ABorrowedDateParameterCrossesAsATemporaryTheMemberFrees()
    {
        Assert.Equal(
            """
            public bool SetDate(System.DateOnly date)
            {
                using Gst.GLib.DateScope dateScope = Gst.GLib.DateScope.Alloc(date);
                int nativeResult = GstWidgetSetDate(Handle, dateScope.Pointer);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool SetDate("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A produced date comes back through a zero initialised pointer local —
    /// a call that answers false leaves the slot unwritten — and the value it
    /// holds is read, released and handed out as a nullable date.
    /// </summary>
    [Fact]
    public void AProducedDateIsAdoptedIntoANullableDateOnly()
    {
        Assert.Equal(
            """
            public bool GetDate(out System.DateOnly? date)
            {
                nint dateNative = default;
                int nativeResult = GstWidgetGetDate(Handle, &dateNative);
                System.GC.KeepAlive(this);
                date = Gst.GLib.DateNative.ToDateOnly(dateNative);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool GetDate("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The entry points cross as bare pointers: nothing of the binding mirrors
    /// the layout of a <c>GDate</c>.
    /// </summary>
    [Fact]
    public void TheEntryPointsTakeAndProduceBarePointers()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains(
            "private static partial int GstWidgetSetDate(nint widget, nint date);",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "private static partial int GstWidgetGetDate(nint widget, nint* date);",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Both shapes say on the parameter what the signature cannot: what the
    /// temporary costs, and why a produced date may be absent.
    /// </summary>
    [Fact]
    public void BothShapesDocumentWhatTheSignatureCannotSay()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains(
            """
                /// The call is handed a temporary native date built from this value and
                /// releases it again when the call returns. The library copies whatever it
                /// keeps.
            """,
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            """
                /// The date the call produced, or <see langword="null"/> when it produced
                /// none. A false answer always leaves it null, and on a generic value — a
                /// field of a structure or of a meta container — a true one may as well:
                /// such a field is allowed to hold no date at all.
                /// A year beyond 9999 has no <c>System.DateOnly</c> — the C year is 16 bits
                /// wide — and throws <see cref="ArgumentOutOfRangeException"/>.
            """,
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every shape but the two is refused, so that one the corpus does not have
    /// today is reported rather than emitted wrong.
    /// </summary>
    /// <remarks>
    /// A transferred date in has no owner once the temporary is gone, a
    /// nullable one has no null to pass — <c>System.DateOnly</c> is a value
    /// type — a <c>ref</c> one would have to be both, a caller allocated one
    /// would need a layout the binding does not mirror, and a returned one has
    /// no parameter to hang the release off. A signal and a callback are
    /// refused because the projection only exists in the two directions a
    /// method uses.
    /// </remarks>
    [Theory]
    [InlineData("TakeDate")]
    [InlineData("MaybeDate")]
    [InlineData("UpdateDate")]
    [InlineData("FillDate")]
    [InlineData("DupDate")]
    [InlineData("SetDateFunction")]
    [InlineData("Dated")]
    public void EveryOtherShapeIsRefused(string member)
    {
        Assert.DoesNotContain(member, Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The callback the refused member would have installed is not emitted
    /// either: a trampoline would have to hand a borrowed date to managed code,
    /// which nothing writes.
    /// </summary>
    [Fact]
    public void ACallbackThatReceivesADateIsRefused()
    {
        Assert.False(Run.HasFile("Callbacks.cs"));
    }
}
