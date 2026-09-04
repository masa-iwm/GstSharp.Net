using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The overlay keys a callback that carries no <c>user_data</c> of its own is
/// bound through, and the two shapes of callback argument that came with them.
/// </summary>
/// <remarks>
/// <para>
/// <c>GstPadChainFunction</c> and its ten siblings are invoked with the pad,
/// the parent and the argument alone: the <c>user_data</c> the setter took
/// stays on the pad, so a trampoline of theirs recovers the managed delegate
/// from the pad and the storage slot of the pad the setter wrote instead. The
/// slot is what <c>instanceKeyedCallbacks</c> states, and a site that offers no
/// destroy notification has nowhere to put the state and is refused.
/// </para>
/// <para>
/// <c>docNotes</c> is the counterpart of <c>vfuncDocNotes</c> for a member, and
/// both keys are reported when they name nothing the run planned.
/// </para>
/// </remarks>
public sealed class InstanceKeyedCallbackTests
{
    /// <summary>
    /// A callback with no <c>user_data</c> of its own, a setter of the shape the
    /// pad functions have, a second setter that offers no destroy notification,
    /// and a callback that fills a mini object in through a pointer to a
    /// pointer.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <callback name="LabelFunc" c:type="GstLabelFunc">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="FilterFunc" c:type="GstFilterFunc">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="caps" transfer-ownership="none">
                  <type name="Caps" c:type="GstCaps**"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="2">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="set_label_function_full" c:identifier="gst_widget_set_label_function_full">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="label" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="LabelFunc" c:type="GstLabelFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_filter_function_full" c:identifier="gst_widget_set_filter_function_full">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="filter" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="FilterFunc" c:type="GstFilterFunc"/>
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

    /// <summary>
    /// The same class with the setter that offers no destroy notification, which
    /// is the site an instance keyed callback has nowhere to live at.
    /// </summary>
    private const string UnnotifiedBody =
        """
            <callback name="LabelFunc" c:type="GstLabelFunc">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="watch_label" c:identifier="gst_widget_watch_label">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="label" transfer-ownership="none" scope="call" closure="1">
                    <type name="LabelFunc" c:type="GstLabelFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    [Fact]
    public void ACallbackWithNoUserDataIsBoundWhenTheOverlaysNameItsSlot()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "instanceKeyedCallbacks": { "Gst.LabelFunc": "label" }
            }
            """);

        // The state is filed under the instance and the slot before the setter
        // runs, and the notification the setter takes is the one that removes
        // the entry again.
        string setter = run.Member("Widget.cs", "public void SetLabelFunctionFull(");
        Assert.Contains(
            "Gst.Interop.InstanceKeyedCallbacks.Install(instanceHandle, \"label\", label)",
            setter,
            StringComparison.Ordinal);
        Assert.Contains(
            "(nint)Gst.Interop.InstanceKeyedCallbacks.DestroyNotify",
            setter,
            StringComparison.Ordinal);

        // And the trampoline reads the delegate back out of the instance it is
        // handed first rather than out of a user data pointer it never gets.
        Assert.Contains(
            "Gst.Interop.InstanceKeyedCallbacks.Lookup<Gst.LabelFunc>(widget, \"label\")",
            run.File("Callbacks.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackWithNoUserDataIsUnboundWithoutAnEntry()
    {
        // Without the slot there is no channel the state could travel through,
        // which is the refusal the entry lifts.
        FixtureRun run = Fixture.Run(Body);
        Assert.DoesNotContain(
            "SetLabelFunctionFull",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnInstanceKeyedCallbackIsRefusedAtASiteWithNoDestroyNotification()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "instanceKeyedCallbacks": { "Gst.LabelFunc": "label" }
            }
            """,
            UnnotifiedBody);

        // The entry is read, so it is not stale; the site is refused all the
        // same, because nothing there would ever take the state out of the
        // table again.
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0041", StringComparison.Ordinal));
        Assert.DoesNotContain("WatchLabel", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryThatNamesNoCallbackIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "instanceKeyedCallbacks": { "Gst.GadgetFunc": "gadget" }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0041", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.GadgetFunc", StringComparison.Ordinal));
    }

    [Fact]
    public void ADocNoteIsWrittenIntoTheDocumentationOfTheMember()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "instanceKeyedCallbacks": { "Gst.LabelFunc": "label" },
              "docNotes": { "gst_widget_set_label_function_full": "The pad lock is held when this runs." }
            }
            """);

        Assert.Contains(
            "The pad lock is held when this runs.",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0042", StringComparison.Ordinal));
    }

    [Fact]
    public void ADocNoteThatNamesNoCallableIsReportedAsStale()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "docNotes": { "gst_widget_unpack": "Nothing names this." }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0042", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_unpack", StringComparison.Ordinal));
    }

    [Fact]
    public void ADirectionOnAPointerToAHandlePointerOfACallbackIsHonoured()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "GstFilterFunc#caps": { "direction": "out", "transfer": "full" }
              }
            }
            """);

        // The gir spells the parameter in, because it states no direction at
        // all. The correction is what says the callback fills it, and the
        // delegate takes it as an out.
        Assert.Contains(
            "public delegate bool FilterFunc(Gst.Widget widget, out Gst.Caps? caps);",
            run.File("Callbacks.cs"),
            StringComparison.Ordinal);

        // What the handler left is handed to the caller with one added
        // reference, and the storage starts empty so that a trap leaves the
        // caller with the null pointer.
        string callbacks = run.File("Callbacks.cs");
        Assert.Contains("*caps = nint.Zero;", callbacks, StringComparison.Ordinal);
        Assert.Contains("Gst.GstNative.MiniObjectRef(capsHandle);", callbacks, StringComparison.Ordinal);

        // Nothing about the correction is reported as ignored: the callback
        // path reads this one rather than dropping it.
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal)
                && diagnostic.Message.Contains("GstFilterFunc#caps", StringComparison.Ordinal));
    }

    [Fact]
    public void ADirectionOnAnythingElseOfACallbackIsStillReportedAsIgnored()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "GstLabelFunc#widget": { "direction": "out" } }
            }
            """);

        // A value the trampoline is handed by value has no out projection to
        // reach, so the correction is weighed and reported rather than taking
        // the callback out of the bindings silently.
        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0017", StringComparison.Ordinal)
                && diagnostic.Message.Contains("GstLabelFunc#widget", StringComparison.Ordinal));
    }

    /// <summary>Runs the fixture with one overlay file written on the fly.</summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <param name="body">The gir body, or the default one.</param>
    /// <returns>The run.</returns>
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
