using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>arrayOverrides</c> key: the corrections it applies to an
/// <c>&lt;array&gt;</c> the gir already spells, the shapes it refuses to
/// invent, and the guard and the note a shared or corrected length brings with
/// it.
/// </summary>
public sealed class ArrayOverrideTests
{
    /// <summary>
    /// One class carrying every shape the corrections are read on: an array
    /// the gir counts nothing off, one it sizes at a fixed four, an out array
    /// it counts off a length, and two that share one length the way
    /// <c>gst_audio_reorder_channels</c> does.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack_channels" c:identifier="gst_widget_pack_channels">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="channels" transfer-ownership="none">
                    <type name="guint8" c:type="guint8"/>
                  </parameter>
                  <parameter name="mapping" transfer-ownership="none">
                    <array c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_matrix" c:identifier="gst_widget_set_matrix">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                  <parameter name="matrix" transfer-ownership="none">
                    <array fixed-size="4" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="get_data" c:identifier="gst_widget_get_data">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="data" direction="out" transfer-ownership="full">
                    <array length="1" c:type="guint8**"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                  <parameter name="length" direction="out" transfer-ownership="none">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="blend" c:identifier="gst_widget_blend">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                  <parameter name="from" transfer-ownership="none">
                    <array length="0" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                  <parameter name="to" transfer-ownership="none">
                    <array length="0" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_formats" c:identifier="gst_widget_set_formats">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="n_formats" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                  <parameter name="formats" transfer-ownership="none">
                    <array length="0" c:type="const GstShape*"><type name="Shape" c:type="GstShape"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="get_formats" c:identifier="gst_widget_get_formats">
                <return-value transfer-ownership="full">
                  <array length="0" c:type="GstShape*"><type name="Shape" c:type="GstShape"/></array>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="len" direction="out" transfer-ownership="none">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_key" c:identifier="gst_widget_set_key">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="key" transfer-ownership="none">
                    <array fixed-size="16" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_seed" c:identifier="gst_widget_set_seed">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="seed" transfer-ownership="none" nullable="1">
                    <array fixed-size="8" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="take_key" c:identifier="gst_widget_take_key">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="key" transfer-ownership="full">
                    <array fixed-size="16" c:type="guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_frame" c:identifier="gst_widget_set_frame">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="len" transfer-ownership="none">
                    <type name="guint16" c:type="guint16"/>
                  </parameter>
                  <parameter name="frame" transfer-ownership="none">
                    <array length="0" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_page" c:identifier="gst_widget_set_page">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="len" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                  <parameter name="page" transfer-ownership="none">
                    <array length="0" c:type="const guint8*"><type name="guint8" c:type="guint8"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="get_sizes" c:identifier="gst_widget_get_sizes">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="sizes" direction="out" caller-allocates="1" transfer-ownership="none">
                    <array fixed-size="4" c:type="gint*"><type name="gint" c:type="gint"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="get_scales" c:identifier="gst_widget_get_scales">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="scales" direction="out" caller-allocates="0" transfer-ownership="full">
                    <array fixed-size="4" c:type="gint**"><type name="gint" c:type="gint"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="get_signature" c:identifier="gst_widget_get_signature">
                <return-value transfer-ownership="full">
                  <array fixed-size="4" c:type="guint8*"><type name="guint8" c:type="guint8"/></array>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
            <enumeration name="Shape" c:type="GstShape">
              <member name="round" value="0" c:identifier="GST_SHAPE_ROUND"/>
              <member name="square" value="1" c:identifier="GST_SHAPE_SQUARE"/>
            </enumeration>
        """;

    [Fact]
    public void ALengthCorrectionHidesItsParameterAndCountsTheSpan()
    {
        // gst_codec_utils_opus_create_caps is the real one: the C function
        // reads channel_mapping[i] for i < channels and the gir carries a bare
        // (array) with no length on it, so without the correction the element
        // count has nowhere to come from and the member stays unbound.
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_channels#mapping": { "length": 0 } }
            }
            """);

        Assert.Equal(
            """
            public bool PackChannels(System.ReadOnlySpan<byte> mapping)
            {
                if (mapping.Length > byte.MaxValue)
                {
                    throw new ArgumentException(
                        "mapping must have at most 255 elements: the call takes its count as a byte.",
                        nameof(mapping));
                }
                fixed (byte* mappingPointer = mapping)
                {
                    int nativeResult = GstWidgetPackChannels(Handle, (byte)mapping.Length, mappingPointer);
                    System.GC.KeepAlive(this);
                    return nativeResult != 0;
                }
            }
            """,
            run.Member("Widget.cs", "public bool PackChannels("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACorrectedLengthIsNamedInTheDocumentation()
    {
        // A length the gir states is visible in the C declaration; one the
        // overlays supply is not, so the member would silently take a C
        // argument off a span's Length with nothing saying which argument.
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_channels#mapping": { "length": 0 } }
            }
            """);

        Assert.Contains(
            "/// Its number of elements is passed to the C function as the <c>channels</c> argument.",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ALengthCorrectionClearsAFixedSize()
    {
        // The two are mutually exclusive in GIR, so an entry that states the
        // length says that the fixed size the gir carries is not the fact
        // about the C function; the array binds as an ordinary counted span.
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_set_matrix#matrix": { "length": 0 } }
            }
            """);

        Assert.Equal(
            """
            public void SetMatrix(System.ReadOnlySpan<byte> matrix)
            {
                fixed (byte* matrixPointer = matrix)
                {
                    GstWidgetSetMatrix(Handle, (uint)matrix.Length, matrixPointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetMatrix("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFixedSizeCorrectionClearsALength()
    {
        // The other half of the same rule. The out array the gir counts off
        // its length parameter binds without the correction and stops binding
        // with it, because an out array of a size the C declaration fixes is
        // storage the caller provides rather than a pointer coming back.
        string plain = Fixture.Run(Body).File("Widget.cs");
        Assert.Contains("public void GetData(out byte[]? data)", plain, StringComparison.Ordinal);

        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_get_data#data": { "fixedSize": 4 } }
            }
            """);

        Assert.DoesNotContain("public void GetData(", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroTerminatedCorrectionChangesNothingOnAByteArray()
    {
        // No arm reads zero-termination for anything but a vector of strings,
        // so the field is carried for the schema rather than for an effect the
        // planner has today. The fixture pins that it has none.
        FixtureRun corrected = RunWithOverlay(
            """
            {
              "arrayOverrides": {
                "gst_widget_pack_channels#mapping": { "length": 0, "zeroTerminated": true }
              }
            }
            """);

        FixtureRun plain = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_channels#mapping": { "length": 0 } }
            }
            """);

        Assert.Equal(
            plain.Member("Widget.cs", "public bool PackChannels("),
            corrected.Member("Widget.cs", "public bool PackChannels("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnElementTypeCorrectionRemapsTheElement()
    {
        // The element of the corrected array is what the mapping is taken
        // from, which is what makes the field take effect at all.
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": {
                "gst_widget_pack_channels#mapping": { "length": 0, "elementType": "gint" }
              }
            }
            """);

        Assert.Equal(
            """
            public bool PackChannels(System.ReadOnlySpan<int> mapping)
            {
                if (mapping.Length > byte.MaxValue)
                {
                    throw new ArgumentException(
                        "mapping must have at most 255 elements: the call takes its count as a byte.",
                        nameof(mapping));
                }
                fixed (int* mappingPointer = mapping)
                {
                    int nativeResult = GstWidgetPackChannels(Handle, (byte)mapping.Length, mappingPointer);
                    System.GC.KeepAlive(this);
                    return nativeResult != 0;
                }
            }
            """,
            run.Member("Widget.cs", "public bool PackChannels("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnEntryOnSomethingThatIsNoArrayIsReportedAsStale()
    {
        // The key never invents an array: deciding that a bare pointer is one
        // is the decision an overlay must not make on its own. The lookup on
        // its own is not a use, which is what lets this be reported.
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_channels#channels": { "length": 0 } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0020", StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "'gst_widget_pack_channels#channels' matched no array parameter or return value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryThatNamesNoSymbolIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_colours#mapping": { "length": 0 } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0020", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack_colours#mapping", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAppliedEntryIsNotReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "arrayOverrides": { "gst_widget_pack_channels#mapping": { "length": 0 } }
            }
            """);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0020", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoArraysOnOneLengthGuardTheirLengths()
    {
        // Only one of them is the span the call site reads Length off, so a
        // caller that hands over a shorter second one would have the C
        // function read past its end.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void Blend(System.ReadOnlySpan<byte> from, System.ReadOnlySpan<byte> to)
            {
                if (from.Length != to.Length)
                {
                    throw new ArgumentException(
                        "from must have the same length as to: the call reads one length for both.",
                        nameof(from));
                }
                fixed (byte* fromPointer = from)
                {
                    fixed (byte* toPointer = to)
                    {
                        GstWidgetBlend(Handle, (uint)to.Length, fromPointer, toPointer);
                        System.GC.KeepAlive(this);
                    }
                }
            }
            """,
            run.Member("Widget.cs", "public void Blend("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheOwnerOfASharedLengthNamesItInTheDocumentation()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Contains(
            "/// Its number of elements is passed to the C function as the <c>count</c> argument.",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumElementIsReadAsTheEnumeration()
    {
        // The block holds the int the enumeration is backed by, which is the
        // reinterpretation a scalar enumeration argument already performs; the
        // import takes a pointer to the public type.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void SetFormats(System.ReadOnlySpan<Gst.Shape> formats)
            {
                fixed (Gst.Shape* formatsPointer = formats)
                {
                    GstWidgetSetFormats(Handle, (uint)formats.Length, formatsPointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetFormats("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial void GstWidgetSetFormats(nint widget, uint nFormats, Gst.Shape* formats);",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEnumElementIsReadBackOutOfAReturnedArray()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public Gst.Shape[]? GetFormats()
            {
                uint lenNative = default;
                nint nativeResult = GstWidgetGetFormats(Handle, &lenNative);
                System.GC.KeepAlive(this);
                Gst.Shape[]? result = null;
                if (nativeResult != 0)
                {
                    result = new Gst.Shape[(int)lenNative];
                    new System.ReadOnlySpan<Gst.Shape>((void*)nativeResult, (int)lenNative).CopyTo(result);
                    Gst.Interop.GMarshal.Free(nativeResult);
                }
                return result;
            }
            """,
            run.Member("Widget.cs", "public Gst.Shape[]? GetFormats("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFixedSizeInArrayIsASpanOfExactlyThatLength()
    {
        // The C function reads the size its declaration states whenever the
        // pointer is not NULL, so a shorter span is an over-read.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void SetKey(System.ReadOnlySpan<byte> key)
            {
                if (key.Length != 16)
                {
                    throw new ArgumentException(
                        "key must have exactly 16 elements.",
                        nameof(key));
                }
                fixed (byte* keyPointer = key)
                {
                    GstWidgetSetKey(Handle, keyPointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetKey("),
            StringComparer.Ordinal);

        Assert.Contains(
            "/// The C declaration sizes this buffer at 16 elements; pass exactly 16.",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANullableFixedSizeInArrayAlsoAcceptsAnEmptySpan()
    {
        // An empty span pins to a null pointer, which is the NULL the C
        // function documents.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void SetSeed(System.ReadOnlySpan<byte> seed)
            {
                if (seed.Length != 8 && seed.Length != 0)
                {
                    throw new ArgumentException(
                        "seed must have exactly 8 elements, or none at all.",
                        nameof(seed));
                }
                fixed (byte* seedPointer = seed)
                {
                    GstWidgetSetSeed(Handle, seedPointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetSeed("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFixedSizeOutArrayTheCallerAllocatesBecomesInlineStorage()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void GetSizes(out Gst.Widget.SizesArray sizes)
            {
                Gst.Widget.SizesArray sizesNative = default;
                GstWidgetGetSizes(Handle, &sizesNative);
                System.GC.KeepAlive(this);
                sizes = sizesNative;
            }
            """,
            run.Member("Widget.cs", "public void GetSizes("),
            StringComparer.Ordinal);

        Assert.Contains("[InlineArray(4)]", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFixedSizeOutArrayTheCalleeAllocatesStaysUnboundWithoutADiagnostic()
    {
        // The caller allocates gate is what makes the shape safe. Without it
        // the storage the caller passed would be read as a pointer the callee
        // handed back, so the member is left unbound - silently, because the
        // girs state a great many fixed size arrays no member reaches and
        // GEN0017 belongs to an explicit fixedArraySize entry.
        FixtureRun run = Fixture.Run(Body);

        Assert.DoesNotContain("public void GetScales(", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal));
    }

    [Fact]
    public void AFixedSizeReturnCopiesExactlyThatManyElements()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public byte[]? GetSignature()
            {
                nint nativeResult = GstWidgetGetSignature(Handle);
                System.GC.KeepAlive(this);
                byte[]? result = null;
                if (nativeResult != 0)
                {
                    result = new byte[4];
                    new System.ReadOnlySpan<byte>((void*)nativeResult, 4).CopyTo(result);
                    Gst.Interop.GMarshal.Free(nativeResult);
                }
                return result;
            }
            """,
            run.Member("Widget.cs", "public byte[]? GetSignature("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFixedArraySizeEntryOnAnArrayIsIgnoredAndReported()
    {
        // The two keys do not overlap: 'fixedArraySize' states the size of a
        // parameter the gir spells as a pointer to one value, and an <array>
        // the gir already spells is corrected through 'arrayOverrides'. The
        // entry is ignored and the parameter keeps the projection its array
        // gives it, which for a fixed size four is the storage below.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_sizes#sizes": { "fixedArraySize": 8 } }
            }
            """);

        Assert.Contains(
            "public void GetSizes(out Gst.Widget.SizesArray sizes)",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.Contains("[InlineArray(4)]", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_get_sizes#sizes", StringComparison.Ordinal));
    }

    [Fact]
    public void AFixedSizeInArrayTheCalleeTakesOverStaysUnbound()
    {
        // The caller keeps owning the memory a span points at, so an array the
        // callee frees cannot be one - and a size the C declaration states
        // changes nothing about who owns the block. The counted arm refuses the
        // same transfer, and this is the fixed size half of that rule.
        FixtureRun run = Fixture.Run(Body);

        Assert.DoesNotContain("public void TakeKey(", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal));
    }

    [Fact]
    public void ACountNarrowerThanALengthIsGuardedAgainstWrapping()
    {
        // The hidden count is a cast of Length, and a cast into a type that
        // cannot hold it wraps silently: a 65536 element span counted by a
        // guint16 would tell the C function there is nothing to read while the
        // pointer it is handed is real.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void SetFrame(System.ReadOnlySpan<byte> frame)
            {
                if (frame.Length > ushort.MaxValue)
                {
                    throw new ArgumentException(
                        "frame must have at most 65535 elements: the call takes its count as a ushort.",
                        nameof(frame));
                }
                fixed (byte* framePointer = frame)
                {
                    GstWidgetSetFrame(Handle, (ushort)frame.Length, framePointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetFrame("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACountThatHoldsEveryLengthIsNotGuarded()
    {
        // A length is a non-negative int, so nothing narrower than int is at
        // stake here and the guard would only be noise.
        FixtureRun run = Fixture.Run(Body);

        Assert.Equal(
            """
            public void SetPage(System.ReadOnlySpan<byte> page)
            {
                fixed (byte* pagePointer = page)
                {
                    GstWidgetSetPage(Handle, (uint)page.Length, pagePointer);
                    System.GC.KeepAlive(this);
                }
            }
            """,
            run.Member("Widget.cs", "public void SetPage("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void EveryGuardedSpanDocumentsTheExceptionItThrows()
    {
        // A length rule only the body states is one a caller meets at run time
        // and nowhere else, so each of the three shapes says so in the
        // documentation of the parameter that carries it.
        string file = Fixture.Run(Body).File("Widget.cs");

        string[] sentences =
        [
            "<paramref name=\"frame\"/> has more than 65535 elements.",
            "<paramref name=\"key\"/> does not have exactly 16 elements.",
            "<paramref name=\"seed\"/> does not have exactly 8 elements and is not empty.",
            "<paramref name=\"from\"/> does not have the same length as <paramref name=\"to\"/>.",
        ];

        foreach (string sentence in sentences)
        {
            Assert.Contains(
                "/// <exception cref=\"ArgumentException\">\n    /// " + sentence + "\n    /// </exception>\n",
                file,
                StringComparison.Ordinal);
        }

        // A span nothing about the length is the caller's to get wrong carries
        // no tag, which is what keeps the documentation honest in both
        // directions.
        Assert.DoesNotContain(
            "<paramref name=\"page\"/> has more than",
            file,
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
