using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The field a record embeds a mini object in. It is the header a derived
/// record carries first, so the field starts at the address of the record that
/// declares it and an accessor for it would be an identity cast - and one the
/// remark of an embedded record would describe as a copy the caller owns,
/// which is the opposite of the alias a mini object wrapper is. The generator
/// refuses the shape itself, with no overlay entry behind it.
/// </summary>
public sealed class EmbeddedMiniObjectFieldTests
{
    /// <summary>
    /// The shape of a derived MIKEY payload: a record embedding a mini object
    /// rooted header first, and a boxed record beside it.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Payload" c:type="GstPayload" glib:type-name="GstPayload" glib:get-type="gst_payload_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <field name="kind" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Bag" c:type="GstBag" glib:type-name="GstBag" glib:get-type="gst_bag_get_type">
              <field name="size" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="label" writable="1">
                <type name="utf8" c:type="gchar*"/>
              </field>
            </record>
            <record name="PayloadKemac" c:type="GstPayloadKemac" glib:type-name="GstPayloadKemac" glib:get-type="gst_payload_kemac_get_type">
              <field name="pt" writable="1">
                <type name="Payload" c:type="GstPayload"/>
              </field>
              <field name="bag" writable="1">
                <type name="Bag" c:type="GstBag"/>
              </field>
            </record>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// The embedded header gets no accessor and stays on the ledger under the
    /// shape that kept it out.
    /// </summary>
    [Fact]
    public void AnEmbeddedMiniObjectGetsNoAccessor()
    {
        string source = Run.File("PayloadKemac.cs");

        Assert.DoesNotContain("GetPt", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Payload", source, StringComparison.Ordinal);
        Assert.Contains(
            "- `PayloadKemac.pt` — EmbeddedStruct\n",
            Run.Result.SkipReport,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A boxed record embedded beside it still gets its accessor: what comes
    /// back is the copy the remark describes, and the storage it was copied out
    /// of belongs to the record that declares the field.
    /// </summary>
    [Fact]
    public void AnEmbeddedBoxedRecordKeepsItsAccessor()
    {
        string source = Run.File("PayloadKemac.cs");

        Assert.Contains("public Gst.Bag GetBag()", source, StringComparison.Ordinal);
        Assert.Contains(
            "The structure is embedded in the one this wrapper points at.",
            source,
            StringComparison.Ordinal);
    }
}
