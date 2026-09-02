using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The accessors of a record field that holds a handle: the form each flavour
/// of wrapper takes, the shapes that are refused, and the <c>accessor</c>
/// correction that holds one back.
/// </summary>
public sealed class RecordHandleFieldTests
{
    /// <summary>
    /// One opaque record with a field per flavour a pointer can be projected
    /// with, a pointer to a pointer, and a pointer to something no wrapper of
    /// the run stands for.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Chunk" c:type="GstChunk" glib:type-name="GstChunk" glib:get-type="gst_chunk_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <record name="Bag" c:type="GstBag" glib:type-name="GstBag" glib:get-type="gst_bag_get_type">
              <field name="size" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Size" c:type="GstSize">
              <field name="width" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="height" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Tag" c:type="GstTag" opaque="1">
              <field name="size" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <class name="Thing" c:type="GstThing" parent="GObject.Object" glib:type-name="GstThing" glib:get-type="gst_thing_get_type">
            </class>
            <record name="Widget" c:type="GstWidget" opaque="1">
              <field name="thing" writable="1">
                <type name="Thing" c:type="GstThing*"/>
              </field>
              <field name="chunk" writable="1">
                <type name="Chunk" c:type="GstChunk*"/>
              </field>
              <field name="bag" writable="1">
                <type name="Bag" c:type="GstBag*"/>
              </field>
              <field name="tag" writable="1">
                <type name="Tag" c:type="GstTag*"/>
              </field>
              <field name="bags" writable="1">
                <type name="Bag" c:type="GstBag**"/>
              </field>
              <field name="opaque" writable="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
              <field name="size" writable="1">
                <type name="Size" c:type="GstSize"/>
              </field>
              <field name="inner" writable="1">
                <type name="Bag" c:type="GstBag"/>
              </field>
              <field name="label" writable="1">
                <type name="Tag" c:type="GstTag"/>
              </field>
            </record>
        """;

    [Fact]
    public void AGObjectAndAnOpaqueRecordAreReadThroughAProperty()
    {
        // Both are read rather than acquired: a GObject wrapper is interned and
        // an opaque wrapper owns nothing, so two reads of one field hand out
        // nothing the caller has to release.
        string source = RunWithOverlay("{}").File("Widget.cs");

        Assert.Contains("public Gst.Thing? Thing\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Thing? value = Gst.GObject.Object.FromNative<Gst.Thing>("
            + "((WidgetRaw*)Handle)->Thing, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);

        Assert.Contains("public Gst.Tag? Tag\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Tag? value = Gst.Tag.FromNative(((WidgetRaw*)Handle)->Tag);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMiniObjectAndABoxedValueAreReadThroughAGetMethod()
    {
        // Both come back owning a reference of their own - a mini object is
        // referenced, a boxed value copied - so the caller disposes what a read
        // produced, and a property is the one shape that must not say so.
        string source = RunWithOverlay("{}").File("Widget.cs");

        Assert.Contains("public Gst.Chunk? GetChunk()\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Chunk? value = Gst.Chunk.FromNative("
            + "((WidgetRaw*)Handle)->Chunk, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);

        Assert.Contains("public Gst.Bag? GetBag()\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Bag? Bag\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APointerToAPointerAndAnUntypedPointerAreRefused()
    {
        // A GstBag** is a NULL terminated array of them, so wrapping the
        // address of the array as one element would read the wrong memory; a
        // gpointer names nothing to wrap at all. Both stay on the ledger.
        FixtureRun run = RunWithOverlay("{}");

        Assert.DoesNotContain("public Gst.Bag? Bags", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetBags", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("- `Widget.bags` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.Contains("- `Widget.opaque` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldTheOverlaysHoldBackKeepsItsAddressAndItsLedgerEntry()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.chunk": { "accessor": false, "$comment": "gstchunk.c:1" } } }
            """);
        string source = run.File("Widget.cs");

        Assert.DoesNotContain("GetChunk", source, StringComparison.Ordinal);
        Assert.Contains("internal nint Chunk;", source, StringComparison.Ordinal);
        Assert.Contains("- `Widget.chunk` — Pointer\n", run.Result.SkipReport, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Result.Diagnostics, static d => d.Code == "GEN0026");
    }

    [Fact]
    public void AnEntryThatStatesBothCorrectionsIsReportedAndChangesNothing()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.thing": { "nullable": false, "accessor": false } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
        Assert.Contains("public Gst.Thing? Thing\n", run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void HoldingBackAFieldThatHandsOutNothingAnywayIsReported()
    {
        // The entry has to describe a decision that was acted on. A field the
        // projection refuses is already absent, so an entry on it reads as a
        // rule that is doing work when it is not.
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.opaque": { "accessor": false, "$comment": "gstwidget.c:1" } } }
            """);

        Assert.Single(run.Result.Diagnostics, static d => d.Code == "GEN0026");
    }

    [Fact]
    public void ANonNullableHandleThrowsRatherThanAnsweringTheNullPointer()
    {
        FixtureRun run = RunWithOverlay(
            """
            { "fieldAnnotations": { "GstWidget.bag": { "nullable": false, "$comment": "gstbag.c:1" } } }
            """);
        string source = run.File("Widget.cs");

        Assert.Contains("public Gst.Bag GetBag()\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "throw new System.InvalidOperationException(\"The 'bag' field of GstWidget is null.\")",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmbeddedPlainStructureIsCopiedOutByAProperty()
    {
        // The assignment is the copy, which is the whole of the ownership
        // question for a value: what comes back is the caller's own structure.
        string source = RunWithOverlay("{}").File("Widget.cs");

        Assert.Contains("public Gst.Size Size\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Size value = ((WidgetRaw*)Handle)->Size;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmbeddedBoxedValueIsCopiedOutByAGetMethod()
    {
        // The address of the field is wrapped through the transfer none
        // projection, which copies the value with g_boxed_copy; the caller
        // disposes that copy, so the member is a method. The address of a field
        // is never null, which is what the check spells out.
        string source = RunWithOverlay("{}").File("Widget.cs");

        Assert.Contains("public Gst.Bag GetInner()\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Bag value = Gst.Bag.FromNative((nint)(&((WidgetRaw*)Handle)->Inner), "
            + "Gst.Interop.Transfer.None)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmbeddedOpaqueRecordIsRefused()
    {
        // The wrapper of an opaque record owns nothing, so one made from the
        // address of an embedded field would borrow storage the declaring
        // structure owns and dangle with it. The field stays on the ledger.
        FixtureRun run = RunWithOverlay("{}");

        Assert.DoesNotContain("public Gst.Tag Label", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("GetLabel", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains("- `Widget.label` — EmbeddedStruct\n", run.Result.SkipReport, StringComparison.Ordinal);
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
