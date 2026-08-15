using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Fixture based tests for the tricky corners of the gir schema.
/// </summary>
public sealed class GirReaderTests
{
    private const string Header =
        """
        <!-- comment instead of an xml declaration, like the GStreamer girs -->
        <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
          <include name="GLib" version="2.0"/>
          <package name="test-1.0"/>
          <c:include name="test/test.h"/>
          <namespace name="Test" version="1.0" shared-library="libtest.so.0" c:identifier-prefixes="Test" c:symbol-prefixes="test">
        """;

    private const string Footer =
        """
          </namespace>
        </repository>
        """;

    [Fact]
    public void ReadsRepositoryHeaderWithoutXmlDeclaration()
    {
        GirRepository repository = Read("<function name=\"noop\" c:identifier=\"test_noop\"><return-value transfer-ownership=\"none\"><type name=\"none\" c:type=\"void\"/></return-value></function>");

        Assert.Equal("1.2", repository.SchemaVersion);
        Assert.Equal(new GirInclude("GLib", "2.0"), Assert.Single(repository.Includes));
        Assert.Equal("test-1.0", Assert.Single(repository.Packages));
        Assert.Equal("test/test.h", Assert.Single(repository.CIncludes));

        GirNamespace ns = Assert.Single(repository.Namespaces);
        Assert.Equal("Test", ns.Name);
        Assert.Equal("1.0", ns.Version);
        Assert.Equal("libtest.so.0", ns.SharedLibrary);
        Assert.Equal("Test", Assert.Single(ns.IdentifierPrefixes));
        Assert.Equal("test", Assert.Single(ns.SymbolPrefixes));
    }

    [Fact]
    public void ReadsClosureDestroyAndScopeAnnotations()
    {
        GirNamespace ns = ReadNamespace(
            """
            <function name="watch" c:identifier="test_watch" throws="1">
              <return-value transfer-ownership="none"><type name="guint" c:type="guint"/></return-value>
              <parameters>
                <parameter name="callback" transfer-ownership="none" nullable="1" allow-none="1" scope="notified" closure="1" destroy="2">
                  <type name="GLib.SourceFunc" c:type="GSourceFunc"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1"><type name="gpointer" c:type="gpointer"/></parameter>
                <parameter name="notify" transfer-ownership="none" scope="async"><type name="GLib.DestroyNotify" c:type="GDestroyNotify"/></parameter>
              </parameters>
            </function>
            """);

        GirFunction function = Assert.Single(ns.Functions);
        Assert.True(function.Throws);
        Assert.Equal(GirCallableKind.Function, function.Kind);

        GirParameter callback = function.Parameters[0];
        Assert.Equal(GirScope.Notified, callback.Scope);
        Assert.Equal(1, callback.ClosureIndex);
        Assert.Equal(2, callback.DestroyIndex);
        Assert.True(callback.IsNullable);
        Assert.Equal(GirScope.Async, function.Parameters[2].Scope);
        Assert.Equal(GirTransfer.None, function.Parameters[2].Transfer);
    }

    [Fact]
    public void TreatsLegacyAllowNoneAsNullable()
    {
        GirNamespace ns = ReadNamespace(
            """
            <function name="legacy" c:identifier="test_legacy">
              <return-value transfer-ownership="none"><type name="none" c:type="void"/></return-value>
              <parameters>
                <parameter name="old" transfer-ownership="none" allow-none="1"><type name="utf8" c:type="const gchar*"/></parameter>
                <parameter name="modern" transfer-ownership="none" nullable="0" allow-none="1"><type name="utf8" c:type="const gchar*"/></parameter>
              </parameters>
            </function>
            """);

        GirFunction function = Assert.Single(ns.Functions);
        Assert.True(function.Parameters[0].IsNullable);
        Assert.True(function.Parameters[0].IsAllowNone);

        // An explicit nullable attribute wins over the legacy alias.
        Assert.False(function.Parameters[1].IsNullable);
        Assert.True(function.Parameters[1].IsAllowNone);
    }

    [Fact]
    public void ReadsArrayAnnotations()
    {
        GirNamespace ns = ReadNamespace(
            """
            <function name="arrays" c:identifier="test_arrays">
              <return-value transfer-ownership="none">
                <array c:type="gchar**" zero-terminated="1"><type name="utf8" c:type="gchar*"/></array>
              </return-value>
              <parameters>
                <parameter name="data" transfer-ownership="none">
                  <array length="1" zero-terminated="0" c:type="guint8*"><type name="guint8" c:type="guint8"/></array>
                </parameter>
                <parameter name="size" transfer-ownership="none"><type name="gsize" c:type="gsize"/></parameter>
                <parameter name="fixed" transfer-ownership="none">
                  <array zero-terminated="0" fixed-size="4"><type name="gpointer" c:type="gpointer"/></array>
                </parameter>
              </parameters>
            </function>
            """);

        GirFunction function = Assert.Single(ns.Functions);
        GirArrayRef returned = Assert.IsType<GirArrayRef>(function.ReturnValue.Type);
        Assert.True(returned.IsZeroTerminated);
        Assert.Equal("utf8", returned.ElementType?.Name);

        GirArrayRef data = Assert.IsType<GirArrayRef>(function.Parameters[0].Type);
        Assert.Equal(1, data.LengthParameterIndex);
        Assert.False(data.IsZeroTerminated);
        Assert.Null(data.FixedSize);

        GirArrayRef fixedSize = Assert.IsType<GirArrayRef>(function.Parameters[2].Type);
        Assert.Equal(4, fixedSize.FixedSize);
    }

    [Fact]
    public void ReadsVarArgsOutParametersAndShadowing()
    {
        GirNamespace ns = ReadNamespace(
            """
            <record name="Thing" c:type="TestThing">
              <constructor name="new_full" c:identifier="test_thing_new_full" shadows="new">
                <return-value transfer-ownership="full"><type name="Thing" c:type="TestThing*"/></return-value>
              </constructor>
              <constructor name="new" c:identifier="test_thing_new" shadowed-by="new_full" introspectable="0">
                <return-value transfer-ownership="full"><type name="Thing" c:type="TestThing*"/></return-value>
                <parameters>
                  <parameter name="format" transfer-ownership="none"><type name="utf8" c:type="const gchar*"/></parameter>
                  <parameter name="..." transfer-ownership="none"><varargs/></parameter>
                </parameters>
              </constructor>
              <method name="peek" c:identifier="test_thing_peek">
                <return-value transfer-ownership="none"><type name="gboolean" c:type="gboolean"/></return-value>
                <parameters>
                  <instance-parameter name="thing" transfer-ownership="none"><type name="Thing" c:type="TestThing*"/></instance-parameter>
                  <parameter name="out_value" direction="out" caller-allocates="1" optional="1" transfer-ownership="none">
                    <type name="gint" c:type="gint*"/>
                  </parameter>
                </parameters>
              </method>
            </record>
            """);

        GirRecord record = Assert.Single(ns.Records);
        GirFunction full = record.Constructors[0];
        Assert.Equal("new", full.Shadows);
        Assert.Equal(GirCallableKind.Constructor, full.Kind);

        GirFunction legacy = record.Constructors[1];
        Assert.Equal("new_full", legacy.ShadowedBy);
        Assert.False(legacy.IsIntrospectable);
        Assert.True(legacy.HasVarArgs);
        Assert.True(legacy.Parameters[1].IsVarArgs);

        GirFunction peek = Assert.Single(record.Methods);
        Assert.NotNull(peek.InstanceParameter);
        Assert.Equal("thing", peek.InstanceParameter!.Name);
        GirParameter outValue = Assert.Single(peek.Parameters);
        Assert.Equal(GirDirection.Out, outValue.Direction);
        Assert.True(outValue.IsCallerAllocates);
        Assert.True(outValue.IsOptional);
    }

    [Fact]
    public void ReadsFieldsIncludingCallbackSlotsAndBits()
    {
        GirNamespace ns = ReadNamespace(
            """
            <record name="Layout" c:type="TestLayout" glib:is-gtype-struct-for="Thing">
              <field name="flags" writable="1" bits="3"><type name="guint" c:type="guint"/></field>
              <field name="reserved" readable="0" private="1"><type name="gpointer" c:type="gpointer"/></field>
              <field name="probe">
                <callback name="probe" c:type="TestProbe">
                  <return-value transfer-ownership="none"><type name="none" c:type="void"/></return-value>
                  <parameters>
                    <parameter name="thing" transfer-ownership="none"><type name="Thing" c:type="TestThing*"/></parameter>
                  </parameters>
                </callback>
              </field>
            </record>
            """);

        GirRecord record = Assert.Single(ns.Records);
        Assert.Equal("Thing", record.GlibIsGTypeStructFor);
        Assert.Equal(3, record.Fields[0].Bits);
        Assert.True(record.Fields[0].IsWritable);
        Assert.False(record.Fields[1].IsReadable);
        Assert.True(record.Fields[1].IsPrivate);

        GirCallback callback = Assert.IsType<GirCallback>(record.Fields[2].Callback);
        Assert.True(callback.IsFieldSlot);
        Assert.Equal("TestProbe", callback.CType);
        Assert.Null(record.Fields[2].Type);
    }

    [Fact]
    public void ReadsEnumerationsBitfieldsAliasesAndDocs()
    {
        GirNamespace ns = ReadNamespace(
            """
            <alias name="Handle" c:type="TestHandle"><type name="guint64" c:type="guint64"/></alias>
            <enumeration name="Kind" c:type="TestKind" glib:type-name="TestKind" glib:get-type="test_kind_get_type" glib:error-domain="test-kind-error">
              <doc xml:space="preserve">First line.

            Second paragraph.</doc>
              <member name="first" value="0" c:identifier="TEST_KIND_FIRST" glib:nick="first" glib:name="TEST_KIND_FIRST"/>
              <member name="second" value="-1" c:identifier="TEST_KIND_SECOND"/>
              <function name="quark" c:identifier="test_kind_quark">
                <return-value transfer-ownership="none"><type name="GLib.Quark" c:type="GQuark"/></return-value>
              </function>
            </enumeration>
            <bitfield name="Mask" c:type="TestMask" deprecated="1" deprecated-version="1.28">
              <doc-deprecated xml:space="preserve">Use something else.</doc-deprecated>
              <member name="none" value="0" c:identifier="TEST_MASK_NONE"/>
              <member name="all" value="4294967295" c:identifier="TEST_MASK_ALL"/>
            </bitfield>
            """);

        GirAlias alias = Assert.Single(ns.Aliases);
        Assert.Equal("guint64", alias.Target.Name);

        GirEnumeration kind = Assert.Single(ns.Enumerations);
        Assert.False(kind.IsBitfield);
        Assert.Equal("test-kind-error", kind.ErrorDomain);
        Assert.Equal("First line.\n\nSecond paragraph.", kind.Doc);
        Assert.Equal("TEST_KIND_FIRST", kind.Members[0].CIdentifier);
        Assert.Equal("first", kind.Members[0].Nick);
        Assert.Equal("-1", kind.Members[1].Value);
        Assert.Single(kind.Functions);

        GirEnumeration mask = Assert.Single(ns.Bitfields);
        Assert.True(mask.IsBitfield);
        Assert.True(mask.IsDeprecated);
        Assert.Equal("1.28", mask.DeprecatedVersion);
        Assert.Equal("Use something else.", mask.DocDeprecated);
    }

    [Fact]
    public void ReadsClassMembersIncludingSignalsAndProperties()
    {
        GirNamespace ns = ReadNamespace(
            """
            <class name="Widget" c:symbol-prefix="widget" c:type="TestWidget" parent="GObject.Object" abstract="1" glib:type-name="TestWidget" glib:get-type="test_widget_get_type" glib:type-struct="WidgetClass">
              <implements name="Pushable"/>
              <property name="child-count" writable="1" construct-only="1" transfer-ownership="none" getter="get_child_count">
                <type name="guint" c:type="guint"/>
              </property>
              <glib:signal name="child-added" when="last" detailed="1">
                <return-value transfer-ownership="none"><type name="none"/></return-value>
                <parameters>
                  <parameter name="child" transfer-ownership="none"><type name="Widget"/></parameter>
                </parameters>
              </glib:signal>
              <virtual-method name="draw" invoker="draw">
                <return-value transfer-ownership="none"><type name="none" c:type="void"/></return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none"><type name="Widget" c:type="TestWidget*"/></instance-parameter>
                </parameters>
              </virtual-method>
              <function name="lookup" c:identifier="test_widget_lookup" moved-to="Widget.find">
                <return-value transfer-ownership="none"><type name="Widget" c:type="TestWidget*"/></return-value>
              </function>
            </class>
            """);

        GirClass widget = Assert.Single(ns.Classes);
        Assert.True(widget.IsAbstract);
        Assert.Equal("GObject.Object", widget.Parent);
        Assert.Equal("WidgetClass", widget.GlibTypeStruct);
        Assert.Equal("Pushable", Assert.Single(widget.Implements));

        GirProperty property = Assert.Single(widget.Properties);
        Assert.True(property.IsConstructOnly);
        Assert.Equal("get_child_count", property.Getter);

        GirSignal signal = Assert.Single(widget.Signals);
        Assert.Equal("last", signal.When);
        Assert.True(signal.IsDetailed);
        Assert.Equal(GirCallableKind.Signal, signal.Kind);

        GirVirtualMethod draw = Assert.Single(widget.VirtualMethods);
        Assert.Equal("draw", draw.Invoker);

        GirFunction lookup = Assert.Single(widget.Functions);
        Assert.Equal("Widget.find", lookup.MovedTo);
    }

    private static GirRepository Read(string body) =>
        GirReader.ReadXml(Header + "\n" + body + "\n" + Footer, "fixture.gir");

    private static GirNamespace ReadNamespace(string body) => Read(body).Namespaces[0];
}
