using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GObject.ParamSpec</c> row of <c>MarshalPlanner.RuntimeTypes</c>: a
/// GType fundamental of a module that emits nothing still reaches a generated
/// signature, wrapped by the hand written <c>Gst.GObject.ParamSpec</c>.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise the three bound directions — borrowed in,
/// borrowed out and transferred out — through <c>gst_child_proxy_lookup</c> and
/// the GES child property family, and the fixtures here are the definition of
/// the feature: they pin the emitted text of each direction, including the null
/// test the constructor of the wrapper cannot perform itself.
/// </para>
/// <para>
/// The last fixture is the rejection and is what keeps the shape from widening.
/// A specification the callee takes over would need a mint the wrapper has no
/// expression for — a <c>GParamSpec</c> is neither a mini object nor a boxed
/// value nor a <c>GObject</c> — so it stays unbound, and the corpus has no such
/// callable for a regression to hide behind.
/// </para>
/// </remarks>
public sealed class ParamSpecArgumentTests
{
    /// <summary>
    /// A class whose four members are the four shapes under test: a borrowed
    /// specification in, a borrowed one out, a transferred one out, and one the
    /// callee takes over.
    /// </summary>
    private const string Body =
        """
            <class name="Holder" c:type="GstHolder" parent="GObject.Object" glib:type-name="GstHolder" glib:get-type="gst_holder_get_type">
              <method name="describe" c:identifier="gst_holder_describe">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="pspec" transfer-ownership="none">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="lookup" c:identifier="gst_holder_lookup">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="pspec" direction="out" caller-allocates="0" transfer-ownership="none">
                    <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="steal" c:identifier="gst_holder_steal">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="pspec" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="adopt" c:identifier="gst_holder_adopt">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="holder" transfer-ownership="none">
                    <type name="Holder" c:type="GstHolder*"/>
                  </instance-parameter>
                  <parameter name="pspec" transfer-ownership="full">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
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
    /// A specification that is passed in is spelled as the hand written wrapper
    /// and crosses as its handle: the wrapper keeps its reference and the
    /// barrier holds it across the call.
    /// </summary>
    [Fact]
    public void ABorrowedSpecificationIsPassedAsTheHandleOfTheWrapper()
    {
        Assert.Equal(
            """
            public void Describe(Gst.GObject.ParamSpec pspec)
            {
                ArgumentNullException.ThrowIfNull(pspec);
                GstHolderDescribe(Handle, pspec.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(pspec);
            }
            """,
            Run.Member("Holder.cs", "public void Describe("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial void GstHolderDescribe(nint holder, nint pspec);",
            Run.File("Holder.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A borrowed specification that comes back is wrapped with a reference of
    /// its own, and the storage is zeroed first, so a callee that answers
    /// <c>FALSE</c> without writing anything reads as <see langword="null"/>.
    /// </summary>
    [Fact]
    public void ABorrowedSpecificationOutTakesAReferenceOfItsOwn()
    {
        Assert.Equal(
            """
            public bool Lookup(out Gst.GObject.ParamSpec? pspec)
            {
                nint pspecNative = default;
                int nativeResult = GstHolderLookup(Handle, &pspecNative);
                System.GC.KeepAlive(this);
                pspec = (pspecNative == 0 ? null : new Gst.GObject.ParamSpec(pspecNative, Gst.Interop.Transfer.None));
                return nativeResult != 0;
            }
            """,
            Run.Member("Holder.cs", "public bool Lookup("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A transferred specification is adopted instead, which is the one place
    /// the GES half of the family differs from the core one.
    /// </summary>
    [Fact]
    public void ATransferredSpecificationOutIsAdopted()
    {
        Assert.Equal(
            """
            public bool Steal(out Gst.GObject.ParamSpec? pspec)
            {
                nint pspecNative = default;
                int nativeResult = GstHolderSteal(Handle, &pspecNative);
                System.GC.KeepAlive(this);
                pspec = (pspecNative == 0 ? null : new Gst.GObject.ParamSpec(pspecNative, Gst.Interop.Transfer.Full));
                return nativeResult != 0;
            }
            """,
            Run.Member("Holder.cs", "public bool Steal("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A specification the callee takes over stays unbound: there is no minting
    /// expression for a GType fundamental, and handing the reference of the
    /// wrapper over would have both of them release it.
    /// </summary>
    [Fact]
    public void ASpecificationTheCalleeTakesOverStaysRejected()
    {
        Assert.DoesNotContain("Adopt(", Run.File("Holder.cs"), StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
