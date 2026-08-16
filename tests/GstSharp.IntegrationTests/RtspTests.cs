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
            Assert.Equal(RTSPVersion._10, version);

            Assert.Equal(RTSPResult.Ok, message.RemoveHeader(RTSPHeaderField.Cseq, 0));
            Assert.Equal(
                RTSPResult.Enotimpl,
                message.GetHeader(RTSPHeaderField.Cseq, out string? removed, 0));
            Assert.Null(removed);
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
}
