using Gst;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The conversions of <see cref="ClockTime"/>, which are pure arithmetic and
/// need no native library.
/// </summary>
/// <remarks>
/// A <see cref="TimeSpan"/> counts hundreds of nanoseconds in a signed 64 bit
/// tick and a clock time counts nanoseconds in an unsigned one, so the tick
/// count reaches about fifty times further than the nanosecond count does.
/// <see cref="ClockTime.FromTimeSpan"/> used to multiply the ticks out
/// unchecked, which wrapped silently for everything past that point: a
/// <see cref="TimeSpan"/> of a thousand years came back as a plausible looking
/// duration of the wrong length.
/// </remarks>
public class ClockTimeTests
{
    /// <summary>
    /// The largest tick count whose nanoseconds still fit below
    /// <see cref="ClockTime.NoneValue"/>, which is about 584 years.
    /// </summary>
    private const long LargestConvertibleTicks = 184_467_440_737_095_516;

    [Fact]
    public void TheLargestConvertibleIntervalConverts()
    {
        ClockTime time = ClockTime.FromTimeSpan(TimeSpan.FromTicks(LargestConvertibleTicks));

        Assert.Equal(18_446_744_073_709_551_600ul, time.Nanoseconds);

        // It converts, so it is a real duration and not the unset marker.
        Assert.False(time.IsNone);
        Assert.True(time.Nanoseconds < ClockTime.NoneValue);
    }

    [Fact]
    public void OneTickBeyondTheLargestConvertibleIntervalThrows()
    {
        TimeSpan value = TimeSpan.FromTicks(LargestConvertibleTicks + 1);

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(() => ClockTime.FromTimeSpan(value));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void TheLargestIntervalOfAllThrowsRatherThanWrappingAround()
    {
        // Unchecked, this multiply came back as 18446744073709551516
        // nanoseconds, which is 584 years and reads like an answer.
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockTime.FromTimeSpan(TimeSpan.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => (ClockTime)TimeSpan.MaxValue);
    }

    [Fact]
    public void ANegativeIntervalStillThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockTime.FromTimeSpan(TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void OrdinaryIntervalsRoundTrip()
    {
        foreach (TimeSpan value in new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromTicks(1),
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(42.5),
            TimeSpan.FromDays(365),
        })
        {
            ClockTime time = ClockTime.FromTimeSpan(value);

            Assert.Equal((ulong)value.Ticks * 100, time.Nanoseconds);
            Assert.Equal(value, time.ToTimeSpan());
        }
    }

    /// <summary>
    /// The unset time is not a duration, so it has no interval to come from and
    /// none to go to. The bound above is what keeps the first half true: no
    /// interval converts to <see cref="ClockTime.NoneValue"/> or past it.
    /// </summary>
    [Fact]
    public void TheUnsetTimeIsOutsideTheConversion()
    {
        Assert.Throws<InvalidOperationException>(() => ClockTime.None.ToTimeSpan());
        Assert.True(ClockTime.FromTimeSpan(TimeSpan.FromTicks(LargestConvertibleTicks)) != ClockTime.None);
    }

    /// <summary>
    /// The scaled conversions reject an overlong value already, and the
    /// interval conversion now says the same thing in the same way.
    /// </summary>
    [Fact]
    public void TheScaledConversionsRejectTheSameWay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockTime.FromSeconds(1e12));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClockTime.FromMilliseconds(1e15));
    }
}
