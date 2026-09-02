using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The rules that keep a signature out of the binding because emitting it
/// would corrupt memory, and the two rules that decide what a shadowed or an
/// action annotated declaration becomes.
/// </summary>
/// <remarks>
/// Every fixture here stands for a shape that the real girs carry and that the
/// generator used to emit: an out parameter of a record that is bound behind a
/// handle, a method that consumes the instance it is called on, and a signal
/// that GObject only carries so that a binding can call a method through it.
/// </remarks>
public sealed class RejectionRulesTests
{
    /// <summary>
    /// A namespace whose members are the rejected shapes, next to the ones they
    /// must not take with them: a caller allocated plain struct still binds,
    /// and the free function of a wrapper that owns nothing still binds.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Rect" c:type="GstRect">
              <field name="x" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="y" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Info" c:type="GstInfo" glib:type-name="GstInfo" glib:get-type="gst_info_get_type">
              <field name="stride" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="owner" writable="1">
                <type name="Widget" c:type="GstWidget*"/>
              </field>
              <method name="make_writable" c:identifier="gst_info_make_writable">
                <return-value transfer-ownership="full">
                  <type name="Info" c:type="GstInfo*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="info" transfer-ownership="full">
                    <type name="Info" c:type="GstInfo*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="merge" c:identifier="gst_info_merge">
                <return-value transfer-ownership="full">
                  <type name="Info" c:type="GstInfo*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="info" transfer-ownership="full">
                    <type name="Info" c:type="GstInfo*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <method name="make_writable" c:identifier="gst_caps_make_writable">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="truncate" c:identifier="gst_caps_truncate">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="unref" c:identifier="gst_caps_unref">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="ref" c:identifier="gst_caps_ref">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="is_empty" c:identifier="gst_caps_is_empty">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <record name="Overlay" c:type="GstOverlay" glib:type-name="GstOverlay" glib:get-type="gst_overlay_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <method name="make_writable" c:identifier="gst_overlay_make_writable">
                <return-value transfer-ownership="full">
                  <type name="Overlay" c:type="GstOverlay*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="overlay" transfer-ownership="full">
                    <type name="Overlay" c:type="GstOverlay*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <record name="Poll" c:type="GstPoll" disguised="1" opaque="1">
              <method name="free" c:identifier="gst_poll_free">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="set" transfer-ownership="full">
                    <type name="Poll" c:type="GstPoll*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="get_info" c:identifier="gst_widget_get_info">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_rect" c:identifier="gst_widget_get_rect">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="rect" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Rect" c:type="GstRect*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_info" c:identifier="gst_widget_take_info">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" direction="out" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="free" c:identifier="gst_widget_free">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="pull-sample" when="last" action="1">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
              </glib:signal>
              <glib:signal name="ready" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A namespace that spells the same boxed record five ways: as an in
    /// parameter of a pointer to a pointer, plain, const, and on a function
    /// rather than a method, which is the <c>gst_play_visualizations_free</c>
    /// shape and is refused; and as the out parameter and the plain in
    /// parameter of the same record, which are the two projections the refusal
    /// must leave alone.
    /// </summary>
    private const string PointerToPointerBody =
        """
            <record name="Visualization" c:type="GstVisualization" glib:type-name="GstVisualization" glib:get-type="gst_visualization_get_type">
              <field name="name" writable="1">
                <type name="utf8" c:type="const gchar*"/>
              </field>
              <function name="release_all" c:identifier="gst_visualization_release_all">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="viss" transfer-ownership="none">
                    <type name="Visualization" c:type="GstVisualization**"/>
                  </parameter>
                </parameters>
              </function>
            </record>
            <class name="Player" c:type="GstPlayer" parent="GObject.InitiallyUnowned" glib:type-name="GstPlayer" glib:get-type="gst_player_get_type">
              <method name="visualizations_free" c:identifier="gst_player_visualizations_free">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="viss" transfer-ownership="none">
                    <type name="Visualization" c:type="GstVisualization**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="visualizations_read" c:identifier="gst_player_visualizations_read">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="viss" transfer-ownership="none">
                    <type name="Visualization" c:type="const GstVisualization**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_visualization" c:identifier="gst_player_get_visualization">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="vis" direction="out" transfer-ownership="full">
                    <type name="Visualization" c:type="GstVisualization**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_visualization" c:identifier="gst_player_set_visualization">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="vis" transfer-ownership="none">
                    <type name="Visualization" c:type="GstVisualization*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// A namespace that carries the same pointer to a pointer on the inbound
    /// side: as the argument of a callback and as the argument of a signal,
    /// which are the two entry points a method parameter does not go through,
    /// each next to the single star spelling that must keep binding.
    /// </summary>
    private const string HandlerBody =
        """
            <record name="Visualization" c:type="GstVisualization" glib:type-name="GstVisualization" glib:get-type="gst_visualization_get_type">
              <field name="name" writable="1">
                <type name="utf8" c:type="const gchar*"/>
              </field>
            </record>
            <callback name="VisualizationsFunc" c:type="GstVisualizationsFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="viss" transfer-ownership="none">
                  <type name="Visualization" c:type="GstVisualization**"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="VisualizationFunc" c:type="GstVisualizationFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="vis" transfer-ownership="none">
                  <type name="Visualization" c:type="GstVisualization*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Player" c:type="GstPlayer" parent="GObject.InitiallyUnowned" glib:type-name="GstPlayer" glib:get-type="gst_player_get_type">
              <method name="watch_visualizations" c:identifier="gst_player_watch_visualizations">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="VisualizationsFunc" c:type="GstVisualizationsFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="watch_visualization" c:identifier="gst_player_watch_visualization">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="player" transfer-ownership="none">
                    <type name="Player" c:type="GstPlayer*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="VisualizationFunc" c:type="GstVisualizationFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="visualizations-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="viss" transfer-ownership="none">
                    <type name="Visualization" c:type="GstVisualization**"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="visualization-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="vis" transfer-ownership="none">
                    <type name="Visualization" c:type="GstVisualization*"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    /// <summary>
    /// A namespace that answers a scalar through a pointer typed
    /// <c>&lt;type&gt;</c> twice - from a method and from a callback - which is
    /// the <c>gst_rtcp_packet_fb_get_fci</c> shape and is refused; beside the
    /// two projections the refusal must leave alone: an out parameter of the
    /// same spelling and a returned <c>gpointer</c>.
    /// </summary>
    private const string PointerToScalarBody =
        """
            <callback name="FciFunc" c:type="GstFciFunc">
              <return-value transfer-ownership="none">
                <type name="guint8" c:type="guint8*"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Packet" c:type="GstPacket" parent="GObject.InitiallyUnowned" glib:type-name="GstPacket" glib:get-type="gst_packet_get_type">
              <method name="get_fci" c:identifier="gst_packet_get_fci">
                <return-value transfer-ownership="none">
                  <type name="guint8" c:type="guint8*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_fci_function" c:identifier="gst_packet_set_fci_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1">
                    <type name="FciFunc" c:type="GstFciFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_ssrc" c:identifier="gst_packet_get_ssrc">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="ssrc" direction="out" transfer-ownership="none">
                    <type name="guint32" c:type="guint32*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_user_data" c:identifier="gst_packet_get_user_data">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="gpointer" c:type="gpointer"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// A namespace whose parameters carry the shape of the
    /// <c>gst_rtcp_packet_xr_get_*</c> readers: a scalar the C function writes
    /// through, typed as the bare scalar with a pointer <c>c:type</c> and no
    /// direction attribute at all, beside one scalar whose <c>c:type</c> has no
    /// star and which a correction must therefore not move.
    /// </summary>
    private const string ScalarPointerBody =
        """
            <class name="Packet" c:type="GstPacket" parent="GObject.InitiallyUnowned" glib:type-name="GstPacket" glib:get-type="gst_packet_get_type">
              <method name="get_ssrc" c:identifier="gst_packet_get_ssrc">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="ssrc" transfer-ownership="none">
                    <type name="guint32" c:type="guint32*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_ipv4" c:identifier="gst_packet_get_ipv4">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="is_ipv4" transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_offset" c:identifier="gst_packet_get_offset">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="offset" transfer-ownership="none">
                    <type name="gint" c:type="gint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_count" c:identifier="gst_packet_get_count">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_flavour" c:identifier="gst_packet_get_flavour">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="flavour" transfer-ownership="none">
                    <type name="Kind" c:type="GstKind"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_kind" c:identifier="gst_packet_get_kind">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="kind" transfer-ownership="none">
                    <type name="Kind" c:type="GstKind*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_mask" c:identifier="gst_packet_get_mask">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="mask" transfer-ownership="none">
                    <type name="Mask" c:type="GstMask*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_slaving_function" c:identifier="gst_packet_set_slaving_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1">
                    <type name="SlavingFunc" c:type="GstSlavingFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_ready_function" c:identifier="gst_packet_set_ready_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="packet" transfer-ownership="none">
                    <type name="Packet" c:type="GstPacket*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1">
                    <type name="ReadyFunc" c:type="GstReadyFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="skew-requested" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="skew" transfer-ownership="none">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="count-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
            <callback name="SlavingFunc" c:type="GstSlavingFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="skew" transfer-ownership="none">
                  <type name="gint64" c:type="gint64*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="ReadyFunc" c:type="GstReadyFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="ready" transfer-ownership="none">
                  <type name="gint64" c:type="gint64"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <enumeration name="Kind" c:type="GstKind">
              <member name="cname" value="0" c:identifier="GST_KIND_CNAME"/>
              <member name="tool" value="1" c:identifier="GST_KIND_TOOL"/>
            </enumeration>
            <bitfield name="Mask" c:type="GstMask">
              <member name="none" value="0" c:identifier="GST_MASK_NONE"/>
              <member name="all" value="1" c:identifier="GST_MASK_ALL"/>
            </bitfield>
        """;

    private static readonly Lazy<FixtureRun> LazyPointerToPointerRun = new(
        static () => Fixture.Run(PointerToPointerBody),
        isThreadSafe: true);

    private static readonly Lazy<FixtureRun> LazyHandlerRun = new(
        static () => Fixture.Run(HandlerBody),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static FixtureRun PointerToPointerRun => LazyPointerToPointerRun.Value;

    private static readonly Lazy<FixtureRun> LazyPointerToScalarRun = new(
        static () => Fixture.Run(PointerToScalarBody),
        isThreadSafe: true);

    private static FixtureRun PointerToScalarRun => LazyPointerToScalarRun.Value;

    private static FixtureRun HandlerRun => LazyHandlerRun.Value;

    [Fact]
    public void ACallerAllocatedHandleIsRejected()
    {
        // GstInfo carries a pointer field, so it is bound behind a handle. The
        // callee writes a whole GstInfo into the storage it is given, which a
        // pointer sized local cannot hold.
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain("gst_widget_get_info", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.CallerAllocates));
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0012" && diagnostic.Message.Contains(
                "gst_widget_get_info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ACallerAllocatedPlainStructIsStillBound()
    {
        // A plain struct is spelled in C# with the size of the C type, so the
        // caller can provide the storage the callee writes into.
        Assert.Equal(
            """
            public void GetRect(out Gst.Rect rect)
            {
                Gst.Rect rectNative = default;
                GstWidgetGetRect(Handle, &rectNative);
                System.GC.KeepAlive(this);
                rect = rectNative;
            }
            """,
            Run.Member("Widget.cs", "public void GetRect"));
    }

    [Fact]
    public void AnOutHandleThatTheCalleeAllocatesIsStillBound()
    {
        // Without caller-allocates the callee hands a pointer back, which is
        // exactly what a handle holds.
        Assert.Contains(
            "public void TakeInfo(out Gst.Info? info)",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMakeWritableAdoptsWhatTheCallAnsweredAndHandsTheWrapperBack()
    {
        // The wrapper gives the reference it owns to the call and takes the
        // answer, so the member is the C idiom
        // `caps = gst_caps_make_writable (caps)` written as one call. The read
        // is the one that refuses a borrowed wrapper, and the adoption is the
        // one that raises the copy the C function could not make.
        //
        // The call goes through the runtime import of what the C macro expands
        // to, because gst_caps_make_writable is a macro rather than a symbol
        // until 1.27.2 and this binding runs on 1.24: importing it by name
        // would raise EntryPointNotFoundException there.
        Assert.Equal(
            """
            public Gst.Caps MakeWritable()
            {
                nint instanceHandle = BeginMakeWritable();
                nint nativeResult = Gst.GstNative.MiniObjectMakeWritable(instanceHandle);
                System.GC.KeepAlive(this);
                AdoptWritable(nativeResult);
                return this;
            }
            """,
            Run.Member("Caps.cs", "public Gst.Caps MakeWritable"));
        Assert.DoesNotContain(
            "EntryPoint = \"gst_caps_make_writable\"",
            Run.File("Caps.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMakeWritableThatIsAFunctionOfItsOwnKeepsItsOwnImport()
    {
        // Only the nine that are macros on the floor are rerouted. A
        // _make_writable whose C implementation is more than the forward to
        // gst_mini_object_make_writable - gst_video_overlay_composition_make_writable
        // copies when a rectangle of an otherwise writable composition is
        // shared - has to keep calling its own symbol.
        Assert.Equal(
            """
            public Gst.Overlay MakeWritable()
            {
                nint instanceHandle = BeginMakeWritable();
                nint nativeResult = GstOverlayMakeWritable(instanceHandle);
                System.GC.KeepAlive(this);
                AdoptWritable(nativeResult);
                return this;
            }
            """,
            Run.Member("Overlay.cs", "public Gst.Overlay MakeWritable"));
        Assert.Contains(
            "EntryPoint = \"gst_overlay_make_writable\"",
            Run.File("Overlay.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AConversionThatConsumesItsInstanceMintsTheReferenceItHandsOver()
    {
        // Everything else of the shape answers a value of its own, so the
        // wrapper keeps what it holds and the call is handed a reference
        // minted for it. Both wrappers are live afterwards, and they may stand
        // for the same object.
        Assert.Equal(
            """
            public Gst.Caps Truncate()
            {
                nint instanceHandle = Handle;
                nint instanceOwned = Gst.GstNative.MiniObjectRef(instanceHandle);
                nint nativeResult = GstCapsTruncate(instanceOwned);
                System.GC.KeepAlive(this);
                return Gst.Caps.FromNative(nativeResult, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_caps_truncate returned no value.");
            }
            """,
            Run.Member("Caps.cs", "public Gst.Caps Truncate"));
    }

    [Fact]
    public void ABoxedMakeWritableAdoptsInPlaceAsWell()
    {
        // GstUri is the one of the eleven that the gir declares as an opaque
        // boxed record, and it is a mini object underneath: its boxed copy is
        // gst_mini_object_ref. The wrapper owns one reference either way, so
        // the adopt in place shape is the same one, off the base class of a
        // boxed wrapper.
        Assert.Equal(
            """
            public Gst.Info MakeWritable()
            {
                nint instanceHandle = BeginMakeWritable();
                nint nativeResult = GstInfoMakeWritable(instanceHandle);
                System.GC.KeepAlive(this);
                AdoptWritable(nativeResult);
                return this;
            }
            """,
            Run.Member("Info.cs", "public Gst.Info MakeWritable"));
    }

    [Fact]
    public void AConversionThatConsumesABoxedInstanceIsStillRejected()
    {
        // The mint of a boxed value is a copy, so a conversion that consumed
        // one would leave the original where it was and answer a value nobody
        // asked for. Only the adopt in place shape is bound for a boxed
        // wrapper; everything else keeps the diagnostic that says so.
        string source = Run.File("Info.cs");

        Assert.DoesNotContain("public Gst.Info Merge()", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.InstanceTransferFull));
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0013" && diagnostic.Message.Contains(
                "gst_info_merge",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ReferenceCountingIsNotExposedOnAWrapperThatOwnsItsInstance()
    {
        // The wrapper of a mini object releases its reference when it is
        // disposed, so a second release path can only corrupt the count. The
        // gir annotates gst_caps_unref with a consumed instance and
        // gst_caps_ref with a borrowed one, so neither annotation alone finds
        // the pair.
        string source = Run.File("Caps.cs");

        Assert.DoesNotContain("public void Unref()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Caps Ref()", source, StringComparison.Ordinal);
        Assert.Contains("public bool IsEmpty()", source, StringComparison.Ordinal);
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.LifetimePrimitive));
    }

    [Fact]
    public void AWrapperThatOwnsNothingKeepsItsFreeFunction()
    {
        // The wrapper of an opaque record is a bare pointer holder and is never
        // disposed, so gst_poll_free is the only way of releasing a GstPoll.
        Assert.Contains("public void Free()", Run.File("Poll.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFreeFunctionThatReleasesItsArgumentIsNotALifetimePrimitive()
    {
        // gst_widget_free is spelled like a lifetime primitive and is not one:
        // it releases the GstInfo it is handed, not the widget it is called on.
        // A lifetime primitive takes nothing besides its instance.
        Assert.Contains("public void Free(Gst.Info info)", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionSignalDoesNotBecomeAnEvent()
    {
        // An action signal is a call API that GObject exposes through the
        // signal machinery. Nothing ever raises it, so subscribing to it is a
        // handler that is never called.
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain("PullSample", source, StringComparison.Ordinal);
        Assert.Contains("public event System.EventHandler Ready", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.ActionSignal));
    }

    [Fact]
    public void APrivateStateShellIsNotEmitted()
    {
        FixtureRun run = Fixture.Run(
            """
                <record name="WidgetPrivate" c:type="GstWidgetPrivate" disguised="1" opaque="1"/>
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                </class>
            """);

        Assert.False(run.HasFile("WidgetPrivate.cs"));
        Assert.True(run.HasFile("Widget.cs"));
    }

    [Fact]
    public void AnInPointerToAHandlePointerIsRejected()
    {
        // gst_play_visualizations_free is the shape: the gir spells the
        // parameter as a plain <type c:type="GstVisualization**"/> with no
        // direction and no array annotation, and the C function walks the block
        // it is handed to its NULL terminator. A handle argument crosses as the
        // pointer the wrapper holds, so the member would hand one level of
        // indirection too few over and the callee would read the record itself
        // as if it were a pointer - a warning free binding that corrupts memory
        // on the first call. The const spelling is the same shape and is
        // refused with it.
        //
        // The two siblings are what keeps the refusal narrow: an out parameter
        // of the very same c:type is the pointer the callee writes back
        // through, and a single star is the ordinary handle argument. Both
        // still bind.
        FixtureRun run = PointerToPointerRun;

        string source = run.File("Player.cs");

        Assert.DoesNotContain("gst_player_visualizations_free", source, StringComparison.Ordinal);
        Assert.DoesNotContain("gst_player_visualizations_read", source, StringComparison.Ordinal);

        // gst_play_visualizations_free is a function rather than a method, so
        // the static form is asserted as well: the rule lives in the parameter
        // loop that every form goes through, and nothing about it reads the
        // instance.
        Assert.DoesNotContain(
            "gst_visualization_release_all",
            run.File("Visualization.cs"),
            StringComparison.Ordinal);
        Assert.Equal(3, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void AnOutPointerToAHandlePointerIsStillBound()
    {
        string source = PointerToPointerRun.File("Player.cs");

        Assert.Contains(
            "public void GetVisualization(out Gst.Visualization? vis)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public void SetVisualization(Gst.Visualization vis)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackPointerToAHandlePointerIsRejected()
    {
        // The same shape on the inbound side. A trampoline hands the delegate
        // what the caller passed, so a c:type of two stars would be projected
        // as if the pointer to the block were the record itself, and every
        // handler of the delegate would read one level of indirection too far.
        // The whole callback type is refused, which takes the method that hands
        // it over with it: nothing else can be done with a delegate whose
        // signature cannot be spelled.
        FixtureRun run = HandlerRun;

        Assert.DoesNotContain(
            "gst_player_watch_visualizations",
            run.File("Player.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("VisualizationsFunc", run.File("Callbacks.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalPointerToAHandlePointerIsRejected()
    {
        // A signal argument reaches the handler the same way a callback
        // argument does, so the refusal is the same one: the event, its
        // arguments class and its trampoline are all left out rather than
        // emitted around a wrong pointer.
        string source = HandlerRun.File("Player.cs");

        Assert.DoesNotContain("VisualizationsChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APlainHandleCallbackAndSignalArgumentStillBind()
    {
        // What keeps the refusal narrow on this side as well: a single star is
        // the ordinary handle argument of a delegate and of an event, and both
        // still bind.
        FixtureRun run = HandlerRun;

        Assert.Contains(
            "public delegate void VisualizationFunc(Gst.Visualization vis);",
            run.File("Callbacks.cs"),
            StringComparison.Ordinal);

        string source = run.File("Player.cs");

        Assert.Contains(
            "public void WatchVisualization(Gst.VisualizationFunc func)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public event System.EventHandler<Gst.Player.VisualizationChangedSignalArgs> VisualizationChanged",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnedPointerToAScalarIsRejected()
    {
        // gst_rtcp_packet_fb_get_fci is the shape: the gir answers the block of
        // feedback control information through a
        // <type name="guint8" c:type="guint8*"/>, so the member would answer a
        // byte and the pointer the C function returns would be truncated to its
        // lowest byte - a warning free binding that answers a number nobody can
        // use. The <type> names the scalar and only the c:type says it is an
        // address, which is why nothing but this rule sees it.
        FixtureRun run = PointerToScalarRun;

        Assert.DoesNotContain("gst_packet_get_fci", run.File("Packet.cs"), StringComparison.Ordinal);

        // The two refusals of the fixture - this one and the callback return -
        // share the count.
        Assert.Equal(2, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void ACallbackThatReturnsAPointerToAScalarIsRejected()
    {
        // The same value coming the other way. A trampoline hands the return of
        // a handler back to the library, so a guint8 standing for a guint8*
        // would be read as an address there; the delegate is refused, and the
        // method that installs it goes with it.
        FixtureRun run = PointerToScalarRun;

        Assert.DoesNotContain("gst_packet_set_fci_function", run.File("Packet.cs"), StringComparison.Ordinal);
        Assert.False(run.HasFile("Callbacks.cs"));
        Assert.Equal(2, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void AnOutPointerToAScalarIsStillBound()
    {
        // The out direction is the one this shape has a projection for, and it
        // is spelled with exactly the same star: the refusal is made on the
        // return side alone, so nothing about an out parameter moves.
        Assert.Contains(
            "public void GetSsrc(out uint ssrc)",
            PointerToScalarRun.File("Packet.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnedPointerIsStillBound()
    {
        // A gpointer is a pointer on purpose - it carries no star in its c:type
        // and is not a scalar the binding passes by value - so the rule leaves
        // the whole untyped pointer surface alone.
        Assert.Contains(
            "public nint GetUserData()",
            PointerToScalarRun.File("Packet.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AShadowedCallableIsEmittedWhenTheShadowingOneCannotBeBound()
    {
        // gst_adapter_copy is the real pair: it is shadowed by
        // gst_adapter_copy_bytes, which returns a GBytes that this milestone
        // cannot marshal. Skipping both would leave the function unbound, so
        // the shadowed declaration takes the clean name. The fixture rejects
        // the shadowing one for another documented planner rule, an array
        // parameter whose elements the call takes over — a shape the planner
        // itself refuses, which is what keeps the shadow retry of the surface
        // builder exercised. (It used to be a transfer-full handle parameter,
        // which the consuming argument kind has since made bindable.)
        FixtureRun run = Fixture.Run(
            """
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" shadowed-by="copy_data">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_data" c:identifier="gst_widget_copy_data" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                      <parameter name="data" transfer-ownership="full">
                        <array length="1" zero-terminated="0" c:type="guint8*">
                          <type name="guint8" c:type="guint8"/>
                        </array>
                      </parameter>
                      <parameter name="size" transfer-ownership="none">
                        <type name="gsize" c:type="gsize"/>
                      </parameter>
                    </parameters>
                  </method>
                </class>
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("public int Copy()", source, StringComparison.Ordinal);
        Assert.Contains("EntryPoint = \"gst_widget_copy\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy_data\"", source, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
    }

    [Fact]
    public void AShadowedCallableStaysOutWhenItIsNotIntrospectable()
    {
        // The fallback only lifts the shadowing rule. Every other reason still
        // applies, which is what keeps the declarations of the real girs out:
        // all three of their shadowed callables are introspectable="0". The
        // shadowing one is rejected by the planner for its owned array, as in
        // the fixture above.
        FixtureRun run = Fixture.Run(
            """
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" introspectable="0" shadowed-by="copy_data">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_data" c:identifier="gst_widget_copy_data" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                      <parameter name="data" transfer-ownership="full">
                        <array length="1" zero-terminated="0" c:type="guint8*">
                          <type name="guint8" c:type="guint8"/>
                        </array>
                      </parameter>
                      <parameter name="size" transfer-ownership="none">
                        <type name="gsize" c:type="gsize"/>
                      </parameter>
                    </parameters>
                  </method>
                </class>
            """);

        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy\"", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.NotIntrospectable));
    }

    [Fact]
    public void AShadowedCallableStaysOutWhenTheShadowingOneBinds()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" shadowed-by="copy_full">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_full" c:identifier="gst_widget_copy_full" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="guint" c:type="guint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                </class>
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("public uint Copy()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy\"", source, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
    }

    [Fact]
    public void ADirectionOverrideDoesNotUnlockARecordBoundBehindAHandle()
    {
        // GstInfo carries a pointer field, so it is bound behind a handle: one
        // pointer wide in C# and a whole structure in C. Letting a correction
        // turn it into an out parameter would hand the callee the address of a
        // pointer sized local to write a structure into, which is the fault the
        // caller-allocates rule exists for. The correction stops at a plain
        // struct, so the member keeps the projection it had.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_free#info": { "direction": "out" } }
            }
            """);

        Assert.Contains("public void Free(Gst.Info info)", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_free#info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ADirectionOverrideReachesAPointerToAScalar()
    {
        // The gst_rtcp_packet_xr_get_* shape: the C function returns TRUE once
        // it has written the value through the pointer, and the gir types the
        // parameter as the bare scalar the pointer points at with no direction
        // on it, so the member would pass the value the callee writes through.
        // The star of the c:type is the evidence the correction stands on.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_ssrc#ssrc": { "direction": "out" } }
            }
            """);

        Assert.Contains("public void GetSsrc(out uint ssrc)", run.File("Packet.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void ADirectionOverrideReachesAPointerToABoolean()
    {
        // gst_rtcp_packet_xr_get_summary_ttl answers whether the block carries
        // IPv4 hop counts through a gboolean*, which is an int on the wire and
        // a bool in the member: the conversion the out projection already has.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_ipv4#is_ipv4": { "direction": "out" } }
            }
            """);

        Assert.Contains("public void GetIpv4(out bool isIpv4)", run.File("Packet.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void ARefOverrideReachesAPointerToAScalar()
    {
        // The other half of the same correction: a value the callee reads and
        // updates is a ref, and the local the member passes the address of is
        // initialized from the argument rather than zeroed.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_offset#offset": { "direction": "ref" } }
            }
            """);

        Assert.Contains("public void GetOffset(ref int offset)", run.File("Packet.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void ARefOverrideReachesAPointerToABoolean()
    {
        // The unproven arm - no ref bool exists in the generated surface today -
        // so the raw import is asserted beside the member: the conversion of a
        // gboolean has to survive both directions of the same argument.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_ipv4#is_ipv4": { "direction": "ref" } }
            }
            """);

        string source = run.File("Packet.cs");

        Assert.Contains("public void GetIpv4(ref bool isIpv4)", source, StringComparison.Ordinal);
        Assert.Contains("int* isIpv4", source, StringComparison.Ordinal);
        Assert.Contains("int isIpv4Native = isIpv4 ? 1 : 0;", source, StringComparison.Ordinal);
        Assert.Contains("isIpv4 = isIpv4Native != 0;", source, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void AnInPointerToAScalarParameterIsRejected()
    {
        // The in half of the refusal, on a member. The gir of
        // gst_rtp_source_meta_set_ssrc has this shape - a scalar with the star
        // in the c:type alone and no direction - so the member would have
        // handed the number itself to a C function that dereferences it. The
        // parameter whose c:type carries no star is the control: it is a value
        // the callee reads and it stays bound.
        FixtureRun run = RunScalarWithOverlay("{}");

        string source = run.File("Packet.cs");

        Assert.DoesNotContain("gst_packet_get_ssrc", source, StringComparison.Ordinal);
        Assert.Contains("public void GetCount(uint count)", source, StringComparison.Ordinal);
        // The seven refusals of the fixture share the count: the five
        // members whose parameter is a pointer to a scalar, an enumeration
        // or a bitfield, the setter whose callback type can no longer be
        // planned, and the skew-requested signal. The callback type itself
        // is not in the number - the census counts callables, and a
        // callback type that is never claimed is simply not emitted. A
        // refusal that lands under another reason moves the number.
        Assert.Equal(7, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void ACallbackArgumentThatIsAPointerToAScalarIsRejected()
    {
        // GstAudioBaseSinkCustomSlavingCallback is the shape: the C caller
        // passes the address of the skew it reads back, and the trampoline
        // would hand that address to the delegate as a number. The callback
        // type goes, and the method that installs it goes with it; the
        // callback whose scalar carries no star is the control.
        FixtureRun run = RunScalarWithOverlay("{}");

        string source = run.File("Packet.cs");
        string callbacks = run.File("Callbacks.cs");

        Assert.DoesNotContain("gst_packet_set_slaving_function", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SlavingFunc", callbacks, StringComparison.Ordinal);
        Assert.Contains("public delegate void ReadyFunc(", callbacks, StringComparison.Ordinal);
        Assert.Contains("public void SetReadyFunction(", source, StringComparison.Ordinal);
        Assert.Equal(7, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void ASignalArgumentThatIsAPointerToAScalarIsRejected()
    {
        // No signal of the corpus carries the shape, which is exactly why the
        // branch is exercised here: a handler is handed the address the
        // emitter passes and not the value at it, so the signal is refused and
        // the one beside it, whose argument is a plain guint, is not.
        FixtureRun run = RunScalarWithOverlay("{}");

        string source = run.File("Packet.cs");

        Assert.DoesNotContain("SkewRequested", source, StringComparison.Ordinal);
        Assert.Contains("CountChanged", source, StringComparison.Ordinal);
        Assert.Equal(7, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void ADirectionOverrideReachesAPointerToAnEnumeration()
    {
        // The gst_rtcp_packet_sdes_get_entry shape: the C body is
        // `if (type) *type = item_type;`, and the gir types the parameter as
        // the bare enumeration with the star in the c:type alone. The out
        // projection is a local of the integer the enumeration is emitted over,
        // which is the width the C declaration names, so the correction is the
        // one a scalar already takes.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_kind#kind": { "direction": "out" } }
            }
            """);

        string source = run.File("Packet.cs");

        Assert.Contains("public void GetKind(out Gst.Kind kind)", source, StringComparison.Ordinal);
        Assert.Contains("int* kind", source, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void ADirectionOverrideReachesAPointerToABitfield()
    {
        // The other half of the same mapping: a bitfield is emitted as a
        // [Flags] enumeration over the same integer, so it is corrected with
        // the enumeration rather than left behind by it.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_mask#mask": { "direction": "out" } }
            }
            """);

        string source = run.File("Packet.cs");

        Assert.Contains("public void GetMask(out Gst.Mask mask)", source, StringComparison.Ordinal);
        Assert.Contains("int* mask", source, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, diagnostic => diagnostic.Code == "GEN0017");
    }

    [Fact]
    public void ADirectionOverrideDoesNotUnlockAScalarPassedByValue()
    {
        // Without a star in the c:type nothing says the C function writes
        // anything: the parameter is a value the callee reads, and correcting it
        // onto an out would hand the callee an address where it expects a
        // number. The correction is refused and the member keeps its argument.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_count#count": { "direction": "out" } }
            }
            """);

        Assert.Contains("public void GetCount(uint count)", run.File("Packet.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_packet_get_count#count",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ADirectionOverrideDoesNotUnlockAnEnumerationPassedByValue()
    {
        // The enumeration reads the star the same way a scalar does, so the
        // control is the same one: a parameter whose c:type carries no star is
        // a value the callee reads, whatever the overlays say about its
        // direction, and correcting it onto an out would hand the callee an
        // address where it expects a member of the enumeration.
        FixtureRun run = RunScalarWithOverlay(
            """
            {
              "annotationOverrides": { "gst_packet_get_flavour#flavour": { "direction": "out" } }
            }
            """);

        Assert.Contains(
            "public void GetFlavour(Gst.Kind flavour)",
            run.File("Packet.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_packet_get_flavour#flavour",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AFixedArraySizeDoesNotUnlockACallerAllocatedHandle()
    {
        // The other half of the same boundary: a caller allocated out parameter
        // of a record that is bound behind a handle stays rejected, whatever
        // the overlays say about its size. Only a blittable value has storage
        // the caller can provide, so gst_video_frame_map and its relatives stay
        // in the CallerAllocates bucket.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_info#info": { "fixedArraySize": 4 } }
            }
            """);

        Assert.DoesNotContain("gst_widget_get_info", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.CallerAllocates));
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_get_info#info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AReturnTypeOverrideNarrowsAFactoryOntoItsDeclaringType()
    {
        FixtureRun run = RunFactoryWithOverlay(
            """
            {
              "returnTypeOverrides": { "gst_widget_new": "Gst.Widget" }
            }
            """);

        Assert.Contains(
            "public static Gst.Widget New()",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "return Gst.GObject.Object.FromNative<Gst.Widget>(nativeResult, Gst.Interop.Transfer.Full)",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnTypeOverrideThatNamesAnotherTypeIsIgnored()
    {
        FixtureRun run = RunFactoryWithOverlay(
            """
            {
              "returnTypeOverrides": { "gst_widget_new": "Gst.Thing" }
            }
            """);

        Assert.Contains("public static Gst.Object New()", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0015");
    }

    /// <summary>Runs the fixture of this class with a hand written <c>fixups.json</c>.</summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string fixups) => RunWithOverlay(Body, fixups);

    /// <summary>
    /// Runs the scalar fixture, whose parameters are the shape the RTCP XR
    /// readers have.
    /// </summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunScalarWithOverlay(string fixups) => RunWithOverlay(ScalarPointerBody, fixups);

    /// <summary>
    /// Runs a factory fixture whose gir return type is the base class, which is
    /// the shape <c>gst_pipeline_new</c> has.
    /// </summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunFactoryWithOverlay(string fixups) => RunWithOverlay(
        """
            <class name="Object" c:type="GstObject" parent="GObject.InitiallyUnowned" glib:type-name="GstObject" glib:get-type="gst_object_get_type">
            </class>
            <class name="Widget" c:type="GstWidget" parent="Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <constructor name="new" c:identifier="gst_widget_new">
                <return-value transfer-ownership="full">
                  <type name="Object" c:type="GstObject*"/>
                </return-value>
              </constructor>
            </class>
        """,
        fixups);

    /// <summary>Runs one gir namespace with a hand written <c>fixups.json</c>.</summary>
    /// <param name="body">The members of the <c>Gst</c> namespace.</param>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string body, string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
