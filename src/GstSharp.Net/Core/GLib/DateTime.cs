using System.Runtime.InteropServices;
using Gst.GObject;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// A <c>GDateTime</c>: an instant in time, quantized to the microsecond and
/// paired with the offset of the zone it was read in.
/// </summary>
/// <remarks>
/// <para>
/// GLib registers <c>GDateTime</c> as a boxed type whose copy function is
/// <c>g_date_time_ref</c> and whose free function is <c>g_date_time_unref</c>,
/// so this is a <see cref="Gst.GObject.Boxed"/> like every other boxed wrapper
/// of the binding and follows the same rule: the wrapper owns a reference of
/// its own and <b>has to be disposed</b>. Taking a reference is what "copying"
/// a <c>GDateTime</c> costs, because the value is immutable and never needs to
/// be duplicated.
/// </para>
/// <para>
/// What is bound here is what the GStreamer entry points that speak
/// <c>GDateTime</c> need — build a value from an instant or from an ISO 8601
/// string, read the instant back, format it — rather than the whole of the C
/// API. The calendar accessors and the arithmetic have managed equivalents on
/// <see cref="DateTimeOffset"/>, which <see cref="ToDateTimeOffset"/> and
/// <see cref="FromDateTimeOffset"/> convert to and from.
/// </para>
/// <para>
/// The floor of the binding is GLib 2.64, which is what GStreamer 1.28 itself
/// requires, so the microsecond precise constructors of GLib 2.80 — the
/// <c>_usec</c> family — are deliberately absent: a value is built from whole
/// seconds and then advanced by its microseconds, which is exact over the same
/// range.
/// </para>
/// <para>
/// <b>The identity of the zone is not carried.</b> A <c>GDateTime</c> holds a
/// <c>GTimeZone</c>, which this binding does not project; what crosses is the
/// numeric UTC offset of the value. <see cref="ToDateTimeOffset"/> is therefore
/// faithful to the instant and to the offset but not to the zone, and
/// <see cref="FromDateTimeOffset"/> answers a value in UTC whose instant is the
/// one it was given. Round tripping compares equal as an instant — with
/// <c>==</c> on <see cref="DateTimeOffset"/> — rather than field by field.
/// </para>
/// <para>
/// Not to be confused with <see cref="Gst.DateTime"/>, which is the calendar
/// value of GStreamer itself: it stores a partial date — a year alone is a
/// valid one — and a floating point hour offset, and it carries no zone
/// identity either. This type is the GLib value that
/// <see cref="Gst.DateTime.ToGDateTime"/> hands out and that
/// <see cref="Gst.DateTime.NewFromGDateTime"/> consumes.
/// </para>
/// </remarks>
public sealed unsafe partial class DateTime : Boxed
{
    /// <summary>
    /// Wraps a native <c>GDateTime</c>.
    /// </summary>
    /// <param name="handle">The value to wrap.</param>
    /// <param name="transfer">
    /// <see cref="Transfer.Full"/> when the caller hands its reference over,
    /// <see cref="Transfer.None"/> to take one of our own.
    /// </param>
    internal DateTime(nint handle, Transfer transfer)
        : base(handle, new GType(GetGType()), transfer)
    {
    }

    /// <summary>
    /// Gets the microsecond of the second, from <c>0</c> to <c>999999</c>.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public int Microsecond
    {
        get
        {
            int microsecond = GDateTimeGetMicrosecond(Handle);
            GC.KeepAlive(this);
            return microsecond;
        }
    }

    /// <summary>
    /// Gets the offset of the zone the value was read in, relative to UTC.
    /// </summary>
    /// <remarks>
    /// This is the offset in force at that instant, so it already accounts for
    /// daylight saving time; the zone it belongs to is not carried by the
    /// wrapper.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public TimeSpan UtcOffset
    {
        get
        {
            // GTimeSpan is a count of microseconds, and one microsecond is ten
            // ticks.
            TimeSpan offset = new(GDateTimeGetUtcOffset(Handle) * 10);
            GC.KeepAlive(this);
            return offset;
        }
    }

    /// <summary>
    /// Gets the abbreviation of the zone the value was read in, such as
    /// <c>JST</c> or <c>CEST</c>.
    /// </summary>
    /// <remarks>
    /// The string belongs to the value and is not copied out of it, so it is
    /// read while this wrapper still holds its reference.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string? TimezoneAbbreviation
    {
        get
        {
            string? abbreviation = GMarshal.PtrToStringUtf8(GDateTimeGetTimezoneAbbreviation(Handle));
            GC.KeepAlive(this);
            return abbreviation;
        }
    }

    /// <summary>
    /// Creates a value in UTC from a Unix timestamp.
    /// </summary>
    /// <param name="seconds">The seconds since the Unix epoch.</param>
    /// <param name="microseconds">
    /// The microseconds to add to <paramref name="seconds"/>, from <c>0</c> to
    /// <c>999999</c>.
    /// </param>
    /// <returns>
    /// The new value, which the caller has to dispose, or
    /// <see langword="null"/> when the instant is outside the range GLib
    /// represents.
    /// </returns>
    /// <remarks>
    /// The microseconds are added with <c>g_date_time_add</c>, which counts in
    /// microseconds exactly; the seconds based <c>g_date_time_add_seconds</c>
    /// takes a <c>double</c> and rounds.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="microseconds"/> is negative or larger than
    /// <c>999999</c>.
    /// </exception>
    public static DateTime? FromUnixUtc(long seconds, int microseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(microseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(microseconds, 999_999);

        nint handle = GDateTimeNewFromUnixUtc(seconds);
        if (handle == nint.Zero)
        {
            return null;
        }

        DateTime whole = new(handle, Transfer.Full);
        if (microseconds == 0)
        {
            return whole;
        }

        // The whole second value is a reference of its own, and disposing its
        // wrapper is the unref that releases it; that is why this type needs no
        // g_date_time_unref import.
        using (whole)
        {
            return FromNative(GDateTimeAdd(whole.Handle, microseconds), Transfer.Full);
        }
    }

    /// <summary>
    /// Parses an ISO 8601 timestamp.
    /// </summary>
    /// <param name="text">
    /// The timestamp to parse. It has to carry a zone suffix, which is either
    /// <c>Z</c> or an offset spelled <c>+hh:mm</c>, <c>-hh:mm</c>, <c>+hh</c>
    /// or <c>-hh</c>.
    /// </param>
    /// <returns>
    /// The new value, which the caller has to dispose, or
    /// <see langword="null"/> when <paramref name="text"/> is not a timestamp
    /// GLib accepts.
    /// </returns>
    /// <remarks>
    /// A timestamp that carries no zone suffix is rejected and answers
    /// <see langword="null"/>: GLib reads such a value in the fallback zone it
    /// is handed, and this binding hands it none, because the
    /// <c>GTimeZone</c> a fallback would be spelled in is not projected.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static DateTime? FromIso8601(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(text, buffer);
        return FromNative(GDateTimeNewFromIso8601(scope.Pointer, nint.Zero), Transfer.Full);
    }

    /// <summary>
    /// Creates a value in UTC from a managed one.
    /// </summary>
    /// <param name="value">The instant to represent.</param>
    /// <returns>
    /// The new value, which the caller has to dispose, or
    /// <see langword="null"/> when the instant is outside the range GLib
    /// represents.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Two things are left behind. The result is a value in UTC: the instant is
    /// exact, but the offset of <paramref name="value"/> is not carried,
    /// because representing it needs a <c>GTimeZone</c> that this binding does
    /// not project. And a <see cref="DateTimeOffset"/> counts in ticks of a
    /// hundred nanoseconds while a <c>GDateTime</c> counts in microseconds, so
    /// anything below a microsecond is truncated.
    /// </para>
    /// <para>
    /// Round tripping through <see cref="ToDateTimeOffset"/> therefore compares
    /// equal with <c>==</c>, which compares instants, and not with
    /// <see cref="DateTimeOffset.EqualsExact"/>, which compares the offsets too.
    /// </para>
    /// </remarks>
    public static DateTime? FromDateTimeOffset(DateTimeOffset value) =>
        FromUnixUtc(value.ToUnixTimeSeconds(), (int)(value.UtcDateTime.Ticks % 10_000_000 / 10));

    /// <summary>
    /// Reads the instant as a Unix timestamp, truncated to the second.
    /// </summary>
    /// <returns>The seconds since the Unix epoch.</returns>
    /// <remarks>
    /// The microseconds of the value are not in the result:
    /// <see cref="Microsecond"/> reads them, and
    /// <see cref="ToDateTimeOffset"/> carries both.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public long ToUnix()
    {
        long seconds = GDateTimeToUnix(Handle);
        GC.KeepAlive(this);
        return seconds;
    }

    /// <summary>
    /// Converts the value into a managed one.
    /// </summary>
    /// <returns>The same instant, at the offset of this value.</returns>
    /// <remarks>
    /// The conversion is exact: a <c>GDateTime</c> is quantized to the
    /// microsecond and spans the years 1 to 9999, which
    /// <see cref="DateTimeOffset"/> covers with room to spare. What it does not
    /// carry is the identity of the zone, only its offset.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public DateTimeOffset ToDateTimeOffset() =>
        DateTimeOffset.FromUnixTimeSeconds(ToUnix())
            .AddTicks(Microsecond * 10)
            .ToOffset(UtcOffset);

    /// <summary>
    /// Formats the value with a <c>strftime</c> style format string.
    /// </summary>
    /// <param name="format">
    /// The format, in the vocabulary of <c>g_date_time_format</c>.
    /// </param>
    /// <returns>
    /// The formatted value, or <see langword="null"/> when
    /// <paramref name="format"/> names a conversion GLib does not know or the
    /// result is not valid UTF-8.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="format"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string? Format(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(format, buffer);
        string? formatted = GMarshal.PtrToStringUtf8AndFree(GDateTimeFormat(Handle, scope.Pointer));
        GC.KeepAlive(this);
        return formatted;
    }

    /// <summary>
    /// Formats the value as an ISO 8601 timestamp.
    /// </summary>
    /// <returns>
    /// The formatted value, or <see langword="null"/> when the value is outside
    /// the range ISO 8601 describes.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public string? FormatIso8601()
    {
        string? formatted = GMarshal.PtrToStringUtf8AndFree(GDateTimeFormatIso8601(Handle));
        GC.KeepAlive(this);
        return formatted;
    }

    /// <summary>
    /// Wraps a native <c>GDateTime</c>, mapping the null pointer onto
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="handle">The value to wrap, or <c>0</c>.</param>
    /// <param name="transfer">How ownership of <paramref name="handle"/> is transferred.</param>
    /// <returns>The wrapper, or <see langword="null"/> for a null handle.</returns>
    internal static DateTime? FromNative(nint handle, Transfer transfer) =>
        handle == nint.Zero ? null : new DateTime(handle, transfer);

    /// <summary>The <c>g_date_time_new_from_unix_utc</c> entry point.</summary>
    /// <remarks>
    /// The imports of this type live next to it rather than in
    /// <see cref="GLibNative"/>, which collects what the runtime itself needs;
    /// the hand written wrappers of the binding carry their own entry points,
    /// as <see cref="Bytes"/> does.
    /// </remarks>
    [LibraryImport("GLib", EntryPoint = "g_date_time_new_from_unix_utc")]
    private static partial nint GDateTimeNewFromUnixUtc(long seconds);

    /// <summary>
    /// The <c>g_date_time_new_from_iso8601</c> entry point. Its second argument
    /// is the <c>GTimeZone</c> to fall back on when the text carries no zone
    /// suffix, and is always <c>NULL</c> here, which makes GLib reject such a
    /// text rather than read it in a zone of its own choosing.
    /// </summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_new_from_iso8601")]
    private static partial nint GDateTimeNewFromIso8601(byte* text, nint defaultTimeZone);

    /// <summary>
    /// The <c>g_date_time_add</c> entry point. Its second argument is a
    /// <c>GTimeSpan</c>, which is a count of microseconds.
    /// </summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_add")]
    private static partial nint GDateTimeAdd(nint dateTime, long timeSpan);

    /// <summary>The <c>g_date_time_to_unix</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_to_unix")]
    private static partial long GDateTimeToUnix(nint dateTime);

    /// <summary>The <c>g_date_time_get_microsecond</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_get_microsecond")]
    private static partial int GDateTimeGetMicrosecond(nint dateTime);

    /// <summary>The <c>g_date_time_get_utc_offset</c> entry point, which answers microseconds.</summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_get_utc_offset")]
    private static partial long GDateTimeGetUtcOffset(nint dateTime);

    /// <summary>The <c>g_date_time_format</c> entry point, which returns a string the caller owns.</summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_format")]
    private static partial nint GDateTimeFormat(nint dateTime, byte* format);

    /// <summary>The <c>g_date_time_format_iso8601</c> entry point, which returns a string the caller owns.</summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_format_iso8601")]
    private static partial nint GDateTimeFormatIso8601(nint dateTime);

    /// <summary>
    /// The <c>g_date_time_get_timezone_abbreviation</c> entry point, which
    /// returns a string that belongs to the value.
    /// </summary>
    [LibraryImport("GLib", EntryPoint = "g_date_time_get_timezone_abbreviation")]
    private static partial nint GDateTimeGetTimezoneAbbreviation(nint dateTime);

    /// <summary>
    /// The <c>g_date_time_get_type</c> entry point, which lives in GObject
    /// rather than in GLib: the boxed registration of <c>GDateTime</c> is part
    /// of the type system, not of the data structure.
    /// </summary>
    /// <returns>The boxed type of the instances of this wrapper.</returns>
    [LibraryImport("GObject", EntryPoint = "g_date_time_get_type")]
    private static partial nuint GetGType();
}
