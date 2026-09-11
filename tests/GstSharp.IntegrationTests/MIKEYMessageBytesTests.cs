using Gst.GLib;
using Gst.Sdp;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two <c>GBytes</c> members of <see cref="MIKEYMessage"/>: a message is
/// written into a block and parsed back out of one, and a block that is not a
/// message answers nothing rather than throwing.
/// </summary>
/// <remarks>
/// <para>
/// Both calls take an opaque info structure the C declares and never defines,
/// so <see langword="null"/> is the only value there is; the overlays widen
/// the parameter to nullable for exactly that reason, and every call here
/// passes null.
/// </para>
/// <para>
/// The return of <see cref="MIKEYMessage.NewFromBytes"/> is nullable for a
/// second one: a parse that fails answers NULL without ever setting the error
/// it is annotated to report - <c>g_set_error</c> does not occur once in
/// <c>gstmikey.c</c> - so the throwing shape would have turned a malformed
/// message into an exception whose message named the wrong cause.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MIKEYMessageBytesTests
{
    /// <summary>
    /// A message written into a block parses back into a message that writes
    /// the same block.
    /// </summary>
    [Fact]
    public void AMessageRoundTripsThroughABlock()
    {
        using MIKEYMessage message = NewMessage();

        using Bytes bytes = message.ToBytes(null);
        Assert.True(bytes.Size > 0);

        using MIKEYMessage? parsed = MIKEYMessage.NewFromBytes(bytes, null);
        Assert.NotNull(parsed);
        Assert.Equal(1u, parsed.GetNPayloads());

        using Bytes again = parsed.ToBytes(null);
        Assert.Equal(bytes.ToArray(), again.ToArray());
    }

    /// <summary>
    /// The same bytes parse through <see cref="MIKEYMessage.NewFromData"/>,
    /// whose info parameter the overlays widened to nullable; before that the
    /// member was uncallable, because nothing constructs the info it demanded.
    /// </summary>
    [Fact]
    public void TheSameBytesParseThroughTheSpanOverload()
    {
        using MIKEYMessage message = NewMessage();

        using Bytes bytes = message.ToBytes(null);

        using MIKEYMessage parsed = MIKEYMessage.NewFromData(bytes.GetData(), null);
        Assert.Equal(1u, parsed.GetNPayloads());
    }

    /// <summary>
    /// Builds the message the tests write out: a pre-shared key initiator of
    /// MIKEY version 1 with one timestamp payload.
    /// </summary>
    /// <returns>The message, which the caller disposes.</returns>
    /// <remarks>
    /// The header matters as much as the payload here, because the parser
    /// refuses anything that is not version 1 and a message built by
    /// <see cref="MIKEYMessage.New"/> alone carries a version of zero.
    /// </remarks>
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
        Assert.True(message.AddTNowNtpUtc());

        return message;
    }

    /// <summary>
    /// A block that is not a message answers nothing, and does not throw: the
    /// library reports the refusal by returning NULL and sets no error.
    /// </summary>
    [Fact]
    public void ABlockThatIsNotAMessageAnswersNothing()
    {
        using Bytes bytes = Bytes.New([1, 2, 3]);

        Assert.Null(MIKEYMessage.NewFromBytes(bytes, null));
    }
}
