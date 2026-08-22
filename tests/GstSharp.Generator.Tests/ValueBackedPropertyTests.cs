using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The properties the gir declares without naming a C accessor for them. They
/// exist on the GObject property system and nowhere else, so they are emitted
/// against <c>g_object_get_property</c> and <c>g_object_set_property</c>
/// through a <c>GValue</c> of the type the specification declares.
/// </summary>
public sealed class ValueBackedPropertyTests
{
    /// <summary>
    /// One class carrying a property of every value kind the rule covers, plus
    /// the ones it refuses: write only, an unsupported type, a name another
    /// member took and a name a base class carries.
    /// </summary>
    private const string Body =
        """
            <enumeration name="Format" c:type="GstFormat">
              <member name="undefined" value="0" c:identifier="GST_FORMAT_UNDEFINED"/>
              <member name="bytes" value="2" c:identifier="GST_FORMAT_BYTES"/>
            </enumeration>
            <bitfield name="TrackType" c:type="GstTrackType">
              <member name="audio" value="1" c:identifier="GST_TRACK_TYPE_AUDIO"/>
              <member name="video" value="2" c:identifier="GST_TRACK_TYPE_VIDEO"/>
            </bitfield>
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
            <record name="Segment" c:type="GstSegment" glib:type-name="GstSegment" glib:get-type="gst_segment_get_type">
              <field name="rate" writable="1">
                <type name="gdouble" c:type="gdouble"/>
              </field>
            </record>
            <class name="Pad" c:type="GstPad" parent="GObject.Object" glib:type-name="GstPad" glib:get-type="gst_pad_get_type">
            </class>
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="is_ready" c:identifier="gst_widget_is_ready">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_muted" c:identifier="gst_widget_set_muted">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="muted" transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_active" c:identifier="gst_widget_set_active">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="active" transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_volume" c:identifier="gst_widget_set_volume">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="volume" transfer-ownership="none">
                    <type name="gint" c:type="gint"/>
                  </parameter>
                </parameters>
              </method>
              <property name="block" writable="1" transfer-ownership="none">
                <doc xml:space="preserve">whether the source blocks when it is full</doc>
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="number" writable="1" transfer-ownership="none">
                <type name="gint" c:type="gint"/>
              </property>
              <property name="count" writable="1" transfer-ownership="none">
                <type name="guint" c:type="guint"/>
              </property>
              <property name="offset" writable="1" transfer-ownership="none">
                <type name="gint64" c:type="gint64"/>
              </property>
              <property name="latency" writable="1" transfer-ownership="none">
                <type name="guint64" c:type="guint64"/>
              </property>
              <property name="gain" writable="1" transfer-ownership="none">
                <type name="gfloat" c:type="gfloat"/>
              </property>
              <property name="ratio" writable="1" transfer-ownership="none">
                <type name="gdouble" c:type="gdouble"/>
              </property>
              <property name="label" writable="1" transfer-ownership="none">
                <type name="utf8" c:type="gchar*"/>
              </property>
              <property name="factory-type" writable="1" transfer-ownership="none">
                <type name="GType" c:type="GType"/>
              </property>
              <property name="format" writable="1" transfer-ownership="none">
                <type name="Format" c:type="GstFormat"/>
              </property>
              <property name="track-type" writable="1" transfer-ownership="none">
                <type name="TrackType" c:type="GstTrackType"/>
              </property>
              <property name="peer" writable="1" transfer-ownership="none">
                <type name="Pad" c:type="GstPad*"/>
              </property>
              <property name="config" writable="1" transfer-ownership="none">
                <type name="Segment" c:type="GstSegment*"/>
              </property>
              <property name="data" writable="1" transfer-ownership="none">
                <type name="Buffer" c:type="GstBuffer*"/>
              </property>
              <property name="state" transfer-ownership="none">
                <type name="gint" c:type="gint"/>
              </property>
              <property name="protocol" writable="1" construct-only="1" transfer-ownership="none">
                <type name="utf8" c:type="gchar*"/>
              </property>
              <property name="anchor" writable="1" construct-only="1" transfer-ownership="none">
                <type name="Pad" c:type="GstPad*"/>
              </property>
              <property name="enable-async" writable="1" readable="0" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="context" writable="1" transfer-ownership="none">
                <type name="gpointer" c:type="gpointer"/>
              </property>
              <property name="is-ready" writable="1" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="muted" writable="1" setter="set_muted" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="active" writable="1" setter="set_active" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="volume" writable="1" setter="set_volume" transfer-ownership="none">
                <type name="gdouble" c:type="gdouble"/>
              </property>
              <property name="old-thing" writable="1" deprecated="1" deprecated-version="1.26" transfer-ownership="none">
                <doc-deprecated xml:space="preserve">Use block instead.</doc-deprecated>
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <property name="fresh" writable="1" version="1.28" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
            </class>
            <class name="Gadget" c:type="GstGadget" parent="Widget" glib:type-name="GstGadget" glib:get-type="gst_gadget_get_type">
              <method name="reset" c:identifier="gst_gadget_reset">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="gadget" transfer-ownership="none">
                    <type name="Gadget" c:type="GstGadget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <property name="block" writable="1" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
            </class>
        """;

    /// <summary>
    /// A class that names one thing twice: a signal and a property both called
    /// <c>eos</c>, which is what <c>GstAppSink</c> does.
    /// </summary>
    private const string SignalBody =
        """
            <class name="Sink" c:type="GstSink" parent="GObject.Object" glib:type-name="GstSink" glib:get-type="gst_sink_get_type">
              <property name="eos" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
              <glib:signal name="eos" when="last">
                <doc xml:space="preserve">the stream has ended</doc>
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    /// <summary>
    /// A class whose property names a C getter the planner cannot bind, here
    /// because the gir marks the method as not introspectable.
    /// </summary>
    private const string UnboundGetterBody =
        """
            <class name="Dial" c:type="GstDial" parent="GObject.Object" glib:type-name="GstDial" glib:get-type="gst_dial_get_type">
              <method name="get_shape" c:identifier="gst_dial_get_shape" introspectable="0">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="dial" transfer-ownership="none">
                    <type name="Dial" c:type="GstDial*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <property name="shape" writable="1" getter="get_shape" transfer-ownership="none">
                <type name="gboolean" c:type="gboolean"/>
              </property>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// Every value kind reads its holder and writes it back, and the local is
    /// called <c>holder</c> because <c>value</c> is the implicit parameter of
    /// the setter.
    /// </summary>
    /// <remarks>
    /// A wrapper valued property hands the wrapper itself to the runtime rather
    /// than the handle read out of it. The overload that takes one keeps it
    /// alive until the value has taken its copy or its reference, which a
    /// generated body cannot do for itself: it carries no <c>GC.KeepAlive</c>.
    /// </remarks>
    /// <param name="signature">The declaration of the emitted property.</param>
    /// <param name="girName">The GObject name the accessors pass.</param>
    /// <param name="read">The expression the getter returns.</param>
    /// <param name="write">The statement the setter fills the holder with.</param>
    [Theory]
    [InlineData("public bool Block", "block", "holder.GetBoolean()", "holder.SetBoolean(value);")]
    [InlineData("public int Number", "number", "holder.GetInt()", "holder.SetInt(value);")]
    [InlineData("public uint Count", "count", "holder.GetUInt()", "holder.SetUInt(value);")]
    [InlineData("public long Offset", "offset", "holder.GetInt64()", "holder.SetInt64(value);")]
    [InlineData("public ulong Latency", "latency", "holder.GetUInt64()", "holder.SetUInt64(value);")]
    [InlineData("public float Gain", "gain", "holder.GetFloat()", "holder.SetFloat(value);")]
    [InlineData("public double Ratio", "ratio", "holder.GetDouble()", "holder.SetDouble(value);")]
    [InlineData("public string? Label", "label", "holder.GetString()", "holder.SetString(value);")]
    [InlineData(
        "public Gst.GObject.GType FactoryType",
        "factory-type",
        "holder.GetGType()",
        "holder.SetGType(value);")]
    [InlineData(
        "public Gst.Format Format",
        "format",
        "(Gst.Format)holder.GetEnum()",
        "holder.SetEnum((int)value);")]
    [InlineData(
        "public Gst.TrackType TrackType",
        "track-type",
        "(Gst.TrackType)holder.GetFlags()",
        "holder.SetFlags((uint)value);")]
    [InlineData(
        "public Gst.Pad? Peer",
        "peer",
        "(Gst.Pad?)holder.GetObject()",
        "holder.SetObject(value);")]
    [InlineData(
        "public Gst.Segment? Config",
        "config",
        "holder.GetBoxed<Gst.Segment>()",
        "holder.SetBoxed(value);")]
    [InlineData(
        "public Gst.Buffer? Data",
        "data",
        "holder.GetMiniObject<Gst.Buffer>()",
        "holder.SetMiniObject(value);")]
    public void AValueKindCrossesTheHolderBothWays(string signature, string girName, string read, string write)
    {
        Assert.Equal(
            string.Join(
                "\n",
                signature,
                "{",
                "    get",
                "    {",
                "        using Gst.GObject.Value holder = GetProperty(\"" + girName + "\");",
                "        return " + read + ";",
                "    }",
                string.Empty,
                "    set",
                "    {",
                "        using Gst.GObject.Value holder = NewPropertyValue(\"" + girName + "\");",
                "        " + write,
                "        SetPropertyValue(\"" + girName + "\", in holder);",
                "    }",
                "}"),
            Run.Member("Widget.cs", signature),
            StringComparer.Ordinal);
    }

    /// <summary>A property that cannot be written is emitted without a setter.</summary>
    [Fact]
    public void AReadOnlyPropertyIsGetOnly()
    {
        Assert.Equal(
            """
            public int State
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("state");
                    return holder.GetInt();
                }
            }
            """,
            Run.Member("Widget.cs", "public int State"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A construct-only property is read only too, and says why: it can be
    /// given to the constructor and nowhere else, which the C# declaration
    /// alone does not explain.
    /// </summary>
    [Fact]
    public void AConstructOnlyPropertyIsGetOnlyAndSaysSo()
    {
        Assert.Equal(
            """
            public string? Protocol
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("protocol");
                    return holder.GetString();
                }
            }
            """,
            Run.Member("Widget.cs", "public string? Protocol"),
            StringComparer.Ordinal);

        Assert.Contains(
            "    /// <para>The property is construct-only and therefore read-only here.</para>\n",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two shapes that are not emitted at all: a property that cannot be
    /// read, and one whose type no <c>GValue</c> accessor of the runtime
    /// covers.
    /// </summary>
    [Fact]
    public void AWriteOnlyOrUnmappablePropertyIsSkipped()
    {
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain(" EnableAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" Context", source, StringComparison.Ordinal);
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// A name a member of the same type already carries is a collision, and the
    /// property is the one that gives way: the method binds a C entry point and
    /// the property binds nothing the library named.
    /// </summary>
    [Fact]
    public void ANameAMethodTookIsACollision()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains("public bool IsReady()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool IsReady\n", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.NameCollision));
    }

    /// <summary>
    /// A name a base class carries is left to the base class. This shape never
    /// emits <c>new</c>: what it would hide is a binding of something the
    /// library named, and this is not one.
    /// </summary>
    [Fact]
    public void ANameABaseClassCarriesIsLeftToIt()
    {
        string source = Run.File("Gadget.cs");

        Assert.DoesNotContain("Block", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new bool", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.ShadowedBy));
    }

    /// <summary>
    /// A setter the gir names is used when it is the shape a property setter
    /// has: void, one visible parameter, and that parameter of the type the
    /// property holds. The getter still goes through the holder, because there
    /// is no getter to call.
    /// </summary>
    [Fact]
    public void AWiredSetterIsUsedWhenItFitsTheProperty()
    {
        Assert.Equal(
            """
            public bool Muted
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("muted");
                    return holder.GetBoolean();
                }

                set => SetMuted(value);
            }
            """,
            Run.Member("Widget.cs", "public bool Muted"),
            StringComparer.Ordinal);

        // Only half of such a property is value backed, and the note says which
        // half. Nothing of it reaches g_object_set_property, so the exception
        // it documents is the one of a read alone.
        Assert.Contains(
            """
                /// <para>
                /// This property has no C getter; it is read through the GObject property
                /// system (<c>g_object_get_property</c>) and written through
                /// <see cref="SetMuted"/>.
                /// </para>
                /// </remarks>
                /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
                /// <exception cref="System.ArgumentException">
                /// The installed GStreamer declares no such property on this class.
                /// </exception>
                public bool Muted
            """,
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A setter that reports a failure is not a property setter, and neither is
    /// one that takes something else than the property holds. Both fall back to
    /// the holder, and the C call stays a method of its own.
    /// </summary>
    /// <param name="signature">The declaration of the emitted property.</param>
    /// <param name="girName">The GObject name the accessors pass.</param>
    /// <param name="method">The C setter that is not used.</param>
    [Theory]
    [InlineData("public bool Active", "active", "SetActive")]
    [InlineData("public double Volume", "volume", "SetVolume")]
    public void AWiredSetterOfTheWrongShapeIsNotUsed(string signature, string girName, string method)
    {
        string member = Run.Member("Widget.cs", signature);

        Assert.Contains(
            "using Gst.GObject.Value holder = NewPropertyValue(\"" + girName + "\");",
            member,
            StringComparison.Ordinal);
        Assert.DoesNotContain("set => " + method + "(value);", member, StringComparison.Ordinal);
        Assert.Contains(" " + method + "(", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>A deprecated property carries the attribute, as any other does.</summary>
    [Fact]
    public void ADeprecatedPropertyIsObsolete()
    {
        Assert.Contains(
            "[Obsolete(\"Use block instead. (deprecated since 1.26)\")]\n    public bool OldThing\n",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A property that arrived after the supported floor says which GStreamer
    /// it needs, in the remarks the generated note shares.
    /// </summary>
    [Fact]
    public void APropertyThatArrivedLateSaysSince()
    {
        Assert.Contains(
            """
                /// <para>
                /// This property has no C accessor; it is read and written through the GObject
                /// property system (<c>g_object_get_property</c> / <c>g_object_set_property</c>).
                /// </para>
                /// <para>Available since GStreamer 1.28.</para>
                /// </remarks>
            """,
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The documentation of the gir stays the summary, and the generated note
    /// is what the remarks add to it.
    /// </summary>
    [Fact]
    public void TheGirDocumentationStaysTheSummary()
    {
        Assert.Contains(
            """
                /// <summary>whether the source blocks when it is full</summary>
                /// <remarks>
                /// <para>
                /// This property has no C accessor; it is read and written through the GObject
                /// property system (<c>g_object_get_property</c> / <c>g_object_set_property</c>).
                /// </para>
                /// </remarks>
                /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
                /// <exception cref="System.ArgumentException">
                /// The installed GStreamer declares no such property on this class, or
                /// declares it read-only.
                /// </exception>
                public bool Block
            """,
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The generated notes are ordered: what backs the property, then what it
    /// cannot do, then who owns what crosses it. A construct-only property of a
    /// wrapper kind is the one shape that carries all three at once.
    /// </summary>
    [Fact]
    public void TheGeneratedNotesAreOrdered()
    {
        Assert.Contains(
            """
                /// <summary>The <c>anchor</c> property.</summary>
                /// <remarks>
                /// <para>
                /// This property has no C accessor; it is read and written through the GObject
                /// property system (<c>g_object_get_property</c> / <c>g_object_set_property</c>).
                /// </para>
                /// <para>The property is construct-only and therefore read-only here.</para>
                /// <para>
                /// Reading hands back the interned wrapper of the object, which the binding
                /// keeps; it is not the reader's to dispose.
                /// </para>
                /// </remarks>
                /// <exception cref="System.ObjectDisposedException">The wrapper was disposed.</exception>
                /// <exception cref="System.ArgumentException">
                /// The installed GStreamer declares no such property on this class.
                /// </exception>
                public Gst.Pad? Anchor
            """,
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A wrapper valued property states who owns what crosses it, because the
    /// C# declaration cannot: the reader of a boxed value or a mini object
    /// disposes what comes back, the reader of an object does not.
    /// </summary>
    /// <param name="declaration">The property whose remarks are read.</param>
    /// <param name="sentence">The ownership line it has to carry.</param>
    [Theory]
    [InlineData(
        "public Gst.Segment? Config",
        "/// Reading builds a wrapper that owns a copy of the value: dispose it when")]
    [InlineData(
        "public Gst.Buffer? Data",
        "/// Reading builds a wrapper that owns a reference of its own: dispose it")]
    [InlineData(
        "public Gst.Pad? Peer",
        "/// Reading hands back the interned wrapper of the object, which the binding")]
    public void AWrapperValuedPropertySaysWhoOwnsWhat(string declaration, string sentence)
    {
        string source = Run.File("Widget.cs");
        int remarks = source.IndexOf(sentence, StringComparison.Ordinal);
        int member = source.IndexOf("    " + declaration + "\n", StringComparison.Ordinal);

        Assert.True(remarks >= 0, "The ownership sentence is missing.");
        Assert.True(member > remarks, "The ownership sentence does not belong to " + declaration + ".");
    }

    /// <summary>
    /// A rename of <c>fixups.json</c> is honoured, which is what binds a
    /// property whose natural name is taken or means something else.
    /// </summary>
    [Fact]
    public void ARenameIsHonoured()
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(
                Path.Combine(directory, "fixups.json"),
                """
                {
                  "rename": { "Gst.Widget:label": "Caption" }
                }
                """);

            FixtureRun renamed = Fixture.Run(Body, Overlays.Load(directory));

            Assert.Contains(
                "using Gst.GObject.Value holder = GetProperty(\"label\");",
                renamed.Member("Widget.cs", "public string? Caption"),
                StringComparison.Ordinal);
            Assert.DoesNotContain("public string? Label\n", renamed.File("Widget.cs"), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An event takes the name first. This is the one place where a value
    /// backed property is planned out of gir order: the properties of a class
    /// are built before its signals, and these are built after them, so that
    /// the event of <c>GstAppSink</c> stays <c>Eos</c> and the property of the
    /// same name gives way rather than the other way round.
    /// </summary>
    [Fact]
    public void AnEventTakesTheNameBeforeAValueBackedPropertyDoes()
    {
        FixtureRun run = Fixture.Run(SignalBody);
        string source = run.File("Sink.cs");

        Assert.Contains("public event System.EventHandler Eos", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public bool Eos", source, StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.NameCollision));
    }

    /// <summary>
    /// A <c>getter=</c> the planner did not bind is no getter at all, so the
    /// property still goes through the holder. The branch asks whether there is
    /// a usable planned accessor and not whether the gir named one.
    /// </summary>
    [Fact]
    public void AGetterThatIsNotBoundLeavesThePropertyValueBacked()
    {
        FixtureRun run = Fixture.Run(UnboundGetterBody);

        Assert.Equal(
            """
            public bool Shape
            {
                get
                {
                    using Gst.GObject.Value holder = GetProperty("shape");
                    return holder.GetBoolean();
                }

                set
                {
                    using Gst.GObject.Value holder = NewPropertyValue("shape");
                    holder.SetBoolean(value);
                    SetPropertyValue("shape", in holder);
                }
            }
            """,
            run.Member("Dial.cs", "public bool Shape"),
            StringComparer.Ordinal);

        // The method the gir named is not emitted either, which is what made
        // the property value backed in the first place.
        Assert.DoesNotContain("GetShape", run.File("Dial.cs"), StringComparison.Ordinal);
        Assert.Equal(1, run.Result.Census.SkippedCount("Gst", SkipReason.NotIntrospectable));
        Assert.Equal(0, run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
