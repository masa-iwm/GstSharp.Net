using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The scopes a callback parameter may carry: the asynchronous arm, whose
/// trampoline releases the state of the one invocation, the <c>forever</c>
/// arm, which releases nothing and says so, and the <c>scope</c> correction
/// that is how a gir that describes neither is repaired. The nullability of a
/// callback parameter belongs here too: a C function that documents what it
/// does without one takes no function at all, and the call site has to hand it
/// the null pointer rather than a trampoline with no delegate behind it.
/// </summary>
/// <remarks>
/// The vendored girs exercise the asynchronous arm through
/// <c>gst_call_async</c> and <c>gst_object_call_async</c>, and the
/// <c>forever</c> arm through the five <c>GstCollectPads</c> setters, whose
/// counts the census tests freeze. The fixtures here are the definition of the
/// feature, including the two shapes that must stay refused: an asynchronous
/// site that also carries a destroy notification, and one callback type that
/// is handed to an asynchronous and to a non asynchronous call at once.
/// </remarks>
public sealed class AsyncCallbackTests
{
    /// <summary>
    /// A class with one call site per scope: <c>alert</c> is asynchronous and
    /// carries no destroy notification, <c>watch</c> is asynchronous and does,
    /// and <c>tick</c> is the ordinary <c>call</c> scope that a correction
    /// turns into <c>forever</c>. Each site has a callback type of its own, so
    /// that no fixture depends on the order the methods are planned in.
    /// </summary>
    private const string Body =
        """
            <callback name="AlertFunc" c:type="GstAlertFunc">
              <doc xml:space="preserve">Called once, from a pool thread</doc>
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="PulseFunc" c:type="GstPulseFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="TickFunc" c:type="GstTickFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <doc xml:space="preserve">A widget</doc>
              <method name="alert" c:identifier="gst_widget_alert">
                <doc xml:space="preserve">Calls func from another thread</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1">
                    <doc xml:space="preserve">the function to call</doc>
                    <type name="AlertFunc" c:type="GstAlertFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1" destroy="2">
                    <type name="PulseFunc" c:type="GstPulseFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_tick_function" c:identifier="gst_widget_set_tick_function">
                <doc xml:space="preserve">Installs the tick function</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <doc xml:space="preserve">the function to set</doc>
                    <type name="TickFunc" c:type="GstTickFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// One callback type handed to a <c>call</c> site and to an <c>async</c>
    /// one. The <c>call</c> site is declared first, so the asynchronous use is
    /// the one that meets a trampoline it cannot have.
    /// </summary>
    private const string SharedBody =
        """
            <callback name="TickFunc" c:type="GstTickFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="each_tick" c:identifier="gst_widget_each_tick">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="TickFunc" c:type="GstTickFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="tick_async" c:identifier="gst_widget_tick_async">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="async" closure="1">
                    <type name="TickFunc" c:type="GstTickFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// Two <c>notified</c> sites that differ in one annotation. <c>func</c> of
    /// <c>set_tick_function</c> is <c>nullable</c>, the way
    /// <c>gst_meta_register_custom#transform_func</c> is, and <c>func</c> of
    /// <c>set_pulse_function</c> is the control that has to keep the guard and
    /// the unconditional hand over.
    /// </summary>
    private const string NullableBody =
        """
            <callback name="TickFunc" c:type="GstTickFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="PulseFunc" c:type="GstPulseFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="set_tick_function" c:identifier="gst_widget_set_tick_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" nullable="1" allow-none="1" scope="notified" closure="1" destroy="2">
                    <type name="TickFunc" c:type="GstTickFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_pulse_function" c:identifier="gst_widget_set_pulse_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="PulseFunc" c:type="GstPulseFunc"/>
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
    public void AnAsynchronousSiteLeavesTheHandleToItsTrampoline()
    {
        FixtureRun run = Fixture.Run(Body);

        // The call site allocates and hands over, and that is all: the one
        // invocation is what releases the state, so a finally that freed it as
        // the call returned would pull the delegate out from under it.
        string member = run.Member("Widget.cs", "public void Alert(");
        Assert.Contains("Gst.Interop.CallbackHandle.Alloc(func)", member, StringComparison.Ordinal);
        Assert.Contains("Gst.AlertFuncTrampoline.Pointer", member, StringComparison.Ordinal);
        Assert.DoesNotContain("try", member, StringComparison.Ordinal);
        Assert.DoesNotContain("funcState.Free()", member, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTrampolineOfAnAsynchronousCallbackFreesItsOwnHandle()
    {
        FixtureRun run = Fixture.Run(Body);

        string source = run.File("Callbacks.cs");
        Assert.Contains(
            """
                    finally
                    {
                        Gst.Interop.CallbackHandle.FromUserData(userData).Free();
                    }
            """,
            source,
            StringComparison.Ordinal);

        // The free covers the early return of a state that could not be read
        // and the trapped exception as well, which is what puts it outside
        // both of them rather than after the invocation.
        int guard = source.IndexOf("is not { } callback", StringComparison.Ordinal);
        int free = source.IndexOf("FromUserData(userData).Free()", StringComparison.Ordinal);
        Assert.True(guard > 0 && free > guard);
    }

    [Fact]
    public void AnAsynchronousSiteWithADestroyNotificationStaysRefused()
    {
        // A trampoline that frees its own handle and a destroy notification
        // that frees the same handle are mutually exclusive, so the pair is
        // refused rather than emitted with one of the two.
        FixtureRun run = Fixture.Run(Body);

        Assert.DoesNotContain("public void Watch(", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("PulseFunc", run.File("Callbacks.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void AScopeCorrectionTurnsACallSiteIntoAForeverOne()
    {
        FixtureRun run = RunWithOverlay(
            Body,
            """
            {
              "annotationOverrides": { "gst_widget_set_tick_function#func": { "scope": "forever" } }
            }
            """);

        string source = run.File("Widget.cs");
        string member = run.Member("Widget.cs", "public void SetTickFunction(");
        Assert.Contains("Gst.Interop.CallbackHandle.Alloc(func)", member, StringComparison.Ordinal);
        Assert.DoesNotContain("try", member, StringComparison.Ordinal);
        Assert.DoesNotContain("funcState.Free()", member, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FromUserData(userData).Free()",
            run.Member("Callbacks.cs", "internal static unsafe class TickFuncTrampoline"),
            StringComparison.Ordinal);

        // The wording is the contract, so it is compared verbatim.
        Assert.Contains(
            """
                /// <para>
                /// The callback is installed for the lifetime of the object. Replacing it
                /// does not release the state of the previous one, so a call per buffer or
                /// per state change leaks.
                /// </para>
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                /// <param name="func">
                /// the function to set
                /// The binding keeps the state of this callback alive for the life of the
                /// process: the library stores the function pointer and calls it from a
                /// streaming thread, and it offers no destroy notification to release the
                /// state again. One handle is leaked per call — install the callback once,
                /// at construction.
                /// </param>
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AScopeCorrectionThatStatesTheGirValueIsReported()
    {
        FixtureRun run = RunWithOverlay(
            Body,
            """
            {
              "annotationOverrides": { "gst_widget_set_tick_function#func": { "scope": "call" } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0021", StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "gst_widget_set_tick_function#func",
                    StringComparison.Ordinal)
                && diagnostic.Message.Contains("states what the gir already says", StringComparison.Ordinal));

        // Reported, and obeyed all the same, because it changes nothing.
        Assert.Contains(
            "funcState.Free()",
            run.Member("Widget.cs", "public void SetTickFunction("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AScopeCorrectionWithAnUnknownSpellingIsReportedAndIgnored()
    {
        FixtureRun run = RunWithOverlay(
            Body,
            """
            {
              "annotationOverrides": { "gst_widget_set_tick_function#func": { "scope": "eventually" } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0021", StringComparison.Ordinal)
                && diagnostic.Message.Contains("names 'eventually'", StringComparison.Ordinal));

        // The gir value stands, so the state is still freed when the call
        // returns rather than left to a scope the overlay could not name.
        Assert.Contains(
            "funcState.Free()",
            run.Member("Widget.cs", "public void SetTickFunction("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OneCallbackTypeCannotBeAsynchronousAtOneSiteOnly()
    {
        // The trampoline is emitted once per callback type and shared by every
        // site, so the epilogue that frees the handle cannot be decided per
        // site. The asynchronous use is dropped rather than given a trampoline
        // that the other site would double free through.
        FixtureRun run = Fixture.Run(SharedBody);

        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => string.Equals(diagnostic.Code, "GEN0022", StringComparison.Ordinal)
                && diagnostic.Message.Contains("GstTickFunc", StringComparison.Ordinal)
                && diagnostic.Message.Contains(
                    "the async use of 'gst_widget_tick_async' is skipped",
                    StringComparison.Ordinal));

        string source = run.File("Widget.cs");
        Assert.Contains("public void EachTick(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public void TickAsync(", source, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
        Assert.DoesNotContain(
            "FromUserData(userData).Free()",
            run.File("Callbacks.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANullableCallbackReachesTheCalleeAsTheNullPointer()
    {
        FixtureRun run = Fixture.Run(NullableBody);

        // The absence of a function is a value the callee acts on -- the C
        // side branches on the function pointer -- so the parameter is
        // nullable, nothing guards it, and the three arguments the call is
        // handed are the null pointer, the null user data of the default
        // handle, and no destroy notification.
        string member = run.Member("Widget.cs", "public void SetTickFunction(Gst.TickFunc? func)");
        Assert.DoesNotContain("ArgumentNullException.ThrowIfNull(func)", member, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Interop.CallbackHandle funcState = func is null ? default : Gst.Interop.CallbackHandle.Alloc(func);",
            member,
            StringComparison.Ordinal);
        Assert.Contains("func is null ? 0 : Gst.TickFuncTrampoline.Pointer", member, StringComparison.Ordinal);
        Assert.Contains(
            "func is null ? 0 : (nint)Gst.Interop.CallbackHandle.DestroyNotify",
            member,
            StringComparison.Ordinal);
        Assert.Contains("funcState.UserData", member, StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackTheGirDoesNotCallNullableKeepsItsGuard()
    {
        FixtureRun run = Fixture.Run(NullableBody);

        // The control site of the same fixture: one annotation apart, and
        // every line of the hand over is the unconditional one.
        string member = run.Member("Widget.cs", "public void SetPulseFunction(Gst.PulseFunc func)");
        Assert.Contains("ArgumentNullException.ThrowIfNull(func);", member, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);",
            member,
            StringComparison.Ordinal);
        Assert.DoesNotContain("func is null", member, StringComparison.Ordinal);
    }

    /// <summary>Runs a fixture with a hand written <c>fixups.json</c>.</summary>
    /// <param name="body">The members of the <c>Gst</c> namespace.</param>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string body, string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
