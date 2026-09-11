using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>GLib.Bytes</c> row of <c>MarshalPlanner.RuntimeTypes</c>: a boxed
/// type of the hand written runtime is planned with the <c>Wrapper</c> flavour,
/// exactly like <c>GLib.DateTime</c>, so a borrowed in parameter is a plain
/// handle, a transferred return is adopted through the typed factory, and a
/// signal that carries one lends it to the handler.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through the GBytes surface of
/// <c>GstRtp</c>, <c>GstSdp</c> and <c>GstWebRTC</c>, but only in the members
/// those happen to be. The fixtures here are the definition of the feature:
/// they name the four positions the type reaches — a <c>GBytes*</c> in under
/// <c>transfer-ownership="none"</c>, one under <c>full</c>, a returned one
/// under <c>full</c>, and one carried by a signal — and they pin the emitted
/// text of each.
/// </para>
/// <para>
/// The signal is the position the hand written binding of
/// <c>on-message-data</c> used to fill. What it has to keep is the release: a
/// signal lends the block to its handler, so the wrapper the trampoline builds
/// is a <c>using</c> that is disposed when the handler returns, and the
/// argument the handler was given is a wrapper of a block it no longer holds.
/// </para>
/// </remarks>
public sealed class GLibBytesRuntimeTypeTests
{
    /// <summary>
    /// A <c>GLib</c> namespace with the one record the fixtures refer to. It is
    /// a stand in for the vendored <c>GLib-2.0.gir</c>: only the attributes
    /// that decide the classification are kept, which for <c>GBytes</c> are
    /// the opacity and the boxed registration.
    /// </summary>
    private const string GLibNamespace =
        """
          <namespace name="GLib" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <record name="Bytes" c:type="GBytes" opaque="1" glib:type-name="GBytes" glib:get-type="g_bytes_get_type" c:symbol-prefix="bytes">
            </record>
          </namespace>
        """;

    /// <summary>
    /// A class whose three members and one signal are the four shapes under
    /// test: a transferred <c>GBytes</c> in, a borrowed one in, a transferred
    /// one returned, and one a signal carries.
    /// </summary>
    private const string Body =
        """
            <class name="Blob" c:type="GstBlob" parent="GObject.Object" glib:type-name="GstBlob" glib:get-type="gst_blob_get_type">
              <method name="take_block" c:identifier="gst_blob_take_block">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="blob" transfer-ownership="none">
                    <type name="Blob" c:type="GstBlob*"/>
                  </instance-parameter>
                  <parameter name="block" transfer-ownership="full" nullable="1">
                    <type name="GLib.Bytes" c:type="GBytes*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="set_block" c:identifier="gst_blob_set_block">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="blob" transfer-ownership="none">
                    <type name="Blob" c:type="GstBlob*"/>
                  </instance-parameter>
                  <parameter name="block" transfer-ownership="none">
                    <type name="GLib.Bytes" c:type="GBytes*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_block" c:identifier="gst_blob_get_block">
                <return-value transfer-ownership="full" nullable="1">
                  <type name="GLib.Bytes" c:type="GBytes*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="blob" transfer-ownership="none">
                    <type name="Blob" c:type="GstBlob*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <glib:signal name="on-block" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="data" transfer-ownership="none" nullable="1">
                    <type name="GLib.Bytes" c:type="GBytes*"/>
                  </parameter>
                </parameters>
              </glib:signal>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GLibNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A borrowed <c>GBytes</c> in parameter is a plain handle: a null guard,
    /// the handle in the call and a <c>GC.KeepAlive</c> after it. Nothing is
    /// minted and nothing is disposed, which is what the library expects of an
    /// argument it only reads.
    /// </summary>
    [Fact]
    public void ABorrowedBytesParameterIsPassedAsTheHandleOfTheRuntimeWrapper()
    {
        Assert.Equal(
            """
            public void SetBlock(Gst.GLib.Bytes block)
            {
                ArgumentNullException.ThrowIfNull(block);
                GstBlobSetBlock(Handle, block.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(block);
            }
            """,
            Run.Member("Blob.cs", "public void SetBlock("),
            StringComparer.Ordinal);

        // The import declares the handle as a pointer, not as anything the GLib
        // module would have to emit.
        Assert.Contains(
            "private static partial void GstBlobSetBlock(nint blob, nint block);",
            Run.File("Blob.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A transferred <c>GBytes</c> in parameter is a consumed argument of the
    /// boxed family: the call is handed a <c>BoxedCopy</c> — which for
    /// <c>GBytes</c> is <c>g_bytes_ref</c> — and the wrapper is disposed when
    /// the member returns.
    /// </summary>
    [Fact]
    public void ATransferredBytesParameterIsConsumedAsABoxedValue()
    {
        Assert.Equal(
            """
            public void TakeBlock(Gst.GLib.Bytes? block)
            {
                nint instanceHandle = Handle;
                nint blockNative = block is null ? 0 : block.Handle;
                nuint blockType = block is null ? 0 : block.BoxedType.Value;
                nint blockOwned = block is null ? 0 : Gst.Interop.GObjectNative.BoxedCopy(blockType, blockNative);
                GstBlobTakeBlock(instanceHandle, blockOwned);
                System.GC.KeepAlive(this);
                block?.Dispose();
            }
            """,
            Run.Member("Blob.cs", "public void TakeBlock("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A transferred <c>GBytes</c> return is adopted through the typed
    /// <c>FromNative</c> of the runtime wrapper, which maps the null pointer
    /// onto <see langword="null"/>.
    /// </summary>
    [Fact]
    public void ATransferredBytesReturnIsAdoptedThroughFromNative()
    {
        Assert.Equal(
            """
            public Gst.GLib.Bytes? GetBlock()
            {
                nint nativeResult = GstBlobGetBlock(Handle);
                System.GC.KeepAlive(this);
                return Gst.GLib.Bytes.FromNative(nativeResult, Gst.Interop.Transfer.Full);
            }
            """,
            Run.Member("Blob.cs", "public Gst.GLib.Bytes? GetBlock("),
            StringComparer.Ordinal);

        Assert.Equal(0, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// A signal that carries a <c>GBytes</c> is emitted, and the block reaches
    /// the handler as a wrapper the trampoline borrows and gives back: the
    /// local is a <c>using</c> of a <c>Transfer.None</c> wrap, so the handler
    /// may read the block and has to copy anything it keeps.
    /// </summary>
    [Fact]
    public void ASignalBytesArgumentIsLentToTheHandlerAndReleasedAfterwards()
    {
        string source = Run.File("Blob.cs");

        Assert.Equal(1, Run.Result.Census.EmittedCount("Gst", "signal"));
        Assert.Contains("public Gst.GLib.Bytes? Data { get; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "using Gst.GLib.Bytes? dataValue = Gst.GLib.Bytes.FromNative(data, Gst.Interop.Transfer.None);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "public event System.EventHandler<Gst.Blob.OnBlockSignalArgs> OnBlock",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Naming the type emits nothing of the GLib namespace: the declaration
    /// stays the hand written <c>Gst.GLib.Bytes</c> of the runtime.
    /// </summary>
    [Fact]
    public void TheBytesDeclarationItselfIsNotEmitted()
    {
        Assert.False(Run.HasFile("Bytes.cs"));
    }
}
