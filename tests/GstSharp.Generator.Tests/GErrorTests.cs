using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GLib.Error</c> projection: an error the callee borrows crosses as a
/// temporary the member builds and frees, a borrowed one that comes back is
/// read into a managed value, and a signal hands its handler the same value.
/// Everything else - a transferred error, an <c>out</c> one, one in a callback
/// - stays refused.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise the feature through fourteen members, whose
/// counts the census tests freeze. The fixtures here are the definition of it:
/// they name each shape once and pin the emitted text, including the refusals,
/// which fail closed and would widen silently if these were deleted.
/// </para>
/// <para>
/// The <c>utf8</c> signal return is here as well, because
/// <c>GESProject::missing-uri</c> is the one signal in the corpus that carries
/// one and the arm was written for it.
/// </para>
/// </remarks>
public sealed class GErrorTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixtures refer to. It
    /// stands in for the vendored <c>GLib-2.0.gir</c>; the type map answers a
    /// projection for it by name, so no attribute of the declaration decides
    /// anything.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="Error" c:type="GError">
              <field name="domain" writable="1">
                <type name="Quark" c:type="GQuark"/>
              </field>
              <field name="code" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
              <field name="message" writable="1">
                <type name="utf8" c:type="gchar*"/>
              </field>
            </record>
          </namespace>
        """;

    /// <summary>
    /// One class carrying every shape: a borrowed error in, a transferred one
    /// in, an <c>out</c> one, a borrowed one returned, a signal with a
    /// nullable and a non nullable error argument, a signal whose argument is
    /// transferred, a signal that returns an owned string, one that returns a
    /// borrowed string, and a callback that receives an error.
    /// </summary>
    private const string Body =
        """
            <callback name="FailFunc" c:type="GstFailFunc">
              <return-value transfer-ownership="none">
                <type name="none" c:type="void"/>
              </return-value>
              <parameters>
                <parameter name="error" transfer-ownership="none">
                  <type name="GLib.Error" c:type="const GError*"/>
                </parameter>
                <parameter name="user_data" transfer-ownership="none" nullable="1" closure="1">
                  <type name="gpointer" c:type="gpointer"/>
                </parameter>
              </parameters>
            </callback>
            <class name="Widget" c:type="GstWidget" parent="GObject.InitiallyUnowned" glib:type-name="GstWidget" glib:get-type="gst_widget_get_type">
              <method name="report" c:identifier="gst_widget_report">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="error" transfer-ownership="none">
                    <doc xml:space="preserve">the error to report</doc>
                    <type name="GLib.Error" c:type="const GError*"/>
                  </parameter>
                  <parameter name="debug" transfer-ownership="none" nullable="1">
                    <type name="utf8" c:type="const gchar*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="take_error" c:identifier="gst_widget_take_error">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="error" transfer-ownership="full">
                    <type name="GLib.Error" c:type="GError*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="pop_error" c:identifier="gst_widget_pop_error">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="error" direction="out" transfer-ownership="full">
                    <type name="GLib.Error" c:type="GError**"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_error" c:identifier="gst_widget_get_error">
                <return-value transfer-ownership="none" nullable="1">
                  <doc xml:space="preserve">the error of the widget, or %NULL</doc>
                  <type name="GLib.Error" c:type="const GError*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_fail_function" c:identifier="gst_widget_set_fail_function">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="widget" transfer-ownership="none">
                    <type name="Widget" c:type="GstWidget*"/>
                  </instance-parameter>
                  <parameter name="func" transfer-ownership="none" scope="call" closure="1">
                    <type name="FailFunc" c:type="GstFailFunc"/>
                  </parameter>
                  <parameter name="user_data" transfer-ownership="none" nullable="1">
                    <type name="gpointer" c:type="gpointer"/>
                  </parameter>
                </parameters>
              </method>
              <glib:signal name="failed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="error" transfer-ownership="none">
                    <doc xml:space="preserve">the error that happened</doc>
                    <type name="GLib.Error" c:type="GError*"/>
                  </parameter>
                  <parameter name="warning" transfer-ownership="none" nullable="1">
                    <doc xml:space="preserve">the warning that happened, or %NULL</doc>
                    <type name="GLib.Error" c:type="GError*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="taken-error" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="error" transfer-ownership="full">
                    <type name="GLib.Error" c:type="GError*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="missing-name" when="last">
                <return-value transfer-ownership="full" nullable="1">
                  <doc xml:space="preserve">the new name, or %NULL</doc>
                  <type name="utf8" c:type="gchar*"/>
                </return-value>
                <parameters>
                  <parameter name="error" transfer-ownership="none">
                    <type name="GLib.Error" c:type="GError*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="borrowed-name" when="last">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="utf8" c:type="const gchar*"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A borrowed error in parameter is guarded, validated and built into a
    /// temporary the scope releases - in that order, so that a guard that
    /// throws finds nothing allocated.
    /// </summary>
    [Fact]
    public void ABorrowedErrorParameterCrossesAsATemporaryTheMemberFrees()
    {
        Assert.Equal(
            """
            public void Report(Gst.GLib.GException error, string? debug)
            {
                ArgumentNullException.ThrowIfNull(error);
                Gst.GLib.GException.ValidateForNative(error, nameof(error));
                using Gst.Interop.GErrorScope errorScope = Gst.Interop.GMarshal.AllocError(error);
                System.Span<byte> debugBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
                using Gst.Interop.Utf8Scope debugScope = Gst.Interop.GMarshal.StackUtf8(debug, debugBuffer);
                GstWidgetReport(Handle, errorScope.Pointer, debugScope.Pointer);
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Widget.cs", "public void Report("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// What the caller has to know is documented on the parameter, and the
    /// domain rule is an exception the member throws before it allocates.
    /// </summary>
    [Fact]
    public void ABorrowedErrorParameterDocumentsTheTemporaryAndTheDomain()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains(
            """
                /// The call is handed a temporary native error built from this value and
                /// releases it again when the call returns. The library copies whatever it
                /// keeps, so the exception object itself is never retained. It needs a
                /// registered error domain: an exception created without one — every
                /// constructor but <c>GException(Quark, int, string)</c> — is rejected.
            """,
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            """
                /// <exception cref="ArgumentException">
                /// <paramref name="error"/> carries no error domain, no message, or a message with an embedded null.
                /// </exception>
            """,
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// An error the callee takes over is refused: the temporary belongs to the
    /// member, and a callee that freed it would free it twice.
    /// </summary>
    [Fact]
    public void ATransferredErrorParameterIsRejected()
    {
        Assert.DoesNotContain("public void TakeError(", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The explicit <c>GError**</c> direction is refused, which is what keeps
    /// <c>gst_message_parse_error</c> and its relatives out of the pipeline.
    /// </summary>
    [Fact]
    public void AnOutErrorParameterIsRejected()
    {
        Assert.DoesNotContain("public void PopError(", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A borrowed error that comes back is read into a managed value, eagerly
    /// and without freeing anything.
    /// </summary>
    [Fact]
    public void ABorrowedErrorReturnIsCopiedIntoAManagedValue()
    {
        Assert.Equal(
            """
            public Gst.GLib.GException? GetError()
            {
                nint nativeResult = GstWidgetGetError(Handle);
                System.GC.KeepAlive(this);
                return Gst.GLib.GException.FromBorrowed(nativeResult);
            }
            """,
            Run.Member("Widget.cs", "public Gst.GLib.GException? GetError("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A signal hands its handler the same managed value. A nullable argument
    /// is nullable on the surface; a non nullable one is checked, because the
    /// trampoline has to answer for a promise the gir made.
    /// </summary>
    [Fact]
    public void ASignalHandsItsHandlerTheErrorItBorrows()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains("public Gst.GLib.GException Error { get; }", source, StringComparison.Ordinal);
        Assert.Contains("public Gst.GLib.GException? Warning { get; }", source, StringComparison.Ordinal);

        Assert.Contains(
            """
                        Gst.GLib.GException errorValue = Gst.GLib.GException.FromBorrowed(error)
                            ?? throw new InvalidOperationException("The failed signal of GstWidget passed no error.");
                        Gst.GLib.GException? warningValue = Gst.GLib.GException.FromBorrowed(warning);
            """,
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A signal argument the emitter transfers is refused for the same reason
    /// a transferred parameter is: nothing in the emission says who frees it.
    /// </summary>
    [Fact]
    public void ATransferredSignalErrorArgumentIsRejected()
    {
        Assert.DoesNotContain("TakenErrorSignalArgs", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A handler that transfers a string out hands over a copy the emitting
    /// library frees, and <see langword="null"/> is the null pointer the
    /// emission reads as no answer.
    /// </summary>
    [Fact]
    public void AnOwnedStringReturnIsCopiedForTheEmitter()
    {
        string source = Run.File("Widget.cs");

        Assert.Contains(
            "public delegate string? MissingNameHandler(object? sender, Gst.Widget.MissingNameSignalArgs args);",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "            return Gst.Interop.GMarshal.StringToUtf8Ptr(result);",
            source,
            StringComparison.Ordinal);

        // The raw return of the trampoline is a pointer, so the failure
        // literal of the no-handler and the exception path is the null pointer.
        Assert.Contains(
            "private static nint MissingNameTrampoline(nint instance, nint error, nint userData)",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>The contract of such a handler is stated on its delegate.</summary>
    [Fact]
    public void AnOwnedStringReturnDocumentsWhoFreesTheString()
    {
        Assert.Contains(
            """
                /// <remarks>
                /// The string the handler returns is copied into memory the emitting library
                /// owns and frees. Returning <see langword="null"/> answers no value, and what
                /// the emission makes of that is the contract of the signal, stated in its own
                /// returns documentation.
                /// </remarks>
            """,
            Run.File("Widget.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A borrowed string return stays refused: nobody would own the string,
    /// and no annotation of the emission says who does.
    /// </summary>
    [Fact]
    public void ABorrowedStringReturnIsRejected()
    {
        Assert.DoesNotContain("BorrowedNameHandler", Run.File("Widget.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A callback that receives an error is refused: a trampoline would need a
    /// delegate contract for how long the value stays valid, which nothing
    /// states.
    /// </summary>
    [Fact]
    public void ACallbackThatReceivesAnErrorIsRejected()
    {
        string source = Run.File("Widget.cs");

        Assert.DoesNotContain("FailFunc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public void SetFailFunction(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A signal argument is addressed by the GObject spelling of its signal,
    /// and the correction reaches the public type rather than only the
    /// nullability flag behind it.
    /// </summary>
    [Fact]
    public void ASignalOverlayCorrectsTheNullabilityOfAnArgument()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "Gst.Widget::failed#error": { "nullable": true } }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.Contains("public Gst.GLib.GException? Error { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "            Gst.GLib.GException? errorValue = Gst.GLib.GException.FromBorrowed(error);",
            source,
            StringComparison.Ordinal);

        // The key of a signal says nothing about the callable of the same
        // namespace: the parameter of Report is still non nullable.
        Assert.Contains(
            "public void Report(Gst.GLib.GException error, string? debug)",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the same rule: a <c>c:identifier</c> key corrects the
    /// callable and leaves the signal of the same type alone.
    /// </summary>
    [Fact]
    public void ACallableOverlayLeavesTheSignalOfTheSameTypeAlone()
    {
        FixtureRun run = RunWithOverlay(
            """
            {
              "annotationOverrides": { "gst_widget_report#error": { "nullable": true } }
            }
            """);

        string source = run.File("Widget.cs");

        Assert.Contains(
            "public void Report(Gst.GLib.GException? error, string? debug)",
            source,
            StringComparison.Ordinal);

        // A nullable error is not guarded and carries no exception note, but
        // it is still validated: an error that is there needs a domain.
        Assert.DoesNotContain("ArgumentNullException.ThrowIfNull(error);", source, StringComparison.Ordinal);
        Assert.Contains(
            "        Gst.GLib.GException.ValidateForNative(error, nameof(error));",
            source,
            StringComparison.Ordinal);

        Assert.Contains("public Gst.GLib.GException Error { get; }", source, StringComparison.Ordinal);
    }

    private static FixtureRun RunWithOverlay(string fixups)
    {
        string directory = Path.Combine(Path.GetTempPath(), "GstSharp.Generator.Tests", Path.GetRandomFileName());
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "fixups.json"), fixups);
            return Fixture.Run(Body, Overlays.Load(directory), GLibNamespace);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
