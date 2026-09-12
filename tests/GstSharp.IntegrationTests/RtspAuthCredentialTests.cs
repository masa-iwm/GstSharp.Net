using Gst.Rtsp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The authentication credentials of a message, against the parser of the
/// installed library: a challenge with two schemes, an answer that carries a
/// blob instead of parameters, a header that is not there at all, and the
/// proof that what comes back is a copy rather than a window into the message.
/// </summary>
/// <remarks>
/// Nothing here opens a socket either. The credentials are what a client reads
/// off a 401 response and what a server reads off the request that answers it,
/// and both of those are header parsing, which is local.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class RtspAuthCredentialTests
{
    /// <summary>
    /// A <c>WWW-Authenticate</c> challenge with one <c>Digest</c> and one
    /// <c>Basic</c> scheme. Every field other than <c>Authorization</c> may
    /// carry more than one credential, and both of these take the parameter
    /// path, so the quoted values come back decoded.
    /// </summary>
    [Fact]
    public void AChallengeCarriesOneCredentialPerSchemeWithItsParameters()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal(
                RTSPResult.Ok,
                message.InitResponse(RTSPStatusCode.Unauthorized, "Unauthorized", null));
            Assert.Equal(
                RTSPResult.Ok,
                message.AddHeader(
                    RTSPHeaderField.WwwAuthenticate,
                    "Digest realm=\"x\", nonce=\"y\""));
            Assert.Equal(
                RTSPResult.Ok,
                message.AddHeader(RTSPHeaderField.WwwAuthenticate, "Basic realm=\"z\""));

            RTSPAuthCredential[] credentials =
                message.ParseAuthCredentials(RTSPHeaderField.WwwAuthenticate);

            Assert.Equal(2, credentials.Length);
            Assert.Equal(RTSPAuthMethod.Digest, credentials[0].Scheme);
            Assert.Equal(RTSPAuthMethod.Basic, credentials[1].Scheme);

            // A challenge states the scheme and its parameters; the
            // authorization blob is the answering direction only.
            Assert.Null(credentials[0].Authorization);
            Assert.Null(credentials[1].Authorization);

            RTSPAuthParam[] digest = credentials[0].GetParams();
            Assert.Equal(2, digest.Length);
            Assert.Equal("realm", digest[0].Name);
            Assert.Equal("x", digest[0].Value);
            Assert.Equal("nonce", digest[1].Name);
            Assert.Equal("y", digest[1].Value);

            RTSPAuthParam[] basic = credentials[1].GetParams();
            RTSPAuthParam single = Assert.Single(basic);
            Assert.Equal("realm", single.Name);
            Assert.Equal("z", single.Value);

            foreach (RTSPAuthParam parameter in digest)
            {
                parameter.Dispose();
            }

            single.Dispose();

            foreach (RTSPAuthCredential credential in credentials)
            {
                credential.Dispose();
            }
        }
    }

    /// <summary>
    /// An <c>Authorization</c> header with a <c>Basic</c> scheme: the C stops
    /// after the first credential of that field, and a basic answer carries its
    /// base64 blob rather than parameters.
    /// </summary>
    [Fact]
    public void ABasicAnswerCarriesItsBlobAndNoParameters()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal(
                RTSPResult.Ok,
                message.InitRequest(RTSPMethod.Describe, "rtsp://host.example:8554/stream"));
            Assert.Equal(
                RTSPResult.Ok,
                message.AddHeader(RTSPHeaderField.Authorization, "Basic dXNlcjpwYXNz"));

            RTSPAuthCredential[] credentials =
                message.ParseAuthCredentials(RTSPHeaderField.Authorization);

            RTSPAuthCredential credential = Assert.Single(credentials);
            using (credential)
            {
                Assert.Equal(RTSPAuthMethod.Basic, credential.Scheme);
                Assert.Equal("dXNlcjpwYXNz", credential.Authorization);
                Assert.Empty(credential.GetParams());
            }
        }
    }

    /// <summary>
    /// A message without the header. The C answers a null pointer rather than
    /// an empty array for it, and the member reads that as the empty array, so
    /// a caller never has a null to answer for.
    /// </summary>
    [Fact]
    public void AMessageWithoutTheHeaderCarriesNoCredential()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        using (message)
        {
            Assert.Equal(
                RTSPResult.Ok,
                message.InitRequest(RTSPMethod.Options, "rtsp://host.example:8554/stream"));
            Assert.Equal(RTSPResult.Ok, message.AddHeader(RTSPHeaderField.Cseq, "1"));

            Assert.Empty(message.ParseAuthCredentials(RTSPHeaderField.WwwAuthenticate));
            Assert.Empty(message.ParseAuthCredentials(RTSPHeaderField.Authorization));
        }
    }

    /// <summary>
    /// The copy the member takes is what makes the answer outlive the message:
    /// the message is disposed first and every value is read afterwards. A
    /// window into the block the C allocated would be freed memory by then,
    /// twice over — the block is released inside the member as well.
    /// </summary>
    [Fact]
    public void TheCredentialsOutliveTheMessageTheyWereParsedFrom()
    {
        Assert.Equal(RTSPResult.Ok, RtspGlobal.RtspMessageNew(out RTSPMessage? message));
        Assert.NotNull(message);

        Assert.Equal(
            RTSPResult.Ok,
            message.InitResponse(RTSPStatusCode.Unauthorized, "Unauthorized", null));
        Assert.Equal(
            RTSPResult.Ok,
            message.AddHeader(
                RTSPHeaderField.WwwAuthenticate,
                "Digest realm=\"live\", nonce=\"abc123\""));

        RTSPAuthCredential[] credentials =
            message.ParseAuthCredentials(RTSPHeaderField.WwwAuthenticate);
        RTSPAuthCredential credential = Assert.Single(credentials);

        message.Dispose();

        Assert.Equal(RTSPAuthMethod.Digest, credential.Scheme);
        Assert.Null(credential.Authorization);

        RTSPAuthParam[] parameters = credential.GetParams();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("realm", parameters[0].Name);
        Assert.Equal("live", parameters[0].Value);
        Assert.Equal("nonce", parameters[1].Name);
        Assert.Equal("abc123", parameters[1].Value);

        foreach (RTSPAuthParam parameter in parameters)
        {
            parameter.Dispose();
        }

        credential.Dispose();
    }
}
