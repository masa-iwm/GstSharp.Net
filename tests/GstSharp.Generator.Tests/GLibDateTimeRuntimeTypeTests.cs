using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GLib.DateTime</c> row of <c>MarshalPlanner.RuntimeTypes</c>: a boxed
/// type of the hand written runtime is planned with the <c>Wrapper</c> flavour,
/// so a transferred in parameter is a consumed argument of the boxed family and
/// a borrowed one is a plain handle.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through ten callables of <c>Gst</c> and
/// <c>GstVideo</c>, but only in the members those happen to be. The fixtures
/// here are the definition of the feature: they name the three positions the
/// type reaches — a <c>GDateTime*</c> in under
/// <c>transfer-ownership="full"</c>, one under <c>none</c>, and a returned one
/// under <c>full</c> — and they pin the emitted text of each.
/// </para>
/// <para>
/// The transferred in parameter is what says that the family is
/// <c>ConsumedFamily.Boxed</c> rather than one of the other three: the
/// materialization reads the boxed type off the wrapper and mints with
/// <c>BoxedCopy</c>, which for <c>GDateTime</c> is <c>g_date_time_ref</c>, and
/// the epilogue disposes the wrapper. That the public type is nullable and
/// that the wrap goes through the typed <c>FromNative</c> of the runtime class
/// rather than through the interning <c>Object.FromNative</c> is what says the
/// flavour is <c>Wrapper</c>.
/// </para>
/// </remarks>
public sealed class GLibDateTimeRuntimeTypeTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixtures refer to. It is
    /// a stand in for the vendored <c>GLib-2.0.gir</c>: only the attributes
    /// that decide the classification are kept, which for <c>GDateTime</c> are
    /// the opacity and the boxed registration.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="DateTime" c:type="GDateTime" opaque="1" glib:type-name="GDateTime" glib:get-type="g_date_time_get_type" c:symbol-prefix="date_time">
            </record>
          </namespace>
        """;

    /// <summary>
    /// A class whose three members are the three shapes under test: a
    /// transferred <c>GDateTime</c> in, a borrowed one in, and a transferred
    /// one returned.
    /// </summary>
    private const string Body =
        """
            <class name="Stamp" c:type="GstStamp" parent="GObject.Object" glib:type-name="GstStamp" glib:get-type="gst_stamp_get_type">
              <method name="take_time" c:identifier="gst_stamp_take_time">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="time" transfer-ownership="full" nullable="1">
                    <type name="GLib.DateTime" c:type="GDateTime*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_time" c:identifier="gst_stamp_set_time">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="time" transfer-ownership="none">
                    <type name="GLib.DateTime" c:type="GDateTime*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_time" c:identifier="gst_stamp_get_time">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GLib.DateTime" c:type="GDateTime*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A transferred <c>GDateTime</c> in parameter is a consumed argument of
    /// the boxed family: the public type is the nullable hand written wrapper,
    /// the call is handed a <c>BoxedCopy</c> — which is
    /// <c>g_date_time_ref</c> — and the wrapper is disposed when the member
    /// returns.
    /// </summary>
    [Fact]
    public void ATransferredDateTimeParameterIsConsumedAsABoxedValue()
    {
        Assert.Equal(
            """
            public void TakeTime(Gst.GLib.DateTime? time)
            {
                nint instanceHandle = Handle;
                nint timeNative = time is null ? 0 : time.Handle;
                nuint timeType = time is null ? 0 : time.BoxedType.Value;
                nint timeOwned = time is null ? 0 : Gst.Interop.GObjectNative.BoxedCopy(timeType, timeNative);
                GstStampTakeTime(instanceHandle, timeOwned);
                System.GC.KeepAlive(this);
                time?.Dispose();
            }
            """,
            Run.Member("Stamp.cs", "public void TakeTime("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A borrowed <c>GDateTime</c> in parameter is a plain handle: a null
    /// guard, the handle in the call and a <c>GC.KeepAlive</c> after it.
    /// Nothing is minted and nothing is disposed.
    /// </summary>
    [Fact]
    public void ABorrowedDateTimeParameterIsPassedAsTheHandleOfTheRuntimeWrapper()
    {
        Assert.Equal(
            """
            public void SetTime(Gst.GLib.DateTime time)
            {
                ArgumentNullException.ThrowIfNull(time);
                GstStampSetTime(Handle, time.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(time);
            }
            """,
            Run.Member("Stamp.cs", "public void SetTime("),
            StringComparer.Ordinal);

        // The import declares the handle as a pointer, not as anything the GLib
        // module would have to emit.
        Assert.Contains(
            "private static partial void GstStampSetTime(nint stamp, nint time);",
            Run.File("Stamp.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred <c>GDateTime</c> return is adopted through the typed
    /// <c>FromNative</c> of the runtime wrapper, which maps the null pointer
    /// onto <see langword="null"/>.
    /// </summary>
    [Fact]
    public void ATransferredDateTimeReturnIsAdoptedThroughFromNative()
    {
        Assert.Equal(
            """
            public Gst.GLib.DateTime? GetTime()
            {
                nint nativeResult = GstStampGetTime(Handle);
                System.GC.KeepAlive(this);
                return Gst.GLib.DateTime.FromNative(nativeResult, Gst.Interop.Transfer.Full);
            }
            """,
            Run.Member("Stamp.cs", "public Gst.GLib.DateTime? GetTime("),
            StringComparer.Ordinal);

        Assert.Equal(0, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// Naming the type emits nothing of the GLib namespace: the declaration
    /// stays the hand written <c>Gst.GLib.DateTime</c> of the runtime.
    /// </summary>
    [Fact]
    public void TheDateTimeDeclarationItselfIsNotEmitted()
    {
        Assert.False(Run.HasFile("DateTime.cs"));
    }
}
