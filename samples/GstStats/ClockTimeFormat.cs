using System.Globalization;

/// <summary>
/// The clock time format of the GStreamer tools, <c>GST_TIME_FORMAT</c> with
/// <c>GST_TIME_ARGS</c>.
/// </summary>
/// <remarks>
/// <see cref="Gst.ClockTime.ToString()"/> stops at the millisecond and writes
/// <c>None</c> for the invalid time, where the C tools print all nine digits of
/// the nanosecond and <c>99:99:99.999999999</c>. The report of
/// <c>gst-stats-1.0</c> is compared byte for byte against this port, so it needs
/// the format the macro produces.
/// </remarks>
internal static class ClockTimeFormat
{
    /// <summary>One second, in nanoseconds: <c>GST_SECOND</c>.</summary>
    private const ulong Second = 1000000000UL;

    /// <summary>The invalid time: <c>GST_CLOCK_TIME_NONE</c>.</summary>
    private const ulong None = ulong.MaxValue;

    /// <summary>
    /// Formats a time the way <c>GST_TIME_FORMAT</c> and <c>GST_TIME_ARGS</c> do.
    /// </summary>
    /// <param name="nanoseconds">The time to format.</param>
    /// <returns>
    /// <c>H:MM:SS.nnnnnnnnn</c>, or <c>99:99:99.999999999</c> for the invalid
    /// time, which is what the macro substitutes for every one of its four
    /// arguments.
    /// </returns>
    internal static string Format(ulong nanoseconds)
    {
        if (nanoseconds == None)
        {
            return "99:99:99.999999999";
        }

        ulong hours = nanoseconds / (Second * 60 * 60);
        ulong minutes = nanoseconds / (Second * 60) % 60;
        ulong seconds = nanoseconds / Second % 60;
        ulong rest = nanoseconds % Second;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours}:{minutes:00}:{seconds:00}.{rest:000000000}");
    }

    /// <summary>
    /// Formats a signed time difference, <c>GstClockTimeDiff</c>, the way the C
    /// tool does when it hands one to <c>GST_TIME_ARGS</c>.
    /// </summary>
    /// <param name="difference">The difference to format.</param>
    /// <returns>The formatted time.</returns>
    /// <remarks>
    /// The macro takes its argument as a <c>GstClockTime</c>, so a negative
    /// difference is read as the very large unsigned number its bits spell.
    /// </remarks>
    internal static string FormatDiff(long difference) => Format(unchecked((ulong)difference));
}
