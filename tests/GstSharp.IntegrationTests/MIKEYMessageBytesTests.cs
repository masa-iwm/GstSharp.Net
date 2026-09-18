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
/// <para>
/// Two answers of the parse are not measured here and are worth knowing all
/// the same. An empty block is the one malformed input that logs: the C reads
/// the data pointer out of the block and refuses a null one with a critical,
/// and a block built from an empty span carries a null pointer, so the answer
/// is a critical and a null rather than the quiet null a short block gets. An
/// empty span through <see cref="MIKEYMessage.NewFromData"/> is the same
/// critical, followed by the <see cref="InvalidOperationException"/> of its
/// non-nullable return. And the block below is short enough to be refused on
/// the size check, which is what keeps this test off the parse loop: the fix
/// for the loop over an unhandled payload type (999c43ada13c) reached the
/// older series as the backports 2c294036df and 897285354d, so it is absent
/// before 1.24.13 and before 1.26.2 and present at 1.24.13, at 1.26.2 and
/// newer and at 1.28.0 and newer. On one of those older hosts a message that
/// reaches such a type never returns from the parse. <c>git tag --contains</c>
/// answers 1.27.1 for the fix and misses both cherry-picks, which is why the
/// question is settled by grepping the tagged file instead.
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
    /// <remarks>
    /// Three bytes is a block the parse gives up on at its size check, before
    /// the payload loop; it is neither the empty block, which is a critical,
    /// nor an input that reaches the loop the 1.24 floor can hang in. See the
    /// remarks of the class.
    /// </remarks>
    [Fact]
    public void ABlockThatIsNotAMessageAnswersNothing()
    {
        using Bytes bytes = Bytes.New([1, 2, 3]);

        Assert.Null(MIKEYMessage.NewFromBytes(bytes, null));
    }
}
