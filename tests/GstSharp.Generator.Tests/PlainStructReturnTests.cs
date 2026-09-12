using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// A blittable structure a call hands back by pointer: the shape
/// <c>gst_format_get_details</c>, <c>gst_rtp_payload_info_for_pt</c> and
/// <c>gst_mikey_message_get_cs_srtp</c> answer, copied out of the row the
/// library keeps owning.
/// </summary>
/// <remarks>
/// <para>
/// The three fixtures are the three shapes the rule tells apart. A borrowed
/// row the gir spells <c>nullable</c> is copied out behind a null test, which
/// makes the member answer a nullable structure; one the gir promises is
/// copied out behind the guard every other non-nullable pointer return
/// carries; and a row the call transfers, as <c>full</c> or as
/// <c>container</c>, stays refused, because a structure the caller owns names
/// no free it could be released through.
/// </para>
/// <para>
/// What the copy is, and what it is not, is stated on the member: the bytes of
/// the structure are the caller's from the moment of the call, while a pointer
/// or a string field inside it still addresses the memory of the library and is
/// read from it at the time it is accessed.
/// </para>
/// </remarks>
public sealed class PlainStructReturnTests
{
    /// <summary>
    /// A row type with no pointer of its own, which is what makes the
    /// classifier call it a plain structure, and a class whose three members
    /// are the three shapes under test.
    /// </summary>
    private const string Body =
        """
            <record name="Row" c:type="GstRow">
              <field name="value" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="weight" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
            <class name="Holder" c:type="GstHolder" parent="GObject.Object" glib:type-name="GstHolder" glib:get-type="gst_holder_get_type">
              <method name="find_row" c:identifier="gst_holder_find_row">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="Row" c:type="const GstRow*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="index" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </method>
              <function name="describe_row" c:identifier="gst_holder_describe_row">
                <return-value transfer-ownership="none">
                  <type name="Row" c:type="const GstRow*"/>
                </return-value>
                <parameters>
                  <parameter name="index" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </function>
              <function name="build_row" c:identifier="gst_holder_build_row">
                <return-value transfer-ownership="full">
                  <type name="Row" c:type="GstRow*"/>
                </return-value>
                <parameters>
                  <parameter name="index" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </function>
              <function name="claim_row" c:identifier="gst_holder_claim_row">
                <return-value transfer-ownership="container">
                  <type name="Row" c:type="GstRow*"/>
                </return-value>
                <parameters>
                  <parameter name="index" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </function>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A borrowed row the gir spells nullable is copied out behind the null
    /// test, and the barrier of the instance follows the copy: the structure is
    /// read out of memory the instance owns.
    /// </summary>
    [Fact]
    public void ANullableBorrowedRowIsCopiedOutBehindItsNullTest()
    {
        Assert.Equal(
            """
            public Gst.Row? FindRow(uint index)
            {
                nint nativeResult = GstHolderFindRow(Handle, index);
                Gst.Row? result = nativeResult == 0 ? null : *(Gst.Row*)nativeResult;
                System.GC.KeepAlive(this);
                return result;
            }
            """,
            Run.Member("Holder.cs", "public Gst.Row? FindRow("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial nint GstHolderFindRow(nint holder, uint index);",
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A row the gir promises carries the guard of every other non-nullable
    /// pointer return: the null pointer is a contract the library broke, not a
    /// value, so it is raised rather than answered as a zeroed structure.
    /// </summary>
    [Fact]
    public void APromisedBorrowedRowIsGuardedRatherThanAnsweredZeroed()
    {
        Assert.Equal(
            """
            public static Gst.Row DescribeRow(uint index)
            {
                nint nativeResult = GstHolderDescribeRow(index);
                return nativeResult == 0 ? throw new InvalidOperationException("gst_holder_describe_row returned no value.") : *(Gst.Row*)nativeResult;
            }
            """,
            Run.Member("Holder.cs", "public static Gst.Row DescribeRow("),
            StringComparer.Ordinal);
    }

    /// <summary>The copy and what stays borrowed inside it are stated on the member.</summary>
    [Fact]
    public void TheMemberSaysWhatTheCopyIs()
    {
        Assert.Contains(
            """
                /// The structure is a copy of a row the library owns, taken at the moment of
                /// the call: writing into it changes nothing native, and the string and
                /// pointer fields it carries are read from the memory of the library at the
                /// time they are accessed.
            """,
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred row stays refused, on either of the two transfers the gir
    /// can spell. The caller would own the structure the pointer addresses, and
    /// nothing in the gir names the free it would have to go back through, so
    /// the copy rule holds for the borrowed shape alone.
    /// </summary>
    [Fact]
    public void ATransferredRowStaysUnsupported()
    {
        Assert.DoesNotContain("BuildRow", Run.File("Holder.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimRow", Run.File("Holder.cs"), StringComparison.Ordinal);
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
