using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The members a value projected structure carries: how the instance is
/// pinned, what makes a member <c>readonly</c>, and what <c>ToString</c>
/// answers for a structure the C side cannot describe.
/// </summary>
public sealed class ValueInstanceMethodTests
{
    /// <summary>
    /// Two plain structures with every shape the gate lift has to cover: a
    /// mutating instance method, a read only one, <c>ref</c> parameters of a
    /// scalar and of another structure, a nullable and a non nullable
    /// <c>to_string</c>, and a static function.
    /// </summary>
    private const string Fixtures =
        """
            <record name="Rect" c:type="GstRect">
              <field name="x" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="y" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Colorimetry" c:type="GstColorimetry">
              <field name="range" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <method name="reset" c:identifier="gst_colorimetry_reset">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="cinfo" transfer-ownership="none">
                    <type name="Colorimetry" c:type="GstColorimetry*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="is_equal" c:identifier="gst_colorimetry_is_equal">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="cinfo" transfer-ownership="none">
                    <type name="Colorimetry" c:type="const GstColorimetry*"/>
                  </instance-parameter>
                  <parameter name="other" transfer-ownership="none">
                    <type name="Colorimetry" c:type="const GstColorimetry*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="transform" c:identifier="gst_colorimetry_transform">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="cinfo" transfer-ownership="none">
                    <type name="Colorimetry" c:type="const GstColorimetry*"/>
                  </instance-parameter>
                  <parameter name="x" direction="inout" caller-allocates="0" transfer-ownership="full">
                    <type name="gint" c:type="gint*"/>
                  </parameter>
                  <parameter name="rect" direction="inout" caller-allocates="0" transfer-ownership="full">
                    <type name="Rect" c:type="GstRect*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="to_string" c:identifier="gst_colorimetry_to_string">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="cinfo" transfer-ownership="none">
                    <type name="Colorimetry" c:type="const GstColorimetry*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <function name="get_quark" c:identifier="gst_colorimetry_get_quark">
                <return-value transfer-ownership="none">
                  <type name="guint" c:type="guint"/>
                </return-value>
              </function>
            </record>
            <record name="Mastering" c:type="GstMastering">
              <field name="level" writable="1">
                <type name="guint" c:type="guint"/>
              </field>
              <method name="to_string" c:identifier="gst_mastering_to_string">
                <return-value transfer-ownership="full">
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="minfo" transfer-ownership="none">
                    <type name="Mastering" c:type="const GstMastering*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Fixtures),
        isThreadSafe: true);

    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static GenerationResult Generated => LazyGenerated.Value;

    [Fact]
    public void AStructWithMembersIsUnsafe()
    {
        Assert.Contains(
            "public unsafe partial struct Colorimetry\n",
            Run.File("Colorimetry.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AConstInstanceIsReadOnlyAndPinsThroughAsRef()
    {
        Assert.Equal(
            """
            public readonly bool IsEqual(Gst.Colorimetry other)
            {
                Gst.Colorimetry otherNative = other;
                fixed (Gst.Colorimetry* self = &System.Runtime.CompilerServices.Unsafe.AsRef(in this))
                {
                    int nativeResult = GstColorimetryIsEqual(self, &otherNative);
                    return nativeResult != 0;
                }
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Colorimetry.cs", "public readonly bool IsEqual"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ANonConstInstanceIsWritableAndPinsThis()
    {
        Assert.Equal(
            """
            public void Reset()
            {
                fixed (Gst.Colorimetry* self = &this)
                {
                    GstColorimetryReset(self);
                }
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Colorimetry.cs", "public void Reset"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AMutatorSaysSoInItsRemarks()
    {
        Assert.Contains(
            """
                /// <remarks>
                /// <para>
                /// Mutates this instance; call it on a variable, not on a copy returned by a
                /// property.
                /// </para>
                /// </remarks>
                public void Reset()
            """.ReplaceLineEndings("\n"),
            Run.File("Colorimetry.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyMemberSaysNothingAboutMutation()
    {
        Assert.DoesNotContain(
            "Mutates this instance",
            Run.Member("Colorimetry.cs", "public readonly bool IsEqual"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANullableToStringHandsOutTheEmptyString()
    {
        Assert.Equal(
            """
            public override readonly string ToString()
            {
                fixed (Gst.Colorimetry* self = &System.Runtime.CompilerServices.Unsafe.AsRef(in this))
                {
                    nint nativeResult = GstColorimetryToString(self);
                    return Gst.Interop.GMarshal.PtrToStringUtf8AndFree(nativeResult) ?? string.Empty;
                }
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Colorimetry.cs", "public override readonly string ToString"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ANullableToStringDocumentsTheEmptyString()
    {
        Assert.Contains(
            """
                /// The empty string when the C function has no representation to hand out,
                /// which is what the default value of this structure is.
            """.ReplaceLineEndings("\n"),
            Run.File("Colorimetry.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANonNullableToStringKeepsTheThrow()
    {
        // The rule keys on the annotation rather than on the declaring kind: a
        // to_string the gir promises a value for is the override every other
        // wrapper gets, throw included.
        Assert.Equal(
            """
            public override readonly string ToString()
            {
                fixed (Gst.Mastering* self = &System.Runtime.CompilerServices.Unsafe.AsRef(in this))
                {
                    nint nativeResult = GstMasteringToString(self);
                    return Gst.Interop.GMarshal.PtrToStringUtf8AndFree(nativeResult)
                        ?? throw new InvalidOperationException("gst_mastering_to_string returned no value.");
                }
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Mastering.cs", "public override readonly string ToString"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void RefParametersOfAStructMethodReachTheCaller()
    {
        Assert.Equal(
            """
            public readonly bool Transform(ref int x, ref Gst.Rect rect)
            {
                int xNative = x;
                Gst.Rect rectNative = rect;
                fixed (Gst.Colorimetry* self = &System.Runtime.CompilerServices.Unsafe.AsRef(in this))
                {
                    int nativeResult = GstColorimetryTransform(self, &xNative, &rectNative);
                    x = xNative;
                    rect = rectNative;
                    return nativeResult != 0;
                }
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Colorimetry.cs", "public readonly bool Transform"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AStaticFunctionOfAStructTakesNoInstance()
    {
        Assert.Equal(
            """
            public static uint GetQuark()
            {
                uint nativeResult = GstColorimetryGetQuark();
                return nativeResult;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Colorimetry.cs", "public static uint GetQuark"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheImportTakesAPointerToThePublicStruct()
    {
        // The declaring type and the import live in the same assembly, so the
        // interop generator accepts the pointer to the public struct; a by-ref
        // parameter of a referenced assembly would be SYSLIB1051.
        Assert.Contains(
            "private static partial int GstColorimetryIsEqual(Gst.Colorimetry* cinfo, Gst.Colorimetry* other);\n",
            Run.File("Colorimetry.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AStructMemberKeepsNothingAlive()
    {
        // GC.KeepAlive of a value type compiles and boxes: the barrier would
        // keep a copy alive and say nothing about the storage the call was
        // handed. A structure has no wrapper to outlive the call, so no
        // barrier is emitted at all.
        Assert.DoesNotContain("GC.KeepAlive", Run.File("Colorimetry.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCursorFamilyStaysUnbound()
    {
        // The four byte and bit cursors are overlay-skipped as whole types
        // (fixups.json, $comment-skip-cursors). Lifting the plain struct gate
        // must not bind the methods they declare, so nothing is emitted for
        // them at all.
        string[] cursors = ["BitReader.cs", "BitWriter.cs", "ByteReader.cs", "ByteWriter.cs"];
        foreach (string cursor in cursors)
        {
            Assert.DoesNotContain(
                Generated.Files,
                file => file.RelativePath.EndsWith("/" + cursor, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheHeapAllocatedRangePairIsOverlaySkipped()
    {
        // gst_rtsp_range_parse hands out a heap pointer and gst_rtsp_range_free
        // is the matching release; a by-value C# struct carries neither, so the
        // pair is on the skip ledger while the three by-value functions bind.
        Assert.Contains("gst_rtsp_range_free", GirFixture.Overlays.SkippedIdentifiers);
        Assert.Contains("gst_rtsp_range_parse", GirFixture.Overlays.SkippedIdentifiers);

        string source = SourceOf("GstSharp.Net.Rtsp/Generated/RTSPRange.cs");
        Assert.DoesNotContain("gst_rtsp_range_parse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("gst_rtsp_range_free", source, StringComparison.Ordinal);
        Assert.Contains(
            "public static bool ConvertUnits(ref Gst.Rtsp.RTSPTimeRange range, Gst.Rtsp.RTSPRangeUnit unit)\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static string? ToString(Gst.Rtsp.RTSPTimeRange range)\n",
            source,
            StringComparison.Ordinal);
    }

    private static string SourceOf(string relativePath)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, relativePath, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException($"The run produced no '{relativePath}'.");
    }
}
