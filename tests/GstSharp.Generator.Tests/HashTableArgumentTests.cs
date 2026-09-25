using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The projection of a <c>GHashTable</c> of string keys: the two shapes a call
/// answers, the one shape a call is given, and the refusals around them.
/// </summary>
/// <remarks>
/// The reference girs exercise all three accepted shapes exactly once each —
/// two getters and a setter of <c>Gst.Uri</c>, and the control bindings of a
/// <c>GESTrackElement</c> — and the census tests freeze those counts. What only
/// a fixture can hold honest is everything around them: a transfer no bound
/// module spells, a value type the runtime has no reader for, and the
/// directions and the inbound positions that have no projection at all, each of
/// which produces no committed diff when it silently widens.
/// </remarks>
public sealed class HashTableArgumentTests
{
    /// <summary>
    /// One class carrying every table shape. <c>get_tags</c> and
    /// <c>get_bindings</c> are the two shapes a call answers, <c>set_tags</c>
    /// and <c>set_labels</c> the one a call is given in its two nullabilities.
    /// Everything below them is a refusal: a transfer that is not the one of the
    /// shape, a direction that hands the address of a table over, a key that is
    /// not a string, a value the runtime cannot read, and the three inbound
    /// positions — a callback, a signal and a virtual slot — in both
    /// directions, where a table is a value the binding receives or one a
    /// trampoline would have to hand back. <c>get_marks</c> and
    /// <c>get_slots</c> are the two accepted shapes with the nullability the
    /// gir states reversed, which is the one thing the projection does not read
    /// off the gir: the table of strings keeps its own answer and the table of
    /// GObjects, which cannot have one, is refused instead.
    /// </summary>
    private const string Body =
        """
            <callback name="TagFunc" c:type="GstTagFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="tags" transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <callback name="LabelFunc" c:type="GstLabelFunc">
              <return-value transfer-ownership="none">
                <type name="GLib.HashTable" c:type="GHashTable*">
                  <type name="utf8"/>
                  <type name="utf8"/>
                </type>
              </return-value>
              <parameters>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="0">
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
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <method name="get_tags" c:identifier="gst_widget_get_tags">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="const GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_bindings" c:identifier="gst_widget_get_bindings">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Widget"/>
                  </type>
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
                  <parameter name="tags" transfer-ownership="none" nullable="1" allow-none="1">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_labels" c:identifier="gst_widget_set_labels">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="labels" transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="get_marks" c:identifier="gst_widget_get_marks">
                <return-value transfer-ownership="full">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="const GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_slots" c:identifier="gst_widget_get_slots">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="peek_tags" c:identifier="gst_widget_peek_tags">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="adopt_tags" c:identifier="gst_widget_adopt_tags">
                <return-value transfer-ownership="container">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="take_bindings" c:identifier="gst_widget_take_bindings">
                <return-value transfer-ownership="full">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="share_bindings" c:identifier="gst_widget_share_bindings">
                <return-value transfer-ownership="container">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_codes" c:identifier="gst_widget_get_codes">
                <return-value transfer-ownership="full">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="guint"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_values" c:identifier="gst_widget_get_values">
                <return-value transfer-ownership="full">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="GObject.Value"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_buffers" c:identifier="gst_widget_get_buffers">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Buffer"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_segments" c:identifier="gst_widget_get_segments">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Segment"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="get_polls" c:identifier="gst_widget_get_polls">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="Poll"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="take_tags" c:identifier="gst_widget_take_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="full">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="lend_tags" c:identifier="gst_widget_lend_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="container">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_bindings" c:identifier="gst_widget_set_bindings">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="bindings" transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="Widget"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="set_codes" c:identifier="gst_widget_set_codes">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="codes" transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="guint"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="produce_tags" c:identifier="gst_widget_produce_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="GLib.HashTable" c:type="GHashTable**">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
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
                  <parameter name="tags" direction="inout" caller-allocates="0" transfer-ownership="full">
                    <type name="GLib.HashTable" c:type="GHashTable**">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </method>
              <method name="walk_tags" c:identifier="gst_widget_walk_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="TagFunc" c:type="GstTagFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="ask_labels" c:identifier="gst_widget_ask_labels">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="LabelFunc" c:type="GstLabelFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <virtual-method name="collect_tags">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
              <virtual-method name="apply_tags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="tags" transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </virtual-method>
              <glib:signal name="tags-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="tags" transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="tags-wanted" when="last">
                <return-value transfer-ownership="none">
                  <type name="GLib.HashTable" c:type="GHashTable*">
                    <type name="utf8"/>
                    <type name="utf8"/>
                  </type>
                </return-value>
              </glib:signal>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.InitiallyUnownedClass" c:type="GInitiallyUnownedClass"/>
              </field>
              <field name="collect_tags">
                <callback name="collect_tags">
                  <return-value transfer-ownership="none">
                    <type name="GLib.HashTable" c:type="GHashTable*">
                      <type name="utf8"/>
                      <type name="utf8"/>
                    </type>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
              <field name="apply_tags">
                <callback name="apply_tags">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="tags" transfer-ownership="none">
                      <type name="GLib.HashTable" c:type="GHashTable*">
                        <type name="utf8"/>
                        <type name="utf8"/>
                      </type>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
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
    /// A table of strings a call hands over: the entries are copied out and the
    /// reference is released in the same call, before anything else runs. The
    /// member is nullable because the gir says so — no query at all is not an
    /// empty query — and the value of an entry is nullable however the gir
    /// spells it, because a key without a value is a state C can hold.
    /// </summary>
    [Fact]
    public void ATransferredTableOfStringsIsCopiedAndReleased() =>
        Assert.Equal(
            """
            public System.Collections.Generic.Dictionary<string, string?>? GetTags()
            {
                nint nativeResult = GstWidgetGetTags(Handle);
                System.Collections.Generic.Dictionary<string, string?>? result = Gst.Interop.HashTableMarshal.ToStringDictionary(nativeResult, unref: true);
                System.GC.KeepAlive(this);
                return result;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public System.Collections.Generic.Dictionary<string, string?>? GetTags("));

    /// <summary>
    /// A table of GObjects the library keeps owning: the values are wrapped by
    /// the very expression a list of the same element type uses, so an element
    /// is interned once however it was reached, and nothing is released.
    /// </summary>
    [Fact]
    public void ABorrowedTableOfObjectsIsWrappedElementByElement() =>
        Assert.Equal(
            """
            public System.Collections.Generic.Dictionary<string, Gst.Widget> GetBindings()
            {
                nint nativeResult = GstWidgetGetBindings(Handle);
                System.Collections.Generic.Dictionary<string, Gst.Widget> result = Gst.Interop.HashTableMarshal.ToObjectDictionary<Gst.Widget>(nativeResult, static nativeItem => Gst.GObject.Object.FromNative<Gst.Widget>(nativeItem, Gst.Interop.Transfer.None));
                System.GC.KeepAlive(this);
                return result;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public System.Collections.Generic.Dictionary<string, Gst.Widget> GetBindings("));

    /// <summary>
    /// The one shape a call is given: a temporary table that the scope releases
    /// when the call returns, whether it returned or threw. A nullable table is
    /// not guarded — the null pointer is a value the callee acts on.
    /// </summary>
    [Fact]
    public void ABorrowedTableIsBuiltIntoAScope() =>
        Assert.Equal(
            """
            public bool SetTags(System.Collections.Generic.IReadOnlyDictionary<string, string?>? tags)
            {
                using Gst.Interop.HashTableScope tagsScope = Gst.Interop.HashTableMarshal.Alloc(tags);
                int nativeResult = GstWidgetSetTags(Handle, tagsScope.Handle);
                bool result = nativeResult != 0;
                System.GC.KeepAlive(this);
                return result;
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public bool SetTags("));

    /// <summary>
    /// And its non-nullable twin, which is guarded: a table the gir does not
    /// mark nullable is one the callee dereferences.
    /// </summary>
    [Fact]
    public void ATableThatIsNotNullableIsGuarded() =>
        Assert.Equal(
            """
            public void SetLabels(System.Collections.Generic.IReadOnlyDictionary<string, string?> labels)
            {
                ArgumentNullException.ThrowIfNull(labels);
                using Gst.Interop.HashTableScope labelsScope = Gst.Interop.HashTableMarshal.Alloc(labels);
                GstWidgetSetLabels(Handle, labelsScope.Handle);
                System.GC.KeepAlive(this);
            }
            """.ReplaceLineEndings("\n"),
            Run.Member("Widget.cs", "public void SetLabels("));

    /// <summary>
    /// What the parameter of such a member is documented with, which is the
    /// only place the copy, the bare key and the difference between no table
    /// and an empty one are stated for a caller.
    /// </summary>
    [Fact]
    public void ATableACallIsGivenSaysWhatBecomesOfIt() =>
        Assert.Contains(
            "/// The entries are copied into a native table that is built for the call and",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);

    /// <summary>
    /// The difference between an absent table and an empty one is stated only
    /// where a caller can produce the first. A nullable table carries the
    /// sentence, because leaving the dictionary out is a call the member
    /// makes; a table the member guards against null carries it not, because
    /// the state it describes is one the signature already refuses.
    /// </summary>
    [Fact]
    public void OnlyANullableTableIsToldApartFromAnEmptyOne()
    {
        const string sentence = "a null dictionary is not an empty one";
        string file = Run.File("Widget.cs");

        Assert.Contains(sentence, Documentation(file, "public bool SetTags("), StringComparison.Ordinal);
        Assert.DoesNotContain(
            sentence,
            Documentation(file, "public void SetLabels("),
            StringComparison.Ordinal);

        // The rest of the note is the same one, so the shorter form is the
        // same paragraph with its last sentence closed a clause earlier.
        Assert.Contains(
            "/// all, which is a state C spells.",
            Documentation(file, "public void SetLabels("),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the documentation comment that stands above one member.
    /// </summary>
    /// <param name="file">The generated source text.</param>
    /// <param name="signature">The start of the member declaration.</param>
    /// <returns>The <c>///</c> lines directly above the declaration.</returns>
    private static string Documentation(string file, string signature)
    {
        string[] lines = file.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith(signature, StringComparison.Ordinal))
            {
                continue;
            }

            int first = i;
            while (first > 0 && lines[first - 1].TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                first--;
            }

            return string.Join("\n", lines[first..i]);
        }

        throw new InvalidOperationException($"No member starting with '{signature}' was generated.");
    }

    /// <summary>
    /// A table of strings the library keeps owning has no shape here: the copy
    /// would be right and the member would read as a snapshot of a table the
    /// caller cannot see change, which is a contract no bound symbol states.
    /// </summary>
    [Fact]
    public void ABorrowedTableOfStringsStaysUnbound() =>
        Assert.DoesNotContain("PeekTags", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// The hybrid transfer, which would hand the table over and leave the
    /// strings in it with their owner, has no introspectable case and is
    /// refused in both value types.
    /// </summary>
    [Fact]
    public void AContainerTransferReturnStaysUnbound()
    {
        Assert.DoesNotContain("AdoptTags", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ShareBindings", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A table of GObjects that a call hands over is refused: the wrappers take
    /// a reference of their own and nothing would release the reference of the
    /// table, which the library alone knows how to unwind.
    /// </summary>
    [Fact]
    public void ATransferredTableOfObjectsStaysUnbound() =>
        Assert.DoesNotContain("TakeBindings", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A table a call takes over stays unbound in either transfer: the callee
    /// would go on editing entries whose shape no managed owner is left to
    /// state.
    /// </summary>
    [Fact]
    public void ATableThatIsHandedOverStaysUnbound()
    {
        Assert.DoesNotContain("TakeTags", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("LendTags", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A table of GObjects a call is given stays unbound: only the string
    /// shape is built here, because only its entries have an allocator and a
    /// destructor this code can name.
    /// </summary>
    [Fact]
    public void ATableOfObjectsInArgumentPositionStaysUnbound() =>
        Assert.DoesNotContain("SetBindings", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// A key that is not a string stays unbound in both directions:
    /// <c>g_str_hash</c> is the only hash this hands GLib.
    /// </summary>
    [Fact]
    public void AKeyThatIsNotAStringStaysUnbound()
    {
        Assert.DoesNotContain("GetCodes", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("SetCodes", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The four value types beside a string and a GObject class: a
    /// <c>GValue</c>, a mini object, a boxed record and an opaque record. None
    /// of them has a per entry read this plans for.
    /// </summary>
    [Fact]
    public void AValueTheRuntimeCannotReadStaysUnbound()
    {
        Assert.DoesNotContain("GetValues", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetBuffers", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetSegments", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetPolls", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// An out and an inout table come back through the address of the caller's
    /// own variable, which is a marshaller of its own and not this one. It is
    /// the shape of the two <c>GMenuModel</c> slots of Gio, which is not a
    /// generated module, so only a fixture states the refusal.
    /// </summary>
    [Fact]
    public void ATableByAddressStaysUnbound()
    {
        Assert.DoesNotContain("ProduceTags", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateTags", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The three inbound positions. A callback that is handed a table takes the
    /// member that installs it out with it, a signal that carries one is not
    /// emitted, and a virtual slot that receives one is not overridable: all
    /// three would have to project a table into managed code from a trampoline,
    /// which is the return side shape and not this one. The one corpus case,
    /// <c>GESBaseEffectTimeTranslationFunc</c>, is bound by hand in
    /// <c>src/GstSharp.Net.GES/Custom/BaseEffect.cs</c>.
    /// </summary>
    [Fact]
    public void ATableAnInboundPositionIsHandedStaysUnbound()
    {
        Assert.DoesNotContain("WalkTags", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("TagsChanged", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyTags", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.False(Run.HasFile("Callbacks.cs"));
    }

    /// <summary>
    /// The same three positions on the way back. A table a trampoline would
    /// have to hand native code is the argument side shape seen from the other
    /// end, and nothing builds one there: the member that installs the callback
    /// goes, the signal is not emitted, and the slot stays off the subclassing
    /// surface with the reason the ledger prints.
    /// </summary>
    [Fact]
    public void ATableAnInboundPositionAnswersStaysUnbound()
    {
        Assert.DoesNotContain("AskLabels", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("TagsWanted", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("OnCollectTags", Run.File("Widget.cs"), StringComparison.Ordinal);

        // The slot only reaches the subclassing surface, and the ledger, for a
        // class the overlays opened; the run above opens none, so the refusal
        // is read off a run that does.
        FixtureRun subclassable = RunWithOverlay("""{ "subclassable": ["Gst.Widget"] }""");
        Assert.DoesNotContain("OnCollectTags", subclassable.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(
            "UnsupportedSignature",
            subclassable.Result.Census.SkippedVirtuals("Gst")["Gst.Widget::collect_tags"]);
    }

    /// <summary>
    /// The nullability of the table itself is the shape's and not the gir's. A
    /// table of strings is nullable however the gir spells it, because the
    /// runtime helper answers nothing for an absent table; a member that said
    /// otherwise would declare a local the helper cannot fill.
    /// </summary>
    [Fact]
    public void ATableOfStringsIsNullableWhateverTheGirSays() =>
        Assert.Contains(
            "public System.Collections.Generic.Dictionary<string, string?>? GetMarks()",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);

    /// <summary>
    /// And the mirror of it: a table of GObjects the library keeps owning is
    /// not nullable, because the helper that reads one never answers nothing.
    /// A gir that marks such a return nullable is a disagreement and not
    /// noise, so the member is left out rather than emitted with the
    /// annotation overruled — the values are borrowed, so the copy cannot tell
    /// an absent table from an empty one, and answering an empty dictionary
    /// would erase a state the C function spells.
    /// </summary>
    [Fact]
    public void ANullableTableOfObjectsTheLibraryKeepsStaysUnbound() =>
        Assert.DoesNotContain("GetSlots", Run.File("Widget.cs"), StringComparison.Ordinal);

    /// <summary>
    /// What the overlays cannot move is the nullability of a table of strings:
    /// a correction that calls the return non-nullable leaves the member
    /// nullable all the same, because what the runtime helper answers for an
    /// absent table is a fact about this binding, which no statement about the
    /// C function can change. The correction is not ignored everywhere,
    /// though — on a table of GObjects it decides whether the member exists at
    /// all, which is the test below.
    /// </summary>
    [Fact]
    public void AnOverlayDoesNotMoveTheNullabilityOfATableOfStrings()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "gst_widget_get_tags#return": { "nullable": false, "$comment": "gstwidget.c:1" }
              }
            }
            """);

        Assert.Contains(
            "public System.Collections.Generic.Dictionary<string, string?>? GetTags()",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror of it, and the one correction of a table annotation that
    /// does move a member: a gir that says nothing about the nullability of a
    /// borrowed table of GObjects is bound, and an overlay that calls the same
    /// return nullable refuses it, exactly as a gir that spelled it would.
    /// The refusal is the same one <c>get_slots</c> gets, so it lands in the
    /// same bucket, which is what keeps the correction from being a quieter
    /// kind of skip than the annotation it corrects.
    /// </summary>
    [Fact]
    public void AnOverlayCallingABorrowedTableOfObjectsNullableRefusesIt()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "gst_widget_get_bindings#return": { "nullable": true, "$comment": "gstwidget.c:2" }
              }
            }
            """);

        Assert.DoesNotContain("GetBindings", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("gst_widget_get_bindings", UnsupportedSymbols(run));
        Assert.Equal(21, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// Every refusal above is one skipped callable and no more, which is what
    /// keeps a rule that silently widens from passing unnoticed: the accepted
    /// members produce a diff, the refused ones produce nothing but this count.
    /// The names are frozen beside it, because a count alone cannot tell a rule
    /// that widened at one end and narrowed at the other from one that stood
    /// still.
    /// </summary>
    [Fact]
    public void OnlyTheRejectedShapesAreSkipped()
    {
        Assert.Equal(20, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

        Assert.Equal(
            [
                "Gst.Widget::tags-changed",
                "Gst.Widget::tags-wanted",
                "gst_widget_adopt_tags",
                "gst_widget_ask_labels",
                "gst_widget_get_buffers",
                "gst_widget_get_codes",
                "gst_widget_get_polls",
                "gst_widget_get_segments",
                "gst_widget_get_slots",
                "gst_widget_get_values",
                "gst_widget_lend_tags",
                "gst_widget_peek_tags",
                "gst_widget_produce_tags",
                "gst_widget_set_bindings",
                "gst_widget_set_codes",
                "gst_widget_share_bindings",
                "gst_widget_take_bindings",
                "gst_widget_take_tags",
                "gst_widget_update_tags",
                "gst_widget_walk_tags",
            ],
            UnsupportedSymbols(Run));
    }

    /// <summary>
    /// Reads the symbols one run left out for an unsupported signature back out
    /// of the ledger it prints.
    /// </summary>
    /// <param name="run">The run to read.</param>
    /// <returns>The symbols, in the order the ledger lists them.</returns>
    private static IReadOnlyList<string> UnsupportedSymbols(FixtureRun run)
    {
        List<string> symbols = [];
        bool inside = false;
        foreach (string line in run.Result.Census.SkipReport().Split('\n'))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inside = line.StartsWith("### UnsupportedSignature", StringComparison.Ordinal);
                continue;
            }

            if (inside && line.StartsWith("- `", StringComparison.Ordinal))
            {
                symbols.Add(line[3..line.LastIndexOf('`')]);
            }
        }

        return symbols;
    }

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
