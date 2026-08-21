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
    public void ANotifyStyleSignalHandsOverTheParameterSpecification()
    {
        string source = Source("Object.cs");

        Assert.Contains("public Gst.GObject.ParamSpec Prop { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.GObject.ParamSpec propValue = new Gst.GObject.ParamSpec(prop, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 23)]
    [InlineData("GstBase", 2)]
    [InlineData("GstApp", 8)]
    [InlineData("GstAudio", 0)]
    [InlineData("GstVideo", 2)]
    [InlineData("GstPbutils", 3)]
    [InlineData("GstSdp", 0)]
    [InlineData("GstWebRTC", 6)]
    [InlineData("GstNet", 0)]
    [InlineData("GstRtsp", 1)]
    [InlineData("GES", 29)]
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
        // and the two that carry a GLib.Bytes or a GLib.Error are not bound at
        // all, which leaves the six that are counted here.
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

        // Seventy four signals are emitted over the eleven modules. Seventy are
        // events of a class; the remaining four belong to a gir interface and
        // are a pair of extension methods instead. The editing services are
        // twenty nine of them, and all twenty nine are events: the one signal
        // of a GES interface, GESMetaContainer::notify-meta, carries a GValue
        // and is not bound. The adder count carries one match that is not a
        // signal pair at all: Gst.ITagSetter's AddTagValue extension, a GValue
        // method whose name the pattern cannot tell from a subscription adder.
        Assert.Equal(70, events);
        Assert.Equal(5, adders);
        Assert.Equal(4, removers);
        Assert.Equal(74, trampolines);

        string[] withSignals =
        [
            "GstSharp.Net", "GstSharp.Net.Base", "GstSharp.Net.App", "GstSharp.Net.Video", "GstSharp.Net.Pbutils",
            "GstSharp.Net.WebRTC", "GstSharp.Net.Rtsp", "GstSharp.Net.GES",
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
