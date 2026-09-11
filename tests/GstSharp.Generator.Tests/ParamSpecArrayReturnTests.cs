using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// A counted block of <c>GParamSpec*</c> in return position: the shape
/// <c>ges_timeline_element_list_children_properties</c> answers, read out one
/// pointer at a time into an array of wrappers.
/// </summary>
/// <remarks>
/// <para>
/// The three returning fixtures are the three transfers of the array, which
/// decide two things apart: who owns the elements — <c>full</c> hands one
/// reference per element over, <c>container</c> and <c>none</c> leave them
/// with their owner and every wrapper takes one of its own — and who frees the
/// block, which the library keeps only under <c>none</c>. The ordering of the
/// elements is a fact about the C function and never about the shape, so
/// nothing here claims one.
/// </para>
/// <para>
/// The last fixture is the rejection and is what keeps the shape from
/// widening: a block of specifications an argument carries stays unbound,
/// because nothing in the corpus asks for one and the ownership of a block the
/// caller allocates is a separate contract.
/// </para>
/// </remarks>
public sealed class ParamSpecArrayReturnTests
{
    /// <summary>
    /// A class whose four members are the four shapes under test: a
    /// transferred block of specifications, a block whose container alone is
    /// transferred, a borrowed block, and one an argument carries.
    /// </summary>
    private const string Body =
        """
            <class name="Holder" c:type="GstHolder" parent="GObject.Object" glib:type-name="GstHolder" glib:get-type="gst_holder_get_type">
              <method name="list_owned" c:identifier="gst_holder_list_owned">
                <return-value transfer-ownership="full">
                  <array length="0" zero-terminated="0" c:type="GParamSpec**">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                  </array>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="n_properties" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="list_container" c:identifier="gst_holder_list_container">
                <return-value transfer-ownership="container">
                  <array length="0" zero-terminated="0" c:type="GParamSpec**">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                  </array>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="n_properties" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="list_borrowed" c:identifier="gst_holder_list_borrowed">
                <return-value transfer-ownership="none">
                  <array length="0" zero-terminated="0" c:type="GParamSpec**">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                  </array>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="n_properties" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="install" c:identifier="gst_holder_install">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="properties" transfer-ownership="none">
                    <array length="1" zero-terminated="0" c:type="GParamSpec**">
                      <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                    </array>
                  </parameter>
                  <parameter name="n_properties" transfer-ownership="none">
                    <type name="guint" c:type="guint"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A transferred block hands one reference per element over: the wrappers
    /// adopt them and the block is freed. The count is read off the out
    /// parameter, which stays out of the managed signature.
    /// </summary>
    [Fact]
    public void ATransferredBlockAdoptsEveryElementAndFreesTheBlock()
    {
        Assert.Equal(
            """
            public Gst.GObject.ParamSpec[] ListOwned()
            {
                uint nPropertiesNative = default;
                nint nativeResult = GstHolderListOwned(Handle, &nPropertiesNative);
                Gst.GObject.ParamSpec[] result = [];
                if (nativeResult != 0)
                {
                    result = new Gst.GObject.ParamSpec[(int)nPropertiesNative];
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] = Gst.GObject.ParamSpec.FromNative(((nint*)nativeResult)[index], Gst.Interop.Transfer.Full);
                    }

                    Gst.Interop.GMarshal.Free(nativeResult);
                }

                System.GC.KeepAlive(this);
                return result;
            }
            """,
            Run.Member("Holder.cs", "public Gst.GObject.ParamSpec[] ListOwned("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial nint GstHolderListOwned(nint holder, uint* nProperties);",
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A block whose container alone is transferred leaves the elements with
    /// their owner: every wrapper takes a reference of its own, and the block
    /// is freed all the same.
    /// </summary>
    [Fact]
    public void AContainerTransferReferencesEveryElementAndFreesTheBlock()
    {
        Assert.Equal(
            """
            public Gst.GObject.ParamSpec[] ListContainer()
            {
                uint nPropertiesNative = default;
                nint nativeResult = GstHolderListContainer(Handle, &nPropertiesNative);
                Gst.GObject.ParamSpec[] result = [];
                if (nativeResult != 0)
                {
                    result = new Gst.GObject.ParamSpec[(int)nPropertiesNative];
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] = Gst.GObject.ParamSpec.FromNative(((nint*)nativeResult)[index], Gst.Interop.Transfer.None);
                    }

                    Gst.Interop.GMarshal.Free(nativeResult);
                }

                System.GC.KeepAlive(this);
                return result;
            }
            """,
            Run.Member("Holder.cs", "public Gst.GObject.ParamSpec[] ListContainer("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A borrowed block belongs to the library in both halves: the wrappers
    /// reference the elements and nothing is freed.
    /// </summary>
    [Fact]
    public void ABorrowedBlockIsNeitherAdoptedNorFreed()
    {
        Assert.Equal(
            """
            public Gst.GObject.ParamSpec[] ListBorrowed()
            {
                uint nPropertiesNative = default;
                nint nativeResult = GstHolderListBorrowed(Handle, &nPropertiesNative);
                Gst.GObject.ParamSpec[] result = [];
                if (nativeResult != 0)
                {
                    result = new Gst.GObject.ParamSpec[(int)nPropertiesNative];
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] = Gst.GObject.ParamSpec.FromNative(((nint*)nativeResult)[index], Gst.Interop.Transfer.None);
                    }
                }

                System.GC.KeepAlive(this);
                return result;
            }
            """,
            Run.Member("Holder.cs", "public Gst.GObject.ParamSpec[] ListBorrowed("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The documentation states the two things the gir does not: the wrappers
    /// are the caller's to dispose, and a call that answers nothing reads as an
    /// empty array rather than as <see langword="null"/>.
    /// </summary>
    [Fact]
    public void TheDocumentationStatesTheOwnershipOfTheElements()
    {
        Assert.Contains(
            "Every specification is the caller's to dispose",
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "<c>G_PARAM_SPEC_TYPE</c>",
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A block of specifications an argument carries stays unbound: the shape
    /// is planned in return position only, so the member is skipped rather
    /// than emitted with a projection nobody described.
    /// </summary>
    [Fact]
    public void ABlockOfSpecificationsInArgumentPositionStaysRejected()
    {
        Assert.DoesNotContain("public void Install(", Run.File("Holder.cs"), StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
