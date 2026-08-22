using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The out parameter whose storage the caller provides: which records the
/// binding can allocate one of, what the member does with it, and which shapes
/// stay rejected.
/// </summary>
/// <remarks>
/// The real girs carry thirty of these. Ten are records that declare a zero
/// argument constructor of their own — <c>gst_allocation_params_new</c>,
/// <c>gst_video_info_new</c>, <c>gst_video_info_dma_drm_new</c> — and that is
/// the whole of the rule: the library sizes and zeroes the storage and the
/// registered boxed free releases it again, so the binding never has to know
/// how large the C structure is. Everything else keeps the rejection it had.
/// </remarks>
public sealed class CallerAllocatedStorageTests
{
    /// <summary>
    /// A namespace with one boxed record that can be allocated, one that
    /// cannot, one opaque record and one plain structure, and a class whose
    /// members fill each of them.
    /// </summary>
    private const string Body =
        """
            <record name="Params" c:type="GstParams" glib:type-name="GstParams" glib:get-type="gst_params_get_type">
              <field name="flags" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="owner" writable="1">
                <type name="Widget" c:type="GstWidget*"/>
              </field>
              <constructor name="new" c:identifier="gst_params_new">
                <return-value transfer-ownership="full">
                  <type name="Params" c:type="GstParams*"/>
                </return-value>
              </constructor>
            </record>
            <record name="Info" c:type="GstInfo" glib:type-name="GstInfo" glib:get-type="gst_info_get_type">
              <field name="stride" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="owner" writable="1">
                <type name="Widget" c:type="GstWidget*"/>
              </field>
            </record>
            <record name="Poll" c:type="GstPoll" disguised="1" opaque="1">
            </record>
            <record name="Rect" c:type="GstRect">
              <field name="x" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="get_params" c:identifier="gst_widget_get_params">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none" optional="1">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="fill_params" c:identifier="gst_widget_fill_params">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="name_params" c:identifier="gst_widget_name_params">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                  <parameter name="name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_owner_and_params" c:identifier="gst_widget_get_owner_and_params">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="owner" direction="out" transfer-ownership="full" optional="1" nullable="1">
                    <type name="Widget" c:type="GstWidget**"/>
                  </parameter>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none" optional="1">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                </parameters>
              </method>
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
              <method name="get_poll" c:identifier="gst_widget_get_poll">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="set" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Poll" c:type="GstPoll*"/>
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
              <method name="get_count" c:identifier="gst_widget_get_count">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="count" direction="out" transfer-ownership="none">
                    <type name="gint" c:type="gint"/>
                  </parameter>
                </parameters>
              </method>
              <method name="load_params" c:identifier="gst_widget_load_params" throws="1">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                </parameters>
              </method>
              <function name="update_params" c:identifier="gst_widget_update_params">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <parameter name="params" direction="out" caller-allocates="1" transfer-ownership="none">
                    <type name="Params" c:type="GstParams*"/>
                  </parameter>
                  <parameter name="flags" transfer-ownership="none">
                    <type name="gint" c:type="gint"/>
                  </parameter>
                </parameters>
              </function>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Fixture.Run(Body), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    [Fact]
    public void AVoidCalleeHandsTheStorageOverUnconditionally()
    {
        // The record is filled by the time the call returns, so the wrapper
        // adopts it and the parameter is not nullable. The optional annotation
        // of the gir is the C caller's freedom to pass NULL and says nothing
        // about the binding, which always provides the storage.
        Assert.Equal(
            """
            public void GetParams(out Gst.Params @params)
            {
                nint instanceHandle = Handle;
                nint @paramsNative = GstParamsNew();
                GstWidgetGetParams(instanceHandle, @paramsNative);
                System.GC.KeepAlive(this);
                @params = Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_params_new returned no value.");
            }
            """,
            Run.Member("Widget.cs", "public void GetParams"));
    }

    [Fact]
    public void ABooleanCalleeFreesTheStorageWhenItFilledNothing()
    {
        // A false answer means the record was never written, so handing back a
        // zeroed instance would be handing back a value that means nothing. The
        // storage goes back through the boxed free instead.
        Assert.Equal(
            """
            public bool FillParams(out Gst.Params? @params)
            {
                nint instanceHandle = Handle;
                nint @paramsNative = GstParamsNew();
                int nativeResult = GstWidgetFillParams(instanceHandle, @paramsNative);
                System.GC.KeepAlive(this);
                if (nativeResult != 0)
                {
                    @params = Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full);
                }
                else
                {
                    // The call filled nothing, so the storage goes back through
                    // the boxed free the wrapper disposes through.
                    Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)?.Dispose();
                    @params = null;
                }
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool FillParams"));
    }

    [Fact]
    public void EveryGuardRunsBeforeTheStorageIsAllocated()
    {
        // The storage is an allocation nothing but the epilogue releases, so a
        // guard that throws after it would strand it. The three phase prologue
        // is what keeps every guard in front of it.
        Assert.Equal(
            """
            public void NameParams(string name, out Gst.Params @params)
            {
                ArgumentNullException.ThrowIfNull(name);
                nint instanceHandle = Handle;
                nint @paramsNative = GstParamsNew();
                System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);
                GstWidgetNameParams(instanceHandle, @paramsNative, nameScope.Pointer);
                System.GC.KeepAlive(this);
                @params = Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_params_new returned no value.");
            }
            """,
            Run.Member("Widget.cs", "public void NameParams"));
    }

    [Fact]
    public void AThrowingCalleeReleasesTheStorageBeforeItRaises()
    {
        // A member that reports through a GError raises before its epilogue
        // runs, so the storage would never be handed over and never be freed.
        // No real callable has this shape today; the fixture is what keeps a
        // future one from leaking silently.
        Assert.Equal(
            """
            public bool LoadParams(out Gst.Params? @params)
            {
                nint instanceHandle = Handle;
                nint @paramsNative = GstParamsNew();
                nint errorNative = 0;
                int nativeResult = GstWidgetLoadParams(instanceHandle, @paramsNative, &errorNative);
                System.GC.KeepAlive(this);
                if (errorNative != 0)
                {
                    Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)?.Dispose();
                }
                Gst.GLib.GException.ThrowIfSet(ref errorNative);
                if (nativeResult != 0)
                {
                    @params = Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full);
                }
                else
                {
                    // The call filled nothing, so the storage goes back through
                    // the boxed free the wrapper disposes through.
                    Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)?.Dispose();
                    @params = null;
                }
                return nativeResult != 0;
            }
            """,
            Run.Member("Widget.cs", "public bool LoadParams"));
    }

    [Fact]
    public void TheStorageOutTrailsThePublicSignature()
    {
        // The gir spells the destination first, because C spells an out
        // parameter wherever it likes. The public signature puts storage the
        // binding provides last, where .NET puts an out parameter, and the
        // native call keeps the gir order.
        Assert.Contains(
            "public static bool UpdateParams(int flags, out Gst.Params? @params)",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "GstWidgetUpdateParams(@paramsNative, flags)",
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheStorageIsHandedOverBeforeAnyOtherOutConversion()
    {
        // Wrapping a handle can throw — a GType that does not match the wrapper
        // it is asked for is what would do it — and a throw between the call
        // and the hand over would leave nobody holding the allocation. The
        // storage therefore goes first among the out conversions; nothing in
        // it depends on the others.
        Assert.Equal(
            """
            public void GetOwnerAndParams(out Gst.Widget? owner, out Gst.Params @params)
            {
                nint instanceHandle = Handle;
                nint ownerNative = default;
                nint @paramsNative = GstParamsNew();
                GstWidgetGetOwnerAndParams(instanceHandle, &ownerNative, @paramsNative);
                System.GC.KeepAlive(this);
                @params = Gst.Params.FromNative(@paramsNative, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_params_new returned no value.");
                owner = Gst.GObject.Object.FromNative<Gst.Widget>(ownerNative, Gst.Interop.Transfer.Full);
            }
            """,
            Run.Member("Widget.cs", "public void GetOwnerAndParams"));
    }

    [Fact]
    public void TheStorageConstructorIsImportedOncePerFile()
    {
        // Five members of the type ask for the same storage, and each of them
        // would otherwise declare the import again.
        string source = Run.File("Widget.cs");

        Assert.Equal(
            1,
            source.Split("EntryPoint = \"gst_params_new\"").Length - 1);
        Assert.Contains(
            """
                /// <summary>The <c>gst_params_new</c> entry point, which allocates the storage of a caller allocated out parameter.</summary>
                /// <returns>A new, zeroed instance the caller owns.</returns>
                [LibraryImport("Gst", EntryPoint = "gst_params_new")]
                private static partial nint GstParamsNew();
            """.ReplaceLineEndings("\n"),
            source.ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordThatDeclaresTheConstructorImportsItOnlyAsAMember()
    {
        // Params.cs binds gst_params_new as its own factory, so the storage
        // import would be a duplicate declaration there.
        Assert.Equal(
            1,
            Run.File("Params.cs").Split("EntryPoint = \"gst_params_new\"").Length - 1);
    }

    [Fact]
    public void TheParameterSaysWhoOwnsTheStorage()
    {
        Assert.Contains(
            """
                /// <param name="params">
                /// The <c>@params</c> argument.
                /// The binding allocates the storage; on return the caller owns
                /// <paramref name="params"/> and disposes it.
                /// </param>
            """.ReplaceLineEndings("\n"),
            Run.File("Widget.cs").ReplaceLineEndings("\n"),
            StringComparison.Ordinal);

        Assert.Contains(
            """
                /// <param name="params">
                /// The <c>@params</c> argument.
                /// The binding allocates the storage; on success the caller owns
                /// <paramref name="params"/> and disposes it. On failure it is
                /// <see langword="null"/>.
                /// </param>
            """.ReplaceLineEndings("\n"),
            Run.File("Widget.cs").ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ABoxedRecordWithoutAConstructorStaysRejected()
    {
        // Nothing sizes a GstInfo but the library that declares it, and it
        // declares no way of asking for one.
        Assert.DoesNotContain("gst_widget_get_info", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0012" && diagnostic.Message.Contains(
                "gst_widget_get_info",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AnOpaqueRecordStaysRejected()
    {
        // An opaque record has no boxed free to pair an allocation with, and
        // its size is not something the binding may know.
        Assert.DoesNotContain("gst_widget_get_poll", Run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            Run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0012" && diagnostic.Message.Contains(
                "gst_widget_get_poll",
                StringComparison.Ordinal));
    }

    [Fact]
    public void APlainStructKeepsTheProjectionItHad()
    {
        // A plain structure is spelled in C# with the size of the C type, so it
        // needs no allocation at all and must not be re-planned.
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
    public void TheRejectedShapesAreCountedAsCallerAllocates()
    {
        Assert.Equal(2, Run.Result.Census.SkippedCount("Gst", SkipReason.CallerAllocates));
    }

    [Fact]
    public void ADirectionOverrideTurnsADestinationRecordIntoAnInParameter()
    {
        // The gir of gst_sdp_media_set_media_from_caps calls its destination a
        // caller allocated out and the C function requires an initialised one.
        // Corrected onto `in` it plans as the ordinary handle it always was,
        // and it leads the public signature the way an instance does.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": {
                "gst_widget_update_params#params": { "direction": "in", "callerAllocates": false, "transfer": "none" }
              }
            }
            """);

        Assert.Equal(
            """
            public static bool UpdateParams(Gst.Params @params, int flags)
            {
                ArgumentNullException.ThrowIfNull(@params);
                int nativeResult = GstWidgetUpdateParams(@params.Handle, flags);
                System.GC.KeepAlive(@params);
                return nativeResult != 0;
            }
            """,
            run.Member("Widget.cs", "public static bool UpdateParams"));

        // Nothing else on the surface says that the instance has to be one the
        // library built: the parameter reads as an ordinary argument, and an
        // uninitialised one is the call that walks storage that was never set
        // up.
        Assert.Contains(
            """
                /// <param name="params">
                /// The <c>@params</c> argument.
                /// Must be an initialised instance; the call updates it in place.
                /// </param>
            """.ReplaceLineEndings("\n"),
            run.File("Widget.cs").ReplaceLineEndings("\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectionOverrideOntoInIsIgnoredForAValueThatIsNotARecord()
    {
        // The redirect only says that a pointer to a record the callee works on
        // was mislabelled. A scalar the callee writes back has an out
        // projection of its own, with a conversion on either side of the call,
        // and keeps it.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_count#count": { "direction": "in" } }
            }
            """);

        Assert.Contains("public void GetCount(out int count)", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_get_count#count",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ADirectionOverrideOntoInIsIgnoredForAPlainStructure()
    {
        // `out` and `ref` are the corrections a pointer to a plain structure
        // takes. `in` is not one of them: the parameter is already passed as a
        // value the callee reads, and the redirect only says that a pointer to
        // a record the C function works on in place was mislabelled.
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_get_rect#rect": { "direction": "in" } }
            }
            """);

        Assert.Contains("public void GetRect(out Gst.Rect rect)", run.File("Widget.cs"), StringComparison.Ordinal);
        Assert.Contains(
            run.Result.Diagnostics,
            diagnostic => diagnostic.Code == "GEN0017" && diagnostic.Message.Contains(
                "gst_widget_get_rect#rect",
                StringComparison.Ordinal));
    }

    /// <summary>Runs the fixture of this class with a hand written <c>fixups.json</c>.</summary>
    /// <param name="fixups">The content of <c>fixups.json</c>.</param>
    /// <returns>The run.</returns>
    private static FixtureRun RunWithOverlay(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(Body, Overlays.Load(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
