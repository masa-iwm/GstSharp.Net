using Xunit;

// Gst.GLib.DateTime and System.DateTime share their bare name, and Gst.DateTime
// is a third value of the same shape; every one of the three is spelled through
// an alias here so that no line of this file is ambiguous to read.
using GDateTime = Gst.GLib.DateTime;
using GstDateTime = Gst.DateTime;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written <c>Gst.GLib.DateTime</c> against the installed GLib, and
/// the bridge that connects it to <see cref="Gst.DateTime"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every entry point called here is GLib 2.62 or older and the two bridge
/// functions are GStreamer 1.4, so the whole file runs on the 1.24 floor of the
/// Linux leg and needs no availability gate.
/// </para>
/// <para>
/// What is measured is the claim of the class remarks: the instant survives
/// every conversion at microsecond precision, and the identity of the zone does
/// not — a value built from a <see cref="DateTimeOffset"/> is in UTC and
/// compares equal as an instant rather than field by field.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class GLibDateTimeTests
{
    /// <summary>
    /// A Unix timestamp with microseconds round trips: the seconds come back
    /// out of <see cref="GDateTime.ToUnix"/>, the microseconds out of
    /// <see cref="GDateTime.Microsecond"/>, and the value is in UTC.
    /// </summary>
    [Fact]
    public void AUnixTimestampWithMicrosecondsRoundTrips()
    {
        GDateTime? value = GDateTime.FromUnixUtc(1_755_000_000L, 123_456);
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal(1_755_000_000L, value.ToUnix());
            Assert.Equal(123_456, value.Microsecond);
            Assert.Equal(TimeSpan.Zero, value.UtcOffset);

            // The abbreviation of the zone is what the value was read in, which
            // for a value built in UTC is UTC.
            Assert.Equal("UTC", value.TimezoneAbbreviation);
        }
    }

    /// <summary>
    /// A value built without microseconds is the whole second, and no second
    /// value is allocated for it.
    /// </summary>
    [Fact]
    public void AWholeSecondNeedsNoMicrosecondStep()
    {
        GDateTime? value = GDateTime.FromUnixUtc(0L);
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal(0L, value.ToUnix());
            Assert.Equal(0, value.Microsecond);
            Assert.Equal(DateTimeOffset.UnixEpoch, value.ToDateTimeOffset());
        }
    }

    /// <summary>
    /// An ISO 8601 timestamp keeps both its offset and its microseconds.
    /// </summary>
    [Fact]
    public void AnIso8601TimestampKeepsItsOffsetAndItsMicroseconds()
    {
        GDateTime? value = GDateTime.FromIso8601("2026-08-22T12:34:56.123456+09:00");
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal(TimeSpan.FromHours(9), value.UtcOffset);
            Assert.Equal(123_456, value.Microsecond);

            // The instant is the wall clock reading minus the offset.
            Assert.Equal(
                new DateTimeOffset(2026, 8, 22, 12, 34, 56, TimeSpan.FromHours(9)).AddTicks(1_234_560),
                value.ToDateTimeOffset());
        }
    }

    /// <summary>
    /// Text that GLib does not accept is <see langword="null"/> rather than a
    /// throw, like every other factory of the type.
    /// </summary>
    [Fact]
    public void TextThatIsNotATimestampAnswersNull()
    {
        Assert.Null(GDateTime.FromIso8601("not a timestamp"));
    }

    /// <summary>
    /// A timestamp that carries no zone suffix is one of the values GLib does
    /// not accept: it reads such a text in the fallback zone it is handed, and
    /// this binding hands it none.
    /// </summary>
    [Fact]
    public void ATimestampWithoutAZoneAnswersNull()
    {
        Assert.Null(GDateTime.FromIso8601("2026-08-22T12:34:56"));

        // The same text with a zone suffix parses, so what the value is
        // measuring is the missing zone and not the shape of the text.
        GDateTime? zoned = GDateTime.FromIso8601("2026-08-22T12:34:56Z");
        Assert.NotNull(zoned);
        zoned.Dispose();
    }

    /// <summary>
    /// A managed value round trips as an instant. The offset does not survive —
    /// the result is in UTC — so the two compare equal with <c>==</c> and not
    /// with <see cref="DateTimeOffset.EqualsExact"/>.
    /// </summary>
    [Fact]
    public void AManagedValueRoundTripsAsAnInstantAndNotAsAnOffset()
    {
        DateTimeOffset source =
            new DateTimeOffset(2026, 8, 22, 12, 34, 56, TimeSpan.FromHours(9)).AddTicks(1_234_560);

        GDateTime? value = GDateTime.FromDateTimeOffset(source);
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal(TimeSpan.Zero, value.UtcOffset);
            Assert.Equal(123_456, value.Microsecond);

            DateTimeOffset back = value.ToDateTimeOffset();
            Assert.True(source == back);
            Assert.False(source.EqualsExact(back));
        }
    }

    /// <summary>
    /// Anything below a microsecond is truncated, which is the second
    /// documented loss of <see cref="GDateTime.FromDateTimeOffset"/>.
    /// </summary>
    [Fact]
    public void SubMicrosecondTicksAreTruncated()
    {
        // .1234569 seconds: the trailing nine hundred nanoseconds have no place
        // in a value that counts microseconds.
        DateTimeOffset source = new DateTimeOffset(2026, 8, 22, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_569);

        GDateTime? value = GDateTime.FromDateTimeOffset(source);
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal(123_456, value.Microsecond);
            Assert.Equal(source.AddTicks(-9), value.ToDateTimeOffset());
        }
    }

    /// <summary>
    /// Both formatters answer a string.
    /// </summary>
    [Fact]
    public void TheFormattersAnswerAString()
    {
        GDateTime? value = GDateTime.FromUnixUtc(1_755_000_000L);
        Assert.NotNull(value);

        using (value)
        {
            Assert.Equal("2025", value.Format("%Y"));

            string? iso = value.FormatIso8601();
            Assert.NotNull(iso);
            Assert.StartsWith("2025-", iso, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A disposed wrapper has given its reference up, and every member of it
    /// throws rather than reading freed memory.
    /// </summary>
    [Fact]
    public void ADisposedWrapperThrows()
    {
        GDateTime? value = GDateTime.FromUnixUtc(0L);
        Assert.NotNull(value);

        value.Dispose();
        Assert.True(value.IsDisposed);

        Assert.Throws<ObjectDisposedException>(() => value.ToUnix());
        Assert.Throws<ObjectDisposedException>(() => _ = value.Microsecond);
        Assert.Throws<ObjectDisposedException>(() => _ = value.UtcOffset);
        Assert.Throws<ObjectDisposedException>(() => _ = value.TimezoneAbbreviation);
        Assert.Throws<ObjectDisposedException>(() => value.FormatIso8601());

        // Disposing twice is a no-op, as it is for every boxed wrapper.
        value.Dispose();
    }

    /// <summary>
    /// The microseconds are a fraction of a second and are guarded as one.
    /// </summary>
    [Fact]
    public void AMicrosecondCountOutsideOneSecondIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GDateTime.FromUnixUtc(0L, 1_000_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => GDateTime.FromUnixUtc(0L, -1));
    }

    /// <summary>
    /// A fully defined <see cref="Gst.DateTime"/> converts, and the microsecond
    /// crosses with it.
    /// </summary>
    [Fact]
    public void AFullyDefinedGstDateTimeConverts()
    {
        GstDateTime? stamp = GstDateTime.NewNowUtc();
        Assert.NotNull(stamp);

        using (stamp)
        {
            GDateTime? value = stamp.ToGDateTime();
            Assert.NotNull(value);

            using (value)
            {
                Assert.Equal(stamp.GetMicrosecond(), value.Microsecond);
                Assert.Equal(stamp.GetYear(), value.ToDateTimeOffset().Year);
            }
        }
    }

    /// <summary>
    /// A <see cref="Gst.DateTime"/> that is only a year has no GLib
    /// counterpart, because a <c>GDateTime</c> is always a whole instant.
    /// </summary>
    [Fact]
    public void APartialGstDateTimeHasNoCounterpart()
    {
        GstDateTime? stamp = GstDateTime.NewY(2026);
        Assert.NotNull(stamp);

        using (stamp)
        {
            Assert.True(stamp.HasYear());
            Assert.False(stamp.HasSecond());
            Assert.Null(stamp.ToGDateTime());
        }
    }

    /// <summary>
    /// The other direction consumes its argument: the call takes the value
    /// over, so the wrapper that was passed in is disposed when the member
    /// returns and the instant is what came out the other side.
    /// </summary>
    [Fact]
    public void TheBridgeIntoGstDateTimeConsumesItsArgument()
    {
        GDateTime? value = GDateTime.FromUnixUtc(1_755_000_000L, 123_456);
        Assert.NotNull(value);

        GstDateTime? stamp = GstDateTime.NewFromGDateTime(value);
        Assert.NotNull(stamp);

        // The consuming contract of docs/ownership.md: the wrapper is spent.
        Assert.True(value.IsDisposed);

        using (stamp)
        {
            Assert.Equal(123_456, stamp.GetMicrosecond());

            GDateTime? back = stamp.ToGDateTime();
            Assert.NotNull(back);

            using (back)
            {
                Assert.Equal(1_755_000_000L, back.ToUnix());
                Assert.Equal(123_456, back.Microsecond);
            }
        }
    }

    /// <summary>
    /// The nullable argument of the bridge is the absence of a value, and
    /// nothing is consumed for it.
    /// </summary>
    [Fact]
    public void TheBridgeAcceptsNoValueAtAll()
    {
        Assert.Null(GstDateTime.NewFromGDateTime(null));
    }
}
