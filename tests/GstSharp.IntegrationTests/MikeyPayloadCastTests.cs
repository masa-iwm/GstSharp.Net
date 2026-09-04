using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The casts of <c>Custom/MikeyCasts.cs</c> against the library that is
/// installed: a <see cref="MIKEYPayload"/> read out of a message is
/// reinterpreted as the variant its type names, and as nothing else.
/// </summary>
/// <remarks>
/// The derived view addresses the same storage as the payload it came from and
/// holds no reference of its own, so every one of them is read inside the scope
/// that keeps the payload alive.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MikeyPayloadCastTests
{
    /// <summary>
    /// The timestamp payload <c>gst_mikey_message_add_t_now_ntp_utc</c> adds
    /// casts to <see cref="MIKEYPayloadT"/>, and the field behind the header is
    /// the one the call set.
    /// </summary>
    [Fact]
    public void ATimestampPayloadCastsToItsVariant()
    {
        using MIKEYMessage message = MIKEYMessage.New();
        Assert.True(message.AddTNowNtpUtc());
        Assert.Equal(1u, message.GetNPayloads());

        using MIKEYPayload? payload = message.GetPayload(0);
        Assert.NotNull(payload);
        Assert.Equal(MIKEYPayloadType.T, payload.Type);

        MIKEYPayloadT? timestamp = MIKEYPayloadT.FromPayload(payload);
        Assert.NotNull(timestamp);
        Assert.Equal(MIKEYTSType.NtpUtc, timestamp.Type);
    }

    /// <summary>
    /// A KEMAC payload built by hand casts to <see cref="MIKEYPayloadKEMAC"/>,
    /// which reads back the two algorithms <c>gst_mikey_payload_kemac_set</c>
    /// wrote.
    /// </summary>
    [Fact]
    public void AKemacPayloadCastsToItsVariant()
    {
        using MIKEYMessage message = MIKEYMessage.New();

        MIKEYPayload? built = MIKEYPayload.New(MIKEYPayloadType.Kemac);
        Assert.NotNull(built);
        Assert.True(built.KemacSet(MIKEYEncAlg.AesCm128, MIKEYMacAlg.HmacSha1160));

        // The call consumes the wrapper, so nothing is read through it again.
        Assert.True(message.AddPayload(built));

        using MIKEYPayload? payload = message.GetPayload(0);
        Assert.NotNull(payload);

        MIKEYPayloadKEMAC? kemac = MIKEYPayloadKEMAC.FromPayload(payload);
        Assert.NotNull(kemac);
        Assert.Equal(MIKEYEncAlg.AesCm128, kemac.EncAlg);
        Assert.Equal(MIKEYMacAlg.HmacSha1160, kemac.MacAlg);
    }

    /// <summary>
    /// A cast to the wrong variant answers <see langword="null"/> rather than a
    /// view of a payload that is not one, and a null payload is rejected.
    /// </summary>
    [Fact]
    public void ACastToAnotherVariantAnswersNull()
    {
        using MIKEYMessage message = MIKEYMessage.New();
        Assert.True(message.AddTNowNtpUtc());

        using MIKEYPayload? payload = message.GetPayload(0);
        Assert.NotNull(payload);

        Assert.Null(MIKEYPayloadKEMAC.FromPayload(payload));
        Assert.Null(MIKEYPayloadKeyData.FromPayload(payload));
        Assert.Null(MIKEYPayloadPKE.FromPayload(payload));
        Assert.Null(MIKEYPayloadRAND.FromPayload(payload));
        Assert.Null(MIKEYPayloadSP.FromPayload(payload));

        Assert.Throws<ArgumentNullException>(() => MIKEYPayloadT.FromPayload(null!));
    }
}
