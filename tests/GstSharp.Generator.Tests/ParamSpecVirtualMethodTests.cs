using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The three shapes of the child property family a virtual slot carries: a
/// counted block of parameter specifications the slot answers, a specification
/// it produces beside a GObject on the true path of a <c>gboolean</c>, and one
/// it is lent beside a <c>GValue</c>.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise all three through
/// <c>GESTimelineElementClass</c>, and the fixtures here are the definition of
/// the feature: the block is allocated and counted the way the C default
/// allocates and counts it, the wrappers the override hands over are consumed,
/// and the storage of a produced out is written on the true path only, because
/// the caller of such a slot releases what it finds there and leaves the
/// storage alone otherwise.
/// </para>
/// <para>
/// The block also pins the overlay route: the gir of the slot carries no
/// <c>&lt;array&gt;</c> at all — upstream marks it <c>introspectable="0"</c> —
/// so the array is the one the overlays state, element type and counting
/// parameter together.
/// </para>
/// </remarks>
public sealed class ParamSpecVirtualMethodTests
{
    /// <summary>
    /// One subclassable class with the three slots, declared the way the gir of
    /// GES declares them: the block with no array and no direction on its
    /// count, the lookup with two optional outs, and the setter with a
    /// <c>GValue</c> the gir does not call const.
    /// </summary>
    private const string Body =
        """
            <class name="Widget" c:type="GstWidget" parent="GObject.Object" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type" glib:type-struct="WidgetClass">
              <virtual-method name="list_children_properties" introspectable="0">
                <return-value>
                  <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="n_properties" transfer-ownership="none">
                    <type name="guint" c:type="guint*"/>
                  </parameter>
                </parameters>
              </virtual-method>
              <virtual-method name="lookup_child">
                <return-value transfer-ownership="none">
                  <type name="gboolean" c:type="gboolean"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="prop_name" transfer-ownership="none">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                  <parameter name="child" direction="out" caller-allocates="0" transfer-ownership="full" optional="1" allow-none="1">
                    <type name="GObject.Object" c:type="GObject**"/>
                  </parameter>
                  <parameter name="pspec" direction="out" caller-allocates="0" transfer-ownership="full" optional="1" allow-none="1">
                    <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                  </parameter>
                </parameters>
              </virtual-method>
              <virtual-method name="set_child_property">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="child" transfer-ownership="none">
                    <type name="GObject.Object" c:type="GObject*"/>
                  </parameter>
                  <parameter name="pspec" transfer-ownership="none">
                    <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                  </parameter>
                  <parameter name="value" transfer-ownership="none">
                    <type name="GObject.Value" c:type="GValue*"/>
                  </parameter>
                </parameters>
              </virtual-method>
            </class>
            <record name="WidgetClass" c:type="GstWidgetClass" glib:is-gtype-struct-for="Widget">
              <field name="parent_class">
                <type name="GObject.ObjectClass" c:type="GObjectClass"/>
              </field>
              <field name="list_children_properties" introspectable="0">
                <callback name="list_children_properties" introspectable="0">
                  <return-value>
                    <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="n_properties" transfer-ownership="none">
                      <type name="guint" c:type="guint*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
              <field name="lookup_child">
                <callback name="lookup_child">
                  <return-value transfer-ownership="none">
                    <type name="gboolean" c:type="gboolean"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="prop_name" transfer-ownership="none">
                      <type name="utf8" c:type="const gchar*"/>
                    </parameter>
                    <parameter name="child" direction="out" caller-allocates="0" transfer-ownership="full" optional="1" allow-none="1">
                      <type name="GObject.Object" c:type="GObject**"/>
                    </parameter>
                    <parameter name="pspec" direction="out" caller-allocates="0" transfer-ownership="full" optional="1" allow-none="1">
                      <type name="GObject.ParamSpec" c:type="GParamSpec**"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
              <field name="set_child_property">
                <callback name="set_child_property">
                  <return-value transfer-ownership="none">
                    <type name="none" c:type="void"/>
                  </return-value>
                  <parameters>
                    <parameter name="widget" transfer-ownership="none">
                      <type name="Widget" c:type="GstWidget*"/>
                    </parameter>
                    <parameter name="child" transfer-ownership="none">
                      <type name="GObject.Object" c:type="GObject*"/>
                    </parameter>
                    <parameter name="pspec" transfer-ownership="none">
                      <type name="GObject.ParamSpec" c:type="GParamSpec*"/>
                    </parameter>
                    <parameter name="value" transfer-ownership="none">
                      <type name="GObject.Value" c:type="GValue*"/>
                    </parameter>
                  </parameters>
                </callback>
              </field>
            </record>
        """;

    /// <summary>
    /// The overlays the block needs: the array the gir does not spell, and the
    /// direction of the parameter the slot writes its length into. The lookup
    /// carries the answer of a parent that installed no slot as well, which is
    /// what the C wrapper answers for one (a <c>g_return_val_if_fail</c> on the
    /// slot, ges-timeline-element.c:2069).
    /// </summary>
    private const string Fixups =
        """
        {
          "subclassable": ["Gst.Widget"],
          "vfuncDefaults": {
            "Gst.Widget::lookup_child": "false"
          },
          "annotationOverrides": {
            "Gst.Widget::list_children_properties#n_properties": { "direction": "out" },
            "Gst.Widget::list_children_properties#return": { "transfer": "full" }
          },
          "arrayOverrides": {
            "Gst.Widget::list_children_properties#return": {
              "length": 0,
              "zeroTerminated": false,
              "elementType": "GObject.ParamSpec"
            }
          }
        }
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(static () => Generate(Fixups), isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    private static string Subclass => Run.File("Subclassing/Widget.Subclass.cs");

    /// <summary>
    /// The block the slot answers becomes an array of wrappers, and the
    /// parameter that counts it is not part of the managed override: the array
    /// carries its own length.
    /// </summary>
    [Fact]
    public void TheAnsweredBlockIsAnArrayAndItsCountIsHidden()
    {
        Assert.Contains(
            "protected virtual Gst.GObject.ParamSpec[] OnListChildrenProperties() =>",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains(
            "protected Gst.GObject.ParamSpec[] ChainUpListChildrenProperties()",
            Subclass,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The count is zeroed before anything that can fail, so that a failure
    /// leaves the NULL block and the count of zero beside each other, and an
    /// override that answered nothing answers exactly that pair.
    /// </summary>
    [Fact]
    public void TheTrampolineZeroesTheCountBeforeItCanFail()
    {
        Assert.Contains(
            "private static nint ListChildrenPropertiesTrampoline(nint widget, uint* nProperties)\n"
            + "    {\n"
            + "        if (nProperties != null)\n"
            + "        {\n"
            + "            *nProperties = 0;\n"
            + "        }\n"
            + "\n"
            + "        try\n",
            Subclass,
            StringComparison.Ordinal);

        Assert.Contains(
            "if (result is not { Length: > 0 })\n"
            + "            {\n"
            + "                return nint.Zero;\n"
            + "            }\n",
            Subclass,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One reference per element is minted for the caller and the wrapper is
    /// disposed right after: the array the override answered is consumed,
    /// because a specification wrapper has no finalizer to give its reference
    /// back later.
    /// </summary>
    [Fact]
    public void TheAnsweredWrappersAreConsumed()
    {
        Assert.Contains(
            "((nint*)block)[index] = Gst.Interop.GObjectNative.ParamSpecRef(resultHandles[index]);",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains("result[index].Dispose();", Subclass, StringComparison.Ordinal);
        Assert.Contains("*nProperties = (uint)result.Length;", Subclass, StringComparison.Ordinal);

        // And the chain-up reads the block back the way the caller of the slot
        // does: every element adopted, the block freed.
        Assert.Contains(
            "result[index] = Gst.GObject.ParamSpec.FromNative(((nint*)resultNative)[index], "
            + "Gst.Interop.Transfer.Full);",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains("Gst.Interop.GMarshal.Free(resultNative);", Subclass, StringComparison.Ordinal);
    }

    /// <summary>
    /// A produced specification is minted with a plain reference — the wrapper
    /// already sank the one it holds — and the wrapper is disposed afterwards.
    /// The produced GObject is not: its wrapper is interned and an override may
    /// well answer one it holds.
    /// </summary>
    [Fact]
    public void AProducedSpecificationIsMintedAndConsumed()
    {
        Assert.Contains(
            "protected virtual bool OnLookupChild(string propName, out Gst.GObject.Object? child, "
            + "out Gst.GObject.ParamSpec? pspec) =>",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains(
            "Gst.Interop.GObjectNative.ParamSpecRef(pspecHandle);",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains("pspecValue?.Dispose();", Subclass, StringComparison.Ordinal);
        Assert.DoesNotContain("childValue?.Dispose();", Subclass, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole trampoline, because the rule is the order of its statements
    /// and not the presence of any one of them: the name is refused when the
    /// caller passed none, the two outs are validated before either is written,
    /// both handles are read before the first store - a wrapper the override
    /// had already disposed throws there, with the caller's storage untouched -
    /// the writes sit inside the true branch behind their optional guards, and
    /// the specification wrapper is disposed on every path.
    /// </summary>
    [Fact]
    public void TheProducedStorageIsWrittenOnTheTruePathOnly()
    {
        Assert.Equal(
            """
            private static int LookupChildTrampoline(nint widget, byte* propName, nint* child, nint* pspec)
            {
                try
                {
                    if (Gst.GObject.Object.TryGetOrFabricate(widget) is not Widget managed)
                    {
                        return (ChainUpLookupChild(widget, propName, child, pspec)) ? 1 : 0;
                    }

                    string propNameValue = Gst.Interop.GMarshal.PtrToStringUtf8((nint)propName)
                        ?? throw new InvalidOperationException("lookup_child passed no propName.");
                    Gst.GObject.Object? childValue = null;
                    Gst.GObject.ParamSpec? pspecValue = null;

                    try
                    {
                        bool result = managed.OnLookupChild(propNameValue, out childValue, out pspecValue);

                        if (result)
                        {
                            if (childValue is null)
                            {
                                throw new InvalidOperationException(
                                    "OnLookupChild answered true without a child, which lookup_child does not allow.");
                            }

                            if (pspecValue is null)
                            {
                                throw new InvalidOperationException(
                                    "OnLookupChild answered true without a pspec, which lookup_child does not allow.");
                            }

                            nint childHandle = childValue is null ? nint.Zero : childValue.Handle;
                            nint pspecHandle = pspecValue is null ? nint.Zero : pspecValue.Handle;
                            if (child != null)
                            {
                                if (childHandle != nint.Zero)
                                {
                                    Gst.Interop.GObjectNative.ObjectRef(childHandle);
                                }
                                *child = childHandle;
                            }
                            if (pspec != null)
                            {
                                if (pspecHandle != nint.Zero)
                                {
                                    Gst.Interop.GObjectNative.ParamSpecRef(pspecHandle);
                                }
                                *pspec = pspecHandle;
                            }
                        }
                        return (result) ? 1 : 0;
                    }
                    finally
                    {
                        pspecValue?.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    Gst.Interop.ExceptionTrap.Report(exception);
                    return default;
                }
            }
            """,
            Run.Member("Subclassing/Widget.Subclass.cs", "private static int LookupChildTrampoline("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A parent that installed no slot answers the overlay default, which is
    /// the FALSE the C wrapper answers for one, rather than the exception a
    /// slot without a default throws.
    /// </summary>
    [Fact]
    public void AParentWithoutTheSlotAnswersFalse()
    {
        Assert.Equal(
            """
            private static bool ChainUpLookupChild(nint widget, byte* propName, nint* child, nint* pspec)
            {
                delegate* unmanaged[Cdecl]<nint, byte*, nint*, nint*, int> slot =
                    (delegate* unmanaged[Cdecl]<nint, byte*, nint*, nint*, int>)ParentClassOf(widget)->LookupChild;

                if (slot is null)
                {
                    if (child != null)
                    {
                        *child = default;
                    }
                    if (pspec != null)
                    {
                        *pspec = default;
                    }
                    return false;
                }

                return slot(widget, propName, child, pspec) != 0;
            }
            """,
            Run.Member("Subclassing/Widget.Subclass.cs", "private static bool ChainUpLookupChild("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// The chain-up of the setter hands the parent a copy of what the view
    /// points at: the view is the caller's storage and the parent takes a
    /// pointer, so the copy is what keeps the two apart for the length of the
    /// call.
    /// </summary>
    [Fact]
    public void TheChainUpOfTheSetterCopiesTheView()
    {
        Assert.Equal(
            """
            protected void ChainUpSetChildProperty(Gst.GObject.Object child, Gst.GObject.ParamSpec pspec, Gst.GObject.ValueView value)
            {
                ArgumentNullException.ThrowIfNull(child);
                ArgumentNullException.ThrowIfNull(pspec);
                using Gst.GObject.Value valueCopy = value.ToValue();
                Gst.GObject.GValueNative* valueNative = &valueCopy.NativeValue;
                ChainUpSetChildProperty(Handle, child.Handle, pspec.Handle, valueNative);
                GC.KeepAlive(this);
                GC.KeepAlive(child);
                GC.KeepAlive(pspec);
            }
            """,
            Run.Member(
                "Subclassing/Widget.Subclass.cs",
                "protected void ChainUpSetChildProperty(Gst.GObject.Object child,"),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// Every handle of the block is read while the elements are validated, so
    /// that an override that answered a wrapper it had already disposed - or
    /// the same wrapper twice, whose second handle would be read after the
    /// first <c>Dispose</c> - is refused before anything is allocated and
    /// before a single reference is minted.
    /// </summary>
    [Fact]
    public void TheAnsweredBlockIsReadBeforeItIsAllocated()
    {
        Assert.Equal(
            """
            private static nint ListChildrenPropertiesTrampoline(nint widget, uint* nProperties)
            {
                if (nProperties != null)
                {
                    *nProperties = 0;
                }

                try
                {
                    if (Gst.GObject.Object.TryGetOrFabricate(widget) is not Widget managed)
                    {
                        return ChainUpListChildrenProperties(widget, nProperties);
                    }

                    Gst.GObject.ParamSpec[] result = managed.OnListChildrenProperties();
                    if (result is not { Length: > 0 })
                    {
                        return nint.Zero;
                    }

                    nint[] resultHandles = new nint[result.Length];
                    for (int index = 0; index < result.Length; index++)
                    {
                        Gst.GObject.ParamSpec specification = result[index];
                        if (specification is null)
                        {
                            throw new InvalidOperationException(
                                "OnListChildrenProperties answered a block with an empty entry, which list_children_properties does not allow.");
                        }

                        resultHandles[index] = specification.Handle;
                    }

                    nint block = Gst.Interop.GMarshal.Malloc0((nuint)result.Length * (nuint)sizeof(nint));
                    for (int index = 0; index < result.Length; index++)
                    {
                        ((nint*)block)[index] = Gst.Interop.GObjectNative.ParamSpecRef(resultHandles[index]);
                        result[index].Dispose();
                    }

                    if (nProperties != null)
                    {
                        *nProperties = (uint)result.Length;
                    }

                    return block;
                }
                catch (Exception exception)
                {
                    Gst.Interop.ExceptionTrap.Report(exception);
                    return default;
                }
            }
            """,
            Run.Member(
                "Subclassing/Widget.Subclass.cs",
                "private static nint ListChildrenPropertiesTrampoline("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// An override that answered true and left an out empty is reported instead
    /// of written out: the caller of the slot dereferences and releases what it
    /// finds on the true path without testing it.
    /// </summary>
    [Fact]
    public void AnEmptyOutOnTheTruePathIsReported()
    {
        Assert.Contains(
            "\"OnLookupChild answered true without a child, which lookup_child does not allow.\");",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"OnLookupChild answered true without a pspec, which lookup_child does not allow.\");",
            Subclass,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A specification the slot is lent is wrapped for the duration of the call
    /// and disposed when the override returns, and the <c>GValue</c> beside it
    /// is the read only view over the caller's own storage.
    /// </summary>
    [Fact]
    public void ALentSpecificationAndValueAreScopedToTheCall()
    {
        Assert.Contains(
            "protected virtual void OnSetChildProperty(Gst.GObject.Object child, "
            + "Gst.GObject.ParamSpec pspec, Gst.GObject.ValueView value) =>",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.GObject.ParamSpec? pspecValue = pspec == nint.Zero ? null : "
            + "Gst.GObject.ParamSpec.FromNative(pspec, Gst.Interop.Transfer.None);",
            Subclass,
            StringComparison.Ordinal);
        Assert.Contains(
            "Gst.GObject.ValueView valueValue = value != null\n"
            + "                ? new Gst.GObject.ValueView(ref *value)\n",
            Subclass,
            StringComparison.Ordinal);
    }

    private static FixtureRun Generate(string fixups)
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
