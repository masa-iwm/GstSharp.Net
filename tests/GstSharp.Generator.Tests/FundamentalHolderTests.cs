using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The static holder a <c>glib:fundamental="1"</c> class is emitted as, and
/// the <c>discardReturn</c> correction its <c>init</c> functions need.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through <c>GstValueArray</c>,
/// <c>GstValueList</c> and <c>GstValueUniqueList</c>, whose counts are frozen
/// by the census tests. The fixtures here are the definition of the feature:
/// what a fundamental with functions emits, what one without functions emits,
/// and that the holder is not a wrapper and is not registered as one.
/// </para>
/// <para>
/// A fundamental <c>GType</c> used to be dropped with everything it declared,
/// before the census ever saw it, so what was not bound was not reported
/// either. That is what these tests hold shut: a function of a fundamental is
/// now either emitted or listed in <c>skip-report.md</c>.
/// </para>
/// </remarks>
public sealed class FundamentalHolderTests
{
    /// <summary>
    /// A fundamental container in the shape of the real ones: an <c>init</c>
    /// that hands its own argument back, a reader, and a mutator that a
    /// correction may take out. <c>Empty</c> declares nothing at all.
    /// </summary>
    private const string Body =
        """
            <class name="Thing" c:type="GstThing" glib:type-name="GstThing" glib:get-type="gst_thing_get_type" glib:fundamental="1">
              <doc xml:space="preserve">A fundamental type that describes a thing</doc>
              <function name="init" c:identifier="gst_thing_init">
                <return-value transfer-ownership="none">
                  <type name="GObject.Value" c:type="GValue*"/>
                </return-value>
                <parameters>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </function>
              <function name="get_size" c:identifier="gst_thing_get_size">
                <return-value transfer-ownership="none">
                  <type name="guint" c:type="guint"/>
                </return-value>
                <parameters>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="const GValue*"/>
                  </parameter>
                </parameters>
              </function>
              <function name="clear" c:identifier="gst_thing_clear">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </function>
            </class>
            <class name="Empty" c:type="GstEmpty" glib:type-name="GstEmpty" glib:get-type="gst_empty_get_type" glib:fundamental="1">
              <doc xml:space="preserve">A fundamental type that declares nothing</doc>
            </class>
        """;

    [Fact]
    public void AFundamentalWithFunctionsBecomesAStaticHolder()
    {
        FixtureRun run = Fixture.Run(Body);

        string source = run.File("Thing.cs");
        Assert.Contains("public static unsafe partial class Thing\n", source, StringComparison.Ordinal);
        Assert.Contains("public static uint GetSize(in Gst.GObject.Value value)", source, StringComparison.Ordinal);
        Assert.Equal(3, run.Result.Census.EmittedCount("Gst", "method"));
        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "value container"));

        // The holder is a namespace for the functions, not a wrapper: there is
        // no instance of a fundamental to create, so nothing of the class path
        // is emitted and the type table does not name it.
        Assert.DoesNotContain("GetGType", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateWrapper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Gst.Thing", run.File("_Module.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheHolderRemarksSayWhereTheContainerLives()
    {
        FixtureRun run = Fixture.Run(Body);

        string source = run.File("Thing.cs");
        Assert.Contains(
            "/// <summary>A fundamental type that describes a thing</summary>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Functions of the fundamental type <c>GstThing</c>.", source, StringComparison.Ordinal);
        Assert.Contains("<see cref=\"Gst.GObject.Value\"/>", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AFundamentalWithoutFunctionsIsDropped()
    {
        FixtureRun run = Fixture.Run(Body);

        Assert.False(run.HasFile("Empty.cs"));
        Assert.Equal(1, run.Result.Census.EmittedCount("Gst", "value container"));
    }

    [Fact]
    public void AFundamentalWhoseFunctionsAreAllSkippedEmitsNoHolder()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "gst_thing_init", "gst_thing_get_size", "gst_thing_clear" ]
            }
            """);

        Assert.False(run.HasFile("Thing.cs"));
        Assert.Equal(0, run.Result.Census.EmittedCount("Gst", "value container"));
        Assert.Equal(3, run.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));
    }

    [Fact]
    public void ADiscardedReturnMakesTheMemberVoid()
    {
        FixtureRun bound = Fixture.Run(Body);
        Assert.Contains(
            "public static Gst.GObject.Value Init(ref Gst.GObject.Value value)",
            bound.File("Thing.cs"),
            StringComparison.Ordinal);

        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_thing_init#return": { "discardReturn": true } }
            }
            """);

        string source = run.File("Thing.cs");
        Assert.Contains("public static void Init(ref Gst.GObject.Value value)", source, StringComparison.Ordinal);

        // The entry point is declared void as well: ignoring a returned
        // register is what a C caller that drops the value does.
        Assert.Contains(
            "private static partial void GstThingInit(Gst.GObject.GValueNative* value);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CopyFrom", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ADiscardedReturnOnAVoidCallableIsReported()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_thing_clear#return": { "discardReturn": true } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0018", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_thing_clear", StringComparison.Ordinal));

        // The correction is stale rather than wrong, so the member still binds.
        Assert.Contains(
            "public static void Clear(ref Gst.GObject.Value value)",
            run.File("Thing.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADiscardedReturnTheCallerOwnsIsRefused()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "gst_thing_init#return": { "transfer": "full", "discardReturn": true }
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0019", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_thing_init", StringComparison.Ordinal));

        // Obeying the correction here would drop an allocation the caller owns
        // on every call, so the return stays bound.
        string source = run.File("Thing.cs");
        Assert.Contains("public static Gst.GObject.Value Init(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public static void Init(", source, StringComparison.Ordinal);
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
