using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>Gio</c> row of <c>MarshalPlanner.RuntimeTypes</c>: a type of a module
/// that emits nothing still reaches generated signatures, as long as the
/// runtime hand writes it.
/// </summary>
/// <remarks>
/// <para>
/// No vendored gir exercises this yet — nothing in the modules that are
/// generated today mentions Gio — so the feature has no coverage from the
/// committed output and none from the verify gate either. These fixtures are
/// what says that it works, and they are what the first module to reference Gio
/// relies on.
/// </para>
/// <para>
/// The third fixture is the boundary: <c>Gio</c> handles are planned and
/// <c>Gio</c> enumerations are not, because the planner has no way of learning
/// the underlying type of an enumeration it does not emit. The member that
/// takes one has to skip cleanly rather than come out as a raw
/// <see langword="nint"/>, which would declare the wrong ABI for a call that
/// passes a 32 bit enumeration.
/// </para>
/// </remarks>
public sealed class GioRuntimeTypeTests
{
    /// <summary>
    /// A <c>Gio</c> namespace with the two classes and the one bitfield the
    /// fixtures below refer to. It is a stand in for the vendored
    /// <c>Gio-2.0.gir</c>: only the attributes that decide the classification
    /// are kept.
    /// </summary>
    private const string GioNamespace =
        """
          <namespace name="Gio" version="2.0" c:identifier-prefixes="G" c:symbol-prefixes="g">
            <class name="Cancellable" c:type="GCancellable" parent="GObject.Object" glib:type-name="GCancellable" glib:get-type="g_cancellable_get_type">
            </class>
            <class name="Socket" c:type="GSocket" parent="GObject.Object" glib:type-name="GSocket" glib:get-type="g_socket_get_type">
            </class>
            <bitfield name="TlsCertificateFlags" c:type="GTlsCertificateFlags" glib:type-name="GTlsCertificateFlags" glib:get-type="g_tls_certificate_flags_get_type">
              <member name="no_flags" value="0" c:identifier="G_TLS_CERTIFICATE_NO_FLAGS"/>
              <member name="validate_all" value="127" c:identifier="G_TLS_CERTIFICATE_VALIDATE_ALL"/>
            </bitfield>
          </namespace>
        """;

    /// <summary>
    /// A class whose three members are the three shapes under test: a
    /// <c>Gio</c> handle in, a <c>Gio</c> handle out under
    /// <c>transfer-ownership="full"</c>, and a <c>Gio</c> enumeration in.
    /// </summary>
    private const string Body =
        """
            <class name="Connection" c:type="GstConnection" parent="GObject.Object" glib:type-name="GstConnection" glib:get-type="gst_connection_get_type">
              <method name="set_socket" c:identifier="gst_connection_set_socket">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                  <parameter name="socket" transfer-ownership="none">
                    <type name="Gio.Socket" c:type="GSocket*"/>
                  </parameter>
                </parameters>
              </method>
              <method name="steal_cancellable" c:identifier="gst_connection_steal_cancellable">
                <return-value transfer-ownership="full">
                  <type name="Gio.Cancellable" c:type="GCancellable*"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_validation_flags" c:identifier="gst_connection_set_validation_flags">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                  <parameter name="flags" transfer-ownership="none">
                    <type name="Gio.TlsCertificateFlags" c:type="GTlsCertificateFlags"/>
                  </parameter>
                </parameters>
              </method>
            </class>
        """;

    private static readonly Lazy<FixtureRun> LazyRun = new(
        static () => Fixture.Run(Body, overlays: null, extraNamespaces: GioNamespace),
        isThreadSafe: true);

    private static FixtureRun Run => LazyRun.Value;

    /// <summary>
    /// A <c>Gio</c> handle that is passed in is spelled as the hand written
    /// wrapper and crosses as its handle, exactly like a generated one.
    /// </summary>
    [Fact]
    public void AGioHandleParameterIsPassedAsTheHandleOfTheRuntimeWrapper()
    {
        Assert.Equal(
            """
            public void SetSocket(Gst.Gio.Socket socket)
            {
                ArgumentNullException.ThrowIfNull(socket);
                GstConnectionSetSocket(Handle, socket.Handle);
                System.GC.KeepAlive(this);
                System.GC.KeepAlive(socket);
            }
            """,
            Run.Member("Connection.cs", "public void SetSocket("),
            StringComparer.Ordinal);

        // The import declares the handle as a pointer, not as anything the Gio
        // module would have to emit.
        Assert.Contains(
            "private static partial void GstConnectionSetSocket(nint connection, nint socket);",
            Run.File("Connection.cs"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>Gio</c> handle that comes back is adopted through the ordinary
    /// <c>FromNative</c> of the runtime, which is what the type registry entries
    /// of the hand written Gio module make resolvable.
    /// </summary>
    [Fact]
    public void AGioHandleReturnIsAdoptedThroughFromNative()
    {
        Assert.Equal(
            """
            public Gst.Gio.Cancellable StealCancellable()
            {
                nint nativeResult = GstConnectionStealCancellable(Handle);
                System.GC.KeepAlive(this);
                return Gst.GObject.Object.FromNative<Gst.Gio.Cancellable>(nativeResult, Gst.Interop.Transfer.Full)
                    ?? throw new InvalidOperationException("gst_connection_steal_cancellable returned no value.");
            }
            """,
            Run.Member("Connection.cs", "public Gst.Gio.Cancellable StealCancellable("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A <c>Gio</c> enumeration is not planned: the member skips, and it skips
    /// rather than degrading to a raw pointer.
    /// </summary>
    [Fact]
    public void AGioEnumerationIsStillUnplannable()
    {
        string source = Run.File("Connection.cs");

        Assert.DoesNotContain("SetValidationFlags", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TlsCertificateFlags", source, StringComparison.Ordinal);
        Assert.Equal(1, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// The module row of <c>Gio</c> is there for resolution only: it names the
    /// assembly and the C# namespace of the hand written wrappers, and emits
    /// nothing.
    /// </summary>
    [Fact]
    public void TheGioModuleRowIsResolutionOnly()
    {
        ModuleInfo? gio = ModuleMap.Find("Gio");

        Assert.NotNull(gio);
        Assert.Equal("Gst.Gio", gio.ClrNamespace, StringComparer.Ordinal);
        Assert.Equal("GstSharp.Net", gio.ProjectDirectory, StringComparer.Ordinal);
        Assert.Equal("Gio", gio.NativeLibrary, StringComparer.Ordinal);
        Assert.False(gio.IsGenerated);
    }

    /// <summary>
    /// Nothing of the <c>Gio</c> namespace itself is emitted: it has no
    /// generated module, so its own declarations produce no files and no
    /// registry entries.
    /// </summary>
    [Fact]
    public void TheGioNamespaceEmitsNothingOfItsOwn()
    {
        Assert.False(Run.HasFile("Socket.cs"));
        Assert.False(Run.HasFile("Cancellable.cs"));
        Assert.DoesNotContain("Gst.Gio", Run.File("_Module.cs"), StringComparison.Ordinal);
    }
}
