using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// One fixture per marshalling rule of <c>MarshalPlanner</c>, checked against
/// the code that comes out of the emitters.
/// </summary>
public sealed class MarshalPlannerTests
{
    /// <summary>
    /// A namespace that exercises every projection the planner knows: strings
    /// in both directions, an enumeration, a handle in both directions and in
    /// both nullabilities, an out parameter, a span, a callback with user data
    /// and a destroy notification, a callable that throws, an owned string
    /// beside handle arguments, a property built from its accessors, a
    /// returned <c>GList</c> in each of its three ownership shapes, a consuming
    /// argument of each wrapper family, a <c>GValue</c> in each of its bound
    /// shapes and each of its rejected ones, and the other shapes that are
    /// rejected on purpose.
    /// </summary>
    private const string Body =
        """
            <enumeration name="State" c:type="GstState">
              <member name="null" value="0" c:identifier="GST_STATE_NULL"/>
              <member name="playing" value="1" c:identifier="GST_STATE_PLAYING"/>
            </enumeration>
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <constructor name="from_payload" c:identifier="gst_caps_from_payload">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <parameter name="payload" transfer-ownership="full">
                    <type name="Payload" c:type="GstPayload*"/>
                  </parameter>
                </parameters>
              </constructor>
            </record>
            <record name="Payload" c:type="GstPayload" glib:type-name="GstPayload" glib:get-type="gst_payload_get_type">
              <field name="kind" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Extent" c:type="GstExtent">
              <field name="width" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Anchor" c:type="GstAnchor" disguised="1"/>
            <callback name="WidgetFunc" c:type="GstWidgetFunc">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="QualityFunc" c:type="GstQualityFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="value" transfer-ownership="none">
                  <type name="GObject.Value" c:type="const GValue*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
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
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <implements name="Sizer"/>
              <constructor name="new" c:identifier="gst_widget_new">
                <return-value transfer-ownership="floating">
                  <type name="Widget" c:type="GstWidget*"/>
                </return-value>
                <parameters>
                  <parameter name="name" transfer-ownership="none" nullable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </constructor>
              <method name="is_named" c:identifier="gst_widget_is_named">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_name" c:identifier="gst_widget_get_name" glib:get-property="name">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_name" c:identifier="gst_widget_set_name" glib:set-property="name">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="name" transfer-ownership="none" nullable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_state" c:identifier="gst_widget_set_state">
                <return-value transfer-ownership="none">
                  <type name="State" c:type="GstState"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="state" transfer-ownership="none">
                    <type name="State" c:type="GstState"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_extents" c:identifier="gst_widget_get_extents">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="width" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="gint" c:type="gint*"/>
                  </parameter>
                  <parameter name="caps" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_extent" c:identifier="gst_widget_get_extent">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="extent" transfer-ownership="none">
                    <type name="Extent" c:type="GstExtent*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="grow_extent" c:identifier="gst_widget_grow_extent">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="extent" transfer-ownership="none">
                    <type name="Extent" c:type="GstExtent*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_plane_sizes" c:identifier="gst_widget_get_plane_sizes">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="plane_size" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="gsize" c:type="gsize*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="write" c:identifier="gst_widget_write" throws="1">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="data" transfer-ownership="none">
                    <array length="1" zero-terminated="0" c:type="const guint8*">
                      <type name="guint8" c:type="guint8"/>
                    </array>
                  </parameter>
                  <parameter name="size" transfer-ownership="none">
                    <type name="gsize" c:type="gsize"/>
                  </parameter>
                </parameters>
              </method>
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="WidgetFunc" c:type="GstWidgetFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
              <method name="visit" c:identifier="gst_widget_visit">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="WidgetFunc" c:type="GstWidgetFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="attach" c:identifier="gst_widget_attach">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </parameter>
                  <parameter name="peer" transfer-ownership="none" nullable="1">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="adopt_label" c:identifier="gst_widget_adopt_label">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </parameter>
                  <parameter name="peer" transfer-ownership="none" nullable="1">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                  <parameter name="label" transfer-ownership="full">
                    <type name="utf8" c:type="gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_caps" c:identifier="gst_widget_take_caps">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_payload" c:identifier="gst_widget_take_payload">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="payload" transfer-ownership="full">
                    <type name="Payload" c:type="GstPayload*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_mark" c:identifier="gst_widget_take_mark">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="mark" transfer-ownership="full" nullable="1">
                    <type name="Payload" c:type="GstPayload*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_peer" c:identifier="gst_widget_take_peer">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="peer" transfer-ownership="full">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_quality" c:identifier="gst_widget_set_quality">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="const GValue*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="merge_quality" c:identifier="gst_widget_merge_quality">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="fetch_quality" c:identifier="gst_widget_fetch_quality">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="value" direction="out" caller-allocates="1" transfer-ownership="none" optional="1">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="peek_quality" c:identifier="gst_widget_peek_quality">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="GObject.Value" c:type="const GValue*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="pull_quality" c:identifier="gst_widget_pull_quality">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GObject.Value" c:type="GValue*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="absorb_quality" c:identifier="gst_widget_absorb_quality">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="value" transfer-ownership="full">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="maybe_set_quality" c:identifier="gst_widget_maybe_set_quality">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="value" transfer-ownership="none" nullable="1">
                    <type name="GObject.Value" c:type="const GValue*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="watch_quality" c:identifier="gst_widget_watch_quality">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="QualityFunc" c:type="GstQualityFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="steal_caps" c:identifier="gst_widget_steal_caps">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="caps" transfer-ownership="container">
                    <type name="Caps" c:type="GstCaps*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="list_children" c:identifier="gst_widget_list_children">
                <return-value transfer-ownership="full">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="list_peers" c:identifier="gst_widget_list_peers">
                <return-value transfer-ownership="none">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="list_labels" c:identifier="gst_widget_list_labels">
                <return-value transfer-ownership="container">
                  <type name="GLib.List" c:type="GList*">
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="list_anchors" c:identifier="gst_widget_list_anchors">
                <return-value transfer-ownership="none">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Anchor"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="take_anchors" c:identifier="gst_widget_take_anchors">
                <return-value transfer-ownership="full">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Anchor"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="list_extents" c:identifier="gst_widget_list_extents">
                <return-value transfer-ownership="full">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Extent"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="list_tags" c:identifier="gst_widget_list_tags">
                <return-value transfer-ownership="full">
                  <type name="GLib.SList" c:type="GSList*">
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="add_children" c:identifier="gst_widget_add_children">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="children" transfer-ownership="none">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <property name="name" writable="1" transfer-ownership="none" getter="get_name" setter="set_name">
                <type name="utf8" c:type="gchar*"/>
              </property>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    [Fact]
    public void AStringParameterIsEncodedOnTheStack()
    {
        Assert.Equal(
            """
            public bool IsNamed(string name)
            {
                ArgumentNullException.ThrowIfNull(name);
                System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                int nativeResult = GstWidgetIsNamed(Handle, nameScope.Pointer);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool IsNamed("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ATransferredStringReturnIsReleased()
    {
        Assert.Equal(
            """
            public string? GetName()
            {
                nint nativeResult = GstWidgetGetName(Handle);
                System.GC.KeepAlive(this);
                return Gst.Interop.GMarshal.PtrToStringUtf8AndFree(nativeResult);
            }
            """,
            Run.Member("Widget.cs", "public string? GetName("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnEnumerationCrossesAsItsUnderlyingType()
    {
        Assert.Equal(
            """
            public Gst.State SetState(Gst.State state)
            {
                int nativeResult = GstWidgetSetState(Handle, (int)state);
                System.GC.KeepAlive(this);
                return (Gst.State)nativeResult;
            }
            """,
            Run.Member("Widget.cs", "public Gst.State SetState("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void OutParametersUseLocalsAndAreNullableWhenTheyAreHandles()
    {
        Assert.Equal(
            """
            public bool GetExtents(out int width, out Gst.Caps? caps)
            {
                int widthNative = default;
                nint capsNative = default;
                int nativeResult = GstWidgetGetExtents(Handle, &widthNative, &capsNative);
                System.GC.KeepAlive(this);
                width = widthNative;
                caps = Gst.Caps.FromNative(capsNative, Gst.Interop.Transfer.Full);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool GetExtents("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The shipped projection of a pointer to a plain structure, which is the
    /// one the corrections below change: the argument is copied into a local
    /// and the address of the copy is handed over, so a callee that writes
    /// through the pointer writes into a temporary the caller never sees.
    /// </summary>
    [Fact]
    public void APointerToAPlainStructIsPassedAsACopyByDefault()
    {
        Assert.Equal(
            """
            public bool GetExtent(Gst.Extent extent)
            {
                Gst.Extent extentNative = extent;
                int nativeResult = GstWidgetGetExtent(Handle, &extentNative);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool GetExtent("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ADirectionOverrideTurnsAFilledStructIntoAnOutParameter()
    {
        // The gir spells the storage a call fills exactly like the value a call
        // reads. Saying which of the two it is makes the local the caller's own
        // variable, which is what the C function was being handed all along.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_extent#extent": { "direction": "out" } }
            }
            """);

        Assert.Equal(
            """
            public bool GetExtent(out Gst.Extent extent)
            {
                Gst.Extent extentNative = default;
                int nativeResult = GstWidgetGetExtent(Handle, &extentNative);
                System.GC.KeepAlive(this);
                extent = extentNative;
                return nativeResult != 0;
            }
            """,
            run.Member("Widget.cs", "public bool GetExtent("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ADirectionOverrideTurnsAnUpdatedStructIntoARefParameter()
    {
        // gst_video_info_align is the real one: it reads the alignment the
        // caller asks for and writes back the padding it had to raise, so both
        // halves of the value have to cross.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_grow_extent#extent": { "direction": "ref" } }
            }
            """);

        Assert.Equal(
            """
            public bool GrowExtent(ref Gst.Extent extent)
            {
                Gst.Extent extentNative = extent;
                int nativeResult = GstWidgetGrowExtent(Handle, &extentNative);
                System.GC.KeepAlive(this);
                extent = extentNative;
                return nativeResult != 0;
            }
            """,
            run.Member("Widget.cs", "public bool GrowExtent("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFixedArraySizeTurnsAScalarOutIntoInlineStorage()
    {
        // The C function writes four values through a pointer the gir describes
        // as one, so the shipped 'out nuint' corrupted the stack of every
        // caller. The storage type carries the length, so the size is never
        // spelled at the call site and nothing is allocated per call.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_plane_sizes#plane_size": { "fixedArraySize": 4 } }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.Equal(
            """
            public void GetPlaneSizes(out Gst.Widget.PlaneSizeArray planeSize)
            {
                Gst.Widget.PlaneSizeArray planeSizeNative = default;
                GstWidgetGetPlaneSizes(Handle, &planeSizeNative);
                System.GC.KeepAlive(this);
                planeSize = planeSizeNative;
            }
            """,
            run.Member("Widget.cs", "public void GetPlaneSizes("),
            StringComparer.Ordinal);

        // The storage is nested in the declaring type and named after the
        // parameter, exactly as the inline storage of a fixed size field is.
        Assert.Contains("using System.Runtime.CompilerServices;", source, StringComparison.Ordinal);
        Assert.Contains(
            """
                [InlineArray(4)]
                public struct PlaneSizeArray
                {
                    private nuint _element0;
                }
            """,
            source,
            StringComparison.Ordinal);

        // The entry point takes the same storage, so the raw signature states
        // the size of the block the call writes into as well.
        Assert.Contains(
            "private static partial void GstWidgetGetPlaneSizes(nint widget, Gst.Widget.PlaneSizeArray* planeSize);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AScalarOutWithoutAFixedArraySizeStaysASingleValue()
    {
        // Nothing is guessed: the size of a caller allocated array is a fact
        // about the C implementation, and without it in the overlays the
        // parameter is the single value the gir describes.
        Assert.Contains(
            "public void GetPlaneSizes(out nuint planeSize)",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnArrayWithALengthBecomesASpanAndTheLengthDisappears()
    {
        Assert.Equal(
            """
            public bool Write(System.ReadOnlySpan<byte> data)
            {
                nint errorNative = 0;
                fixed (byte* dataPointer = data)
                {
                    int nativeResult = GstWidgetWrite(Handle, dataPointer, (nuint)data.Length, &errorNative);
                    System.GC.KeepAlive(this);
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
                    return nativeResult != 0;
                }
            }
            """,
            Run.Member("Widget.cs", "public bool Write("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void EveryHandleArgumentIsKeptAliveAcrossTheCall()
    {
        // The call reads the raw handle out of each wrapper and nothing mentions
        // the wrappers afterwards, so the arguments need the same barrier as the
        // instance. A nullable argument needs no guard: GC.KeepAlive takes null.
        Assert.Equal(
            """
            public void Attach(Gst.Caps caps, Gst.Widget? peer)
            {
                ArgumentNullException.ThrowIfNull(caps);
                GstWidgetAttach(Handle, caps.Handle, peer is null ? 0 : peer.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(caps);
                System.GC.KeepAlive(peer);
            }
            """,
            Run.Member("Widget.cs", "public void Attach("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnOwnedStringOrdersThePrologueInThreePhases()
    {
        // The UTF-8 copy of an owned string is an allocation that only the
        // call releases, so the member runs every guard first, reads every
        // handle next — a disposed wrapper throws before anything is
        // allocated — and materializes the string last. The barriers stay on
        // the wrappers: the locals keep nothing alive.
        Assert.Equal(
            """
            public void AdoptLabel(Gst.Caps caps, Gst.Widget? peer, string label)
            {
                ArgumentNullException.ThrowIfNull(caps);
                ArgumentNullException.ThrowIfNull(label);
                nint instanceHandle = Handle;
                nint capsNative = caps.Handle;
                nint peerNative = peer is null ? 0 : peer.Handle;
                nint labelNative = Gst.Interop.GMarshal.StringToUtf8Ptr(label);
                GstWidgetAdoptLabel(instanceHandle, capsNative, peerNative, labelNative);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(caps);
                System.GC.KeepAlive(peer);
            }
            """,
            Run.Member("Widget.cs", "public void AdoptLabel("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ANotifiedCallbackHandsItsStateToTheDestroyNotification()
    {
        Assert.Equal(
            """
            public void Watch(Gst.WidgetFunc func)
            {
                nint instanceHandle = Handle;
                ArgumentNullException.ThrowIfNull(func);
                Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
                GstWidgetWatch(instanceHandle, Gst.WidgetFuncTrampoline.Pointer, funcState.UserData, (nint)Gst.Interop.CallbackHandle.DestroyNotify);
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Widget.cs", "public void Watch("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACallScopedCallbackIsReleasedWhenTheCallReturns()
    {
        Assert.Equal(
            """
            public void Visit(Gst.WidgetFunc func)
            {
                nint instanceHandle = Handle;
                ArgumentNullException.ThrowIfNull(func);
                Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
                try
                {
                    GstWidgetVisit(instanceHandle, Gst.WidgetFuncTrampoline.Pointer, funcState.UserData);
                    System.GC.KeepAlive(this);
                }
                finally
                {
                    funcState.Free();
                }
            }
            """,
            Run.Member("Widget.cs", "public void Visit("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AConstructorAdoptsWhatItReturns()
    {
        Assert.Equal(
            """
            public static Gst.Widget New(string? name)
            {
                System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                nint nativeResult = GstWidgetNew(nameScope.Pointer);
                return Gst.GObject.Object.FromNative<Gst.Widget>(nativeResult, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_widget_new returned no value.");
            }
            """,
            Run.Member("Widget.cs", "public static Gst.Widget New("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void APropertyDelegatesToTheAccessorsTheGirNames()
    {
        Assert.Equal(
            """
            public string? Name
            {
                get => GetName();
                set => SetName(value);
            }
            """,
            Run.Member("Widget.cs", "public string? Name"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnInterfaceMethodBecomesAnExtensionMethod()
    {
        string source = Run.File("ISizer.cs");

        Assert.Contains("public interface ISizer\n", source, StringComparison.Ordinal);
        Assert.Contains("nint Handle { get; }", source, StringComparison.Ordinal);
        Assert.Contains("public static unsafe partial class SizerExtensions\n", source, StringComparison.Ordinal);
        Assert.Equal(
            """
            public static int GetSize(this Gst.ISizer sizer)
            {
                ArgumentNullException.ThrowIfNull(sizer);
                int nativeResult = GstSizerGetSize(sizer.Handle);
                System.GC.KeepAlive(sizer);
                return nativeResult;
            }
            """,
            Run.Member("ISizer.cs", "public static int GetSize("),
            StringComparer.Ordinal);

        // The class that the gir says implements the interface declares it.
        Assert.Contains(
            "public unsafe partial class Widget : Gst.GObject.InitiallyUnowned, Gst.ISizer\n",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackGetsADelegateAndATrampoline()
    {
        string source = Run.File("Callbacks.cs");

        // The delegate carries the nullability the gir states, and the gir
        // states none here, so the handler is handed an instance rather than
        // something it has to check.
        Assert.Contains("public delegate bool WidgetFunc(Gst.Widget widget);", source, StringComparison.Ordinal);

        // Native code that passes NULL there all the same is a broken promise,
        // not a case the handler has to answer: the conversion throws inside
        // the try of the trampoline, so the trap reports it and the callback
        // answers its failure value without the handler being entered.
        Assert.Contains(
            "Gst.Widget widgetValue = Gst.GObject.Object.FromNative<Gst.Widget>(widget, Gst.Interop.Transfer.None)\n"
            + "                ?? throw new InvalidOperationException(\"GstWidgetFunc passed no widget.\");",
            source,
            StringComparison.Ordinal);

        Assert.Contains("internal static unsafe class WidgetFuncTrampoline\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&Invoke;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (Gst.Interop.CallbackHandle.GetState<Gst.WidgetFunc>(userData) is not { } callback)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Gst.Interop.ExceptionTrap.Report(exception);", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A callback parameter the gir promises but the library does not always
    /// deliver is corrected by an annotation override, keyed by the
    /// <c>c:type</c> of the callback because a callback carries no
    /// <c>c:identifier</c>. This is how <c>GstCapsForeachFunc</c> keeps the
    /// nullable features that <c>gst_caps_foreach</c> really passes.
    /// </summary>
    [Fact]
    public void AnOverrideKeyedByTheCallbackTypeRestoresNullability()
    {
        string source = RunWithOverlay(
            """
            {
              "annotationOverrides": { "GstWidgetFunc#widget": { "nullable": true } }
            }
            """).File("Callbacks.cs");

        Assert.Contains(
            "public delegate bool WidgetFunc(Gst.Widget? widget);",
            source,
            StringComparison.Ordinal);

        // A parameter that may be null is handed over as it arrives: there
        // is nothing broken to report, so the handler decides.
        Assert.Contains(
            "Gst.Widget? widgetValue = Gst.GObject.Object.FromNative<Gst.Widget>"
            + "(widget, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("passed no widget", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnedListIsMaterializedBeforeItsSpineIsFreed()
    {
        // The order is the whole design: the element pointers are copied out,
        // the spine is freed, and only then is an element wrapped. An adoption
        // that throws can therefore neither free the spine twice nor leave a
        // wrapper pointing into a freed node. The list itself never reaches
        // managed code, and a NULL list comes back as an empty one, because
        // CollectAndFreeSpine answers a null head with an empty array.
        Assert.Equal(
            """
            public System.Collections.Generic.IReadOnlyList<Gst.Widget> ListChildren()
            {
                nint nativeResult = GstWidgetListChildren(Handle);
                System.GC.KeepAlive(this);
                nint[] nativeItems = Gst.Interop.GListMarshal.CollectAndFreeSpine(nativeResult);
                System.Collections.Generic.List<Gst.Widget> result = new(nativeItems.Length);
                foreach (nint nativeItem in nativeItems)
                {
                    if (nativeItem != 0 && Gst.GObject.Object.FromNative<Gst.Widget>(nativeItem, Gst.Interop.Transfer.Full) is { } adopted)
                    {
                        result.Add(adopted);
                    }
                }

                return result;
            }
            """,
            Run.Member("Widget.cs", "public System.Collections.Generic.IReadOnlyList<Gst.Widget> ListChildren("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AListTheLibraryKeepsIsWalkedWithoutFreeingIt()
    {
        // transfer-ownership="none": the spine belongs to the library, so only
        // the contents are read and each element is referenced by its own
        // wrapper.
        Assert.Equal(
            """
            public System.Collections.Generic.IReadOnlyList<Gst.Widget> ListPeers()
            {
                nint nativeResult = GstWidgetListPeers(Handle);
                System.GC.KeepAlive(this);
                nint[] nativeItems = Gst.Interop.GListMarshal.Collect(nativeResult);
                System.Collections.Generic.List<Gst.Widget> result = new(nativeItems.Length);
                foreach (nint nativeItem in nativeItems)
                {
                    if (nativeItem != 0 && Gst.GObject.Object.FromNative<Gst.Widget>(nativeItem, Gst.Interop.Transfer.None) is { } adopted)
                    {
                        result.Add(adopted);
                    }
                }

                return result;
            }
            """,
            Run.Member("Widget.cs", "public System.Collections.Generic.IReadOnlyList<Gst.Widget> ListPeers("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ABorrowedListOfOpaqueRecordsIsWalkedWithoutOwningAnything()
    {
        // The wrapper of an opaque record is a bare pointer holder that owns
        // nothing, so it can only stand in for an element the library keeps.
        // Under transfer-ownership="none" it does: neither the spine nor the
        // elements are released, and the wrapper takes no transfer argument
        // because there is nothing for it to take.
        Assert.Equal(
            """
            public System.Collections.Generic.IReadOnlyList<Gst.Anchor> ListAnchors()
            {
                nint nativeResult = GstWidgetListAnchors(Handle);
                System.GC.KeepAlive(this);
                nint[] nativeItems = Gst.Interop.GListMarshal.Collect(nativeResult);
                System.Collections.Generic.List<Gst.Anchor> result = new(nativeItems.Length);
                foreach (nint nativeItem in nativeItems)
                {
                    if (nativeItem != 0 && Gst.Anchor.FromNative(nativeItem) is { } adopted)
                    {
                        result.Add(adopted);
                    }
                }

                return result;
            }
            """,
            Run.Member("Widget.cs", "public System.Collections.Generic.IReadOnlyList<Gst.Anchor> ListAnchors("),
            StringComparer.Ordinal);

        // The other half of the same rule: transfer-ownership="full" hands the
        // elements over, and an opaque wrapper has no way of releasing one, so
        // the member is refused rather than emitted as a leak.
        Assert.DoesNotContain("TakeAnchors", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AContainerTransferFreesTheSpineAndLeavesTheElementsAlone()
    {
        // transfer-ownership="container": the nodes are ours and the strings
        // are not, so the spine is freed and the strings are copied without
        // being freed.
        Assert.Equal(
            """
            public System.Collections.Generic.IReadOnlyList<string> ListLabels()
            {
                nint nativeResult = GstWidgetListLabels(Handle);
                System.GC.KeepAlive(this);
                nint[] nativeItems = Gst.Interop.GListMarshal.CollectAndFreeSpine(nativeResult);
                System.Collections.Generic.List<string> result = new(nativeItems.Length);
                foreach (nint nativeItem in nativeItems)
                {
                    if (nativeItem != 0 && Gst.Interop.GMarshal.PtrToStringUtf8(nativeItem) is { } adopted)
                    {
                        result.Add(adopted);
                    }
                }

                return result;
            }
            """,
            Run.Member("Widget.cs", "public System.Collections.Generic.IReadOnlyList<string> ListLabels("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ContainerTransfersAndUnsupportedContainersAreSkipped()
    {
        string source = Run.File("Widget.cs");

        // gst_widget_steal_caps takes its parameter transfer="container": the
        // callee would own the container and not the contents, a split no
        // minting rule covers, so it stays out while the transfer="full"
        // neighbours of the fixture now bind. No introspectable in parameter of
        // the real girs carries the container transfer, so this synthetic one
        // is the only thing that keeps the rejection honest — a regression
        // here produces no committed diff at all. The four containers are
        // refused for reasons of their own: a list of a plain record has no
        // projection of its elements, a list that hands over opaque records has
        // nobody to release them, a GSList is not bound at all, and a list that
        // is passed in would have to be allocated here and handed over under an
        // ownership rule that is the callee's to state.
        Assert.DoesNotContain("StealCaps", source, StringComparison.Ordinal);
        Assert.Contains("public void TakeCaps(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListExtents", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TakeAnchors", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListTags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AddChildren", source, StringComparison.Ordinal);

        // The eight: steal_caps, list_extents, take_anchors, list_tags and
        // add_children above, and the three GValue rejections that
        // TheTakeValueShapeStaysUnbound, ANullableGValueParameterStaysUnbound
        // and AGValueTakingCallbackStaysUnbound pin.
        Assert.Equal(8, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void AConsumedMiniObjectIsMintedAReferenceAndDisposed()
    {
        // The consuming contract of docs/ownership.md: guards first, every
        // handle read next, then the reference minted for the callee, and the
        // dispose of the wrapper after the barriers — unconditionally, because
        // the C function offers no way back. No KeepAlive is emitted for the
        // consumed argument: the dispose is its last use.
        Assert.Equal(
            """
            public void TakeCaps(Gst.Caps caps)
            {
                ArgumentNullException.ThrowIfNull(caps);
                nint instanceHandle = Handle;
                nint capsNative = caps.Handle;
                nint capsOwned = Gst.GstNative.MiniObjectRef(capsNative);
                GstWidgetTakeCaps(instanceHandle, capsOwned);
                System.GC.KeepAlive(this);
                caps.Dispose();
            }
            """,
            Run.Member("Widget.cs", "public void TakeCaps("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AConsumedBoxedValueIsMintedACopyAndDisposed()
    {
        // A boxed value has no reference count, so the copy is what a
        // reference is there. The boxed type is read in the handle phase, right
        // after the handle whose read throws on a disposed wrapper, because the
        // copy of the third phase is dispatched through it and the third phase
        // allocates.
        Assert.Equal(
            """
            public void TakePayload(Gst.Payload payload)
            {
                ArgumentNullException.ThrowIfNull(payload);
                nint instanceHandle = Handle;
                nint payloadNative = payload.Handle;
                nuint payloadType = payload.BoxedType.Value;
                nint payloadOwned = Gst.Interop.GObjectNative.BoxedCopy(payloadType, payloadNative);
                GstWidgetTakePayload(instanceHandle, payloadOwned);
                System.GC.KeepAlive(this);
                payload.Dispose();
            }
            """,
            Run.Member("Widget.cs", "public void TakePayload("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AConsumedGObjectIsMintedAReferenceAndDisposed()
    {
        Assert.Equal(
            """
            public void TakePeer(Gst.Widget peer)
            {
                ArgumentNullException.ThrowIfNull(peer);
                nint instanceHandle = Handle;
                nint peerNative = peer.Handle;
                nint peerOwned = Gst.Interop.GObjectNative.ObjectRef(peerNative);
                GstWidgetTakePeer(instanceHandle, peerOwned);
                System.GC.KeepAlive(this);
                peer.Dispose();
            }
            """,
            Run.Member("Widget.cs", "public void TakePeer("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ANullableConsumedArgumentMintsAndDisposesNothingForNull()
    {
        // Null is the absence of a payload: zero crosses, nothing is minted
        // and nothing is disposed. Every conditional keys on the argument, so
        // a disposed wrapper still throws from its handle read.
        Assert.Equal(
            """
            public void TakeMark(Gst.Payload? mark)
            {
                nint instanceHandle = Handle;
                nint markNative = mark is null ? 0 : mark.Handle;
                nuint markType = mark is null ? 0 : mark.BoxedType.Value;
                nint markOwned = mark is null ? 0 : Gst.Interop.GObjectNative.BoxedCopy(markType, markNative);
                GstWidgetTakeMark(instanceHandle, markOwned);
                System.GC.KeepAlive(this);
                mark?.Dispose();
            }
            """,
            Run.Member("Widget.cs", "public void TakeMark("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFactoryDisposesItsConsumedArgumentBeforeTheWrapFailureThrow()
    {
        // The dispose sits before the throw of the return wrap on purpose:
        // the C call has consumed the minted copy whatever it returned, so the
        // argument is spent even on that exception path — the order
        // Gst.Event.NewCustom pins by hand.
        Assert.Equal(
            """
            public static Gst.Caps FromPayload(Gst.Payload payload)
            {
                ArgumentNullException.ThrowIfNull(payload);
                nint payloadNative = payload.Handle;
                nuint payloadType = payload.BoxedType.Value;
                nint payloadOwned = Gst.Interop.GObjectNative.BoxedCopy(payloadType, payloadNative);
                nint nativeResult = GstCapsFromPayload(payloadOwned);
                payload.Dispose();
                return Gst.Caps.FromNative(nativeResult, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_caps_from_payload returned no value.");
            }
            """,
            Run.Member("Caps.cs", "public static Gst.Caps FromPayload("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AReadOnlyGValueIsGuardedAndPinnedInPlace()
    {
        // Rule of the read-only shape: a `const GValue*` becomes an `in`
        // parameter, the empty value is refused before anything else — the C
        // side would g_critical and silently do nothing — and the call is
        // handed the pinned address of the layout field inside the caller's
        // own value. Nothing is allocated and nothing is disposed: the callee
        // copies what it keeps.
        Assert.Equal(
            """
            public void SetQuality(in Gst.GObject.Value value)
            {
                if (value.IsEmpty)
                {
                    throw new ArgumentException(
                        "An empty value cannot be passed: it has no type for the call to read.",
                        nameof(value));
                }
                fixed (Gst.GObject.GValueNative* valuePointer = &System.Runtime.CompilerServices.Unsafe.AsRef(in value).NativeValue)
                {
                    GstWidgetSetQuality(Handle, valuePointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            Run.Member("Widget.cs", "public void SetQuality("),
            StringComparer.Ordinal);

        // The import takes a typed pointer: a by-ref struct from a referenced
        // assembly is not strictly blittable to the interop generator
        // (SYSLIB1051), and the fixed scope above is the same AOT-safe stub.
        Assert.Contains(
            "private static partial void GstWidgetSetQuality(nint widget, Gst.GObject.GValueNative* value);",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AWritableGValueCrossesAsARefWithoutAGuard()
    {
        // A non-const GValue* in parameter is storage the callee writes under
        // a contract of its own — gst_value_set_fraction wants an initialized
        // fraction, gst_value_fixate initializes its dest itself — so it is a
        // `ref` and carries no empty guard: which states are valid is the
        // callee's to say, and C asserts misuse.
        Assert.Equal(
            """
            public void MergeQuality(ref Gst.GObject.Value value)
            {
                fixed (Gst.GObject.GValueNative* valuePointer = &value.NativeValue)
                {
                    GstWidgetMergeQuality(Handle, valuePointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            Run.Member("Widget.cs", "public void MergeQuality("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACallerAllocatedGValueOutIsZeroedAndFilledInPlace()
    {
        // The member zeroes the caller's storage — the uninitialized state the
        // callee's g_value_init expects to find — and the callee fills it in
        // place; there is no epilogue, because there is no local to copy back.
        // The gir marks the parameter optional, and that is ignored: storage
        // is always passed, and a callee that declines leaves it empty, which
        // disposes as a no-op.
        Assert.Equal(
            """
            public bool FetchQuality(out Gst.GObject.Value value)
            {
                value = default;
                fixed (Gst.GObject.GValueNative* valuePointer = &value.NativeValue)
                {
                    int nativeResult = GstWidgetFetchQuality(Handle, valuePointer);
                    System.GC.KeepAlive(this);
                    return nativeResult != 0;
                }
            }
            """,
            Run.Member("Widget.cs", "public bool FetchQuality("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ABorrowedGValueReturnIsCopiedIntoTheCallersOwn()
    {
        // The C function hands out a pointer it keeps owning, so the member
        // returns a copy of the caller's own, and NULL is the empty value
        // rather than a nullable return.
        Assert.Equal(
            """
            public Gst.GObject.Value PeekQuality()
            {
                nint nativeResult = GstWidgetPeekQuality(Handle);
                System.GC.KeepAlive(this);
                return Gst.GObject.Value.CopyFrom(nativeResult);
            }
            """,
            Run.Member("Widget.cs", "public Gst.GObject.Value PeekQuality("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ATransferredGValueReturnIsAdoptedShellAndAll()
    {
        // transfer-ownership="full": the contents move into the caller's value
        // and the heap shell is freed, which is what Value.TakeOwnership does.
        Assert.Equal(
            """
            public Gst.GObject.Value PullQuality()
            {
                nint nativeResult = GstWidgetPullQuality(Handle);
                System.GC.KeepAlive(this);
                return Gst.GObject.Value.TakeOwnership(nativeResult);
            }
            """,
            Run.Member("Widget.cs", "public Gst.GObject.Value PullQuality("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheTakeValueShapeStaysUnbound()
    {
        // A GValue in parameter the callee takes over
        // (transfer-ownership="full", the take_value family) would leave the
        // caller's struct owning what the callee now owns; binding it needs an
        // emission that moves the contents out of the caller's value, which
        // does not exist. Every real case is under an overlay skip, so this
        // synthetic fixture is the only thing that keeps the rejection honest
        // — a regression here produces no committed diff at all.
        Assert.DoesNotContain("AbsorbQuality", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ANullableGValueParameterStaysUnbound()
    {
        // A C# `in` struct cannot be null, so a nullable GValue has no
        // spelling on the public surface and the member is rejected rather
        // than bound with an unreachable null.
        Assert.DoesNotContain("MaybeSetQuality", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AGValueTakingCallbackStaysUnbound()
    {
        // A callback receives its arguments rather than passing them, and a
        // trampoline has no equivalent of a pointer into caller owned storage,
        // so a GValue carrying callback and the member that takes it stay
        // unbound — GstControlBindingConvert and the iterator fold family are
        // the real cases.
        Assert.DoesNotContain("WatchQuality", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("QualityFunc", Run.File("Callbacks.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalArgumentThatTransfersOwnershipStaysUnbound()
    {
        // The consuming kind is a contract for arguments this code passes in.
        // A signal argument is received: the handler borrows it for the length
        // of the emission, so a transfer-full argument stays rejected exactly
        // as it was before the kind existed.
        FixtureRun run = Fixture.Run(
            """
                <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
                  <field name="type" writable="1">
                    <type name="GType" c:type="GType"/>
                  </field>
                </record>
                <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
                  <field name="mini_object" writable="1">
                    <type name="MiniObject" c:type="GstMiniObject"/>
                  </field>
                </record>
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <glib:signal name="caps-taken" when="last">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                    <parameters>
                      <parameter name="caps" transfer-ownership="full">
                        <type name="Caps" c:type="GstCaps*"/>
                      </parameter>
                    </parameters>
                  </glib:signal>
                  <glib:signal name="ready" when="last">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                  </glib:signal>
                </class>
            """);

        string source = run.File("Widget.cs");

        Assert.DoesNotContain("CapsTaken", source, StringComparison.Ordinal);
        Assert.Contains("public event System.EventHandler Ready", source, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void TheModuleRegistersEveryWrapperWithATypeFunction()
    {
        string source = Run.File("_Module.cs");

        Assert.Contains("internal static unsafe partial class GstModule\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static Gst.Interop.ModuleTypeEntry[] CreateEntries() =>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Caps.GetGType, &Gst.Caps.CreateWrapper),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Widget.GetGType, &Gst.Widget.CreateWrapper),",
            source,
            StringComparison.Ordinal);

        // An interface has no instances of its own, so it is not registered.
        Assert.DoesNotContain("Gst.ISizer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractClassKeepsAConcreteWrapperForTheRegistry()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Base" c:type="GstBase" parent="GObject.Object" abstract="1" glib:type-name="GstBase" glib:get-type="gst_base_get_type">
                </class>
            """);

        string source = run.File("Base.cs");

        Assert.Contains("public abstract unsafe partial class Base : Gst.GObject.Object\n", source, StringComparison.Ordinal);
        Assert.Contains("private sealed class Concrete : Base\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static object CreateWrapper(nint handle, Gst.Interop.Transfer transfer) => new Concrete(handle, transfer);",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>Runs the fixture with a hand written <c>fixups.json</c>.</summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
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
