using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What a <c>&lt;glib:signal&gt;</c> is emitted as: the arguments class, the
/// event accessors, the trampoline GObject invokes, and the ownership rules
/// that keep an emission free of leaks.
/// </summary>
public sealed class SignalEmitterTests
{
    /// <summary>
    /// A namespace whose one class carries a signal of every shape that this
    /// milestone binds: no argument, a <c>GObject</c> argument, a string
    /// argument, a mini object argument and a value the handler returns.
    /// </summary>
    private const string SignalFixture =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Message" c:type="GstMessage" glib:type-name="GstMessage" glib:get-type="gst_message_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <class name="Pad" c:type="GstPad" parent="GObject.Object" glib:type-name="GstPad" glib:get-type="gst_pad_get_type">
            </class>
            <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
              <glib:signal name="no-more-pads" when="last">
                <doc xml:space="preserve">the element has no more pads to add</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
              <glib:signal name="pad-added" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="new_pad" transfer-ownership="none">
                    <doc xml:space="preserve">the pad that has been added</doc>
                    <type name="Pad" c:type="GstPad*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="renamed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="new_name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="posted" when="last" detailed="1">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="message" transfer-ownership="none">
                    <type name="Message" c:type="GstMessage*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="do-latency" when="last">
                <return-value transfer-ownership="none">
                  <doc xml:space="preserve">whether the latency was recalculated</doc>
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A boxed argument of a signal: a record GObject registers a type for and
    /// that carries no mini object header is projected onto a boxed wrapper,
    /// which is the other half of what a <c>borrow</c> is legal on.
    /// </summary>
    private const string BoxedSignalFixture =
        """
            <record name="Structure" c:type="GstStructure" glib:type-name="GstStructure" glib:get-type="gst_structure_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
              <glib:signal name="structured" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="structure" transfer-ownership="none">
                    <type name="Structure" c:type="GstStructure*"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A mini object on every key that is not a signal argument: the parameter
    /// and the return of a method, and the argument of a virtual method whose
    /// key differs from the signal key of the same concept by an underscore.
    /// </summary>
    private const string SlotFixture =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Buffer" c:type="GstBuffer" glib:type-name="GstBuffer" glib:get-type="gst_buffer_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <method name="wrap" c:identifier="gst_widget_wrap">
                <return-value transfer-ownership="none">
                  <type name="Buffer" c:type="GstBuffer*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="buf" transfer-ownership="none">
                    <type name="Buffer" c:type="GstBuffer*"/>
                  </parameter>
                </parameters>
              </method>
              <virtual-method name="prepare">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="buf" transfer-ownership="none">
                    <type name="Buffer" c:type="GstBuffer*"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="prepare">
                <callback name="prepare">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="buf" transfer-ownership="none">
                      <type name="Buffer" c:type="GstBuffer*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The same mini object beside a callback that a method hands over, which
    /// is the other inbound path a <c>borrow</c> entry can land on and the one
    /// that does not honour it.
    /// </summary>
    private const string CallbackFixture =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
              <field name="refcount" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <record name="Message" c:type="GstMessage" glib:type-name="GstMessage" glib:get-type="gst_message_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <callback name="PostedFunc" c:type="GstPostedFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="message" transfer-ownership="none">
                  <type name="Message" c:type="GstMessage*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
              <method name="watch" c:identifier="gst_element_watch">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="element" transfer-ownership="none">
                    <type name="Element" c:type="GstElement*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="PostedFunc" c:type="GstPostedFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Fact]
    public void ASignalWithoutArgumentsBecomesAPlainEventHandler()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        Assert.Contains("public event System.EventHandler NoMorePads\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain("class NoMorePadsSignalArgs", source, StringComparison.Ordinal);
        Assert.Equal(
            """
            private static void NoMorePadsTrampoline(nint instance, nint userData)
            {
                try
                {
                    if (Gst.Interop.CallbackHandle.GetState<System.EventHandler>(userData) is not { } handler)
                    {
                        return;
                    }

                    handler(
                        Gst.GObject.Object.FromNative(instance, Gst.Interop.Transfer.None),
                        System.EventArgs.Empty);
                }
                catch (Exception exception)
                {
                    Gst.Interop.ExceptionTrap.Report(exception);
                }
            }
            """,
            run.Member("Element.cs", "private static void NoMorePadsTrampoline"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AGObjectArgumentIsTheInternedWrapperAndIsNotDisposed()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        Assert.Contains("public sealed class PadAddedSignalArgs : System.EventArgs\n", source, StringComparison.Ordinal);
        Assert.Contains("internal PadAddedSignalArgs(Gst.Pad newPad)", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.Pad NewPad { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "public event System.EventHandler<Gst.Element.PadAddedSignalArgs> PadAdded\n",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            """
            public event System.EventHandler<Gst.Element.PadAddedSignalArgs> PadAdded
            {
                add => Gst.SignalConnections.Add(this, "pad-added", (nint)(delegate* unmanaged[Cdecl]<nint, nint, nint, void>)&PadAddedTrampoline, value);
                remove => Gst.SignalConnections.Remove(this, "pad-added", value);
            }
            """,
            run.Member("Element.cs", "public event System.EventHandler<Gst.Element.PadAddedSignalArgs> PadAdded"),
            StringComparer.Ordinal);

        // The wrapper of a GObject is interned and owned by nobody but itself,
        // so it is neither scoped nor disposed.
        string trampoline = run.Member("Element.cs", "private static void PadAddedTrampoline");
        Assert.Contains(
            "            Gst.Pad newPadValue = Gst.GObject.Object.FromNative<Gst.Pad>(newPad, Gst.Interop.Transfer.None)\n"
            + "                ?? throw new InvalidOperationException(\"The pad-added signal of GstElement passed no new_pad.\");",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("using ", trampoline, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispose", trampoline, StringComparison.Ordinal);
    }

    [Fact]
    public void AStringArgumentIsReadWithoutBeingFreed()
    {
        FixtureRun run = Fixture.Run(SignalFixture);

        Assert.Contains(
            "public string NewName { get; }",
            run.File("Element.cs"),
            StringComparison.Ordinal);

        string trampoline = run.Member("Element.cs", "private static void RenamedTrampoline");
        Assert.Contains("private static void RenamedTrampoline(nint instance, byte* newName, nint userData)", trampoline, StringComparison.Ordinal);
        Assert.Contains(
            "string newNameValue = Gst.Interop.GMarshal.PtrToStringUtf8((nint)newName)",
            trampoline,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AndFree", trampoline, StringComparison.Ordinal);
    }

    [Fact]
    public void AMiniObjectArgumentIsScopedSoItsReferenceIsReleased()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        // The wrapper takes a reference of its own, which the trampoline gives
        // back as soon as the handler returned; the property says as much, and
        // it says it in terms managed code can act on. Taking a reference is
        // not one of them — no wrapper exposes the reference count — so the
        // remark names reading and copying instead.
        Assert.Contains(
            "/// The value is only valid while the handler runs: the wrapper is disposed\n"
            + "        /// once it returns. Read out of it what is needed, or copy it where the\n"
            + "        /// type offers a copy.",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("take a reference of your own", source, StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.Message messageValue = Gst.Message.FromNative(message, Gst.Interop.Transfer.None)\n"
            + "                ?? throw new InvalidOperationException(\"The posted signal of GstElement passed no message.\");",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADetailedSignalIsConnectedWithoutItsDetail()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        Assert.Contains(
            "/// The signal is detailed. The handler is connected to <c>posted</c>",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Gst.SignalConnections.Add(this, \"posted\", ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"posted::", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every event says which instance a handler has to be removed from. The
    /// handlers are held in a table that is keyed by the wrapper, so add and
    /// remove are only a pair when both see the same one, and the interning of
    /// the wrappers that makes the usual code work is an implementation detail
    /// rather than a promise of the surface.
    /// </summary>
    [Fact]
    public void EveryEventSaysWhichInstanceAHandlerIsRemovedFrom()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        const string Remark =
            "    /// <remarks>\n"
            + "    /// The handler is remembered on the wrapper it was added to and has to be\n"
            + "    /// removed from that same instance. Looking the object up again normally\n"
            + "    /// hands the same wrapper out, but one that was disposed in between is\n"
            + "    /// replaced by a new one, which knows nothing of the handler.\n"
            + "    /// </remarks>\n";

        // Once per event, whatever the shape of the signal: no argument, an
        // argument, a return value, a detail.
        Assert.Equal(
            source.Split("public event ").Length - 1,
            source.Split(Remark).Length - 1);

        Assert.Contains(Remark + "    public event System.EventHandler NoMorePads\n", source, StringComparison.Ordinal);
        Assert.Contains(
            Remark + "    public event Gst.Element.DoLatencyHandler DoLatency\n",
            source,
            StringComparison.Ordinal);

        // The signal of an interface is reached through extension methods
        // rather than an event, and the table behind them is keyed the same
        // way, so both halves of the pair carry the same remark.
        string childProxy = Source("IChildProxy.cs");

        Assert.Contains(
            Remark
            + "    public static void AddChildAddedHandler(this Gst.IChildProxy self, ",
            childProxy,
            StringComparison.Ordinal);
        Assert.Contains(
            Remark
            + "    public static void RemoveChildAddedHandler(this Gst.IChildProxy self, ",
            childProxy,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalThatReturnsAValueGetsADelegateOfItsOwn()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        Assert.Contains(
            "public delegate bool DoLatencyHandler(object? sender, System.EventArgs args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("public event Gst.Element.DoLatencyHandler DoLatency\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "add => Gst.SignalConnections.Add(this, \"do-latency\", "
            + "(nint)(delegate* unmanaged[Cdecl]<nint, nint, int>)&DoLatencyTrampoline, value);",
            source,
            StringComparison.Ordinal);

        Assert.Equal(
            """
            private static int DoLatencyTrampoline(nint instance, nint userData)
            {
                try
                {
                    if (Gst.Interop.CallbackHandle.GetState<Gst.Element.DoLatencyHandler>(userData) is not { } handler)
                    {
                        return default;
                    }

                    bool result = handler(
                        Gst.GObject.Object.FromNative(instance, Gst.Interop.Transfer.None),
                        System.EventArgs.Empty);
                    return result ? 1 : 0;
                }
                catch (Exception exception)
                {
                    Gst.Interop.ExceptionTrap.Report(exception);
                    return default;
                }
            }
            """,
            run.Member("Element.cs", "private static int DoLatencyTrampoline"),
            StringComparer.Ordinal);
    }

    [Fact]
    public void EveryTrampolineIsAnUnmanagedCallersOnlyCdeclMethod()
    {
        FixtureRun run = Fixture.Run(SignalFixture);
        string source = run.File("Element.cs");

        int trampolines = source.Split("Trampoline(nint instance").Length - 1;
        Assert.Equal(5, trampolines);
        Assert.Equal(
            5,
            source.Split(
                "[UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]")
                .Length - 1);
    }

    [Fact]
    public void TheHolderOfTheConnectedHandlersIsOnlyEmittedForAModuleWithSignals()
    {
        Assert.True(Fixture.Run(SignalFixture).HasFile("SignalConnections.cs"));

        FixtureRun without = Fixture.Run(
            """
                <class name="Pad" c:type="GstPad" parent="GObject.Object" glib:type-name="GstPad" glib:get-type="gst_pad_get_type">
                </class>
            """);

        Assert.False(without.HasFile("SignalConnections.cs"));
    }

    [Fact]
    public void ASignalWhoseNameIsTakenIsSkippedWithADiagnostic()
    {
        // gst_element_no_more_pads is bound as Element.NoMorePads, so the event
        // of the signal of that name cannot be emitted too.
        FixtureRun run = Fixture.Run(
            """
                <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
                  <method name="no_more_pads" c:identifier="gst_element_no_more_pads">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="element" transfer-ownership="none">
                        <type name="Element" c:type="GstElement*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                  <glib:signal name="no-more-pads" when="last">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                  </glib:signal>
                </class>
            """);

        string source = run.File("Element.cs");
        Assert.Contains("public void NoMorePads()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("event", source, StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0011"
                && diagnostic.Message.Contains("'no-more-pads'", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEventThatHidesAnInheritedMemberSaysSo()
    {
        // Hiding a member of a base class without saying so is a compiler
        // warning, and warnings are errors in this repository.
        FixtureRun run = Fixture.Run(
            """
                <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
                  <method name="renamed" c:identifier="gst_element_renamed">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                    <parameters>
                      <instance-parameter name="element" transfer-ownership="none">
                        <type name="Element" c:type="GstElement*"/>
                      </instance-parameter>
                    </parameters>
                  </method>
                </class>
                <class name="Bin" c:type="GstBin" parent="Element" glib:type-name="GstBin" glib:get-type="gst_bin_get_type">
                  <glib:signal name="renamed" when="last">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                  </glib:signal>
                </class>
            """);

        Assert.Contains("public new event System.EventHandler Renamed\n", run.File("Bin.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalWhoseArgumentTransfersOwnershipIsNotEmitted()
    {
        // A handler that takes ownership of an argument would have to decide
        // whether to release it, and nothing in the gir says whether the
        // emitter of the signal still uses it.
        FixtureRun run = Fixture.Run(
            """
                <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
                  <field name="type" writable="1">
                    <type name="GType" c:type="GType"/>
                  </field>
                  <field name="refcount" writable="1">
                    <type name="gint" c:type="gint"/>
                  </field>
                </record>
                <record name="Message" c:type="GstMessage" glib:type-name="GstMessage" glib:get-type="gst_message_get_type">
                  <field name="mini_object" writable="1">
                    <type name="MiniObject" c:type="GstMiniObject"/>
                  </field>
                </record>
                <class name="Bus" c:type="GstBus" parent="GObject.Object" glib:type-name="GstBus" glib:get-type="gst_bus_get_type">
                  <glib:signal name="message" when="last">
                    <return-value transfer-ownership="none">
                      <type name="none" c:type="void"/>
                    </return-value>
                    <parameters>
                      <parameter name="message" transfer-ownership="full">
                        <type name="Message" c:type="GstMessage*"/>
                      </parameter>
                    </parameters>
                  </glib:signal>
                </class>
            """);

        Assert.DoesNotContain("event", run.File("Bus.cs"), StringComparison.Ordinal);
        Assert.False(run.HasFile("SignalConnections.cs"));
    }

    [Fact]
    public void TheEventsOfTheRealGirAreEmittedWhereTheGirDeclaresThem()
    {
        Assert.Contains(
            "public event System.EventHandler<Gst.Element.PadAddedSignalArgs> PadAdded\n",
            Source("Element.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "private static void PadAddedTrampoline(nint instance, nint newPad, nint userData)\n",
            Source("Element.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public event System.EventHandler<Gst.Bin.ElementAddedSignalArgs> ElementAdded\n",
            Source("Bin.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public event System.EventHandler<Gst.Registry.PluginAddedSignalArgs> PluginAdded\n",
            Source("Registry.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheMessageOfTheBusIsHandedOverForTheDurationOfTheHandler()
    {
        string source = Source("Bus.cs");

        Assert.Contains("public sealed class MessageSignalArgs : System.EventArgs\n", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.Message Message { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.Message messageValue = Gst.Message.FromNative(message, Gst.Interop.Transfer.None)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnOverlaySelectedArgumentIsBorrowedRatherThanReferenced()
    {
        // The projection an argument the C registered G_SIGNAL_TYPE_STATIC_SCOPE
        // and reads back takes: the object of the emitter itself, no reference,
        // no copy, which is the only shape that is writable in place. It is
        // written the way a virtual method trampoline writes it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::posted#message": { "borrow": true } }
            }
            """);

        string source = run.File("Element.cs");

        Assert.Contains(
            "using Gst.Message messageValue = (message == nint.Zero ? null : Gst.Message.Borrow(message))\n"
            + "                ?? throw new InvalidOperationException(\"The posted signal of GstElement passed no message.\");",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Gst.Message.FromNative(message, Gst.Interop.Transfer.None)",
            source,
            StringComparison.Ordinal);

        // The remark of the property says what the handler may do with it, and
        // it replaces the remark of an owning wrapper rather than joining it.
        Assert.Contains(
            "/// The emission lends this object for the length of the handler: the wrapper\n"
            + "        /// borrows it, holds no reference and no copy of its own, and is disposed\n"
            + "        /// once the handler returns, so it must not be stored. It is writable in\n"
            + "        /// place - what the handler writes is what the sender of the value reads\n"
            + "        /// back - unless that sender shares the object with somebody else, which\n"
            + "        /// leaves it writable for nobody. <c>MakeWritable()</c> throws",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/// The value is only valid while the handler runs",
            source,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0017" or "GEN0024" or "GEN0054");
    }

    [Fact]
    public void ABorrowOnAnArgumentThatIsNoWrapperIsRefused()
    {
        // A GObject argument is interned and reference counted, and no
        // generated GObject wrapper carries the Borrow factory a mini object
        // and a boxed value do: the entry describes output that cannot be
        // written, so it is an error rather than a correction.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::pad-added#new_pad": { "borrow": true } }
            }
            """,
            allowErrors: true);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Element::pad-added#new_pad", StringComparison.Ordinal)
                && diagnostic.Message.Contains("mini object or a boxed wrapper", StringComparison.Ordinal));

        Assert.Contains(
            "Gst.Pad newPadValue = Gst.GObject.Object.FromNative<Gst.Pad>(newPad, Gst.Interop.Transfer.None)",
            run.File("Element.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ABorrowOnAStringArgumentIsRefused()
    {
        // The other half of the same rule: an argument that is no handle at all
        // has nothing to borrow either.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::renamed#new_name": { "borrow": true } }
            }
            """,
            allowErrors: true);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Element::renamed#new_name", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowThatNamesNoArgumentIsReportedAsStale()
    {
        // The key is read where the argument is planned, so one that names no
        // argument of a rendered signal is never read and falls to the stale
        // report of the annotation overrides.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::posted#msg": { "borrow": true } }
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0024", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Element::posted#msg", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowOnACallbackParameterIsRefused()
    {
        // Only the signal path borrows. A callback argument is planned by the
        // same reader and has no borrowing projection, so the flag is refused
        // rather than silently consumed.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "GstPostedFunc#message": { "borrow": true } }
            }
            """,
            CallbackFixture,
            allowErrors: true);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("GstPostedFunc#message", StringComparison.Ordinal)
                && diagnostic.Message.Contains("only an argument of a signal", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowOnAVirtualMethodArgumentIsRefused()
    {
        // The likeliest way to write the entry wrong: the key of a slot and the
        // key of the signal of the same concept differ by an underscore and by
        // the type that owns them. Reading it plans the argument and consumes
        // the key, so nothing downstream would report it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "subclassable": ["Gst.Widget"],
              "annotationOverrides": { "Gst.Widget::prepare#buf": { "borrow": true } }
            }
            """,
            SlotFixture,
            allowErrors: true);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Widget::prepare#buf", StringComparison.Ordinal)
                && diagnostic.Message.Contains("only an argument of a signal", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowOnAMethodParameterIsRefused()
    {
        // A parameter of a callable travels the other way: the member hands the
        // value to the C, and there is no wrapper of the binding's making to
        // borrow with.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_wrap#buf": { "borrow": true } }
            }
            """,
            SlotFixture,
            allowErrors: true);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_wrap#buf", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowOnAReturnIsRefusedOnce()
    {
        // The return of a callable is read several times over - for its
        // transfer, for its nullability, for the discard flag - and the refusal
        // is reported once all the same.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_wrap#return": { "borrow": true } }
            }
            """,
            SlotFixture,
            allowErrors: true);

        Assert.Single(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0054", StringComparison.Ordinal)
                && diagnostic.Message.Contains("gst_widget_wrap#return", StringComparison.Ordinal));
    }

    [Fact]
    public void ABorrowOfFalseStatesTheDefaultAndChangesNothing()
    {
        // Read the way a nullable of false is: it states what the planner does
        // anyway, so it is accepted, silent, and the argument keeps the
        // reference holding wrapper of every other signal.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::posted#message": { "borrow": false } }
            }
            """);

        Assert.Contains(
            "using Gst.Message messageValue = Gst.Message.FromNative(message, Gst.Interop.Transfer.None)",
            run.File("Element.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0017" or "GEN0024" or "GEN0054");
    }

    [Fact]
    public void ABorrowedArgumentTheEmissionMayLeaveOutStaysNullable()
    {
        // The two flags of a signal argument key are read side by side: the
        // borrow decides what the wrapper holds, the nullable whether the
        // handler is handed none, and the borrow keeps the null of the
        // emission rather than throwing on it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "Gst.Element::posted#message": { "borrow": true, "nullable": true }
              }
            }
            """);

        Assert.Contains(
            "using Gst.Message? messageValue = (message == nint.Zero ? null : Gst.Message.Borrow(message));",
            run.File("Element.cs"),
            StringComparison.Ordinal);
        Assert.Contains("public Gst.Message? Message { get; }", run.File("Element.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0017" or "GEN0024" or "GEN0054");
    }

    [Fact]
    public void ABoxedArgumentIsBorrowedThroughTheFactoryOfItsWrapper()
    {
        // The other arm of the rule the legality predicate states: a boxed
        // wrapper carries the same borrowing factory a mini object one does,
        // so the projection is written the same way.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Element::structured#structure": { "borrow": true } }
            }
            """,
            BoxedSignalFixture);

        string source = run.File("Element.cs");

        Assert.Contains("public sealed unsafe partial class Structure : Gst.GObject.Boxed", run.File("Structure.cs"), StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.Structure structureValue = (structure == nint.Zero ? null : Gst.Structure.Borrow(structure))",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => diagnostic.Code is "GEN0017" or "GEN0024" or "GEN0054");
    }

    [Fact]
    public void ANotifyStyleSignalHandsOverTheParameterSpecification()
    {
        string source = Source("Object.cs");

        Assert.Contains("public Gst.GObject.ParamSpec Prop { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.GObject.ParamSpec propValue = Gst.GObject.ParamSpec.FromNative(prop, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 23)]
    [InlineData("GstBase", 2)]
    [InlineData("GstApp", 8)]
    [InlineData("GstAudio", 0)]
    [InlineData("GstVideo", 2)]
    [InlineData("GstPbutils", 5)]
    [InlineData("GstSdp", 0)]
    [InlineData("GstWebRTC", 8)]
    [InlineData("GstNet", 0)]
    [InlineData("GstRtsp", 1)]
    [InlineData("GstRtp", 2)]
    [InlineData("GstRtspServer", 40)]
    [InlineData("GstAllocators", 0)]
    [InlineData("GstTag", 0)]
    [InlineData("GstTranscoder", 6)]
    [InlineData("GstPlay", 13)]
    [InlineData("GES", 39)]
    public void TheSignalCensusIsStable(string module, int signals)
    {
        // The two signals whose C# name a method of the same class had taken
        // (GstElement::no-more-pads and GstPadTemplate::pad-created) are bound
        // through the renames of fixups.json, so nothing collides any more.
        // Two more renames are cosmetic: GESTimeline and GESTrack spell their
        // commit signal 'commited', and the event of both is Committed.
        // The nine action signals of GstApp are not events: they are the call
        // API of GstAppSrc and GstAppSink, which is already bound as methods.
        // Four of the twelve signals of GstWebRTC are action signals as well,
        // which leaves the eight that are counted here; on-message-data is one
        // of them since the planner learned to marshal a GLib.Bytes, and it
        // was the one signal of the corpus that used to be hand written.
        Assert.Equal(signals, Generated.Census.EmittedCount(module, "signal"));
        Assert.DoesNotContain(Generated.Diagnostics, diagnostic => diagnostic.Code == "GEN0011");
    }

    [Fact]
    public void EveryEmittedSignalIsBackedByATrampolineAndAConnection()
    {
        int events = 0;
        int trampolines = 0;
        int adders = 0;
        int removers = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            events += file.Content.Split("    public event ").Length - 1;
            trampolines += file.Content.Split("Trampoline(nint instance").Length - 1;
            adders += file.Content.Split("    public static void Add").Length - 1;
            removers += file.Content.Split("    public static void Remove").Length - 1;
        }

        // A hundred and forty nine signals are emitted over the seventeen
        // modules. A hundred and forty four are events of a class; the
        // remaining five belong to a gir interface and are a pair of extension
        // methods instead. The editing services are thirty nine of them:
        // thirty eight events and the one signal of a GES interface,
        // GESMetaContainer::notify-meta, whose lent GValue the arguments show
        // through a view they refuse to build once the emission has ended.
        // Three of the thirty nine are the pointer array signals: GESLayer's
        // active-changed and GESTimeline's group-removed, whose container is
        // read out into an array of wrappers before the handler runs, and
        // GESTimeline's select-tracks-for-object, whose handler answers with
        // one the timeline takes over.
        // The six of the transcoder and the thirteen of the play are the
        // signals of GstTranscoderSignalAdapter and GstPlaySignalAdapter,
        // which are classes as well. The two of the RTP module are the
        // request-extension of GstRTPBasePayload and the one of
        // GstRTPBaseDepayload; the four signals beside them, add-extension and
        // clear-extensions on each of the two classes, carry action="1" and
        // are skipped on that rule, since an action signal is a call API and
        // not a notification. The forty of the RTSP server are the
        // eighteen that carry no GstRTSPContext plus the twenty two signals of
        // GstRTSPClient whose context is copied out of the emission into the
        // arguments; check-requirements is one of them, and the NULL
        // terminated vector of strings beside its context is read out into an
        // array the handler owns. send-message would have been a nineteenth of
        // the first group, and it is the one signal of the corpus the overlays
        // skip: the gir types its first argument as a GstRTSPSession where the
        // C registers and emits a GstRTSPContext, which no annotation can
        // correct. The event is written by hand in
        // src/GstSharp.Net.RtspServer/Custom instead, as SendingMessage,
        // because the method beside it had taken SendMessage. The adder and remover counts carry matches that are
        // not a signal pair at all: Gst.ITagSetter's AddTagValue extension, and
        // the AddAllSchemas, AddSchema, RemoveAllSchemas and RemoveSchema
        // extensions of Gst.Tag.ITagXmpWriter, methods whose names the pattern
        // cannot tell from a subscription adder or remover.
        Assert.Equal(144, events);
        Assert.Equal(8, adders);
        Assert.Equal(7, removers);
        Assert.Equal(149, trampolines);

        string[] withSignals =
        [
            "GstSharp.Net", "GstSharp.Net.Base", "GstSharp.Net.App", "GstSharp.Net.Video", "GstSharp.Net.Pbutils",
            "GstSharp.Net.WebRTC", "GstSharp.Net.Rtsp", "GstSharp.Net.Rtp", "GstSharp.Net.RtspServer",
            "GstSharp.Net.Transcoder",
            "GstSharp.Net.Play", "GstSharp.Net.GES",
        ];

        foreach (string module in withSignals)
        {
            Assert.Contains(
                Generated.Files,
                file => file.RelativePath == module + "/Generated/SignalConnections.cs");
        }

        // A module without signals does not carry the holder at all.
        Assert.DoesNotContain(
            Generated.Files,
            file => file.RelativePath == "GstSharp.Net.Audio/Generated/SignalConnections.cs");
    }

    [Fact]
    public void ASignalOfAnInterfaceBecomesAPairOfExtensionMethods()
    {
        string source = Source("IChildProxy.cs");

        Assert.Contains(
            "public static void AddChildAddedHandler(this Gst.IChildProxy self, "
            + "System.EventHandler<Gst.ChildProxyExtensions.ChildAddedSignalArgs> handler) =>\n"
            + "        Gst.SignalConnections.Add((Gst.GObject.Object)self, \"child-added\", ",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static void RemoveChildAddedHandler(this Gst.IChildProxy self, "
            + "System.EventHandler<Gst.ChildProxyExtensions.ChildAddedSignalArgs> handler) =>\n"
            + "        Gst.SignalConnections.Remove((Gst.GObject.Object)self, \"child-added\", handler);",
            source,
            StringComparison.Ordinal);

        // The arguments carrier is nested in the extension class, because the
        // interface itself only exposes the native handle.
        Assert.Contains("public sealed class ChildAddedSignalArgs : System.EventArgs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public event", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRenamedSignalsOfTheRealGirAreEmitted()
    {
        Assert.Contains(
            "public event System.EventHandler NoMorePadsSignal\n",
            Source("Element.cs"),
            StringComparison.Ordinal);
        // The arguments class is named after the resolved event name with a
        // single Signal suffix, so the rename does not spell it twice.
        Assert.Contains(
            "public event System.EventHandler<Gst.PadTemplate.PadCreatedSignalArgs> PadCreatedSignal\n",
            Source("PadTemplate.cs"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("SignalSignalArgs", Source("PadTemplate.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net.GES/Generated/Timeline.cs")]
    [InlineData("GstSharp.Net.GES/Generated/Track.cs")]
    public void ARenameCorrectsTheCSharpNameOfASignalAndNotTheOneOnTheWire(string path)
    {
        // GESTimeline and GESTrack emit 'commited', with one 't'. The typo is
        // the API, so the name the generated code hands to GObject has to keep
        // it while the event reads the way a C# member should.
        string source = SourceOf(path);

        Assert.Contains("public event System.EventHandler Committed\n", source, StringComparison.Ordinal);
        Assert.Contains("GES.SignalConnections.Add(this, \"commited\", ", source, StringComparison.Ordinal);
        Assert.Contains("GES.SignalConnections.Remove(this, \"commited\", value);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"committed\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowReturningTrampolineReportsAnErrorWhenTheHandlerCannotRun()
    {
        // The default of GstFlowReturn is GST_FLOW_OK. Reporting that after the
        // handler threw tells the pipeline that a buffer it never got was
        // accepted, so a flow returning trampoline reports an error instead,
        // both when the handler throws and when its state is already gone.
        string source = SourceOf("GstSharp.Net.App/Generated/AppSink.cs");

        Assert.Contains(
            """
                        if (Gst.Interop.CallbackHandle.GetState<Gst.App.AppSink.NewSampleHandler>(userData) is not { } handler)
                        {
                            return (int)Gst.FlowReturn.Error;
                        }
            """,
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            """
                    catch (Exception exception)
                    {
                        Gst.Interop.ExceptionTrap.Report(exception);
                        return (int)Gst.FlowReturn.Error;
                    }
            """,
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFlowReturningCallbackTrampolineReportsAnErrorToo()
    {
        // The same rule reaches the callbacks the emitters hand to native code,
        // GstBaseParse and the simple callbacks of GstAppSink among them.
        string source = SourceOf("GstSharp.Net.App/Generated/Callbacks.cs");

        Assert.Contains("return (int)Gst.FlowReturn.Error;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AReturnValuedSignalWithoutArgumentsIsEmittedForAppSink()
    {
        string source = SourceOf("GstSharp.Net.App/Generated/AppSink.cs");

        Assert.Contains(
            "public delegate Gst.FlowReturn NewSampleHandler(object? sender, System.EventArgs args);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public event Gst.App.AppSink.NewSampleHandler NewSample\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "add => Gst.App.SignalConnections.Add(this, \"new-sample\", ",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A namespace whose one signal carries a plain structure, the shape the
    /// request signals of <c>GstRTSPClient</c> have.
    /// </summary>
    private const string PlainStructSignalFixture =
        """
            <record name="RequestInfo" c:type="GstRequestInfo">
              <field name="method" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="uri" writable="1">
                <type name="gpointer" c:type="gpointer"/>
              </field>
            </record>
            <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
              <glib:signal name="describe-request" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="ctx" transfer-ownership="none">
                    <doc xml:space="preserve">the context of the request</doc>
                    <type name="RequestInfo"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    [Fact]
    public void APlainStructArgumentIsCopiedOutOfTheEmission()
    {
        FixtureRun run = Fixture.Run(PlainStructSignalFixture);
        string source = run.File("Element.cs");

        // The gir of a signal parameter states no c:type, so the pointer the
        // marshaller of GObject passes is not spelled there; the trampoline
        // takes it all the same, and the handler is handed the value at it.
        Assert.Contains(
            "private static void DescribeRequestTrampoline(nint instance, Gst.RequestInfo* ctx, nint userData)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "(nint)(delegate* unmanaged[Cdecl]<nint, Gst.RequestInfo*, nint, void>)&DescribeRequestTrampoline",
            source,
            StringComparison.Ordinal);
        Assert.Contains("Gst.RequestInfo ctxValue = *ctx;", source, StringComparison.Ordinal);
        Assert.Contains("internal DescribeRequestSignalArgs(Gst.RequestInfo ctx)", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.RequestInfo Ctx { get; }", source, StringComparison.Ordinal);

        // The copy outlives the emission, the pointers in it do not, and the
        // property says so.
        Assert.Contains(
            "/// A read only snapshot: the structure is copied out of the storage the\n"
            + "        /// emitter holds, so writing to it changes nothing the emission reads\n"
            + "        /// back. Every pointer inside it is borrowed for the length of the\n"
            + "        /// handler and must not be kept past it.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASkippedSignalIsNotEmitted()
    {
        // A signal whose C registration no annotation can describe is kept off
        // the surface by name, the way a property is, so that the member which
        // answers it can be written by hand under the name the generator would
        // have taken. Nothing of the signal is left behind: not the event, not
        // its arguments class, not its trampoline.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "Gst.Element::pad-added" ]
            }
            """);

        string source = run.File("Element.cs");

        Assert.DoesNotContain("PadAdded", source, StringComparison.Ordinal);
        Assert.DoesNotContain("pad-added", source, StringComparison.Ordinal);

        // The entry matched, so it is not reported stale, and what it kept out
        // is counted where a skipped property is counted: one ledger row under
        // the reason the key carries, spelt the way the census spells a signal.
        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0055", StringComparison.Ordinal));

        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));
        Assert.Contains(
            "### OverlaySkip (1)\n\n- `Gst.Element::pad-added`\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASkippedSignalWithAHandBoundTwinIsFiledAsHandBound()
    {
        // The other half of the pair: the skip is what keeps the event out and
        // the hand bound entry is what says the member exists all the same, so
        // the row moves off the real gap and neither entry reads as stale.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "Gst.Element::pad-added" ],
              "handBound": [ "Gst.Element::pad-added" ]
            }
            """);

        Assert.DoesNotContain("PadAdded", run.File("Element.cs"), StringComparison.Ordinal);

        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.HandBound));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.OverlaySkip));
        Assert.Contains(
            "### HandBound (1)\n\n- `Gst.Element::pad-added`\n",
            run.Result.SkipReport,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0023", StringComparison.Ordinal)
                || string.Equals(diagnostic.Code, "GEN0055", StringComparison.Ordinal));
    }

    [Fact]
    public void ASkippedSignalThatNamesNoSignalIsReportedAsStale()
    {
        // The other half: a key the signal loop never matched keeps nothing
        // out, and the event it was written against is generated again beside
        // the hand written member that replaced it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "skip": [ "Gst.Element::vanished" ]
            }
            """);

        Assert.Contains(
            run.Result.Diagnostics,
            static diagnostic => string.Equals(diagnostic.Code, "GEN0055", StringComparison.Ordinal)
                && diagnostic.Message.Contains("Gst.Element::vanished", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the signal fixture, or another body, over a fixups file written for
    /// the run.
    /// </summary>
    /// <param name="fixups">The contents of the overlay file.</param>
    /// <param name="body">The gir body, or <see langword="null"/> for the signal fixture.</param>
    /// <param name="allowErrors">Whether the run is expected to report an error.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string fixups, string? body = null, bool allowErrors = false)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(body ?? SignalFixture, Overlays.Load(directory), allowErrors: allowErrors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string Source(string fileName) => SourceOf("GstSharp.Net/Generated/" + fileName);

    private static string SourceOf(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no " + path + ".");
    }
}
