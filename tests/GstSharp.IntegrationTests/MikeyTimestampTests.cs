using Gst.GLib;
using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two timestamp members of the MIKEY surface against the library that is
/// installed: the number of bytes they read is a table lookup on the kind of
/// timestamp, and the wrapper is what measures the array against it.
/// </summary>
/// <remarks>
/// The message is built the way <see cref="MIKEYMessageBytesTests"/> builds
/// it - the header has to be set before a message can be written - and the
/// remarks of that class hold here as well.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MikeyTimestampTests
{
    /// <summary>
    /// A timestamp payload added by hand survives the round trip through a
    /// block, with the kind of timestamp it was given.
    /// </summary>
    [Fact]
    public void ATimestampPayloadRoundTripsThroughABlock()
    {
        using MIKEYMessage message = NewMessage();

        Assert.True(message.AddT(MIKEYTSType.NtpUtc, [1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Equal(1u, message.GetNPayloads());

        using Bytes bytes = message.ToBytes(null);
        using MIKEYMessage? parsed = MIKEYMessage.NewFromBytes(bytes, null);
        Assert.NotNull(parsed);

        using MIKEYPayload? payload = parsed.FindPayload(MIKEYPayloadType.T, 0);
        Assert.NotNull(payload);

        MIKEYPayloadT? timestamp = MIKEYPayloadT.FromPayload(payload);
        Assert.NotNull(timestamp);
        Assert.Equal(MIKEYTSType.NtpUtc, timestamp.Type);
    }

    /// <summary>
    /// A counter is four bytes, and the same four bytes are eight too few for
    /// an NTP time.
    /// </summary>
    [Fact]
    public void EachKindOfTimestampHasItsOwnLength()
    {
        using MIKEYMessage message = NewMessage();

        Assert.True(message.AddT(MIKEYTSType.Counter, [1, 2, 3, 4]));
        Assert.True(message.AddT(MIKEYTSType.Ntp, [1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Equal(2u, message.GetNPayloads());
    }

    /// <summary>
    /// An array of the wrong length is refused: the call is handed no count
    /// and reads the number of bytes the kind of timestamp calls for, so a
    /// short one would be read past its end.
    /// </summary>
    [Fact]
    public void AnArrayOfTheWrongLengthIsRefused()
    {
        using MIKEYMessage message = NewMessage();

        Assert.Throws<ArgumentException>(
            () => message.AddT(MIKEYTSType.Counter, [1, 2, 3, 4, 5, 6, 7, 8]));

        Assert.Throws<ArgumentException>(
            () => message.AddT(MIKEYTSType.NtpUtc, [1, 2, 3, 4]));

        Assert.Throws<ArgumentException>(
            () => message.AddT(MIKEYTSType.NtpUtc, []));

        Assert.Equal(0u, message.GetNPayloads());
    }

    /// <summary>
    /// A kind of timestamp the library does not size is refused before the
    /// call is made, rather than passed on for the plain FALSE the C answers
    /// it with.
    /// </summary>
    [Fact]
    public void AKindOfTimestampTheLibraryDoesNotSizeIsRefused()
    {
        using MIKEYMessage message = NewMessage();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => message.AddT((MIKEYTSType)7, [1, 2, 3, 4]));
    }

    /// <summary>
    /// A payload that is not a timestamp payload refuses the timestamp: the C
    /// answers that with an assertion failure on the console and changes
    /// nothing.
    /// </summary>
    [Fact]
    public void APayloadOfAnotherTypeRefusesTheTimestamp()
    {
        using MIKEYPayload? payload = MIKEYPayload.New(MIKEYPayloadType.Rand);
        Assert.NotNull(payload);

        Assert.Throws<InvalidOperationException>(
            () => payload.TSet(MIKEYTSType.NtpUtc, [1, 2, 3, 4, 5, 6, 7, 8]));
    }

    /// <summary>
    /// A timestamp payload takes the timestamp directly, and the message it
    /// is added to writes it.
    /// </summary>
    [Fact]
    public void ATimestampPayloadTakesTheTimestampDirectly()
    {
        using MIKEYMessage message = NewMessage();

        MIKEYPayload? payload = MIKEYPayload.New(MIKEYPayloadType.T);
        Assert.NotNull(payload);
        Assert.True(payload.TSet(MIKEYTSType.Counter, [9, 8, 7, 6]));
        Assert.True(message.AddPayload(payload));

        using Bytes bytes = message.ToBytes(null);
        using MIKEYMessage? parsed = MIKEYMessage.NewFromBytes(bytes, null);
        Assert.NotNull(parsed);

        using MIKEYPayload? read = parsed.FindPayload(MIKEYPayloadType.T, 0);
        Assert.NotNull(read);
        Assert.Equal(MIKEYTSType.Counter, MIKEYPayloadT.FromPayload(read)!.Type);
    }

    /// <summary>
    /// A message with a header, which is what a message needs before it can be
    /// written into a block.
    /// </summary>
    private static MIKEYMessage NewMessage()
    {
        MIKEYMessage message = MIKEYMessage.New();

        Assert.True(message.SetInfo(
            1,
            MIKEYType.PskInit,
            false,
            MIKEYPRFFunc.MikeyPrfMikey1,
            0x0BADF00D,
            MIKEYMapType.MikeyMapTypeSrtp));

        return message;
    }
}
