using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>preconditions</c> overlay, which opens the generated body of a
/// callable with hand written statements.
/// </summary>
/// <remarks>
/// The entry buys a position rather than a text: the statements stand after
/// every argument guard, so that one may read the parameter it refuses, and
/// before the first marshalling statement, so that one which throws finds
/// nothing allocated and nothing handed to the C. A key that no rendered
/// callable consumed is reported rather than dropped, because the member it was
/// written for would otherwise go on calling the C the entry exists to keep it
/// out of.
/// </remarks>
public sealed class PreconditionTests
{
    /// <summary>
    /// One instance method and one function, both with a string parameter,
    /// which is the pair of shapes a precondition lands on: a member of a class
    /// and a member of the global holder.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="pack" c:identifier="gst_widget_pack">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="label" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
            </class>
            <function name="describe" c:identifier="gst_describe">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="label" transfer-ownership="none">
                  <type name="utf8" c:type="const gchar*"/>
                </parameter>
              </parameters>
            </function>
        """;

    /// <summary>
    /// One instance method that takes a callback, which is the shape whose
    /// body is written in three phases rather than in one interleaved pass:
    /// two of the shipped entries ride it.
    /// </summary>
    private const string CallbackBody =
        """
            <callback name="WatchFunc" c:type="GstWatchFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="WatchFunc" c:type="GstWatchFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    [Fact]
    public void TheStatementsOfAnInstanceMethodStandBetweenTheGuardsAndTheMarshalling()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "preconditions": {
                "gst_widget_pack": {
                  "statements": ["ThrowIfPacked(nameof(Pack));", "ThrowIfSealed(label);"],
                  "$comment": "gstwidget.c:1-2, a fixture."
                }
              }
            }
            """);

        string source = run.File("Widget.cs");

        int guard = source.IndexOf("ArgumentNullException.ThrowIfNull(label);", StringComparison.Ordinal);
        int first = source.IndexOf("ThrowIfPacked(nameof(Pack));", StringComparison.Ordinal);
        int second = source.IndexOf("ThrowIfSealed(label);", StringComparison.Ordinal);
        int marshalling = source.IndexOf("stackalloc byte[", StringComparison.Ordinal);

        Assert.True(guard >= 0, "the null guard of the string parameter is written");
        Assert.True(marshalling >= 0, "the UTF-8 copy of the string parameter is written");

        // The order is the whole contract: after the guards, in the order the
        // overlay wrote them, and before anything is allocated.
        Assert.InRange(first, guard + 1, marshalling - 1);
        Assert.InRange(second, first + 1, marshalling - 1);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0049", StringComparison.Ordinal));
    }

    [Fact]
    public void TheStatementsOfAFunctionStandBeforeTheUtf8CopyOfItsString()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "preconditions": {
                "gst_describe": {
                  "statements": ["ThrowIfUnready(label);"],
                  "$comment": "gstwidget.c:3, a fixture."
                }
              }
            }
            """);

        string source = run.File("Global.cs");

        int guard = source.IndexOf("ArgumentNullException.ThrowIfNull(label);", StringComparison.Ordinal);
        int statement = source.IndexOf("ThrowIfUnready(label);", StringComparison.Ordinal);
        int buffer = source.IndexOf("stackalloc byte[", StringComparison.Ordinal);
        int scope = source.IndexOf("Gst.Interop.Utf8Scope", StringComparison.Ordinal);

        Assert.True(guard >= 0, "the null guard of the string parameter is written");
        Assert.True(statement >= 0, "the precondition of the function is written");

        // The lower bound is half the contract: a statement that reads the
        // parameter it refuses has to stand after the null check of it, or it
        // reads a null of its own.
        Assert.InRange(statement, guard + 1, buffer - 1);
        Assert.InRange(statement, guard + 1, scope - 1);
    }

    [Fact]
    public void TheStatementsOfACallbackTakingMethodStandBeforeTheFirstHandleRead()
    {
        // A member that materializes an argument is written in three phases -
        // guards, handle reads, allocations - rather than in the interleaved
        // pass the other fixtures take. The statements belong at the head of
        // it: after the guards, but before the instance handle is read and
        // long before the state of the callback is allocated, which nothing
        // but the call itself releases.
        FixtureRun run = RunWithOverlay(
            """
            {
              "preconditions": {
                "gst_widget_watch": {
                  "statements": ["ThrowIfUnwatchable(nameof(Watch));"],
                  "$comment": "gstwidget.c:4, a fixture."
                }
              }
            }
            """,
            CallbackBody);

        string source = run.File("Widget.cs");

        int guard = source.IndexOf("ArgumentNullException.ThrowIfNull(func);", StringComparison.Ordinal);
        int statement = source.IndexOf("ThrowIfUnwatchable(nameof(Watch));", StringComparison.Ordinal);
        int handle = source.IndexOf("nint instanceHandle = Handle;", StringComparison.Ordinal);
        int allocation = source.IndexOf("CallbackHandle.Alloc", StringComparison.Ordinal);

        Assert.True(guard >= 0, "the null guard of the callback is written");
        Assert.True(handle >= 0, "the instance handle is read");
        Assert.True(allocation >= 0, "the state of the callback is allocated");

        Assert.InRange(statement, guard + 1, handle - 1);
        Assert.InRange(statement, guard + 1, allocation - 1);
    }

    [Fact]
    public void AnEntryThatNamesNoCallableIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "preconditions": {
                "gst_widget_unpack": {
                  "statements": ["ThrowIfPacked(nameof(Unpack));"],
                  "$comment": "Nothing names this."
                }
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0049", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_unpack", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryOnASkippedCallableIsReportedAsStale()
    {
        // A skipped member renders no body, so the statements would be written
        // nowhere. That is the same silence a misspelled key leaves behind.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": ["gst_widget_pack"],
              "preconditions": {
                "gst_widget_pack": {
                  "statements": ["ThrowIfPacked(nameof(Pack));"],
                  "$comment": "Skipped, so nothing consumes it."
                }
              }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0049", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_pack", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEntryWithoutAStatementIsRefusedWhileTheOverlaysLoad()
    {
        // An entry that emits nothing is consumed by the very member it leaves
        // unguarded, so the stale key report cannot catch it: the load is the
        // only place it can be refused.
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "preconditions": { "gst_widget_pack": { "statements": [] } }
                }
                """);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Overlays.Load(directory));
            Assert.Contains("gst_widget_pack", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnEntryWithABlankStatementIsRefusedWhileTheOverlaysLoad()
    {
        // A blank line is written out as readily as a statement, and guards
        // exactly as much as an entry with no statement at all - so the load
        // refuses it in the same breath, and names the key while it still can.
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "preconditions": {
                    "gst_widget_pack": { "statements": ["ThrowIfPacked(nameof(Pack));", "  "] }
                  }
                }
                """);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() => Overlays.Load(directory));
            Assert.Contains("gst_widget_pack", error.Message, StringComparison.Ordinal);
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
