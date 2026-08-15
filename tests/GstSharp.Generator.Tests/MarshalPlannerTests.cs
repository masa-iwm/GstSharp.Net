using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// One fixture per marshalling rule of <c>MarshalPlanner</c>, checked against
/// the code that comes out of the emitters.
/// </summary>
public sealed class MarshalPlannerTests
{
    /// <summary>
    /// A namespace that exercises every projection the planner knows: strings
    /// in both directions, an enumeration, a handle, an out parameter, a span,
    /// a callback with user data and a destroy notification, a callable that
    /// throws, a property built from its accessors, and the two shapes that are
    /// rejected on purpose.
    /// </summary>
    private const string Body =
        """
            <enumeration name="State" c:type="GstState">
              <member name="null" value="0" c:identifier="GST_STATE_NULL"/>
              <member name="playing" value="1" c:identifier="GST_STATE_PLAYING"/>
            </enumeration>
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <callback name="WidgetFunc" c:type="GstWidgetFunc">
              <return-value transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </return-value>
              <parameters>
                <parameter name="widget" transfer-ownership="none">
                  <type name="Widget" c:type="GstWidget*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <interface name="Sizer" c:type="GstSizer" glib:type-name="GstSizer" glib:get-type="gst_sizer_get_type">
              <method name="get_size" c:identifier="gst_sizer_get_size">
                <return-value transfer-ownership="none">
                  <type name="gint" c:type="gint"/>
                </return-value>
                <parameters>
                  <instance-parameter name="sizer" transfer-ownership="none">
                    <type name="Sizer" c:type="GstSizer*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </interface>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <implements name="Sizer"/>
              <constructor name="new" c:identifier="gst_widget_new">
                <return-value transfer-ownership="floating">
                  <type name="Widget" c:type="GstWidget*"/>
                </return-value>
                <parameters>
                  <parameter name="name" transfer-ownership="none" nullable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </constructor>
              <method name="is_named" c:identifier="gst_widget_is_named">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_name" c:identifier="gst_widget_get_name" glib:get-property="name">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_name" c:identifier="gst_widget_set_name" glib:set-property="name">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="name" transfer-ownership="none" nullable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_state" c:identifier="gst_widget_set_state">
                <return-value transfer-ownership="none">
                  <type name="State" c:type="GstState"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="state" transfer-ownership="none">
                    <type name="State" c:type="GstState"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_extents" c:identifier="gst_widget_get_extents">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="width" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="gint" c:type="gint*"/>
                  </parameter>
                  <parameter name="caps" direction="out" caller-allocates="0" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="write" c:identifier="gst_widget_write" throws="1">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="data" transfer-ownership="none">
                    <array length="1" zero-terminated="0" c:type="const guint8*">
                      <type name="guint8" c:type="guint8"/>
                    </array>
                  </parameter>
                  <parameter name="size" transfer-ownership="none">
                    <type name="gsize" c:type="gsize"/>
                  </parameter>
                </parameters>
              </method>
              <method name="watch" c:identifier="gst_widget_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="notified" closure="1" destroy="2">
                    <type name="WidgetFunc" c:type="GstWidgetFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                  <parameter name="notify" transfer-ownership="none" scope="async">
                    <type name="GLib.DestroyNotify" c:type="GDestroyNotify"/>
                  </parameter>
                </parameters>
              </method>
              <method name="visit" c:identifier="gst_widget_visit">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="WidgetFunc" c:type="GstWidgetFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_caps" c:identifier="gst_widget_take_caps">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="list_children" c:identifier="gst_widget_list_children">
                <return-value transfer-ownership="full">
                  <type name="GLib.List" c:type="GList*">
                    <type name="Widget"/>
                  </type>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <property name="name" writable="1" transfer-ownership="none" getter="get_name" setter="set_name">
                <type name="utf8" c:type="gchar*"/>
              </property>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    [Fact]
    public void AStringParameterIsEncodedOnTheStack()
    {
        Assert.Equal(
            """
            public bool IsNamed(string name)
            {
                ArgumentNullException.ThrowIfNull(name);
                System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                int nativeResult = GstWidgetIsNamed(Handle, nameScope.Pointer);
                System.GC.KeepAlive(this);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool IsNamed("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ATransferredStringReturnIsReleased()
    {
        Assert.Equal(
            """
            public string? GetName()
            {
                nint nativeResult = GstWidgetGetName(Handle);
                System.GC.KeepAlive(this);
                return Gst.Interop.GMarshal.PtrToStringUtf8AndFree(nativeResult);
            }
            """,
            Run.Member("Widget.cs", "public string? GetName("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnEnumerationCrossesAsItsUnderlyingType()
    {
        Assert.Equal(
            """
            public Gst.State SetState(Gst.State state)
            {
                int nativeResult = GstWidgetSetState(Handle, (int)state);
                System.GC.KeepAlive(this);
                return (Gst.State)nativeResult;
            }
            """,
            Run.Member("Widget.cs", "public Gst.State SetState("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void OutParametersUseLocalsAndAreNullableWhenTheyAreHandles()
    {
        Assert.Equal(
            """
            public bool GetExtents(out int width, out Gst.Caps? caps)
            {
                int widthNative = default;
                nint capsNative = default;
                int nativeResult = GstWidgetGetExtents(Handle, &widthNative, &capsNative);
                System.GC.KeepAlive(this);
                width = widthNative;
                caps = Gst.Caps.FromNative(capsNative, Gst.Interop.Transfer.Full);
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool GetExtents("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnArrayWithALengthBecomesASpanAndTheLengthDisappears()
    {
        Assert.Equal(
            """
            public bool Write(System.ReadOnlySpan<byte> data)
            {
                nint errorNative = 0;
                fixed (byte* dataPointer = data)
                {
                    int nativeResult = GstWidgetWrite(Handle, dataPointer, (nuint)data.Length, &errorNative);
                    System.GC.KeepAlive(this);
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
                    return nativeResult != 0;
                }
            }
            """,
            Run.Member("Widget.cs", "public bool Write("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ANotifiedCallbackHandsItsStateToTheDestroyNotification()
    {
        Assert.Equal(
            """
            public void Watch(Gst.WidgetFunc func)
            {
                ArgumentNullException.ThrowIfNull(func);
                Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
                GstWidgetWatch(Handle, Gst.WidgetFuncTrampoline.Pointer, funcState.UserData, (nint)Gst.Interop.CallbackHandle.DestroyNotify);
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Widget.cs", "public void Watch("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACallScopedCallbackIsReleasedWhenTheCallReturns()
    {
        Assert.Equal(
            """
            public void Visit(Gst.WidgetFunc func)
            {
                ArgumentNullException.ThrowIfNull(func);
                Gst.Interop.CallbackHandle funcState = Gst.Interop.CallbackHandle.Alloc(func);
                try
                {
                    GstWidgetVisit(Handle, Gst.WidgetFuncTrampoline.Pointer, funcState.UserData);
                    System.GC.KeepAlive(this);
                }
                finally
                {
                    funcState.Free();
                }
            }
            """,
            Run.Member("Widget.cs", "public void Visit("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AConstructorAdoptsWhatItReturns()
    {
        Assert.Equal(
            """
            public static Gst.Widget New(string? name)
            {
                System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                nint nativeResult = GstWidgetNew(nameScope.Pointer);
                return Gst.GObject.Object.FromNative<Gst.Widget>(nativeResult, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_widget_new returned no value.");
            }
            """,
            Run.Member("Widget.cs", "public static Gst.Widget New("),
            StringComparer.Ordinal);
    }

    [Fact]
    public void APropertyDelegatesToTheAccessorsTheGirNames()
    {
        Assert.Equal(
            """
            public string? Name
            {
                get => GetName();
                set => SetName(value);
            }
            """,
            Run.Member("Widget.cs", "public string? Name"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnInterfaceMethodBecomesAnExtensionMethod()
    {
        string source = Run.File("ISizer.cs");

        Assert.Contains("public interface ISizer\n", source, StringComparison.Ordinal);
        Assert.Contains("nint Handle { get; }", source, StringComparison.Ordinal);
        Assert.Contains("public static unsafe partial class SizerExtensions\n", source, StringComparison.Ordinal);
        Assert.Equal(
            """
            public static int GetSize(this Gst.ISizer sizer)
            {
                ArgumentNullException.ThrowIfNull(sizer);
                int nativeResult = GstSizerGetSize(sizer.Handle);
                System.GC.KeepAlive(sizer);
                return nativeResult;
            }
            """,
            Run.Member("ISizer.cs", "public static int GetSize("),
            StringComparer.Ordinal);

        // The class that the gir says implements the interface declares it.
        Assert.Contains(
            "public unsafe partial class Widget : Gst.GObject.InitiallyUnowned, Gst.ISizer\n",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACallbackGetsADelegateAndATrampoline()
    {
        string source = Run.File("Callbacks.cs");

        // A handle a callback receives is nullable: native code passes NULL
        // where the gir promises an instance (gst_caps_foreach does).
        Assert.Contains("public delegate bool WidgetFunc(Gst.Widget? widget);", source, StringComparison.Ordinal);
        Assert.Contains("internal static unsafe class WidgetFuncTrampoline\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static nint Pointer => (nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&Invoke;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (Gst.Interop.CallbackHandle.GetState<Gst.WidgetFunc>(userData) is not { } callback)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Gst.Interop.ExceptionTrap.Report(exception);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void OwnershipTakingParametersAndContainersAreSkipped()
    {
        string source = Run.File("Widget.cs");

        // gst_widget_take_caps would hand the only reference of the wrapper to
        // native code, and gst_widget_list_children returns a GList.
        Assert.DoesNotContain("TakeCaps", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ListChildren", source, StringComparison.Ordinal);
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    [Fact]
    public void TheModuleRegistersEveryWrapperWithATypeFunction()
    {
        string source = Run.File("_Module.cs");

        Assert.Contains("internal static unsafe partial class GstModule\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static Gst.Interop.ModuleTypeEntry[] CreateEntries() =>",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Caps.GetGType, &Gst.Caps.CreateWrapper),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Widget.GetGType, &Gst.Widget.CreateWrapper),",
            source,
            StringComparison.Ordinal);

        // An interface has no instances of its own, so it is not registered.
        Assert.DoesNotContain("Gst.ISizer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbstractClassKeepsAConcreteWrapperForTheRegistry()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Base" c:type="GstBase" parent="GObject.Object" abstract="1" glib:type-name="GstBase" glib:get-type="gst_base_get_type">
                </class>
            """);

        string source = run.File("Base.cs");

        Assert.Contains("public abstract unsafe partial class Base : Gst.GObject.Object\n", source, StringComparison.Ordinal);
        Assert.Contains("private sealed class Concrete : Base\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "internal static object CreateWrapper(nint handle, Gst.Interop.Transfer transfer) => new Concrete(handle, transfer);",
            source,
            StringComparison.Ordinal);
    }
}
