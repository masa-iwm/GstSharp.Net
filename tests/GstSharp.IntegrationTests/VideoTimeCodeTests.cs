using Gst;
using Gst.Video;
using Xunit;
using Buffer = Gst.Buffer;
using GDateTime = Gst.GLib.DateTime;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The eight <c>GstVideoTimeCode</c> members that speak <c>GDateTime</c>, and
/// the buffer metadata constructor beside them.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point called here is GStreamer 1.16 or older, so the file runs
/// on the 1.24 floor of the Linux leg and needs no availability gate.
/// </para>
/// <para>
/// Two of the members carry a documentation correction the generator writes:
/// their gir says the reference of the daily jam is stolen from the caller,
/// and the C code refs it instead. The borrow is asserted here rather than
/// only documented — the daily jam a call was handed is read again after the
/// timecode that took it has been disposed, which is a use after free if the
/// gir were right.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class VideoTimeCodeTests
{
    /// <summary>
    /// A timecode built on a daily jam converts back into the instant the jam
    /// names plus the time the timecode counts.
    /// </summary>
    [Fact]
    public void ATimeCodeConvertsBackIntoItsDailyJamPlusItsOffset()
    {
        GDateTime? jam = GDateTime.FromUnixUtc(1_755_000_000L);
        Assert.NotNull(jam);

        using (jam)
        {
            using VideoTimeCode code = VideoTimeCode.New(
                25, 1, jam, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0);

            Assert.True(code.IsValid());
            Assert.Equal(1u, code.Hours);
            Assert.Equal(2u, code.Minutes);
            Assert.Equal(3u, code.Seconds);
            Assert.Equal(4u, code.Frames);

            GDateTime? back = code.ToDateTime();
            Assert.NotNull(back);

            using (back)
            {
                // One hour, two minutes, three seconds and four frames of a
                // twenty five frame second: 3723.16 seconds after the jam.
                TimeSpan elapsed = back.ToDateTimeOffset() - jam.ToDateTimeOffset();
                Assert.True(
                    Math.Abs((elapsed - new TimeSpan(0, 1, 2, 3, 160)).TotalMicroseconds) <= 2,
                    "The converted instant was " + elapsed + " after the daily jam.");
            }
        }
    }

    /// <summary>
    /// The daily jam is borrowed, whatever the gir documentation of
    /// <c>gst_video_time_code_new</c> says: the timecode takes a reference of
    /// its own, so the wrapper the caller passed in outlives it.
    /// </summary>
    [Fact]
    public void TheDailyJamIsBorrowedAndNotStolen()
    {
        GDateTime? jam = GDateTime.FromUnixUtc(1_755_000_000L, 500_000);
        Assert.NotNull(jam);

        using (jam)
        {
            VideoTimeCode code = VideoTimeCode.New(25, 1, jam, VideoTimeCodeFlags.None, 0, 0, 1, 0, 0);
            Assert.False(jam.IsDisposed);

            // Releasing the timecode releases the reference the timecode took,
            // and not the one the caller holds. Reading the jam afterwards is
            // what a stolen reference would have made a use after free.
            code.Dispose();

            Assert.False(jam.IsDisposed);
            Assert.Equal(1_755_000_000L, jam.ToUnix());
            Assert.Equal(500_000, jam.Microsecond);
        }
    }

    /// <summary>
    /// The daily jam is optional, which the gir of
    /// <c>gst_video_time_code_new</c> omits and the overlay corrects. Without
    /// one there is no instant to convert back into.
    /// </summary>
    [Fact]
    public void ATimeCodeWithoutADailyJamHasNoInstant()
    {
        using VideoTimeCode code = VideoTimeCode.New(
            25, 1, null, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0);

        Assert.True(code.IsValid());
        Assert.Null(code.ToDateTime());
    }

    /// <summary>
    /// <see cref="VideoTimeCode.Init"/> writes the same timecode into an
    /// existing value, and takes the daily jam the same way.
    /// </summary>
    [Fact]
    public void InitWritesTheTimeCodeIntoAnExistingValue()
    {
        GDateTime? jam = GDateTime.FromUnixUtc(1_755_000_000L);
        Assert.NotNull(jam);

        using (jam)
        {
            using VideoTimeCode code = VideoTimeCode.NewEmpty();
            Assert.False(code.IsValid());

            code.Init(25, 1, jam, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0);

            Assert.True(code.IsValid());
            Assert.Equal(1u, code.Hours);
            Assert.Equal(4u, code.Frames);
            Assert.False(jam.IsDisposed);
        }
    }

    /// <summary>
    /// A timecode built from an instant keeps the wall clock reading of that
    /// instant and rounds the sub second part onto a frame.
    /// </summary>
    /// <remarks>
    /// The daily jam the C function derives is <b>local</b> midnight of the
    /// calendar date of the value it was given (gstvideotimecode.c:289), so
    /// what is asserted here is the reading of the clock rather than the
    /// instant: the zone of the argument is not carried over.
    /// </remarks>
    [Fact]
    public void ATimeCodeFromAnInstantKeepsTheReadingAndRoundsToAFrame()
    {
        GDateTime? value = GDateTime.FromIso8601("2026-08-22T01:02:03.500000Z");
        Assert.NotNull(value);

        using (value)
        {
            VideoTimeCode? code = VideoTimeCode.NewFromDateTimeFull(
                25, 1, value, VideoTimeCodeFlags.None, 0);
            Assert.NotNull(code);

            using (code)
            {
                Assert.True(code.IsValid());
                Assert.Equal(1u, code.Hours);
                Assert.Equal(2u, code.Minutes);
                Assert.Equal(3u, code.Seconds);

                // Half a second of a twenty five frame second is twelve and a
                // half frames, and the C rounds it.
                Assert.Equal(13u, code.Frames);

                GDateTime? back = code.ToDateTime();
                Assert.NotNull(back);

                using (back)
                {
                    DateTimeOffset reading = back.ToDateTimeOffset();
                    Assert.Equal(new DateOnly(2026, 8, 22), DateOnly.FromDateTime(reading.Date));
                    Assert.True(
                        Math.Abs((reading.TimeOfDay - new TimeSpan(0, 1, 2, 3, 520)).TotalMicroseconds) <= 2,
                        "The converted reading was " + reading.TimeOfDay + " of its day.");
                }
            }
        }
    }

    /// <summary>
    /// The non nullable sibling builds the same timecode, and the two
    /// initializers write it into an existing value.
    /// </summary>
    [Fact]
    public void TheDateTimeFamilyAgreesOnTheSameTimeCode()
    {
        GDateTime? value = GDateTime.FromIso8601("2026-08-22T01:02:03.500000Z");
        Assert.NotNull(value);

        using (value)
        {
            using VideoTimeCode constructed = VideoTimeCode.NewFromDateTime(
                25, 1, value, VideoTimeCodeFlags.None, 0);
            using VideoTimeCode initialized = VideoTimeCode.NewEmpty();
            initialized.InitFromDateTime(25, 1, value, VideoTimeCodeFlags.None, 0);

            using VideoTimeCode checkedValue = VideoTimeCode.NewEmpty();
            Assert.True(checkedValue.InitFromDateTimeFull(25, 1, value, VideoTimeCodeFlags.None, 0));

            Assert.Equal(0, constructed.Compare(initialized));
            Assert.Equal(0, constructed.Compare(checkedValue));

            // Nothing of the three consumed the value it was handed.
            Assert.False(value.IsDisposed);
        }
    }

    /// <summary>
    /// A missing value is refused rather than passed on as a null pointer: the
    /// gir marks the parameter non nullable and the C asserts on it.
    /// </summary>
    [Fact]
    public void TheDateTimeFamilyRefusesNoValueAtAll()
    {
        using VideoTimeCode code = VideoTimeCode.NewEmpty();

        Assert.Throws<ArgumentNullException>(
            () => VideoTimeCode.NewFromDateTime(25, 1, null!, VideoTimeCodeFlags.None, 0));
        Assert.Throws<ArgumentNullException>(
            () => code.InitFromDateTime(25, 1, null!, VideoTimeCodeFlags.None, 0));
    }

    /// <summary>
    /// The buffer metadata constructor takes the timecode apart rather than as
    /// a value, and its daily jam is optional in the same way.
    /// </summary>
    [Fact]
    public void TheTimeCodeMetaConstructorTakesAnOptionalDailyJam()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        Assert.NotNull(VideoGlobal.BufferAddVideoTimeCodeMetaFull(
            buffer, 25, 1, null, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0));

        GDateTime? jam = GDateTime.FromUnixUtc(1_755_000_000L);
        Assert.NotNull(jam);

        using (jam)
        {
            Assert.NotNull(VideoGlobal.BufferAddVideoTimeCodeMetaFull(
                buffer, 25, 1, jam, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0));

            // The daily jam is borrowed here too.
            Assert.False(jam.IsDisposed);
            Assert.Equal(1_755_000_000L, jam.ToUnix());
        }

        // An invalid timecode — thirty frames of a twenty five frame second —
        // is refused rather than attached.
        Assert.Null(VideoGlobal.BufferAddVideoTimeCodeMetaFull(
            buffer, 25, 1, null, VideoTimeCodeFlags.None, 1, 2, 3, 30, 0));
    }

    /// <summary>
    /// The timecode a metadata item embeds is handed out as a copy of itself,
    /// with the daily jam it carries referenced along with it.
    /// </summary>
    [Fact]
    public void TheTimeCodeMetaHandsOutACopyOfWhatItWasAddedWith()
    {
        using Buffer buffer = Assert.IsType<Buffer>(Buffer.NewAllocate(null, 16, null));

        using VideoTimeCode plain = VideoTimeCode.New(
            25, 1, null, VideoTimeCodeFlags.None, 1, 2, 3, 4, 0);

        VideoTimeCodeMeta? withoutJam = VideoGlobal.BufferAddVideoTimeCodeMeta(buffer, plain);
        Assert.NotNull(withoutJam);

        using (VideoTimeCode copied = withoutJam.GetTc())
        {
            Assert.Equal(1u, copied.Hours);
            Assert.Equal(2u, copied.Minutes);
            Assert.Equal(3u, copied.Seconds);
            Assert.Equal(4u, copied.Frames);
        }

        GDateTime? jam = GDateTime.FromUnixUtc(1_755_000_000L);
        Assert.NotNull(jam);

        using (jam)
        {
            using VideoTimeCode dated = VideoTimeCode.New(
                25, 1, jam, VideoTimeCodeFlags.None, 5, 6, 7, 8, 0);

            VideoTimeCodeMeta? withJam = VideoGlobal.BufferAddVideoTimeCodeMeta(buffer, dated);
            Assert.NotNull(withJam);

            using (VideoTimeCode copied = withJam.GetTc())
            {
                Assert.Equal(5u, copied.Hours);
                Assert.Equal(8u, copied.Frames);
            }

            // Disposing that copy leaves the item reading the same value,
            // because what the item holds is a structure of its own.
            using VideoTimeCode again = withJam.GetTc();
            Assert.Equal(5u, again.Hours);
            Assert.Equal(6u, again.Minutes);

            // The daily jam is the part a copy has to reference rather than
            // take: converting needs it, and the copy that was disposed above
            // would have carried it off if the copy had stolen it.
            using GDateTime? when = again.ToDateTime();
            Assert.NotNull(when);
        }
    }
}
