using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// The <c>Gio</c> rows of <c>MarshalPlanner.RuntimeTypes</c> and
/// <c>MarshalPlanner.RuntimeEnums</c>: a type of a module that emits nothing
/// still reaches generated signatures, as long as the runtime hand writes it.
/// </summary>
/// <remarks>
/// <para>
/// The vendored girs exercise this through <c>GstNet</c> and <c>GstRtsp</c>, but
/// only for the shapes those two happen to declare. The fixtures here are the
/// definition of the feature: they name the four positions a <c>Gio</c> type
/// reaches — a handle in and out, an enumeration in and out — and they pin the
/// emitted text of each, which the committed output of a module only pins for
/// the members it has.
/// </para>
/// <para>
/// The enumeration fixtures are what says that the value crosses as the integer
/// the gir declares for it and not as a raw <see langword="nint"/>, which is the
/// ABI a call passing a 32 bit enumeration would be broken by. The two maps stay
/// separate for that reason: a handle crosses as a pointer, an enumeration as
/// its underlying integer.
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
            <enumeration name="TlsAuthenticationMode" c:type="GTlsAuthenticationMode" glib:type-name="GTlsAuthenticationMode" glib:get-type="g_tls_authentication_mode_get_type">
              <member name="none" value="0" c:identifier="G_TLS_AUTHENTICATION_NONE"/>
              <member name="required" value="2" c:identifier="G_TLS_AUTHENTICATION_REQUIRED"/>
            </enumeration>
            <enumeration name="SocketFamily" c:type="GSocketFamily" glib:type-name="GSocketFamily" glib:get-type="g_socket_family_get_type">
              <member name="invalid" value="0" c:identifier="G_SOCKET_FAMILY_INVALID"/>
              <member name="ipv6" value="10" c:identifier="G_SOCKET_FAMILY_IPV6"/>
            </enumeration>
          </namespace>
        """;

    /// <summary>
    /// A class whose four members are the four shapes under test: a
    /// <c>Gio</c> handle in, a <c>Gio</c> handle out under
    /// <c>transfer-ownership="full"</c>, a <c>Gio</c> enumeration in and a
    /// <c>Gio</c> enumeration out.
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
              <method name="get_validation_flags" c:identifier="gst_connection_get_validation_flags">
                <return-value transfer-ownership="none">
                  <type name="Gio.TlsCertificateFlags" c:type="GTlsCertificateFlags"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_authentication_mode" c:identifier="gst_connection_set_authentication_mode">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                  <parameter name="mode" transfer-ownership="none">
                    <type name="Gio.TlsAuthenticationMode" c:type="GTlsAuthenticationMode"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_authentication_mode" c:identifier="gst_connection_get_authentication_mode">
                <return-value transfer-ownership="none">
                  <type name="Gio.TlsAuthenticationMode" c:type="GTlsAuthenticationMode"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <method name="set_family" c:identifier="gst_connection_set_family">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                  <parameter name="family" transfer-ownership="none">
                    <type name="Gio.SocketFamily" c:type="GSocketFamily"/>
                  </parameter>
                </parameters>
              </method>
              <method name="get_family" c:identifier="gst_connection_get_family">
                <return-value transfer-ownership="none">
                  <type name="Gio.SocketFamily" c:type="GSocketFamily"/>
                </return-value>
                <parameters>
                  <instance-parameter name="connection" transfer-ownership="none">
                    <type name="Connection" c:type="GstConnection*"/>
                  </instance-parameter>
                </parameters>
              </method>
              <glib:signal name="family-changed" when="last">
                <return-value transfer-ownership="none">
                  <type name="none" c:type="void"/>
                </return-value>
                <parameters>
                  <parameter name="family" transfer-ownership="none">
                    <type name="Gio.SocketFamily" c:type="GSocketFamily"/>
                  </parameter>
                </parameters>
              </glib:signal>
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
    /// A <c>Gio</c> enumeration that is passed in is spelled as the hand written
    /// enumeration and crosses as the integer its gir declares, which is the
    /// same projection a generated enumeration gets.
    /// </summary>
    [Fact]
    public void AGioEnumerationParameterIsPassedAsTheUnderlyingIntegerOfItsGir()
    {
        Assert.Equal(
            """
            public void SetValidationFlags(Gst.Gio.TlsCertificateFlags flags)
            {
                GstConnectionSetValidationFlags(Handle, (int)flags);
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Connection.cs", "public void SetValidationFlags("),
            StringComparer.Ordinal);

        // The import declares the 32 bit enumeration the C function takes. The
        // members of GTlsCertificateFlags run to 127, so int is what
        // EnumFacts derives and what Gst.Gio.TlsCertificateFlags is declared
        // with; a raw nint here would be the wrong ABI.
        Assert.Contains(
            "private static partial void GstConnectionSetValidationFlags(nint connection, int flags);",
            Run.File("Connection.cs"),
            StringComparison.Ordinal);

        Assert.Equal(0, Run.Result.Census.SkippedCount("Gst", SkipReason.UnsupportedSignature));
    }

    /// <summary>
    /// The second plain enumeration of the map, which the
    /// <c>GstRtspServer</c> authentication surface names in both directions.
    /// It has no converter, because <c>GTlsAuthenticationMode</c> numbers its
    /// three members itself rather than from the platform.
    /// </summary>
    [Fact]
    public void TheAuthenticationModeOfTheMapCrossesAsItsUnderlyingInteger()
    {
        Assert.Equal(
            """
            public void SetAuthenticationMode(Gst.Gio.TlsAuthenticationMode mode)
            {
                GstConnectionSetAuthenticationMode(Handle, (int)mode);
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Connection.cs", "public void SetAuthenticationMode("),
            StringComparer.Ordinal);

        Assert.Equal(
            """
            public Gst.Gio.TlsAuthenticationMode GetAuthenticationMode()
            {
                int nativeResult = GstConnectionGetAuthenticationMode(Handle);
                System.GC.KeepAlive(this);
                return (Gst.Gio.TlsAuthenticationMode)nativeResult;
            }
            """,
            Run.Member("Connection.cs", "public Gst.Gio.TlsAuthenticationMode GetAuthenticationMode("),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A <c>Gio</c> enumeration that comes back is cast out of the same integer,
    /// so the return position is bound as well as the parameter one.
    /// </summary>
    [Fact]
    public void AGioEnumerationReturnIsCastOutOfTheUnderlyingInteger()
    {
        Assert.Equal(
            """
            public Gst.Gio.TlsCertificateFlags GetValidationFlags()
            {
                int nativeResult = GstConnectionGetValidationFlags(Handle);
                System.GC.KeepAlive(this);
                return (Gst.Gio.TlsCertificateFlags)nativeResult;
            }
            """,
            Run.Member("Connection.cs", "public Gst.Gio.TlsCertificateFlags GetValidationFlags("),
            StringComparer.Ordinal);

        Assert.Contains(
            "private static partial int GstConnectionGetValidationFlags(nint connection);",
            Run.File("Connection.cs"),
            StringComparison.Ordinal);
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
    /// A <c>Gio</c> enumeration whose native numbers are not the ones of the
    /// gir crosses through the converter of the runtime rather than through a
    /// cast, in every direction the emitters convert in.
    /// </summary>
    /// <remarks>
    /// <c>GSocketFamily</c> is defined from the <c>AF_*</c> constants of the
    /// platform, so the number of <c>AF_INET6</c> differs per operating system
    /// while the hand written enumeration keeps the one of the gir.
    /// </remarks>
    [Fact]
    public void AConvertedGioEnumerationCrossesThroughTheConverterOfTheRuntime()
    {
        Assert.Equal(
            """
            public void SetFamily(Gst.Gio.SocketFamily family)
            {
                GstConnectionSetFamily(Handle, Gst.Gio.SocketFamilyNative.ToNative(family));
                System.GC.KeepAlive(this);
            }
            """,
            Run.Member("Connection.cs", "public void SetFamily("),
            StringComparer.Ordinal);

        Assert.Equal(
            """
            public Gst.Gio.SocketFamily GetFamily()
            {
                int nativeResult = GstConnectionGetFamily(Handle);
                System.GC.KeepAlive(this);
                return Gst.Gio.SocketFamilyNative.FromNative(nativeResult);
            }
            """,
            Run.Member("Connection.cs", "public Gst.Gio.SocketFamily GetFamily("),
            StringComparer.Ordinal);

        // The handler of a signal is handed the converted value too.
        Assert.Contains(
            "Gst.Gio.SocketFamily familyValue = Gst.Gio.SocketFamilyNative.FromNative(family);",
            Run.File("Connection.cs"),
            StringComparison.Ordinal);
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

        // Naming the enumeration in a signature does not emit it either: the
        // declaration stays the hand written one in src/GstSharp.Net/Core/Gio.
        Assert.False(Run.HasFile("Enums.cs"));
        Assert.DoesNotContain("Gst.Gio", Run.File("_Module.cs"), StringComparison.Ordinal);
    }
}
