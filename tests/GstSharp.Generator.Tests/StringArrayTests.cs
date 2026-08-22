using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The projection of a <c>NULL</c> terminated array of strings, in both
/// directions, and the three shapes of one that stay rejected.
/// </summary>
public sealed class StringArrayTests
{
    /// <summary>
    /// One class carrying every string array shape: a borrowed and an owned
    /// return, a non nullable and a nullable <c>in</c> vector, and the three
    /// rejections — a vector the callee would take over, one it would replace,
    /// and one that is indexed by a length rather than terminated.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="get_tags" c:identifier="gst_widget_get_tags">
                <return-value transfer-ownership="none">
                  <array c:type="gchar**"><type name="utf8" c:type="gchar*"/></array>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="steal_tags" c:identifier="gst_widget_steal_tags">
                <return-value transfer-ownership="full">
                  <array c:type="gchar**"><type name="utf8" c:type="gchar*"/></array>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_tags" c:identifier="gst_widget_set_tags">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="none">
                    <array c:type="const gchar**"><type name="utf8" c:type="const gchar*"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_filters" c:identifier="gst_widget_set_filters">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="filters" transfer-ownership="none" nullable="1">
                    <array c:type="const gchar**"><type name="utf8" c:type="const gchar*"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="own_tags" c:identifier="gst_widget_own_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="full">
                    <array c:type="gchar**"><type name="utf8" c:type="gchar*"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="update_tags" c:identifier="gst_widget_update_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" direction="inout" caller-allocates="0" transfer-ownership="none">
                    <array c:type="gchar***"><type name="utf8" c:type="gchar*"/></array>
                  </parameter>
                </parameters>
              </method>
              <method name="set_counted_tags" c:identifier="gst_widget_set_counted_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="none">
                    <array length="1" c:type="const gchar**"><type name="utf8" c:type="const gchar*"/></array>
                  </parameter>
                  <parameter name="count" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A borrowed vector is decoded into a copy and left where it is; the
    /// result is nullable whatever the gir claims, because the C side answers
    /// <c>NULL</c> for an empty one often enough that the two are told apart.
    /// </summary>
    [Fact]
    public void ABorrowedStringArrayReturnIsDecodedWithoutFreeing()
    {
        Assert.Equal(
            """
            public string[]? GetTags()
            {
                nint nativeResult = GstWidgetGetTags(Handle);
                System.GC.KeepAlive(this);
                return Gst.Interop.GMarshal.StrvToArray(nativeResult, free: false);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public string[]? GetTags("),
            StringComparer.Ordinal);
    }

    /// <summary>The owned half of the same shape releases what it decoded.</summary>
    [Fact]
    public void AnOwnedStringArrayReturnIsFreedAfterItIsDecoded()
    {
        Assert.Equal(
            """
            public string[]? StealTags()
            {
                nint nativeResult = GstWidgetStealTags(Handle);
                System.GC.KeepAlive(this);
                return Gst.Interop.GMarshal.StrvToArray(nativeResult, free: true);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public string[]? StealTags("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The <c>in</c> direction: a guard, the scope that owns the vector for the
    /// length of the call, its pointer at the call site, and no epilogue — the
    /// <c>using</c> declaration is the release.
    /// </summary>
    [Fact]
    public void ANonNullableStringArrayParameterIsGuardedAndEncodedIntoAScope()
    {
        Assert.Equal(
            """
            public bool SetTags(string[] tags)
            {
                ArgumentNullException.ThrowIfNull(tags);
                using Gst.Interop.StrvScope tagsScope = Gst.Interop.GMarshal.AllocStrv(tags);
                int nativeResult = GstWidgetSetTags(Handle, tagsScope.Pointer);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public bool SetTags("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A vector the C function accepts as <c>NULL</c> is nullable and is not
    /// guarded; the encode answers a scope whose pointer is null.
    /// </summary>
    [Fact]
    public void ANullableStringArrayParameterIsNotGuarded()
    {
        Assert.Equal(
            """
            public void SetFilters(string[]? filters)
            {
                using Gst.Interop.StrvScope filtersScope = Gst.Interop.GMarshal.AllocStrv(filters);
                GstWidgetSetFilters(Handle, filtersScope.Pointer);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void SetFilters("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The vector and its strings belong to the scope, which frees both when
    /// the call returns. A callee that took them over would free memory the
    /// scope frees a second time, so the shape is refused.
    /// </summary>
    [Fact]
    public void AStringArrayTheCalleeTakesOverStaysUnbound()
    {
        Assert.DoesNotContain("OwnTags", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A vector the callee replaces is neither the <c>in</c> shape nor the out
    /// one: reading the answer back would read the caller's own allocation. No
    /// <c>char***</c> of the reference girs is spelled zero terminated, so this
    /// fixture is the only thing that keeps the rejection honest — a regression
    /// here produces no committed diff at all.
    /// </summary>
    [Fact]
    public void AnInOutStringArrayStaysUnbound()
    {
        Assert.DoesNotContain("UpdateTags", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// An array of strings that a length indexes is not terminated, and the
    /// runtime reads a terminator; the shape is out of the string array
    /// projection altogether.
    /// </summary>
    [Fact]
    public void ALengthIndexedStringArrayStaysUnbound()
    {
        Assert.DoesNotContain("SetCountedTags", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The three rejections above, counted: nothing else of the fixture is
    /// dropped, so a rule that widens shows up here as well as in the member
    /// assertions.
    /// </summary>
    [Fact]
    public void OnlyTheThreeRejectedShapesAreSkipped()
    {
        Assert.Equal(3, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
