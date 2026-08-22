using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The handle arm of <c>MarshalPlanner.PlanSignalReturn</c>: a handler may
/// return a GObject it transfers ownership of, and nothing else that is not
/// blittable.
/// </summary>
/// <remarks>
/// <para>
/// The arm is a shape rather than a list: the generic marshal hands what the
/// handler returned to <c>g_value_take_object</c>, so the trampoline mints a
/// reference for the value it passes out and the wrapper the handler holds
/// stays the handler's own. The vendored girs exercise it through three
/// signals — <c>GESTimeline::select-element-track</c> and the two
/// <c>load-serialized-info</c> siblings — and the first fixture below pins the
/// emitted text.
/// </para>
/// <para>
/// The other three fixtures are the rejections, and they are here because no
/// vendored gir declares them: a transfer-none GObject return would make the
/// marshal take a reference nobody minted, and a mini object or a boxed value
/// needs a different minting function than <c>g_object_ref</c>. They fail
/// closed, and deleting them would let the arm widen silently.
/// </para>
/// </remarks>
public sealed class SignalHandleReturnTests
{
    /// <summary>
    /// A namespace whose one class carries four signals: a transferred GObject
    /// return, a borrowed one, a transferred mini object and a transferred
    /// boxed value.
    /// </summary>
    private const string Body =
        """
            <record name="MiniObject" c:type="GstMiniObject" glib:type-name="GstMiniObject" glib:get-type="gst_mini_object_get_type">
              <field name="type" writable="1">
                <type name="GType" c:type="GType"/>
              </field>
            </record>
            <record name="Message" c:type="GstMessage" glib:type-name="GstMessage" glib:get-type="gst_message_get_type">
              <field name="mini_object" writable="1">
                <type name="MiniObject" c:type="GstMiniObject"/>
              </field>
            </record>
            <record name="Payload" c:type="GstPayload" glib:type-name="GstPayload" glib:get-type="gst_payload_get_type">
              <field name="kind" writable="1">
                <type name="gint" c:type="gint"/>
              </field>
            </record>
            <class name="Track" c:type="GstTrack" parent="GObject.Object" glib:type-name="GstTrack" glib:get-type="gst_track_get_type">
            </class>
            <class name="Pad" c:type="GstPad" parent="GObject.Object" glib:type-name="GstPad" glib:get-type="gst_pad_get_type">
            </class>
            <class name="Element" c:type="GstElement" parent="GObject.Object" glib:type-name="GstElement" glib:get-type="gst_element_get_type">
              <glib:signal name="select-track" when="last">
                <return-value transfer-ownership="full" nullable="1">
                  <doc xml:space="preserve">the track to use, or %NULL</doc>
                  <type name="Track" c:type="GstTrack*"/>
                </return-value>
                <parameters>
                  <parameter name="pad" transfer-ownership="none">
                    <doc xml:space="preserve">the pad being selected for</doc>
                    <type name="Pad" c:type="GstPad*"/>
                  </parameter>
                </parameters>
              </glib:signal>
              <glib:signal name="borrow-track" when="last">
                <return-value transfer-ownership="none" nullable="1">
                  <type name="Track" c:type="GstTrack*"/>
                </return-value>
              </glib:signal>
              <glib:signal name="take-message" when="last">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="Message" c:type="GstMessage*"/>
                </return-value>
              </glib:signal>
              <glib:signal name="take-payload" when="last">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="Payload" c:type="GstPayload*"/>
                </return-value>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A signal whose handler transfers a GObject out gets its delegate, its
    /// event and a trampoline that mints the reference the marshal takes.
    /// </summary>
    [Fact]
    public void ATransferredGObjectReturnIsMintedByTheTrampoline()
    {
        string source = Run.File("Element.cs");

        Assert.Contains(
            "public delegate Gst.Track? SelectTrackHandler(object? sender, Gst.Element.SelectTrackSignalArgs args);",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "public event Gst.Element.SelectTrackHandler SelectTrack",
            source,
            StringComparison.Ordinal);

        // The wrapper is kept alive until the reference exists: the handle read
        // is its last use, and a handler that returns a freshly built wrapper
        // holds the only reference there is.
        Assert.Contains(
            """
                        nint owned = result is null ? 0 : Gst.Interop.GObjectNative.ObjectRef(result.Handle);
                        System.GC.KeepAlive(result);
                        return owned;
            """,
            source,
            StringComparison.Ordinal);

        // The raw signature of the trampoline is a pointer wide return, so the
        // failure literal of the no-handler and the exception path is the null
        // pointer the C side reads as "no answer".
        Assert.Contains(
            "private static nint SelectTrackTrampoline(nint instance, nint pad, nint userData)",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The delegate of such a signal says what the handler keeps: the value it
    /// returns stays its own, and what returning <see langword="null"/> means
    /// is left to the documentation of the signal itself, because it differs
    /// from one signal to the next.
    /// </summary>
    [Fact]
    public void ATransferredGObjectReturnDocumentsWhoOwnsTheValue()
    {
        Assert.Contains(
            """
                /// <remarks>
                /// The value the handler returns is handed to native code with a reference
                /// minted for it, so the wrapper the handler holds stays usable and is still
                /// the caller's to manage. Returning <see langword="null"/> answers no value,
                /// and what the emission makes of that is the contract of the signal, stated
                /// in its own returns documentation: it may go on to another handler, to the
                /// class handler, or read the empty answer as the result.
                /// </remarks>
            """,
            Run.File("Element.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A borrowed GObject return is rejected: the marshal takes ownership of
    /// what the handler returns, so binding one would hand native code a
    /// reference nobody minted.
    /// </summary>
    [Fact]
    public void ABorrowedGObjectReturnIsRejected()
    {
        Assert.DoesNotContain("BorrowTrackHandler", Run.File("Element.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred mini object return is rejected: minting one is
    /// <c>gst_mini_object_ref</c> rather than <c>g_object_ref</c>, and the arm
    /// is written for the reference the marshal takes.
    /// </summary>
    [Fact]
    public void ATransferredMiniObjectReturnIsRejected()
    {
        Assert.DoesNotContain("TakeMessageHandler", Run.File("Element.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred boxed return is rejected for the same reason: minting one
    /// is <c>g_boxed_copy</c>.
    /// </summary>
    [Fact]
    public void ATransferredBoxedReturnIsRejected()
    {
        Assert.DoesNotContain("TakePayloadHandler", Run.File("Element.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The three rejections are the whole of what the fixture skips, and they
    /// are skipped as an unsupported signature rather than dropped silently.
    /// </summary>
    [Fact]
    public void TheThreeRejectionsAreCountedAsUnsupportedSignatures()
    {
        Assert.Equal(3, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }
}
