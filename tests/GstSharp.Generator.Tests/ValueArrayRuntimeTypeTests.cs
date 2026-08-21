using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GObject.ValueArray</c> row of <c>MarshalPlanner.RuntimeTypes</c>: a
/// boxed type of the hand written runtime is planned with the
/// <c>Wrapper</c> flavour, so its handles cross like those of a generated
/// boxed type — read for a borrowed in parameter, adopted through the typed
/// <c>FromNative</c> for a transferred out parameter.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through the six <c>GValueArray</c> entry
/// points of <c>Gst</c> (<c>gst_structure_get_array</c> and relatives), but
/// only in the members those happen to be. The fixtures here are the
/// definition of the feature: they name the two positions the type reaches —
/// a <c>const GValueArray*</c> in under <c>transfer-ownership="none"</c> and
/// a <c>GValueArray**</c> out under <c>transfer-ownership="full"</c> — and
/// they pin the emitted text of each.
/// </para>
/// <para>
/// The out fixture is also the refusal contract of the real callables: the
/// prologue zeroes the local, a callee that answers <c>FALSE</c> leaves it
/// untouched, and <c>FromNative</c> maps the zero onto <see langword="null"/>.
/// </para>
/// </remarks>
public sealed class ValueArrayRuntimeTypeTests
{
    /// <summary>
    /// A class whose two members are the two shapes under test: a
    /// <c>GObject.ValueArray</c> handle in that the callee only reads, and a
    /// <c>GObject.ValueArray</c> handle out that the callee allocates and
    /// transfers.
    /// </summary>
    private const string Body =
        """
            <class name="Palette" c:type="GstPalette" parent="GObject.Object" glib:type-name="GstPalette" glib:get-type="gst_palette_get_type">
              <method name="set_colors" c:identifier="gst_palette_set_colors">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="palette" transfer-ownership="none">
                    <type name="Palette" c:type="GstPalette*"/>
                  </instance-parameter>
                  <parameter name="colors" transfer-ownership="none">
                    <type name="GObject.ValueArray" c:type="const GValueArray*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_colors" c:identifier="gst_palette_get_colors">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="palette" transfer-ownership="none">
                    <type name="Palette" c:type="GstPalette*"/>
                  </instance-parameter>
                  <parameter name="colors" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="GObject.ValueArray" c:type="GValueArray**"/>
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
    /// A borrowed <c>GValueArray</c> in parameter is spelled as the hand
    /// written wrapper and crosses as its handle, exactly like a generated
    /// boxed type: a null guard, the handle in the call, and a
    /// <c>GC.KeepAlive</c> after it. Nothing is disposed — the callee copies
    /// what it keeps.
    /// </summary>
    [Fact]
    public void AValueArrayParameterIsPassedAsTheHandleOfTheRuntimeWrapper()
    {
        Assert.Equal(
            """
            public void SetColors(Gst.GObject.ValueArray colors)
            {
                ArgumentNullException.ThrowIfNull(colors);
                GstPaletteSetColors(Handle, colors.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(colors);
            }
            """,
            Run.Member("Palette.cs", "public void SetColors("),
            StringComparer.Ordinal);

        // The import declares the handle as a pointer, not as anything the
        // GObject module would have to emit.
        Assert.Contains(
            "private static partial void GstPaletteSetColors(nint palette, nint colors);",
            Run.File("Palette.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred <c>GValueArray</c> out parameter is adopted through the
    /// typed <c>FromNative</c> of the runtime wrapper over a zeroed local, so
    /// a callee that declines — and leaves the local untouched — hands the
    /// caller <see langword="null"/> rather than a wrapper around nothing.
    /// </summary>
    [Fact]
    public void AValueArrayOutParameterIsAdoptedThroughFromNative()
    {
        Assert.Equal(
            """
            public bool GetColors(out Gst.GObject.ValueArray? colors)
            {
                nint colorsNative = default;
                int nativeResult = GstPaletteGetColors(Handle, &colorsNative);
                System.GC.KeepAlive(this);
                colors = Gst.GObject.ValueArray.FromNative(colorsNative, Gst.Interop.Transfer.Full);
                return nativeResult != 0;
            }
            """,
            Run.Member("Palette.cs", "public bool GetColors("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial int GstPaletteGetColors(nint palette, nint* colors);",
            Run.File("Palette.cs"),
            StringComparison.Ordinal);

        Assert.Equal(0, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// Naming the type emits nothing of the GObject namespace: the declaration
    /// stays the hand written <c>Gst.GObject.ValueArray</c> of the runtime.
    /// </summary>
    [Fact]
    public void TheValueArrayDeclarationItselfIsNotEmitted()
    {
        Assert.False(Run.HasFile("ValueArray.cs"));
    }
}
