using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The projection of a <c>GSList</c> a call hands back: the shape
/// <c>gst_debug_get_all_categories</c> has, the borrowed twin of it, and the
/// two positions that stay refused.
/// </summary>
/// <remarks>
/// The girs of the bound modules carry exactly one singly linked return, and it
/// is a namespace level function rather than a method, so the arm has no method
/// shaped witness in the committed sources at all. What is asserted here is
/// therefore the whole of it: the member the real shape produces, the release
/// the list type decides, and the refusals that keep the rule from widening
/// into positions no plan covers.
/// </remarks>
public sealed class ListReturnTests
{
    /// <summary>
    /// One opaque record to list, three namespace level functions that answer a
    /// <c>GSList</c> of it or of strings, and a class whose slot answers one.
    /// <c>get_polls</c> is the shape of the real symbol: a
    /// <c>transfer-ownership="container"</c> list of records the library keeps.
    /// <c>steal_polls</c> hands the records over, which no opaque wrapper can
    /// release, and <c>peek_names</c> is the borrowed twin whose spine stays the
    /// library's. <c>list_marks</c> is the slot.
    /// </summary>
    private const string Body =
        """
            <record name="Poll" c:type="GstPoll" disguised="1" opaque="1">
            </record>
            <function name="get_polls" c:identifier="gst_get_polls">
              <return-value transfer-ownership="container">
                <type name="GLib.SList" c:type="GSList*">
                  <type name="Poll"/>
                </type>
              </return-value>
            </function>
            <function name="steal_polls" c:identifier="gst_steal_polls">
              <return-value transfer-ownership="full">
                <type name="GLib.SList" c:type="GSList*">
                  <type name="Poll"/>
                </type>
              </return-value>
            </function>
            <function name="peek_names" c:identifier="gst_peek_names">
              <return-value transfer-ownership="none">
                <type name="GLib.SList" c:type="GSList*">
                  <type name="utf8"/>
                </type>
              </return-value>
            </function>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="list_marks">
                <return-value transfer-ownership="container">
                  <type name="GLib.SList" c:type="GSList*">
                    <type name="utf8"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.InitiallyUnownedClass" c:type="GInitiallyUnownedClass"/>
              </field>
              <field name="list_marks">
                <callback name="list_marks">
                  <return-value transfer-ownership="container">
                    <type name="GLib.SList" c:type="GSList*">
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
            </record>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// The shape of the real symbol, on the surface it lands on: a namespace
    /// level function answers a read only list of borrowed wrappers, the spine
    /// goes back to <c>g_slist_free</c>, and nothing releases an element.
    /// </summary>
    [Fact]
    public void ASinglyLinkedContainerReturnFreesTheSpineAndBorrowsTheElements()
    {
        Assert.Equal(
            """
            public static System.Collections.Generic.IReadOnlyList<Gst.Poll> GetPolls()
            {
                nint nativeResult = GstGetPolls();
                nint[] nativeItems = Gst.Interop.GListMarshal.CollectAndFreeSpine(nativeResult, singly: true);
                System.Collections.Generic.List<Gst.Poll> result = new(nativeItems.Length);
                foreach (nint nativeItem in nativeItems)
                {
                    if (nativeItem != 0 && Gst.Poll.FromNative(nativeItem) is { } adopted)
                    {
                        result.Add(adopted);
                    }
                }

                return result;
            }
            """,
            Run.Member("Global.cs", "public static System.Collections.Generic.IReadOnlyList<Gst.Poll> GetPolls("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A list the library goes on owning is read and nothing is freed, which is
    /// the one case where the two list types need no telling apart at all: the
    /// walk reads the fields both layouts share and never reaches an allocator.
    /// </summary>
    [Fact]
    public void ABorrowedSinglyLinkedReturnIsOnlyRead()
    {
        string member = Run.Member(
            "Global.cs",
            "public static System.Collections.Generic.IReadOnlyList<string> PeekNames(");

        Assert.Contains(
            "nint[] nativeItems = Gst.Interop.GListMarshal.Collect(nativeResult);",
            member,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CollectAndFreeSpine", member, StringComparison.Ordinal);
        Assert.DoesNotContain("singly", member, StringComparison.Ordinal);
    }

    /// <summary>
    /// The element rule is the one the doubly linked return already carries and
    /// the list type does not soften it: the wrapper of an opaque record owns
    /// nothing and is never disposed, so a list that hands its elements over has
    /// nobody to release them.
    /// </summary>
    [Fact]
    public void ASinglyLinkedReturnOfOwnedOpaqueRecordsStaysUnbound() =>
        Assert.DoesNotContain("StealPolls", Run.File("Global.cs"), StringComparison.Ordinal);

    /// <summary>
    /// Only the return of a call is planned. A slot that answers a list would
    /// have to build one out of what an override supplied, which is the
    /// parameter side story and has no plan on the trampoline; the slot stays
    /// out and the ledger says why.
    /// </summary>
    [Fact]
    public void ASinglyLinkedReturnInAVirtualMethodStaysUnbound()
    {
        FixtureRun subclassable = RunWithOverlay("""{ "subclassable": ["Gst.Widget"] }""");

        Assert.DoesNotContain("OnListMarks", subclassable.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(
            "UnsupportedSignature",
            subclassable.Result.Census.SkippedVirtuals("Gst")["Gst.Widget::list_marks"]);
    }

    /// <summary>
    /// The one refusal above, counted: the fixture drops nothing else, so a
    /// rule that widens shows up here as well as in the member assertions.
    /// </summary>
    [Fact]
    public void OnlyTheOwnedOpaqueListIsSkipped() =>
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));

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
