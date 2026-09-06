using Gst;
using Gst.Gio;
using Gst.Rtsp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The <c>GstRtsp</c> binding against the library that is installed: a URL is
/// parsed and written back, a request message is built and read back, and the
/// TLS validation flags of a connection travel through the hand written
/// <see cref="TlsCertificateFlags"/>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here opens a socket. Everything the module does before the first
/// byte goes out — parsing, message building, configuring a connection — is
/// local, and that is the part a test can measure without a server.
/// </para>
/// <para>
/// The last test is what this wave added the runtime-enumeration support for.
/// <c>Gio.TlsCertificateFlags</c> belongs to a module that emits nothing, so the
/// three callables that carry it used to skip — the two accessors below and the
/// accept-certificate callback. They are now spelled with the enumeration of
/// <c>src/GstSharp.Net/Core/Gio</c> and cross as the 32 bit integer the gir
/// declares. Reading the flags back out of the library is what says that both
/// directions of that cast agree with the C ABI.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspTests
{
    /// <summary>
    /// A URL with credentials, a port and a path. The request URI the library
    /// builds from it is the one that goes out on the wire, and RTSP puts the
    /// credentials in an <c>Authorization</c> header rather than in the URI, so
    /// they are not part of it.
    /// </summary>
    [Fact]
    public void AUrlParsesIntoTheRequestUriItIsBuiltFrom()
    {
        Assert.Equal(
            RTSPResult.Ok,
            RTSPUrl.Parse("rtsp://user:pw@host.example:8554/stream", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            Assert.Equal(RTSPResult.Ok, url.GetPort(out ushort port));
            Assert.Equal(8554, port);

            Assert.Equal("rtsp://host.example:8554/stream", url.GetRequestUri());

            // A control path is resolved against the path of the URL the way a
            // relative reference is, so it replaces the last segment rather
            // than being appended to it.
            Assert.Equal(
                "rtsp://host.example:8554/streamid=0",
                url.GetRequestUriWithControl("streamid=0"));

            // The port is part of the URL rather than of the request only, so
            // setting it changes what the next request URI reads. It is spelled
            // out even when it is the default port of the scheme.
            Assert.Equal(RTSPResult.Ok, url.SetPort(554));
            Assert.Equal("rtsp://host.example:554/stream", url.GetRequestUri());
        }
    }

    /// <summary>
    /// A request message is initialised, given two headers and read back, and
    /// the kind of the message is asked for through the renamed accessor.
    /// </summary>
    /// <remarks>
    /// <c>gst_rtsp_message_get_type</c> would be emitted as <c>GetType</c>,
    /// which every object already declares, so fixups.json renames it to
    /// <see cref="RTSPMessage.GetMessageType"/>. Without the rename the kind of
    /// a message is unreadable, which is what makes it the one mandatory
    /// overlay entry of this module.
    /// </remarks>
    [Fact]
    public void ARequestCarriesTheMethodAndTheHeadersItWasGiven()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal(RTSPMsgType.Invalid, message.GetMessageType());

            Assert.Equal(
                RTSPResult.Ok,
                message.InitRequest(RTSPMethod.Options, "rtsp://host.example:8554/stream"));
            Assert.Equal(RTSPMsgType.Request, message.GetMessageType());

            Assert.Equal(RTSPResult.Ok, message.AddHeader(RTSPHeaderField.Cseq, "1"));
            Assert.Equal(RTSPResult.Ok, message.AddHeaderByName("User-Agent", "GstSharp.Net"));

            Assert.Equal(RTSPResult.Ok, message.GetHeader(RTSPHeaderField.Cseq, out string? cseq, 0));
            Assert.Equal("1", cseq);
            Assert.Equal(RTSPResult.Ok, message.GetHeaderByName("User-Agent", out string? agent, 0));
            Assert.Equal("GstSharp.Net", agent);

            Assert.Equal(
                RTSPResult.Ok,
                message.ParseRequest(out RTSPMethod method, out string? uri, out RTSPVersion version));
            Assert.Equal(RTSPMethod.Options, method);
            Assert.Equal("rtsp://host.example:8554/stream", uri);
            Assert.Equal(RTSPVersion.V1_0, version);

            Assert.Equal(RTSPResult.Ok, message.RemoveHeader(RTSPHeaderField.Cseq, 0));
            Assert.Equal(
                RTSPResult.Enotimpl,
                message.GetHeader(RTSPHeaderField.Cseq, out string? removed, 0));
            Assert.Null(removed);
        }
    }

    /// <summary>
    /// The headers of a message are serialised into a
    /// <see cref="System.Text.StringBuilder"/> in the form they take on the
    /// wire.
    /// </summary>
    /// <remarks>
    /// <c>gst_rtsp_message_append_headers</c> writes <c>Name: value</c> and a
    /// CRLF per header and nothing else — no request line, no blank line
    /// closing the block — so the whole of the output is the two headers below,
    /// and what the builder already held is still in front of them.
    /// </remarks>
    [Fact]
    public void TheHeadersOfARequestAreAppendedInTheirWireForm()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal(
                RTSPResult.Ok,
                message.InitRequest(RTSPMethod.Options, "rtsp://host.example:8554/stream"));

            System.Text.StringBuilder builder = new("OPTIONS rtsp://host.example:8554/stream RTSP/1.0\r\n");

            // A message with no header appends nothing, and leaves what the
            // builder held alone.
            Assert.Equal(RTSPResult.Ok, message.AppendHeaders(builder));
            Assert.Equal("OPTIONS rtsp://host.example:8554/stream RTSP/1.0\r\n", builder.ToString());

            Assert.Equal(RTSPResult.Ok, message.AddHeader(RTSPHeaderField.Cseq, "1"));
            Assert.Equal(RTSPResult.Ok, message.AddHeaderByName("User-Agent", "GstSharp.Net"));

            Assert.Equal(RTSPResult.Ok, message.AppendHeaders(builder));

            string text = builder.ToString();
            Assert.Equal(
                "OPTIONS rtsp://host.example:8554/stream RTSP/1.0\r\n"
                + "CSeq: 1\r\n"
                + "User-Agent: GstSharp.Net\r\n",
                text);

            // The null argument is refused before anything native happens.
            Assert.Throws<ArgumentNullException>(() => message.AppendHeaders(null!));
        }
    }

    /// <summary>
    /// The validation flags of a connection are written and read back through
    /// the hand written <see cref="TlsCertificateFlags"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gst_rtsp_connection_create</c> allocates the connection and copies the
    /// URL into it; it opens nothing, which is why the test needs no server. The
    /// wrapper of an opaque record owns nothing either, so the connection is
    /// released by the explicit <see cref="RTSPConnection.Free"/> that pairs
    /// with it rather than by a <c>using</c>.
    /// </para>
    /// <para>
    /// The flags are stored on the connection and applied when the TLS
    /// handshake happens, so they are readable on a connection that was never
    /// connected.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheValidationFlagsOfAConnectionRoundTripThroughTheHandWrittenEnumeration()
    {
        Assert.Equal(RTSPResult.Ok, RTSPUrl.Parse("rtsps://host.example:322/stream", out RTSPUrl? url));
        Assert.NotNull(url);

        using (url)
        {
            Assert.Equal(RTSPResult.Ok, RTSPConnection.Create(url, out RTSPConnection? connection));
            Assert.NotNull(connection);

            try
            {
                // The default is every check the platform knows.
                Assert.Equal(TlsCertificateFlags.ValidateAll, connection.GetTlsValidationFlags());

                Assert.True(
                    connection.SetTlsValidationFlags(
                        TlsCertificateFlags.Expired | TlsCertificateFlags.UnknownCa));
                Assert.Equal(
                    TlsCertificateFlags.Expired | TlsCertificateFlags.UnknownCa,
                    connection.GetTlsValidationFlags());

                // Nothing has negotiated TLS, so there is no database and no
                // interaction on the connection yet.
                Assert.Null(connection.GetTlsDatabase());
                Assert.Null(connection.GetTlsInteraction());
            }
            finally
            {
                Assert.Equal(RTSPResult.Ok, connection.Free());
            }
        }
    }

    /// <summary>
    /// The hand written <c>GetTransports</c> reads the string the C function
    /// writes through its <c>gchar**</c>, and answers <see langword="null"/>
    /// where nothing wrote one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>gst_rtsp_extension_get_transports</c> is a vfunc dispatcher that
    /// leaves the destination untouched when the extension implements no
    /// <c>get_transports</c>
    /// (<c>gst-plugins-base/gst-libs/gst/rtsp/gstrtspextension.c:169-180</c>),
    /// which is the case for every implementation the GStreamer tree still
    /// carries: <c>rtspwms</c> fills five slots of the interface and not this
    /// one (<c>gst-plugins-ugly/gst/asfdemux/gstrtspwms.c:235-239</c>). What
    /// this measures is therefore the marshalling and the contract around it —
    /// the call is made, <see cref="RTSPResult.Ok"/> comes back and the out
    /// parameter is written before the member returns — and not an extension
    /// that names a transport, which nothing installable can produce.
    /// </para>
    /// <para>
    /// The shipped overload takes a <see cref="string"/> and cannot receive
    /// that destination at all, which is why it is obsolete and why this test
    /// does not call it.
    /// </para>
    /// <para>
    /// <c>rtspwms</c> comes with <c>gst-plugins-ugly</c>, so an installation
    /// that carries only the core and the good plugins skips this test rather
    /// than failing it.
    /// </para>
    /// </remarks>
    [RequiresElementFact("rtspwms")]
    public void AnExtensionThatImplementsNoTransportReaderAnswersNoTransport()
    {
        using Element extension = ElementFactory.Make("rtspwms", null)
            ?? throw new InvalidOperationException("rtspwms is what the fact gated on.");

        // The element implements GstRTSPExtension in C; nothing generated
        // declares a managed type for that, because no gir type does, so the
        // handle is carried into the interface by a shim. GetTransports reads
        // the handle and nothing else of it.
        ExtensionOf shim = new(extension.Handle);

        Assert.Equal(
            RTSPResult.Ok,
            shim.GetTransports(RTSPLowerTrans.Udp | RTSPLowerTrans.Tcp, out string? transport));
        Assert.Null(transport);

        GC.KeepAlive(extension);
    }

    /// <summary>
    /// An element that implements <c>GstRTSPExtension</c> in C, seen as the
    /// interface.
    /// </summary>
    /// <param name="handle">The native instance.</param>
    private sealed class ExtensionOf(nint handle) : IRTSPExtension
    {
        /// <summary>Gets the native instance that implements the interface.</summary>
        public nint Handle { get; } = handle;
    }
}
