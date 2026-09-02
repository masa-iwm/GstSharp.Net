using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The projection of a <c>GList</c> or a <c>GSList</c> a call is given: the
/// borrowed shape, the consumed shape, and the twelve shapes that stay
/// rejected.
/// </summary>
/// <remarks>
/// The reference girs exercise the borrowed shape thirteen times and the
/// consumed one twice, and the census tests freeze those counts. What only a
/// fixture can hold honest is the other half: a <c>GSList</c> parameter, which
/// no bound module declares, and every refusal, each of which produces no
/// committed diff at all when it silently widens.
/// </remarks>
public sealed class ListArgumentTests
{
    /// <summary>
    /// One class carrying every list shape. <c>set_widgets</c> and
    /// <c>set_owners</c> are the borrowed GObject list in its two
    /// nullabilities, <c>set_tags</c> is the borrowed list of strings,
    /// <c>set_marks</c> is the singly linked twin, <c>take_tags</c> and
    /// <c>take_buffers</c> are the two consumed shapes, and
    /// <c>to_string_with_keys</c> borrows the entry point of a real member so
    /// that the upstream paragraph it carries is written by the run rather than
    /// asserted against the committed sources. Everything below it is a
    /// refusal; <c>take_tags_and_name</c> and <c>take_both_lists</c> are
    /// refused for the order of the prologue rather than for the shape of a
    /// single argument.
    /// </summary>
    private const string Body =
        """
            <callback name="ListFunc" c:type="GstListFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="widgets" transfer-ownership="none">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Widget"/>
                  </type>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Buffer" c:type="GstBuffer" glib:type-name="GstBuffer" glib:get-type="gst_buffer_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <record name="Segment" c:type="GstSegment" glib:type-name="GstSegment" glib:get-type="gst_segment_get_type">
              <field name="rate" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
            <record name="Poll" c:type="GstPoll" disguised="1" opaque="1">
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="set_widgets" c:identifier="gst_widget_set_widgets">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="widgets" transfer-ownership="none">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_owners" c:identifier="gst_widget_set_owners">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="owners" transfer-ownership="none" nullable="1" allow-none="1">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_tags" c:identifier="gst_widget_set_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="none" nullable="1" allow-none="1">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_marks" c:identifier="gst_widget_set_marks">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="marks" transfer-ownership="none" nullable="1" allow-none="1">
                    <type name="GLib.SList" c:type="const GSList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="take_tags" c:identifier="gst_widget_take_tags">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="full" nullable="1" allow-none="1">
                    <type name="GLib.List" c:type="GList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="take_buffers" c:identifier="gst_widget_take_buffers">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="buffers" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Buffer"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="to_string_with_keys" c:identifier="gst_uri_to_string_with_keys">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="keys" transfer-ownership="none" nullable="1" allow-none="1">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="iterate_widgets" c:identifier="gst_widget_iterate_widgets">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="list" transfer-ownership="none">
                    <type name="GLib.List" c:type="GList**">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="produce_widgets" c:identifier="gst_widget_produce_widgets">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="widgets" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="update_widgets" c:identifier="gst_widget_update_widgets">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="widgets" direction="inout" caller-allocates="0" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="adopt_spine" c:identifier="gst_widget_adopt_spine">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="widgets" transfer-ownership="container">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_segments" c:identifier="gst_widget_set_segments">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="segments" transfer-ownership="none">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="Segment"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_polls" c:identifier="gst_widget_set_polls">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="polls" transfer-ownership="none">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="Poll"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="take_widgets" c:identifier="gst_widget_take_widgets">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="widgets" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="peek_buffers" c:identifier="gst_widget_peek_buffers">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="buffers" transfer-ownership="none">
                    <type name="GLib.List" c:type="const GList*">
                      <type name="Buffer"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="get_categories" c:identifier="gst_widget_get_categories">
                <return-value transfer-ownership="none">
                  <type name="GLib.SList" c:type="GSList*">
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="take_tags_and_name" c:identifier="gst_widget_take_tags_and_name">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                  <parameter name="name" transfer-ownership="full">
                    <type name="utf8" c:type="gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_both_lists" c:identifier="gst_widget_take_both_lists">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="utf8"/>
                    </type>
                  </parameter>
                  <parameter name="buffers" transfer-ownership="full">
                    <type name="GLib.List" c:type="GList*">
                      <type name="Buffer"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="walk" c:identifier="gst_widget_walk">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="ListFunc" c:type="GstListFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static GenerationResult Generated => LazyGenerated.Value;

    /// <summary>
    /// The borrowed shape: a guard, the scope that owns the spine for the
    /// length of the call, its head at the call site, and no epilogue. There is
    /// no <c>try</c>/<c>finally</c> — the <c>using</c> declaration is the
    /// release — and no <c>GC.KeepAlive</c> of the sequence: the scope holds the
    /// wrappers and is live until it is disposed.
    /// </summary>
    [Fact]
    public void ABorrowedListOfHandlesIsBuiltIntoAScope()
    {
        Assert.Equal(
            """
            public bool SetWidgets(System.Collections.Generic.IEnumerable<Gst.Widget> widgets)
            {
                ArgumentNullException.ThrowIfNull(widgets);
                using Gst.Interop.GListScope widgetsScope = Gst.Interop.GMarshal.AllocList(widgets, singly: false);
                int nativeResult = GstWidgetSetWidgets(Handle, widgetsScope.Head);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public bool SetWidgets("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A list the C function accepts as <c>NULL</c> is nullable and is not
    /// guarded; the factory answers a scope whose head is the null pointer for
    /// a null sequence and for an empty one alike.
    /// </summary>
    [Fact]
    public void ANullableListIsNotGuarded()
    {
        Assert.Equal(
            """
            public void SetOwners(System.Collections.Generic.IEnumerable<Gst.Widget>? owners)
            {
                using Gst.Interop.GListScope ownersScope = Gst.Interop.GMarshal.AllocList(owners, singly: false);
                GstWidgetSetOwners(Handle, ownersScope.Head);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void SetOwners("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A borrowed list of strings takes the same route; what differs is the
    /// overload the factory resolves to, which is where the UTF-8 copies the
    /// scope owns are made.
    /// </summary>
    [Fact]
    public void ABorrowedListOfStringsIsBuiltIntoAScope()
    {
        Assert.Equal(
            """
            public void SetTags(System.Collections.Generic.IEnumerable<string>? tags)
            {
                using Gst.Interop.GListScope tagsScope = Gst.Interop.GMarshal.AllocList(tags, singly: false);
                GstWidgetSetTags(Handle, tagsScope.Head);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void SetTags("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The singly linked twin differs in one literal. No module of the fifteen
    /// declares a <c>GSList</c> parameter, so this fixture is the only thing
    /// that keeps the arm alive.
    /// </summary>
    [Fact]
    public void ASinglyLinkedListSaysSoAtTheFactory()
    {
        Assert.Equal(
            """
            public void SetMarks(System.Collections.Generic.IEnumerable<string>? marks)
            {
                using Gst.Interop.GListScope marksScope = Gst.Interop.GMarshal.AllocList(marks, singly: true);
                GstWidgetSetMarks(Handle, marksScope.Head);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void SetMarks("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The consumed shape is a materializing member: the three phase prologue
    /// puts the handle read of the instance before the build, so that nothing
    /// which can throw runs between the build and the call that takes it over.
    /// Nothing is released afterwards.
    /// </summary>
    [Fact]
    public void AConsumedListOfStringsIsHandedOverAndNeverReleased()
    {
        Assert.Equal(
            """
            public bool TakeTags(System.Collections.Generic.IEnumerable<string>? tags)
            {
                nint instanceHandle = Handle;
                nint tagsOwned = Gst.Interop.GMarshal.ConsumeList(tags, singly: false);
                int nativeResult = GstWidgetTakeTags(instanceHandle, tagsOwned);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public bool TakeTags("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The mini object half of the consumed shape, where the factory mints one
    /// reference per element rather than a copy of a string.
    /// </summary>
    [Fact]
    public void AConsumedListOfMiniObjectsIsHandedOver()
    {
        Assert.Equal(
            """
            public void TakeBuffers(System.Collections.Generic.IEnumerable<Gst.Buffer> buffers)
            {
                ArgumentNullException.ThrowIfNull(buffers);
                nint instanceHandle = Handle;
                nint buffersOwned = Gst.Interop.GMarshal.ConsumeList(buffers, singly: false);
                GstWidgetTakeBuffers(instanceHandle, buffersOwned);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void TakeBuffers("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The note of a borrowed list, which states what the temporary allocation
    /// costs and what an empty sequence means.
    /// </summary>
    [Fact]
    public void ABorrowedListCarriesItsNote()
    {
        Assert.Contains(
            """
                /// <param name="tags">
                /// The <c>tags</c> argument.
                /// The call reads the list while it runs and copies whatever it keeps. A
                /// temporary native list is built for the call and released when it returns,
                /// and an empty sequence is passed as the null pointer, which is how C spells
                /// the empty list.
                /// </param>
            """.ReplaceLineEndings("\n"),
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The note of a consumed list, whose last sentence is the one a caller has
    /// to read: its own objects survive the call.
    /// </summary>
    [Fact]
    public void AConsumedListCarriesItsNote()
    {
        Assert.Contains(
            """
                /// <param name="buffers">
                /// The <c>buffers</c> argument.
                /// The call takes the list over. The binding hands it a native list of its own
                /// and one reference per element, and releases neither afterwards - the callee
                /// owns both from the moment the call is made, including when it answers false.
                /// Your own objects keep their references and stay usable.
                /// </param>
            """.ReplaceLineEndings("\n"),
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The upstream facts are keyed on the entry point, so the paragraph
    /// follows the C function wherever it is emitted.
    /// </summary>
    [Fact]
    public void TheUpstreamParagraphFollowsTheEntryPoint()
    {
        Assert.Contains(
            """
                /// A null or empty sequence asks for the unordered query string, which is what
                /// the C function falls back to when it is given no keys.
            """.ReplaceLineEndings("\n"),
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same paragraph on the member it was written for, in the committed
    /// sources: the fixture proves the mechanism and this proves the wiring.
    /// </summary>
    [Fact]
    public void TheLeakOfSetPathSegmentsIsDocumented()
    {
        string source = Generated.Files
            .Single(file => file.RelativePath.EndsWith("GstSharp.Net/Generated/Uri.cs", StringComparison.Ordinal))
            .Content;

        Assert.Contains(
            """
                /// On a URI that is not writable the call answers false and the list is leaked:
                /// C takes ownership before it checks (gsturi.c:2518-2532). Test
                /// <see cref="Gst.Uri.IsWritable"/> first.
            """.ReplaceLineEndings("\n"),
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>GList**</c> is the address of the caller's own list variable, which
    /// the callee keeps and re-reads; nothing about it is a value read once.
    /// </summary>
    [Fact]
    public void AListByAddressStaysUnbound() =>
        Assert.DoesNotContain("IterateWidgets", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>A list the call produces is not a list the call is given.</summary>
    [Fact]
    public void AnOutListStaysUnbound() =>
        Assert.DoesNotContain("ProduceWidgets", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>Neither is one it replaces.</summary>
    [Fact]
    public void AnInOutListStaysUnbound() =>
        Assert.DoesNotContain("UpdateWidgets", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// <c>container</c> is the hybrid that consumes the spine and borrows the
    /// elements, and neither of the two marshallers is that. No introspectable
    /// case exists, so this fixture is the whole of the refusal.
    /// </summary>
    [Fact]
    public void AContainerTransferListStaysUnbound() =>
        Assert.DoesNotContain("AdoptSpine", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A boxed element has no per element mint story in either shape: the
    /// borrowed one would hand out a pointer into a copy nobody owns and the
    /// consumed one has no copy to make.
    /// </summary>
    [Fact]
    public void ABoxedElementStaysUnbound() =>
        Assert.DoesNotContain("SetSegments", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>An opaque record is refused for the same reason.</summary>
    [Fact]
    public void AnOpaqueElementStaysUnbound() =>
        Assert.DoesNotContain("SetPolls", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A call that takes a list of GObjects over releases the references the
    /// binding would have to mint for it, which is the <c>*_list_free</c>
    /// family: a no-op with a double release hazard.
    /// </summary>
    [Fact]
    public void ATransferredListOfHandlesStaysUnbound() =>
        Assert.DoesNotContain("TakeWidgets", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A borrowed list of mini objects has no factory: the borrowed overload
    /// takes GObject wrappers, and a mini object is not one.
    /// </summary>
    [Fact]
    public void ABorrowedListOfMiniObjectsStaysUnbound() =>
        Assert.DoesNotContain("PeekBuffers", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// The return planner intercepts a <c>GList</c> before the scalar switch
    /// and leaves a <c>GSList</c> to it, so the new case has to refuse a return
    /// itself or a singly linked one would be planned as a parameter.
    /// </summary>
    [Fact]
    public void ASinglyLinkedReturnStaysUnbound() =>
        Assert.DoesNotContain("GetCategories", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A trampoline that is handed a list would have to project it into managed
    /// code, which is the return side shape. The callback is denied, so nothing
    /// uses it and no callback file is written at all.
    /// </summary>
    [Fact]
    public void ACallbackThatIsHandedAListStaysUnbound()
    {
        Assert.DoesNotContain("Walk", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.False(Run.HasFile("Callbacks.cs"));
    }

    /// <summary>
    /// A consumed list is handed over the moment it is built, and nothing in
    /// the generated body releases it again. A string the callee takes over is
    /// encoded after it, in the same phase and in argument order, and that
    /// encoding refuses an embedded NUL - which would strand the spine and
    /// every UTF-8 copy in it. The whole callable is refused rather than
    /// emitted with the leak in it.
    /// </summary>
    [Fact]
    public void AConsumedListFollowedByAnOwnedStringStaysUnbound() =>
        Assert.DoesNotContain("TakeTagsAndName", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// Two consumed lists are the same story with the second list in the role
    /// of the string: building it walks a sequence the caller supplied, which
    /// throws on a null element, and the first list is already gone.
    /// </summary>
    [Fact]
    public void TwoConsumedListsStayUnbound() =>
        Assert.DoesNotContain("TakeBothLists", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// The twelve refusals above, counted: nothing else of the fixture is
    /// dropped, so a rule that widens shows up here as well as in the member
    /// assertions.
    /// </summary>
    [Fact]
    public void OnlyTheTwelveRejectedShapesAreSkipped() =>
        Assert.Equal(12, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
}
