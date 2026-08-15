using System.Globalization;

namespace Gst;

/// <summary>
/// A duration or a point on a clock, counted in nanoseconds
/// (<c>GstClockTime</c>).
/// </summary>
/// <remarks>
/// <see cref="None"/> is the "no value" marker of GStreamer
/// (<c>GST_CLOCK_TIME_NONE</c>), which is why it is the largest possible value
/// rather than a negative one: an unset time therefore sorts after every real
/// one. The conversions to seconds guard against it, so that an unset time does
/// not silently turn into 18446744073.7 seconds.
/// </remarks>
public readonly record struct ClockTime : IComparable<ClockTime>
{
    /// <summary>
    /// The raw value of <c>GST_CLOCK_TIME_NONE</c>.
    /// </summary>
    public const ulong NoneValue = ulong.MaxValue;

    /// <summary>
    /// The number of nanoseconds in a second (<c>GST_SECOND</c>).
    /// </summary>
    public const ulong NanosecondsPerSecond = 1_000_000_000;

    /// <summary>
    /// The number of nanoseconds in a millisecond (<c>GST_MSECOND</c>).
    /// </summary>
    public const ulong NanosecondsPerMillisecond = 1_000_000;

    private const ulong NanosecondsPerTick = 100;

    /// <summary>
    /// Initialises a time from a raw nanosecond count.
    /// </summary>
    /// <param name="nanoseconds">The number of nanoseconds.</param>
    public ClockTime(ulong nanoseconds) => Nanoseconds = nanoseconds;

    /// <summary>
    /// Gets the unset time, <c>GST_CLOCK_TIME_NONE</c>.
    /// </summary>
    public static ClockTime None => new(NoneValue);

    /// <summary>
    /// Gets the zero duration.
    /// </summary>
    public static ClockTime Zero => default;

    /// <summary>
    /// Gets the raw nanosecond count.
    /// </summary>
    public ulong Nanoseconds { get; }

    /// <summary>
    /// Gets a value indicating whether this is the unset time.
    /// </summary>
    public bool IsNone => Nanoseconds == NoneValue;

    /// <summary>
    /// Gets the time in seconds, or <see cref="double.NaN"/> when it is unset.
    /// </summary>
    public double TotalSeconds => IsNone ? double.NaN : Nanoseconds / (double)NanosecondsPerSecond;

    /// <summary>
    /// Gets the time in milliseconds, or <see cref="double.NaN"/> when it is
    /// unset.
    /// </summary>
    public double TotalMilliseconds => IsNone ? double.NaN : Nanoseconds / (double)NanosecondsPerMillisecond;

    /// <summary>Compares two times.</summary>
    /// <param name="left">The first time.</param>
    /// <param name="right">The second time.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is the shorter one.</returns>
    public static bool operator <(ClockTime left, ClockTime right) => left.Nanoseconds < right.Nanoseconds;

    /// <summary>Compares two times.</summary>
    /// <param name="left">The first time.</param>
    /// <param name="right">The second time.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not the longer one.</returns>
    public static bool operator <=(ClockTime left, ClockTime right) => left.Nanoseconds <= right.Nanoseconds;

    /// <summary>Compares two times.</summary>
    /// <param name="left">The first time.</param>
    /// <param name="right">The second time.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is the longer one.</returns>
    public static bool operator >(ClockTime left, ClockTime right) => left.Nanoseconds > right.Nanoseconds;

    /// <summary>Compares two times.</summary>
    /// <param name="left">The first time.</param>
    /// <param name="right">The second time.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not the shorter one.</returns>
    public static bool operator >=(ClockTime left, ClockTime right) => left.Nanoseconds >= right.Nanoseconds;

    /// <summary>
    /// Converts a time to a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="value">The time to convert.</param>
    /// <exception cref="InvalidOperationException">The time is unset.</exception>
    public static explicit operator TimeSpan(ClockTime value) => value.ToTimeSpan();

    /// <summary>
    /// Converts a <see cref="TimeSpan"/> to a time.
    /// </summary>
    /// <param name="value">The interval to convert.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static explicit operator ClockTime(TimeSpan value) => FromTimeSpan(value);

    /// <summary>
    /// Creates a time from a nanosecond count.
    /// </summary>
    /// <param name="nanoseconds">The number of nanoseconds.</param>
    /// <returns>The time.</returns>
    public static ClockTime FromNanoseconds(ulong nanoseconds) => new(nanoseconds);

    /// <summary>
    /// Creates a time from a number of seconds.
    /// </summary>
    /// <param name="seconds">The number of seconds.</param>
    /// <returns>The time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="seconds"/> is negative, not a number, or too large to be
    /// counted in nanoseconds.
    /// </exception>
    public static ClockTime FromSeconds(double seconds) => FromScaled(seconds, NanosecondsPerSecond, nameof(seconds));

    /// <summary>
    /// Creates a time from a number of milliseconds.
    /// </summary>
    /// <param name="milliseconds">The number of milliseconds.</param>
    /// <returns>The time.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="milliseconds"/> is negative, not a number, or too large
    /// to be counted in nanoseconds.
    /// </exception>
    public static ClockTime FromMilliseconds(double milliseconds) =>
        FromScaled(milliseconds, NanosecondsPerMillisecond, nameof(milliseconds));

    /// <summary>
    /// Creates a time from a <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="value">The interval to convert.</param>
    /// <returns>The time.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static ClockTime FromTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A clock time cannot be negative.");
        }

        return new ClockTime((ulong)value.Ticks * NanosecondsPerTick);
    }

    /// <summary>
    /// Converts the time to a <see cref="TimeSpan"/>, truncating it to the
    /// hundred nanosecond resolution of <see cref="TimeSpan"/>.
    /// </summary>
    /// <returns>The interval.</returns>
    /// <exception cref="InvalidOperationException">The time is unset.</exception>
    public TimeSpan ToTimeSpan()
    {
        if (IsNone)
        {
            throw new InvalidOperationException("An unset clock time cannot be converted to a TimeSpan.");
        }

        return TimeSpan.FromTicks((long)(Nanoseconds / NanosecondsPerTick));
    }

    /// <inheritdoc/>
    public int CompareTo(ClockTime other) => Nanoseconds.CompareTo(other.Nanoseconds);

    /// <summary>
    /// Formats the time as <c>HH:MM:SS.mmm</c>, or as <c>None</c> when it is
    /// unset.
    /// </summary>
    /// <returns>The formatted time.</returns>
    public override string ToString()
    {
        if (IsNone)
        {
            return "None";
        }

        ulong totalSeconds = Nanoseconds / NanosecondsPerSecond;
        ulong hours = totalSeconds / 3600;
        ulong minutes = totalSeconds / 60 % 60;
        ulong seconds = totalSeconds % 60;
        ulong milliseconds = Nanoseconds % NanosecondsPerSecond / NanosecondsPerMillisecond;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{minutes:00}:{seconds:00}.{milliseconds:000}");
    }

    private static ClockTime FromScaled(double value, ulong scale, string parameterName)
    {
        if (double.IsNaN(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A clock time cannot be negative.");
        }

        double nanoseconds = Math.Round(value * scale);
        if (nanoseconds >= NoneValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "The value does not fit into a clock time.");
        }

        return new ClockTime((ulong)nanoseconds);
    }
}
