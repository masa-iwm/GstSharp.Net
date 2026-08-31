using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GLib.MainContext</c> row of <c>MarshalPlanner.RuntimeTypes</c>: the
/// one entry that is borrowed only, because the wrapper of the runtime carries
/// its <c>Handle</c> and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through one callable,
/// <c>gst_transcoder_get_signal_adapter</c>, whose <c>GMainContext</c> is a
/// nullable borrowed argument of a call the binding makes. The fixtures here
/// are the definition of the feature: they name that one supported position
/// and the five the row refuses, and pin the emitted text of the one and the
/// refusal of the other five.
/// </para>
/// <para>
/// The five split by mechanism. Three of them belong to a callable this code
/// calls - a transferred in parameter, an <c>out</c> parameter and a
/// transferred return - and are refused as <c>UnsupportedSignature</c>, so the
/// member is skipped and counted. The other two are inbound, that is a value a
/// trampoline is handed: a parameter of a signal and a parameter of a
/// callback. Those are refused without a reason of their own, because the
/// planner has none to file there: the signal is skipped by the signal plan
/// under its own <c>UnsupportedSignature</c>, and a callback takes every member
/// that would hand it over with it. Their fixtures therefore assert on the
/// emitted text rather than on the census.
/// </para>
/// <para>
/// Refusing them is what keeps the entry honest. A handle the binding is handed
/// - returned, written to an <c>out</c> parameter, or received by a trampoline
/// - is adopted through the typed <c>FromNative</c> of the wrapper class, and a
/// transferred one is minted with <c>BoxedCopy</c> off its <c>BoxedType</c>;
/// <c>Gst.GLib.DateTime</c> carries both and <c>Gst.GLib.MainContext</c>
/// carries neither, so a gir that reached one of those positions would emit
/// text naming a member that does not exist, which is a build failure of the
/// shipped tree rather than an entry in <c>girs/skip-report.md</c>. Should such
/// a gir ever arrive, the answer is to give the wrapper the members the flavour
/// assumes and to drop the <c>BorrowedOnly</c> flag, not to widen the plan.
/// </para>
/// </remarks>
public sealed class GLibMainContextRuntimeTypeTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixtures refer to. It is
    /// a stand in for the vendored <c>GLib-2.0.gir</c>, whose
    /// <c>GMainContext</c> carries the same opacity and the same boxed
    /// registration.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="MainContext" c:type="GMainContext" opaque="1" glib:type-name="GMainContext" glib:get-type="g_main_context_get_type" c:symbol-prefix="main_context">
            </record>
          </namespace>
        """;

    /// <summary>
    /// A class whose four members are the four positions under test: a borrowed
    /// nullable <c>GMainContext</c> in, a transferred one in, one written to an
    /// <c>out</c> parameter, and a transferred one returned.
    /// </summary>
    private const string Body =
        """
            <class name="Stamp" c:type="GstStamp" parent="GObject.Object" glib:type-name="GstStamp" glib:get-type="gst_stamp_get_type">
              <method name="set_context" c:identifier="gst_stamp_set_context">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="context" transfer-ownership="none" nullable="1">
                    <type name="GLib.MainContext" c:type="GMainContext*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_context" c:identifier="gst_stamp_take_context">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="context" transfer-ownership="full">
                    <type name="GLib.MainContext" c:type="GMainContext*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="peek_context" c:identifier="gst_stamp_peek_context">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                  <parameter name="context" direction="out" caller-allocates="0" transfer-ownership="none">
                    <type name="GLib.MainContext" c:type="GMainContext**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_context" c:identifier="gst_stamp_get_context">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GLib.MainContext" c:type="GMainContext*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="stamp" transfer-ownership="none">
                    <type name="Stamp" c:type="GstStamp*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </class>
        """;

    /// <summary>
    /// The two inbound positions: a class with a signal that carries a
    /// <c>GMainContext</c>, and a callback that carries one, handed over by a
    /// method of the same class. The <c>ping</c> method carries nothing and is
    /// bound, so the file of the class is emitted whatever happens to the other
    /// two and the assertions on its text are not vacuous.
    /// </summary>
    private const string InboundBody =
        """
            <callback name="ContextFunc" c:type="GstContextFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="context" transfer-ownership="none">
                  <type name="GLib.MainContext" c:type="GMainContext*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Beacon" c:type="GstBeacon" parent="GObject.Object" glib:type-name="GstBeacon" glib:get-type="gst_beacon_get_type">
              <method name="ping" c:identifier="gst_beacon_ping">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="beacon" transfer-ownership="none">
                    <type name="Beacon" c:type="GstBeacon*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="watch_context" c:identifier="gst_beacon_watch_context">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="beacon" transfer-ownership="none">
                    <type name="Beacon" c:type="GstBeacon*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="ContextFunc" c:type="GstContextFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="context-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="context" transfer-ownership="none">
                    <type name="GLib.MainContext" c:type="GMainContext*"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static readonly Lazy<FixtureRun> LazyInboundRun = new(
        static () => Fixture.Run(InboundBody, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static FixtureRun InboundRun => LazyInboundRun.Value;

    /// <summary>
    /// A borrowed nullable <c>GMainContext</c> is the handle of the runtime
    /// wrapper, with the null pointer for the absent one and a
    /// <c>GC.KeepAlive</c> after the call. Nothing is minted and nothing is
    /// disposed. This is the shape
    /// <c>Transcoder.GetSignalAdapter(Gst.GLib.MainContext?)</c> is emitted in.
    /// </summary>
    [Fact]
    public void ABorrowedMainContextParameterIsPassedAsTheHandleOfTheRuntimeWrapper()
    {
        Assert.Equal(
            """
            public void SetContext(Gst.GLib.MainContext? context)
            {
                GstStampSetContext(Handle, context is null ? 0 : context.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(context);
            }
            """,
            Run.Member("Stamp.cs", "public void SetContext("),
            StringComparer.Ordinal);

        // The import declares the handle as a pointer, not as anything the GLib
        // module would have to emit.
        Assert.Contains(
            "private static partial void GstStampSetContext(nint stamp, nint context);",
            Run.File("Stamp.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other three positions are refused, and the callables that reach them
    /// are reported as unsupported signatures rather than emitted.
    /// </summary>
    [Fact]
    public void EveryPositionButABorrowedArgumentIsRefused()
    {
        string emitted = Run.File("Stamp.cs");

        Assert.DoesNotContain("TakeContext", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("PeekContext", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("GetContext", emitted, StringComparison.Ordinal);

        Assert.Equal(3, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// A signal that hands a <c>GMainContext</c> to its handler is not bound.
    /// The trampoline would have to wrap the pointer it is given, and the only
    /// spelling the <c>Wrapper</c> flavour has for that is the typed
    /// <c>FromNative</c> the class does not carry.
    /// </summary>
    [Fact]
    public void ASignalArgumentOfTheBorrowedOnlyRowIsRefused()
    {
        string emitted = InboundRun.File("Beacon.cs");

        // The class itself is bound, so the absences below are the refusal and
        // not a missing file.
        Assert.Contains("public void Ping()", emitted, StringComparison.Ordinal);

        Assert.DoesNotContain("ContextChanged", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("public event", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("Trampoline", emitted, StringComparison.Ordinal);
        Assert.False(InboundRun.HasFile("SignalConnections.cs"));
    }

    /// <summary>
    /// A callback that hands a <c>GMainContext</c> to the handler is not bound
    /// either, and it takes the method that would hand it over with it: the
    /// delegate is the only surface the argument could be named on, so a method
    /// whose callback has none is not emitted at all.
    /// </summary>
    [Fact]
    public void ACallbackArgumentOfTheBorrowedOnlyRowIsRefusedWithItsConsumer()
    {
        string emitted = InboundRun.File("Beacon.cs");

        Assert.Contains("public void Ping()", emitted, StringComparison.Ordinal);

        Assert.DoesNotContain("ContextFunc", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("WatchContext", emitted, StringComparison.Ordinal);

        // The delegates of a module are emitted into one Callbacks.cs, which is
        // written only when the module has one to declare.
        Assert.False(InboundRun.HasFile("Callbacks.cs"));
    }

    /// <summary>
    /// Naming the type emits nothing of the GLib namespace: the declaration
    /// stays the hand written <c>Gst.GLib.MainContext</c> of the runtime.
    /// </summary>
    [Fact]
    public void TheMainContextDeclarationItselfIsNotEmitted()
    {
        Assert.False(Run.HasFile("MainContext.cs"));
    }
}
