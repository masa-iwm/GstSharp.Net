using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The classification rules of <see cref="Classifier"/> against the real girs.
/// </summary>
public sealed class ClassifierTests
{
    private static readonly Classifier Subject = new(GirFixture.Repository, Overlays.Empty, new DiagnosticBag());

    [Theory]
    [InlineData("Gst.Buffer")]
    [InlineData("Gst.BufferList")]
    [InlineData("Gst.Caps")]
    [InlineData("Gst.Context")]
    [InlineData("Gst.Event")]
    [InlineData("Gst.Memory")]
    [InlineData("Gst.Message")]
    [InlineData("Gst.MiniObject")]
    [InlineData("Gst.Promise")]
    [InlineData("Gst.Query")]
    [InlineData("Gst.Sample")]
    [InlineData("Gst.TagList")]
    public void MiniObjectsAreRecognized(string qualifiedName) =>
        Assert.Equal(TypeKind.MiniObject, Subject.Classify(GirFixture.Symbol(qualifiedName)));

    [Fact]
    public void PromiseIsAMiniObjectAlthoughItsFirstFieldIsCalledParent()
    {
        GirRecord promise = Assert.IsType<GirRecord>(GirFixture.Symbol("Gst.Promise").Declaration);

        Assert.Equal("parent", promise.Fields[0].Name);
        Assert.Equal("MiniObject", promise.Fields[0].Type?.Name);
        Assert.Equal(TypeKind.MiniObject, Subject.Classify(promise));
    }

    [Fact]
    public void FieldlessMiniObjectsComeFromTheAllowList()
    {
        // Sample, BufferList and Context are opaque in the gir, so the first
        // field rule cannot see the embedded GstMiniObject.
        foreach (string name in new[] { "Gst.Sample", "Gst.BufferList", "Gst.Context" })
        {
            GirRecord record = Assert.IsType<GirRecord>(GirFixture.Symbol(name).Declaration);
            Assert.Empty(record.Fields);
            Assert.NotNull(record.GlibGetType);
            Assert.Equal(TypeKind.MiniObject, Subject.Classify(record));
        }
    }

    [Theory]
    [InlineData("Gst.Fraction")]
    [InlineData("Gst.IntRange")]
    [InlineData("Gst.ValueList")]
    [InlineData("GObject.ParamSpecBoolean")]
    public void FundamentalsAreSkipped(string qualifiedName) =>
        Assert.Equal(TypeKind.Fundamental, Subject.Classify(GirFixture.Symbol(qualifiedName)));

    [Theory]
    [InlineData("Gst.ElementClass")]
    [InlineData("Gst.ObjectClass")]
    [InlineData("Gst.ChildProxyInterface")]
    public void GTypeStructsAreSkipped(string qualifiedName) =>
        Assert.Equal(TypeKind.GTypeStruct, Subject.Classify(GirFixture.Symbol(qualifiedName)));

    [Fact]
    public void MapInfoIsAPlainStruct()
    {
        GirRecord mapInfo = Assert.IsType<GirRecord>(GirFixture.Symbol("Gst.MapInfo").Declaration);

        // Blittable in spite of its arrays: data decays to a pointer and
        // user_data/_gst_reserved are fixed size gpointer arrays.
        Assert.Null(mapInfo.GlibGetType);
        Assert.Equal(TypeKind.PlainStruct, Subject.Classify(mapInfo));
        Assert.True(Subject.IsBlittableRecord(mapInfo));
    }

    [Theory]
    [InlineData("Gst.Segment")]
    [InlineData("Gst.Structure")]
    [InlineData("Gst.AllocationParams")]
    [InlineData("Gst.CapsFeatures")]
    public void RecordsWithAGetTypeAreBoxed(string qualifiedName)
    {
        GirRecord record = Assert.IsType<GirRecord>(GirFixture.Symbol(qualifiedName).Declaration);

        // Segment and AllocationParams carry plain blittable fields but are
        // registered boxed types, and glib:get-type wins over blittability.
        Assert.NotNull(record.GlibGetType);
        Assert.Equal(TypeKind.Boxed, Subject.Classify(record));
    }

    [Theory]
    [InlineData("Gst.Poll")]
    [InlineData("Gst.DebugMessage")]
    [InlineData("Gst.PadPrivate")]
    public void OpaqueRecordsWithoutAGetTypeStayOpaque(string qualifiedName)
    {
        GirRecord record = Assert.IsType<GirRecord>(GirFixture.Symbol(qualifiedName).Declaration);

        Assert.Null(record.GlibGetType);
        Assert.Equal(TypeKind.OpaqueRecord, Subject.Classify(record));
    }

    [Fact]
    public void BlittableRecordsWithoutAGetTypeArePlainStructs()
    {
        // GstDebugCategory is a plain C struct: two integers and two string
        // pointers, no boxed registration.
        GirRecord record = Assert.IsType<GirRecord>(GirFixture.Symbol("Gst.DebugCategory").Declaration);

        Assert.Equal(TypeKind.PlainStruct, Subject.Classify(record));
    }

    [Theory]
    [InlineData("Gst.Element", nameof(TypeKind.GObjectClass))]
    [InlineData("Gst.Bin", nameof(TypeKind.GObjectClass))]
    [InlineData("Gst.ChildProxy", nameof(TypeKind.Interface))]
    [InlineData("Gst.PadProbeCallback", nameof(TypeKind.Callback))]
    [InlineData("Gst.ClockTime", nameof(TypeKind.Alias))]
    [InlineData("Gst.MessageType", nameof(TypeKind.FlagsType))]
    [InlineData("Gst.State", nameof(TypeKind.EnumType))]
    public void RemainingKindsAreRecognized(string qualifiedName, string expected) =>
        Assert.Equal(Enum.Parse<TypeKind>(expected), Subject.Classify(GirFixture.Symbol(qualifiedName)));

    [Theory]
    [InlineData("Gst.MessageType", nameof(EnumUnderlyingType.UInt32))]
    [InlineData("Gst.DebugGraphDetails", nameof(EnumUnderlyingType.UInt32))]
    [InlineData("Gst.SeekFlags", nameof(EnumUnderlyingType.Int32))]
    [InlineData("Gst.FlowReturn", nameof(EnumUnderlyingType.Int32))]
    public void EnumWidthFollowsTheMemberValues(string qualifiedName, string expected)
    {
        GirEnumeration enumeration = Assert.IsType<GirEnumeration>(GirFixture.Symbol(qualifiedName).Declaration);

        Assert.Equal(Enum.Parse<EnumUnderlyingType>(expected), EnumFacts.GetUnderlyingType(enumeration, new DiagnosticBag()));
    }

    [Fact]
    public void MessageTypeUsesBitThirtyOne()
    {
        GirEnumeration messageType = Assert.IsType<GirEnumeration>(GirFixture.Symbol("Gst.MessageType").Declaration);

        GirEnumMember any = messageType.Members.Single(m => string.Equals(m.Name, "any", StringComparison.Ordinal));
        Assert.True(EnumFacts.TryParseValue(any.Value, out long value));
        Assert.Equal(4294967295L, value);
    }
}
