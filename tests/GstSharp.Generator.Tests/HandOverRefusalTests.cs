using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>handOverRefusals</c> overlay, which releases the reference minted for
/// a handed over argument again when the call refuses the argument rather than
/// taking it.
/// </summary>
/// <remarks>
/// The entry states a reading of the C - that the refusal return means the
/// reference was not taken - and the generator turns it into one conditional
/// right after the call. Both refusals read as a zero raw result, so the shape
/// the entry needs is a callable that answers a <c>gboolean</c> or a pointer
/// and hands at least one argument over; anything else is the entry naming the
/// wrong member, and a key no rendered callable read is a release that no
/// longer happens.
/// </remarks>
public sealed class HandOverRefusalTests
{
    /// <summary>
    /// The four shapes an entry can land on: a method that answers a
    /// <c>gboolean</c> and takes a GObject over, a function that answers a
    /// pointer and does the same, a method that answers neither, and a method
    /// that hands nothing over.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="add" c:identifier="gst_widget_add">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="child" transfer-ownership="full">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="count" c:identifier="gst_widget_count">
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="child" transfer-ownership="full">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="holds" c:identifier="gst_widget_holds">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="child" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
            <function name="wrap" c:identifier="gst_widget_wrap">
              <return-value transfer-ownership="full">
                <type name="Widget" c:type="GstWidget*"/>
              </return-value>
              <parameters>
                <parameter name="child" transfer-ownership="full">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
              </parameters>
            </function>
        """;

    [Fact]
    public void ABooleanRefusalReleasesTheMintRightAfterTheCall()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "handOverRefusals": {
                "gst_widget_add": "gstwidget.c:1-2, a fixture: FALSE means the child was not taken."
              }
            }
            """);

        string source = run.File("Widget.cs");

        int call = source.IndexOf("int nativeResult = GstWidgetAdd(", StringComparison.Ordinal);
        int release = source.IndexOf(
            "Gst.Interop.GObjectNative.ObjectUnref(childOwned);",
            StringComparison.Ordinal);
        int result = source.IndexOf("bool result = nativeResult != 0;", StringComparison.Ordinal);

        Assert.True(call >= 0, "the call is written");
        Assert.True(result >= 0, "the result is converted");

        // The release stands between the call and every conversion of what it
        // answered, which is what makes it run on a path that raises as well.
        Assert.Contains("if (nativeResult == 0)", source, StringComparison.Ordinal);
        Assert.InRange(release, call + 1, result - 1);

        // The contract is documented where the hand over is.
        Assert.Contains(
            "the <see langword=\"false\"/> the C answers is",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0050" or "GEN0051");
    }

    [Fact]
    public void APointerRefusalReleasesTheMintBeforeTheReturnIsWrapped()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "handOverRefusals": {
                "gst_widget_wrap": "gstwidget.c:3-4, a fixture: NULL means the child was not taken."
              }
            }
            """);

        string source = run.File("Global.cs");

        int call = source.IndexOf("nint nativeResult = GstWidgetWrap(", StringComparison.Ordinal);
        int release = source.IndexOf(
            "Gst.Interop.GObjectNative.ObjectUnref(childOwned);",
            StringComparison.Ordinal);
        int wrap = source.IndexOf("FromNative<Gst.Widget>(nativeResult", StringComparison.Ordinal);

        Assert.True(call >= 0, "the call is written");
        Assert.True(wrap >= 0, "the answer is wrapped");
        Assert.InRange(release, call + 1, wrap - 1);

        Assert.Contains(
            "the <see langword=\"null\"/> the C answers is",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0050" or "GEN0051");
    }

    [Fact]
    public void AnEntryOnACallableWhoseRefusalCannotBeReadIsReported()
    {
        // A return that is neither a gboolean nor a pointer carries no refusal
        // the generated body could test, so the entry would release the mint
        // on an ordinary answer - a count of zero here.
        FixtureRun run = RunWithOverlay(
            """
            {
              "handOverRefusals": {
                "gst_widget_count": "gstwidget.c:5, a fixture that answers a number."
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0050", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_count", StringComparison.Ordinal));

        Assert.DoesNotContain("if (nativeResult == 0)", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryOnACallableThatHandsNothingOverIsReported()
    {
        // Nothing is minted for a borrowed argument, so there is no reference
        // the refusal could leave with no owner.
        FixtureRun run = RunWithOverlay(
            """
            {
              "handOverRefusals": {
                "gst_widget_holds": "gstwidget.c:6, a fixture that borrows its argument."
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0050", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_holds", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryThatNamesNoCallableIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "handOverRefusals": {
                "gst_widget_remove": "Nothing names this."
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0051", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_remove", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryOnASkippedCallableIsReportedAsStale()
    {
        // A skipped member renders no body, so the release would be written
        // nowhere: the same silence a misspelled key leaves behind.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": ["gst_widget_add"],
              "handOverRefusals": {
                "gst_widget_add": "Skipped, so nothing reads it."
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0051", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_add", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryWithoutACitationIsRefusedWhileTheOverlaysLoad()
    {
        // The citation is the whole of the entry: it says where the C states
        // that a refusal leaves the reference untaken, which is the one thing
        // a reader of the overlay has to be able to check.
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "handOverRefusals": { "gst_widget_add": "  " }
                }
                """);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Overlays.Load(directory));
            Assert.Contains("gst_widget_add", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FixtureRun RunWithOverlay(string fixups, string? body = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body ?? Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
