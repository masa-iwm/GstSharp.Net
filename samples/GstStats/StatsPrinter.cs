using System.Globalization;
using Gst;

/// <summary>
/// The report of gst-stats.c: <c>print_stats</c> and the printers it calls, with
/// the format strings of the C tool reproduced through .NET alignment.
/// </summary>
internal sealed partial class StatsPrinter
{
    /// <summary>The invalid time, <c>GST_CLOCK_TIME_NONE</c>.</summary>
    private const ulong TimeNone = ClockTime.NoneValue;

    /// <summary>The unset index, <c>G_MAXUINT</c>.</summary>
    private const uint NoIndex = uint.MaxValue;

    private readonly StatsCollector _stats;
    private readonly TextWriter _writer;

    /// <summary>Initialises the printer.</summary>
    /// <param name="stats">What was collected from the log.</param>
    /// <param name="writer">Where the report goes.</param>
    internal StatsPrinter(StatsCollector stats, TextWriter writer)
    {
        _stats = stats;
        _writer = writer;
    }

    /// <summary>Prints the whole report: <c>print_stats</c>.</summary>
    internal void Print()
    {
        int numThreads = _stats.Threads.Count;

        Line();
        Line("Overall Statistics:");
        Line(Text($"Number of Threads: {numThreads}"));
        Line(Text($"Number of Elements: {unchecked(_stats.NumElements - _stats.NumBins)}"));
        Line(Text($"Number of Bins: {_stats.NumBins}"));
        Line(Text($"Number of Pads: {unchecked(_stats.NumPads - _stats.NumGhostPads)}"));
        Line(Text($"Number of GhostPads: {_stats.NumGhostPads}"));
        Line(Text($"Number of Buffers passed: {_stats.NumBuffers}"));
        Line(Text($"Number of Events sent: {_stats.NumEvents}"));
        Line(Text($"Number of Message sent: {_stats.NumMessages}"));
        Line(Text($"Number of Queries sent: {_stats.NumQueries}"));
        Line(Text($"Time: {ClockTimeFormat.Format(_stats.LastTs)}"));

        if (_stats.HaveCpuLoad)
        {
            Line(Text($"Avg CPU load: {CpuLoad(_stats.TotalCpuLoad),4} %"));
        }

        Line();

        if (numThreads != 0)
        {
            List<PadStats> pads = [];

            foreach (PadStats? pad in _stats.Pads)
            {
                // The C tool hands every slot of the array to the sorter, a hole
                // of the index space included, and then reads through it.
                if (pad is not null)
                {
                    InsertSorted(pads, pad, ComparePadsByFirstActivity);
                }
            }

            foreach ((ulong id, ThreadStats thread) in _stats.Threads)
            {
                PrintThreadStats(id, thread, pads);
            }

            Line();
        }

        if (_stats.NumElements != 0)
        {
            List<ElementStats> elements = [];

            Line("Element Statistics:");

            foreach (ElementStats? element in _stats.Elements)
            {
                if (element is { IsBin: false })
                {
                    InsertSorted(elements, element, CompareElementsByFirstActivity);
                }
            }

            foreach (ElementStats element in elements)
            {
                AccumElementStats(element);
            }

            foreach (ElementStats element in elements)
            {
                PrintElementStats(element);
            }

            Line();
        }

        if (_stats.NumBins != 0)
        {
            List<ElementStats> bins = [];

            Line("Bin Statistics:");
            AccumBinStats();

            foreach (ElementStats? element in _stats.Elements)
            {
                if (element is { IsBin: true })
                {
                    InsertSorted(bins, element, CompareElementsByFirstActivity);
                }
            }

            foreach (ElementStats bin in bins)
            {
                PrintElementStats(bin);
            }

            Line();
        }

        if (_stats.HaveLatency)
        {
            Line("Latency Statistics:");
            PrintLatencies(_stats.Latencies);
            Line();
        }

        if (_stats.HaveElementLatency)
        {
            Line("Element Latency Statistics:");
            PrintLatencies(_stats.ElementLatencies);
            Line();
        }

        if (_stats.HaveElementReportedLatency)
        {
            Line("Element Reported Latency:");

            foreach (ReportedLatency latency in _stats.ElementReportedLatencies)
            {
                Line(
                    Text($"\t{latency.Element}: min={ClockTimeFormat.Format(latency.Min)}") +
                    Text($" max={ClockTimeFormat.Format(latency.Max)}") +
                    Text($" ts={ClockTimeFormat.Format(latency.Ts)}"));
            }

            Line();
        }

        if (_stats.Plugins.Count > 0)
        {
            PrintPlugins();
        }
    }

    /// <summary>Formats a string with the culture the C library prints with.</summary>
    /// <param name="text">The already interpolated text.</param>
    /// <returns>The text.</returns>
    private static string Text(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a load the way <c>%4.1f</c> does, as the tenths of a percent the
    /// rusage tracer reports divided by ten.
    /// </summary>
    /// <param name="load">The load, in tenths of a percent.</param>
    /// <returns>The formatted load.</returns>
    private static string CpuLoad(uint load) =>
        ((float)load / 10.0f).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a double the way <c>%lf</c> does, the three names of the special
    /// values included: a pad that saw one buffer has no running time, and the
    /// C tool then prints <c>inf</c> rather than a number.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted value.</returns>
    private static string Double(double value)
    {
        if (double.IsNaN(value))
        {
            return double.IsNegative(value) ? "-nan" : "nan";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "inf";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "-inf";
        }

        return value.ToString("F6", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a thread id the way <c>%p</c> does on the platform this runs on:
    /// the C library of Windows pads it to sixteen hexadecimal digits and glibc
    /// prefixes it with <c>0x</c> and pads nothing.
    /// </summary>
    /// <param name="id">The id of the thread.</param>
    /// <returns>The formatted id.</returns>
    private static string Pointer(ulong id)
    {
        if (OperatingSystem.IsWindows())
        {
            return id.ToString("x16", CultureInfo.InvariantCulture);
        }

        return id == 0 ? "(nil)" : "0x" + id.ToString("x", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Truncates a string the way <c>g_snprintf</c> does, which writes at most
    /// one character less than the size of the buffer it is given.
    /// </summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="size">The size of the buffer.</param>
    /// <returns>The truncated text.</returns>
    private static string Truncate(string text, int size) =>
        text.Length > size - 1 ? text[..(size - 1)] : text;

    /// <summary>
    /// The difference of two clock times, as the comparison functions of the C
    /// tool see it: <c>GST_CLOCK_DIFF</c> is 64 bit and the comparison function
    /// it feeds returns a 32 bit int, so the difference is truncated -- which
    /// this reproduces, because the truncation decides the order of two records
    /// that are more than about two seconds apart.
    /// </summary>
    /// <param name="a">The first time.</param>
    /// <param name="b">The second time.</param>
    /// <returns>The truncated difference.</returns>
    private static int ClockDiff(ulong a, ulong b) => unchecked((int)((long)a - (long)b));

    /// <summary>Orders two pads: <c>sort_pad_stats_by_first_activity</c>.</summary>
    /// <param name="s1">The pad to place.</param>
    /// <param name="s2">The pad it is compared against.</param>
    /// <returns>The order of the two.</returns>
    private static int ComparePadsByFirstActivity(PadStats s1, PadStats s2)
    {
        int order = ClockDiff(s1.FirstTs, s2.FirstTs);

        return order != 0 ? order : (int)s1.Direction - (int)s2.Direction;
    }

    /// <summary>Orders two elements: <c>sort_element_stats_by_first_activity</c>.</summary>
    /// <param name="es1">The element to place.</param>
    /// <param name="es2">The element it is compared against.</param>
    /// <returns>The order of the two.</returns>
    private static int CompareElementsByFirstActivity(ElementStats es1, ElementStats es2) =>
        ClockDiff(es1.FirstTs, es2.FirstTs);

    /// <summary>Orders two latency rows: <c>sort_latency_stats_by_first_ts</c>.</summary>
    /// <param name="ls1">The row to place.</param>
    /// <param name="ls2">The row it is compared against.</param>
    /// <returns>The order of the two.</returns>
    private static int CompareLatenciesByFirstTs(LatencyStats ls1, LatencyStats ls2) =>
        ClockDiff(ls1.FirstLatencyTs, ls2.FirstLatencyTs);

    /// <summary>
    /// Inserts into a sorted list the way <c>g_slist_insert_sorted</c> does: in
    /// front of the first entry the new one does not sort after, so that entries
    /// that compare equal come out in the reverse of the order they went in.
    /// </summary>
    /// <typeparam name="T">What the list holds.</typeparam>
    /// <param name="list">The list to insert into.</param>
    /// <param name="item">The entry to insert.</param>
    /// <param name="compare">How two entries are ordered.</param>
    private static void InsertSorted<T>(List<T> list, T item, Comparison<T> compare)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (compare(item, list[i]) <= 0)
            {
                list.Insert(i, item);
                return;
            }
        }

        list.Add(item);
    }

    /// <summary>
    /// Sorts in place and keeps the order of equal entries, which is what
    /// <c>g_list_sort</c>, a merge sort, does.
    /// </summary>
    /// <typeparam name="T">What the list holds.</typeparam>
    /// <param name="list">The list to sort.</param>
    /// <param name="compare">How two entries are ordered.</param>
    private static void StableSort<T>(List<T> list, Comparison<T> compare)
    {
        for (int i = 1; i < list.Count; i++)
        {
            T item = list[i];
            int j = i - 1;

            while (j >= 0 && compare(list[j], item) > 0)
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = item;
        }
    }

    /// <summary>Writes an empty line.</summary>
    private void Line() => _writer.Write('\n');

    /// <summary>Writes one line.</summary>
    /// <param name="text">The text of the line.</param>
    private void Line(string text)
    {
        _writer.Write(text);
        _writer.Write('\n');
    }
}
