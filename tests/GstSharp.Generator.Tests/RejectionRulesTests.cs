using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The rules that keep a signature out of the binding because emitting it
/// would corrupt memory, and the two rules that decide what a shadowed or an
/// action annotated declaration becomes.
/// </summary>
/// <remarks>
/// Every fixture here stands for a shape that the real girs carry and that the
/// generator used to emit: an out parameter of a record that is bound behind a
/// handle, a method that consumes the instance it is called on, and a signal
/// that GObject only carries so that a binding can call a method through it.
/// </remarks>
public sealed class RejectionRulesTests
{
    /// <summary>
    /// A namespace whose members are the rejected shapes, next to the ones they
    /// must not take with them: a caller allocated plain struct still binds,
    /// and the free function of a wrapper that owns nothing still binds.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Rect" c:type="GstRect">
              <field name="x" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="y" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Info" c:type="GstInfo" glib:type-name="GstInfo" glib:get-type="gst_info_get_type">
              <field name="stride" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="owner" writable="1">
                <type name="Widget" c:type="GstWidget*"/>
              </field>
            </record>
            <record name="Caps" c:type="GstCaps" glib:type-name="GstCaps" glib:get-type="gst_caps_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
              <method name="make_writable" c:identifier="gst_caps_make_writable">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="unref" c:identifier="gst_caps_unref">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="full">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="ref" c:identifier="gst_caps_ref">
                <return-value transfer-ownership="full">
                  <type name="Caps" c:type="GstCaps*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="is_empty" c:identifier="gst_caps_is_empty">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="caps" transfer-ownership="none">
                    <type name="Caps" c:type="GstCaps*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <record name="Poll" c:type="GstPoll" disguised="1" opaque="1">
              <method name="free" c:identifier="gst_poll_free">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="set" transfer-ownership="full">
                    <type name="Poll" c:type="GstPoll*"/>
                  </instance-parameter>
                </parameters>
              </method>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="get_info" c:identifier="gst_widget_get_info">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_rect" c:identifier="gst_widget_get_rect">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="rect" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Rect" c:type="GstRect*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_info" c:identifier="gst_widget_take_info">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" direction="out" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="free" c:identifier="gst_widget_free">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="info" transfer-ownership="none">
                    <type name="Info" c:type="GstInfo*"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="pull-sample" when="last" action="1">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
              </glib:signal>
              <glib:signal name="ready" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    [Fact]
    public void ACallerAllocatedHandleIsRejected()
    {
        // GstInfo carries a pointer field, so it is bound behind a handle. The
        // callee writes a whole GstInfo into the storage it is given, which a
        // pointer sized local cannot hold.
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain("gst_widget_get_info", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.CallerAllocates));
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0012" && diagnostic.Message.Contains(
                "gst_widget_get_info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ACallerAllocatedPlainStructIsStillBound()
    {
        // A plain struct is spelled in C# with the size of the C type, so the
        // caller can provide the storage the callee writes into.
        Assert.Equal(
            """
            public void GetRect(out Gst.Rect rect)
            {
                Gst.Rect rectNative = default;
                GstWidgetGetRect(Handle, &rectNative);
                System.GC.KeepAlive(this);
                rect = rectNative;
            }
            """,
            Run.Member("Widget.cs", "public void GetRect"));
    }

    [Fact]
    public void AnOutHandleThatTheCalleeAllocatesIsStillBound()
    {
        // Without caller-allocates the callee hands a pointer back, which is
        // exactly what a handle holds.
        Assert.Contains(
            "public void TakeInfo(out Gst.Info? info)",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodThatConsumesItsInstanceAndReplacesItIsRejected()
    {
        string source = Run.File("Caps.cs");

        Assert.DoesNotContain("MakeWritable", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.InstanceTransferFull));
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0013" && diagnostic.Message.Contains(
                "gst_caps_make_writable",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ReferenceCountingIsNotExposedOnAWrapperThatOwnsItsInstance()
    {
        // The wrapper of a mini object releases its reference when it is
        // disposed, so a second release path can only corrupt the count. The
        // gir annotates gst_caps_unref with a consumed instance and
        // gst_caps_ref with a borrowed one, so neither annotation alone finds
        // the pair.
        string source = Run.File("Caps.cs");

        Assert.DoesNotContain("public void Unref()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public Gst.Caps Ref()", source, StringComparison.Ordinal);
        Assert.Contains("public bool IsEmpty()", source, StringComparison.Ordinal);
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.LifetimePrimitive));
    }

    [Fact]
    public void AWrapperThatOwnsNothingKeepsItsFreeFunction()
    {
        // The wrapper of an opaque record is a bare pointer holder and is never
        // disposed, so gst_poll_free is the only way of releasing a GstPoll.
        Assert.Contains("public void Free()", Run.File("Poll.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AFreeFunctionThatReleasesItsArgumentIsNotALifetimePrimitive()
    {
        // gst_widget_free is spelled like a lifetime primitive and is not one:
        // it releases the GstInfo it is handed, not the widget it is called on.
        // A lifetime primitive takes nothing besides its instance.
        Assert.Contains("public void Free(Gst.Info info)", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionSignalDoesNotBecomeAnEvent()
    {
        // An action signal is a call API that GObject exposes through the
        // signal machinery. Nothing ever raises it, so subscribing to it is a
        // handler that is never called.
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain("PullSample", source, StringComparison.Ordinal);
        Assert.Contains("public event System.EventHandler Ready", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.ActionSignal));
    }

    [Fact]
    public void APrivateStateShellIsNotEmitted()
    {
        FixtureRun run = Fixture.Run(
            """
                <record name="WidgetPrivate" c:type="GstWidgetPrivate" disguised="1" opaque="1"/>
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                </class>
            """);

        Assert.False(run.HasFile("WidgetPrivate.cs"));
        Assert.True(run.HasFile("Widget.cs"));
    }

    [Fact]
    public void AShadowedCallableIsEmittedWhenTheShadowingOneCannotBeBound()
    {
        // gst_adapter_copy is the real pair: it is shadowed by
        // gst_adapter_copy_bytes, which returns a GBytes that this milestone
        // cannot marshal. Skipping both would leave the function unbound, so
        // the shadowed declaration takes the clean name. The fixture rejects
        // the shadowing one for the other documented reason, a handle
        // parameter whose ownership the call takes over.
        FixtureRun run = Fixture.Run(
            """
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
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" shadowed-by="copy_caps">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_caps" c:identifier="gst_widget_copy_caps" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
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
                </class>
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("public int Copy()", source, StringComparison.Ordinal);
        Assert.Contains("EntryPoint = \"gst_widget_copy\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy_caps\"", source, StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
    }

    [Fact]
    public void AShadowedCallableStaysOutWhenItIsNotIntrospectable()
    {
        // The fallback only lifts the shadowing rule. Every other reason still
        // applies, which is what keeps the declarations of the real girs out:
        // all three of their shadowed callables are introspectable="0".
        FixtureRun run = Fixture.Run(
            """
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
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" introspectable="0" shadowed-by="copy_caps">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_caps" c:identifier="gst_widget_copy_caps" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
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
                </class>
            """);

        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy\"", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.NotIntrospectable));
    }

    [Fact]
    public void AShadowedCallableStaysOutWhenTheShadowingOneBinds()
    {
        FixtureRun run = Fixture.Run(
            """
                <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
                  <method name="copy" c:identifier="gst_widget_copy" shadowed-by="copy_full">
                    <return-value transfer-ownership="none">
                      <type name="gint" c:type="gint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <method name="copy_full" c:identifier="gst_widget_copy_full" shadows="copy">
                    <return-value transfer-ownership="none">
                      <type name="guint" c:type="guint"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="widget" transfer-ownership="none">
                        <type name="Widget" c:type="GstWidget*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                </class>
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("public uint Copy()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EntryPoint = \"gst_widget_copy\"", source, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
    }

    [Fact]
    public void ADirectionOverrideDoesNotUnlockARecordBoundBehindAHandle()
    {
        // GstInfo carries a pointer field, so it is bound behind a handle: one
        // pointer wide in C# and a whole structure in C. Letting a correction
        // turn it into an out parameter would hand the callee the address of a
        // pointer sized local to write a structure into, which is the fault the
        // caller-allocates rule exists for. The correction stops at a plain
        // struct, so the member keeps the projection it had.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_free#info": { "direction": "out" } }
            }
            """);

        Assert.Contains("public void Free(Gst.Info info)", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_free#info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AFixedArraySizeDoesNotUnlockACallerAllocatedHandle()
    {
        // The other half of the same boundary: a caller allocated out parameter
        // of a record that is bound behind a handle stays rejected, whatever
        // the overlays say about its size. Only a blittable value has storage
        // the caller can provide, so gst_video_frame_map and its relatives stay
        // in the CallerAllocates bucket.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_info#info": { "fixedArraySize": 4 } }
            }
            """);

        Assert.DoesNotContain("gst_widget_get_info", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.CallerAllocates));
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_get_info#info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AReturnTypeOverrideNarrowsAFactoryOntoItsDeclaringType()
    {
        FixtureRun run = RunFactoryWithOverlay(
            """
            {
              "returnTypeOverrides": { "gst_widget_new": "Gst.Widget" }
            }
            """);

        Assert.Contains(
            "public static Gst.Widget New()",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "return Gst.GObject.Object.FromNative<Gst.Widget>(nativeResult, Gst.Interop.Transfer.Full)",
            run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnTypeOverrideThatNamesAnotherTypeIsIgnored()
    {
        FixtureRun run = RunFactoryWithOverlay(
            """
            {
              "returnTypeOverrides": { "gst_widget_new": "Gst.Thing" }
            }
            """);

        Assert.Contains("public static Gst.Object New()", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0015");
    }

    /// <summary>Runs the fixture of this class with a hand written <c>fixups.json</c>.</summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string fixups) => RunWithOverlay(Body, fixups);

    /// <summary>
    /// Runs a factory fixture whose gir return type is the base class, which is
    /// the shape <c>gst_pipeline_new</c> has.
    /// </summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunFactoryWithOverlay(string fixups) => RunWithOverlay(
        """
            <class name="Object" c:type="GstObject" parent="GObject.InitiallyUnowned" glib:type-name="GstObject" glib:get-type="gst_object_get_type">
            </class>
            <class name="Widget" c:type="GstWidget" parent="Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <constructor name="new" c:identifier="gst_widget_new">
                <return-value transfer-ownership="full">
                  <type name="Object" c:type="GstObject*"/>
                </return-value>
              </constructor>
            </class>
        """,
        fixups);

    /// <summary>Runs one gir namespace with a hand written <c>fixups.json</c>.</summary>
    /// <param name="body">The members of the <c>Gst</c> namespace.</param>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string body, string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
