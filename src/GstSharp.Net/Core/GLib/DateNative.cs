using System.Runtime.InteropServices;

namespace Gst.GLib;

/// <summary>
/// Raw entry points of <c>GDate</c>, the calendar date of GLib.
/// </summary>
/// <remarks>
/// <para>
/// <c>GDate</c> gets no wrapper of its own. It is a plain calendar date with no
/// identity, no reference count and no state a caller could observe, so the
/// binding converts it to and from <see cref="System.DateOnly"/> at the
/// boundary and never hands one out: a generated member that takes a date takes
/// a <see cref="System.DateOnly"/>, and one that produces a date produces a
/// <c>DateOnly?</c>.
/// </para>
/// <para>
/// The conversion is total in one direction and partial in the other. Every
/// <see cref="System.DateOnly"/> is a valid <c>GDate</c>, because
/// <c>GDateYear</c> covers 1 to 65535 and the day and the month are those of a
/// real date. The other way round, <c>GDateYear</c> is 16 bits wide and
/// <see cref="System.DateOnly"/> stops at 9999, so a date beyond that year
/// cannot be represented and <see cref="ToDateOnly"/> lets the
/// <see cref="ArgumentOutOfRangeException"/> of the constructor through — after
/// releasing the value it was given, so nothing leaks.
/// </para>
/// </remarks>
internal static partial class DateNative
{
    /// <summary>Allocates a date from a day, a month and a year.</summary>
    /// <param name="day">The day of the month, 1 to 31.</param>
    /// <param name="month">The month, 1 to 12.</param>
    /// <param name="year">The year, 1 to 65535.</param>
    /// <returns>The date, which the caller frees with <see cref="Free"/>.</returns>
    [LibraryImport("GLib", EntryPoint = "g_date_new_dmy")]
    internal static partial nint NewDmy(byte day, int month, ushort year);

    /// <summary>Reads the year of a date.</summary>
    /// <param name="date">The date to read.</param>
    /// <returns>The year.</returns>
    [LibraryImport("GLib", EntryPoint = "g_date_get_year")]
    internal static partial ushort GetYear(nint date);

    /// <summary>Reads the month of a date.</summary>
    /// <param name="date">The date to read.</param>
    /// <returns>The month, 1 to 12.</returns>
    [LibraryImport("GLib", EntryPoint = "g_date_get_month")]
    internal static partial int GetMonth(nint date);

    /// <summary>Reads the day of the month of a date.</summary>
    /// <param name="date">The date to read.</param>
    /// <returns>The day, 1 to 31.</returns>
    [LibraryImport("GLib", EntryPoint = "g_date_get_day")]
    internal static partial byte GetDay(nint date);

    /// <summary>Answers whether a date represents a day.</summary>
    /// <param name="date">The date to test.</param>
    /// <returns>Non zero when the date is valid.</returns>
    [LibraryImport("GLib", EntryPoint = "g_date_valid")]
    internal static partial int Valid(nint date);

    /// <summary>Releases a date.</summary>
    /// <param name="date">The date to release.</param>
    [LibraryImport("GLib", EntryPoint = "g_date_free")]
    internal static partial void Free(nint date);

    /// <summary>
    /// Adopts the date a call produced and converts it to a managed value.
    /// </summary>
    /// <param name="date">The date to adopt, or <c>0</c>.</param>
    /// <returns>
    /// The date, or <see langword="null"/> when there was none and when the
    /// value does not represent a day.
    /// </returns>
    /// <remarks>
    /// The pointer is always released, whichever way the conversion goes. A
    /// callee that answered <c>TRUE</c> with a null date — which
    /// <c>gst_structure_get_date</c> and <c>ges_meta_container_get_date</c> both
    /// can, because a generic value may hold one — hands out
    /// <see langword="null"/> beside the <c>true</c>, exactly as C sees it.
    /// A value that is only julian rather than day-month-year is converted by
    /// the accessors themselves, so no such case is left out.
    /// </remarks>
    internal static System.DateOnly? ToDateOnly(nint date)
    {
        if (date == 0)
        {
            return null;
        }

        try
        {
            if (Valid(date) == 0)
            {
                return null;
            }

            return new System.DateOnly(GetYear(date), GetMonth(date), GetDay(date));
        }
        finally
        {
            Free(date);
        }
    }
}

/// <summary>
/// A transient <c>GDate</c> built from a managed date, valid until the scope is
/// disposed.
/// </summary>
/// <remarks>
/// It is the shape a <c>const GDate*</c> parameter takes: the callee reads the
/// value and copies whatever it keeps, so the allocation belongs to the call and
/// <see cref="Dispose"/> is the <c>g_date_free</c> that matches it.
/// </remarks>
internal ref struct DateScope
{
    private nint _handle;

    private DateScope(nint handle) => _handle = handle;

    /// <summary>Gets the pointer to the native date.</summary>
    internal readonly nint Pointer => _handle;

    /// <summary>Builds a native date from a managed one.</summary>
    /// <param name="date">The date to build.</param>
    /// <returns>The scope that owns it.</returns>
    /// <remarks>
    /// Every <see cref="System.DateOnly"/> is a day-month-year GLib accepts, so
    /// the allocation does not fail; a pointer of zero would reach the callee as
    /// the null it guards against, which is the same answer C gives.
    /// </remarks>
    internal static DateScope Alloc(System.DateOnly date) =>
        new(DateNative.NewDmy((byte)date.Day, date.Month, (ushort)date.Year));

    /// <summary>Releases the native date.</summary>
    public void Dispose()
    {
        if (_handle != 0)
        {
            DateNative.Free(_handle);
        }

        _handle = 0;
    }
}
