using Gst.GLib;
using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The security policy payload of a MIKEY message: the parameters a policy
/// carries, the span <c>Custom/MIKEYPayloadSPParam.cs</c> projects over the
/// value of one, and the positive branch of the casts that the payload types
/// beside it answer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="MIKEYPayloadSPParam.Val"/> is a view over memory the payload
/// owns, so every read of it happens inside the scope that keeps the payload
/// alive. The second test is the one that matters for that projection: it
/// reads the span over a parameter the parser allocated rather than over one
/// the test handed the library, which is the only place the length and the
/// pointer could disagree.
/// </para>
/// <para>
/// Every value below is non-empty on purpose.
/// <c>gst_mikey_payload_sp_add_param</c> copies <c>len</c> bytes out of
/// <c>val</c> with no null check of its own (gstmikey.c, the
/// <c>INIT_MEMDUP</c> of the add), so a length without a value is not a case
/// this binding is asked to carry.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MikeySecurityPolicyTests
{
    private static readonly byte[] FirstValue = [0x01];
    private static readonly byte[] SecondValue = [0xAA, 0xBB];
    private static readonly byte[] ThirdValue = [0x10, 0x20, 0x30];

    /// <summary>
    /// A policy payload answers the parameters it was given, in the order they
    /// were added, and removing one shifts the rest down.
    /// </summary>
    [Fact]
    public void ASecurityPolicyCarriesTheParametersItWasGiven()
    {
        using MIKEYPayload? payload = MIKEYPayload.New(MIKEYPayloadType.Sp);
        Assert.NotNull(payload);

        Assert.True(payload.SpSet(7, MIKEYSecProto.MikeySecProtoSrtp));
        Assert.True(payload.SpAddParam(1, FirstValue));
        Assert.True(payload.SpAddParam(2, SecondValue));
        Assert.True(payload.SpAddParam(3, ThirdValue));

        Assert.Equal(3u, payload.SpGetNParams());

        AssertParameter(payload, 0, 1, FirstValue);
        AssertParameter(payload, 1, 2, SecondValue);
        AssertParameter(payload, 2, 3, ThirdValue);

        MIKEYPayloadSP? policy = MIKEYPayloadSP.FromPayload(payload);
        Assert.NotNull(policy);
        Assert.Equal(7u, policy.Policy);
        Assert.Equal(MIKEYSecProto.MikeySecProtoSrtp, policy.Proto);

        // The array of parameters is compacted, so the third one is now the
        // second.
        Assert.True(payload.SpRemoveParam(1));
        Assert.Equal(2u, payload.SpGetNParams());
        AssertParameter(payload, 0, 1, FirstValue);
        AssertParameter(payload, 1, 3, ThirdValue);
    }

    /// <summary>
    /// The same parameters read back out of a message that was written into a
    /// block and parsed out of one, which is the only reading of them over
    /// memory the parser allocated.
    /// </summary>
    /// <remarks>
    /// The header is set the way <c>MIKEYMessageBytesTests</c> sets it: the
    /// parser refuses any version but 1, and a message built by
    /// <see cref="MIKEYMessage.New"/> alone carries a version of zero. The
    /// policy payload itself is one the parse handles (gstmikey.c:2091, the
    /// <c>GST_MIKEY_PT_SP</c> case), so this does not reach the payload loop
    /// that hangs on an unhandled type on the 1.24 floor.
    /// </remarks>
    [Fact]
    public void AParsedMessageStillAnswersItsPolicyParameters()
    {
        using MIKEYMessage message = MIKEYMessage.New();

        Assert.True(message.SetInfo(
            1,
            MIKEYType.PskInit,
            false,
            MIKEYPRFFunc.MikeyPrfMikey1,
            0x0BADF00D,
            MIKEYMapType.MikeyMapTypeSrtp));

        MIKEYPayload? built = MIKEYPayload.New(MIKEYPayloadType.Sp);
        Assert.NotNull(built);
        Assert.True(built.SpSet(7, MIKEYSecProto.MikeySecProtoSrtp));
        Assert.True(built.SpAddParam(1, FirstValue));
        Assert.True(built.SpAddParam(2, SecondValue));
        Assert.True(built.SpAddParam(3, ThirdValue));

        // The call consumes the wrapper, so nothing is read through it again.
        Assert.True(message.AddPayload(built));

        using Bytes bytes = message.ToBytes(null);
        Assert.True(bytes.Size > 0);

        using MIKEYMessage? parsed = MIKEYMessage.NewFromBytes(bytes, null);
        Assert.NotNull(parsed);

        using MIKEYPayload? payload = parsed.FindPayload(MIKEYPayloadType.Sp, 0);
        Assert.NotNull(payload);

        MIKEYPayloadSP? policy = MIKEYPayloadSP.FromPayload(payload);
        Assert.NotNull(policy);
        Assert.Equal(7u, policy.Policy);
        Assert.Equal(MIKEYSecProto.MikeySecProtoSrtp, policy.Proto);

        Assert.Equal(3u, payload.SpGetNParams());
        AssertParameter(payload, 0, 1, FirstValue);
        AssertParameter(payload, 1, 2, SecondValue);
        AssertParameter(payload, 2, 3, ThirdValue);
    }

    /// <summary>
    /// Key data, public key and random payloads each cast to their own variant
    /// and answer what was set on them, and to nothing else.
    /// </summary>
    /// <remarks>
    /// <c>gst_mikey_payload_key_data_set</c> does not exist: the key data
    /// payload is filled through the setters of its parts, and
    /// <see cref="MIKEYPayload.KeyDataSetKey"/> is the one that gives it a key.
    /// </remarks>
    [Fact]
    public void TheOtherCastsAnswerOnTheirOwnPayloadType()
    {
        byte[] key = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];
        byte[] publicKeyData = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] randomData = [15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0];

        using MIKEYPayload? keyData = MIKEYPayload.New(MIKEYPayloadType.KeyData);
        Assert.NotNull(keyData);
        Assert.True(keyData.KeyDataSetKey(MIKEYKeyDataType.Tek, key));

        MIKEYPayloadKeyData? keyDataView = MIKEYPayloadKeyData.FromPayload(keyData);
        Assert.NotNull(keyDataView);
        Assert.Equal(MIKEYKeyDataType.Tek, keyDataView.KeyType);
        Assert.Equal((ushort)key.Length, keyDataView.KeyLen);

        using MIKEYPayload? publicKey = MIKEYPayload.New(MIKEYPayloadType.Pke);
        Assert.NotNull(publicKey);
        Assert.True(publicKey.PkeSet(MIKEYCacheType.None, publicKeyData));

        MIKEYPayloadPKE? pkeView = MIKEYPayloadPKE.FromPayload(publicKey);
        Assert.NotNull(pkeView);
        Assert.Equal(MIKEYCacheType.None, pkeView.C);
        Assert.Equal((ushort)publicKeyData.Length, pkeView.DataLen);

        using MIKEYPayload? random = MIKEYPayload.New(MIKEYPayloadType.Rand);
        Assert.NotNull(random);
        Assert.True(random.RandSet(randomData));

        MIKEYPayloadRAND? randView = MIKEYPayloadRAND.FromPayload(random);
        Assert.NotNull(randView);
        Assert.Equal((byte)randomData.Length, randView.Len);

        // Each cast is the only one that answers on its own payload.
        Assert.Null(MIKEYPayloadPKE.FromPayload(keyData));
        Assert.Null(MIKEYPayloadRAND.FromPayload(keyData));
        Assert.Null(MIKEYPayloadSP.FromPayload(keyData));
        Assert.Null(MIKEYPayloadKeyData.FromPayload(publicKey));
        Assert.Null(MIKEYPayloadRAND.FromPayload(publicKey));
        Assert.Null(MIKEYPayloadKeyData.FromPayload(random));
        Assert.Null(MIKEYPayloadPKE.FromPayload(random));
    }

    /// <summary>
    /// Reads one parameter of a policy payload and asserts its type, its
    /// length and the bytes of its value.
    /// </summary>
    /// <param name="payload">The policy payload, which stays alive around the read.</param>
    /// <param name="index">The index of the parameter.</param>
    /// <param name="type">The type the parameter was added with.</param>
    /// <param name="value">The value the parameter was added with.</param>
    private static void AssertParameter(MIKEYPayload payload, uint index, byte type, byte[] value)
    {
        MIKEYPayloadSPParam? parameter = payload.SpGetParam(index);
        Assert.NotNull(parameter);

        MIKEYPayloadSPParam read = parameter.Value;
        Assert.Equal(type, read.Type);
        Assert.Equal((byte)value.Length, read.Len);
        Assert.Equal(value, read.Val.ToArray());
    }
}
