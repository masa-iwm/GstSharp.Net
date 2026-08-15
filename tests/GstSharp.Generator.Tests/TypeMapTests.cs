using GstSharp.Generator.GirParsing;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// Gir type reference to raw and public C# type mapping.
/// </summary>
public sealed class TypeMapTests
{
    private static readonly DiagnosticBag Diagnostics = new();

    private static readonly TypeMap Subject = new(
        GirFixture.Repository,
        new Classifier(GirFixture.Repository, Diagnostics),
        new NameMapper(Overlays.Empty),
        Diagnostics);

    [Theory]
    [InlineData("gboolean", "int", "bool", nameof(MarshalKind.Boolean))]
    [InlineData("gint8", "sbyte", "sbyte", nameof(MarshalKind.Blittable))]
    [InlineData("guint8", "byte", "byte", nameof(MarshalKind.Blittable))]
    [InlineData("gint16", "short", "short", nameof(MarshalKind.Blittable))]
    [InlineData("guint16", "ushort", "ushort", nameof(MarshalKind.Blittable))]
    [InlineData("gint", "int", "int", nameof(MarshalKind.Blittable))]
    [InlineData("guint", "uint", "uint", nameof(MarshalKind.Blittable))]
    [InlineData("gint64", "long", "long", nameof(MarshalKind.Blittable))]
    [InlineData("guint64", "ulong", "ulong", nameof(MarshalKind.Blittable))]
    [InlineData("glong", "System.Runtime.InteropServices.CLong", "System.Runtime.InteropServices.CLong", nameof(MarshalKind.Blittable))]
    [InlineData("gulong", "System.Runtime.InteropServices.CULong", "System.Runtime.InteropServices.CULong", nameof(MarshalKind.Blittable))]
    [InlineData("gsize", "nuint", "nuint", nameof(MarshalKind.Blittable))]
    [InlineData("gssize", "nint", "nint", nameof(MarshalKind.Blittable))]
    [InlineData("gfloat", "float", "float", nameof(MarshalKind.Blittable))]
    [InlineData("gdouble", "double", "double", nameof(MarshalKind.Blittable))]
    [InlineData("gunichar", "uint", "uint", nameof(MarshalKind.Blittable))]
    [InlineData("gpointer", "nint", "nint", nameof(MarshalKind.Pointer))]
    [InlineData("utf8", "nint", "string", nameof(MarshalKind.Utf8String))]
    [InlineData("filename", "nint", "string", nameof(MarshalKind.FilenameString))]
    [InlineData("GType", "nuint", "Gst.GObject.GType", nameof(MarshalKind.GType))]
    [InlineData("none", "void", "void", nameof(MarshalKind.Void))]
    public void ScalarsFollowTheCoreRules(string girName, string raw, string publicType, string kind)
    {
        MappedType mapped = Subject.Map(new GirTypeRef { Name = girName }, GirFixture.Namespace("Gst"));

        Assert.Equal(raw, mapped.RawType);
        Assert.Equal(publicType, mapped.PublicType);
        Assert.Equal(Enum.Parse<MarshalKind>(kind), mapped.Kind);
    }

    [Fact]
    public void QuarkBecomesAWrapper()
    {
        MappedType mapped = Subject.Map(new GirTypeRef { Name = "GLib.Quark", CType = "GQuark" }, GirFixture.Namespace("Gst"));

        Assert.Equal("uint", mapped.RawType);
        Assert.Equal("Gst.GLib.Quark", mapped.PublicType);
        Assert.Equal(MarshalKind.Quark, mapped.Kind);
    }

    [Fact]
    public void ClockTimeKeepsItsOwnWrapper()
    {
        MappedType mapped = Subject.Map(
            new GirTypeRef { Name = "ClockTime", CType = "GstClockTime" },
            GirFixture.Namespace("Gst"));

        Assert.Equal("ulong", mapped.RawType);
        Assert.Equal("Gst.ClockTime", mapped.PublicType);
    }

    [Fact]
    public void OtherAliasesCollapseOntoTheirTarget()
    {
        MappedType mapped = Subject.Map(
            new GirTypeRef { Name = "ClockTimeDiff", CType = "GstClockTimeDiff" },
            GirFixture.Namespace("Gst"));

        Assert.Equal("long", mapped.RawType);
        Assert.Equal("long", mapped.PublicType);
    }

    [Theory]
    [InlineData("MessageType", "uint", "Gst.MessageType", nameof(MarshalKind.Flags))]
    [InlineData("FlowReturn", "int", "Gst.FlowReturn", nameof(MarshalKind.Enum))]
    [InlineData("SeekFlags", "int", "Gst.SeekFlags", nameof(MarshalKind.Flags))]
    public void EnumerationsUseTheComputedUnderlyingType(
        string girName,
        string raw,
        string publicType,
        string kind)
    {
        MappedType mapped = Subject.Map(new GirTypeRef { Name = girName }, GirFixture.Namespace("Gst"));

        Assert.Equal(raw, mapped.RawType);
        Assert.Equal(publicType, mapped.PublicType);
        Assert.Equal(Enum.Parse<MarshalKind>(kind), mapped.Kind);
    }

    [Theory]
    [InlineData("Element", "Gst.Element", nameof(MarshalKind.GObject))]
    [InlineData("Buffer", "Gst.Buffer", nameof(MarshalKind.MiniObject))]
    [InlineData("Structure", "Gst.Structure", nameof(MarshalKind.Boxed))]
    [InlineData("ChildProxy", "Gst.ChildProxy", nameof(MarshalKind.Interface))]
    [InlineData("PadProbeCallback", "Gst.PadProbeCallback", nameof(MarshalKind.Callback))]
    [InlineData("Poll", "Gst.Poll", nameof(MarshalKind.OpaqueRecord))]
    public void HandleTypesMarshalAsPointers(string girName, string publicType, string kind)
    {
        MappedType mapped = Subject.Map(
            new GirTypeRef { Name = girName, CType = "Gst" + girName + "*" },
            GirFixture.Namespace("Gst"));

        Assert.Equal("nint", mapped.RawType);
        Assert.Equal(publicType, mapped.PublicType);
        Assert.Equal(Enum.Parse<MarshalKind>(kind), mapped.Kind);
    }

    [Fact]
    public void PlainStructsArePassedByValueUnlessTheGirSpellsAPointer()
    {
        GirNamespace gst = GirFixture.Namespace("Gst");

        MappedType byValue = Subject.Map(new GirTypeRef { Name = "MapInfo", CType = "GstMapInfo" }, gst);
        Assert.Equal("Gst.MapInfo", byValue.RawType);
        Assert.Equal(MarshalKind.PlainStruct, byValue.Kind);

        MappedType byPointer = Subject.Map(new GirTypeRef { Name = "MapInfo", CType = "GstMapInfo*" }, gst);
        Assert.Equal("nint", byPointer.RawType);
        Assert.Equal("Gst.MapInfo", byPointer.PublicType);
    }

    [Fact]
    public void ArraysCarryTheirLengthPairing()
    {
        GirFunction function = ReadFunction(
            """
            <function name="fill" c:identifier="test_fill">
              <return-value transfer-ownership="none"><type name="none" c:type="void"/></return-value>
              <parameters>
                <parameter name="data" transfer-ownership="none">
                  <array length="1" zero-terminated="0" c:type="guint8*"><type name="guint8" c:type="guint8"/></array>
                </parameter>
                <parameter name="size" transfer-ownership="none"><type name="gsize" c:type="gsize"/></parameter>
              </parameters>
            </function>
            """);

        MappedType mapped = Subject.Map(function.Parameters[0].Type, GirFixture.Namespace("Gst"));

        Assert.Equal(MarshalKind.Array, mapped.Kind);
        Assert.Equal("nint", mapped.RawType);
        Assert.Equal("byte[]", mapped.PublicType);
        Assert.Equal(1, mapped.LengthParameterIndex);
        Assert.Equal("byte", mapped.ElementType?.PublicType);
    }

    [Fact]
    public void ListsKeepTheirElementType()
    {
        GirFunction function = ReadFunction(
            """
            <function name="list" c:identifier="test_list">
              <return-value transfer-ownership="full">
                <type name="GLib.List" c:type="GList*"><type name="Gst.Buffer"/></type>
              </return-value>
            </function>
            """);

        MappedType mapped = Subject.Map(function.ReturnValue.Type, GirFixture.Namespace("Gst"));

        Assert.Equal(MarshalKind.GList, mapped.Kind);
        Assert.Equal("nint", mapped.RawType);
        Assert.Equal("Gst.Buffer", mapped.ElementType?.PublicType);
    }

    [Fact]
    public void UnknownTypesFallBackToAPointerAndWarn()
    {
        DiagnosticBag diagnostics = new();
        TypeMap map = new(
            GirFixture.Repository,
            new Classifier(GirFixture.Repository, diagnostics),
            new NameMapper(Overlays.Empty),
            diagnostics);

        MappedType mapped = map.Map(new GirTypeRef { Name = "Nonexistent", CType = "GstNonexistent*" }, GirFixture.Namespace("Gst"));

        Assert.Equal("nint", mapped.RawType);
        Assert.Equal(MarshalKind.Pointer, mapped.Kind);
        Assert.Contains(diagnostics.Items, d => string.Equals(d.Code, "GEN0001", StringComparison.Ordinal));
    }

    private static GirFunction ReadFunction(string body) =>
        GirReader.ReadXml(
            $"""
            <repository xmlns="http://www.gtk.org/introspection/core/1.0" xmlns:c="http://www.gtk.org/introspection/c/1.0" xmlns:glib="http://www.gtk.org/introspection/glib/1.0" version="1.2">
              <namespace name="Test" version="1.0" c:identifier-prefixes="Test" c:symbol-prefixes="test">
            {body}
              </namespace>
            </repository>
            """,
            "fixture.gir").Namespaces[0].Functions[0];
}
