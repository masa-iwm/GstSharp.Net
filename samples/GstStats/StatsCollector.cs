using System.Text;
using System.Text.RegularExpressions;
using Gst;
using Gst.GObject;

/// <summary>
/// The parser and the aggregation of gst-stats.c: it reads the log line by
/// line, turns every payload into a <see cref="Structure"/> and folds the twelve
/// tracer records it knows into the tables the report is printed from.
/// </summary>
internal sealed partial class StatsCollector
{
    /// <summary>
    /// The factory types <c>factory-used</c> is grouped by, in the order the
    /// report lists them: <c>FACTORY_TYPES</c> of gst-stats.c.
    /// </summary>
    internal static readonly IReadOnlyList<string> FactoryTypes =
    [
        "element",
        "device-provider",
        "typefind",
        "dynamic-type",
    ];

    /// <summary>The invalid time, <c>GST_CLOCK_TIME_NONE</c>.</summary>
    private const ulong TimeNone = ClockTime.NoneValue;

    /// <summary>The unset index, <c>G_MAXUINT</c>.</summary>
    private const uint NoIndex = uint.MaxValue;

    private readonly Regex? _customLog;
    private readonly Regex _rawLog;
    private readonly Regex _ansiLog;
    private readonly GType _padDirectionType;
    private readonly GType _bufferFlagsType;

    /// <summary>
    /// Initialises the collector, which compiles the log line parsers and looks
    /// the two enumeration types up that the tracer serialises its fields with.
    /// </summary>
    /// <param name="tracerRegexp">
    /// The regular expression of <c>--tracer-regexp</c>, or <see langword="null"/>
    /// when the log is read with the built-in ones.
    /// </param>
    /// <exception cref="ArgumentException">The custom expression does not compile.</exception>
    /// <exception cref="InvalidOperationException">
    /// GStreamer did not register one of the two types, which would leave
    /// <c>pad-direction</c> or <c>buffer-flags</c> unreadable.
    /// </exception>
    internal StatsCollector(string? tracerRegexp)
    {
        if (tracerRegexp is not null)
        {
            _customLog = new Regex(tracerRegexp, RegexOptions.CultureInvariant);
        }

        // The two expressions of init(), character for character. The raw one
        // reads what a GST_DEBUG_FILE holds; the ansi one reads a log that was
        // captured off a terminal, where the level and the category are wrapped
        // in colour escapes. The file and function of a tracer record are empty,
        // which is why their part of the line matches nothing as well.
        _rawLog = new Regex(
            "^[0-9:.]+ +" +
            "[0-9]+ +" +
            "0?x?[0-9a-fA-F]+ +" +
            "TRACE +" +
            "[a-zA-Z_-]+ +" +
            "[^:]*:[0-9]+:[^:]*: +" +
            "(.*)$",
            RegexOptions.CultureInvariant);

        _ansiLog = new Regex(
            "^[0-9:.]+ +" +
            "\x1b\\[[0-9;]+m *[0-9]+\x1b\\[00m +" +
            "0?x?[0-9a-fA-F]+ +" +
            "(?:\x1b\\[[0-9;]+m)?TRACE +\x1b\\[00m +" +
            "\x1b\\[[0-9;]+m +[a-zA-Z_-]+ +" +
            "[^:]*:[0-9]+:[^:]*:?:\x1b\\[00m? +" +
            "(.*)$",
            RegexOptions.CultureInvariant);

        _padDirectionType = TypeOf("GstPadDirection");
        _bufferFlagsType = TypeOf("GstBufferFlags");
    }

    /// <summary>Gets the threads that were seen, by thread id.</summary>
    internal SortedDictionary<ulong, ThreadStats> Threads { get; } = [];

    /// <summary>Gets the elements, indexed the way the tracer indexed them.</summary>
    internal List<ElementStats?> Elements { get; } = [];

    /// <summary>Gets the pads, indexed the way the tracer indexed them.</summary>
    internal List<PadStats?> Pads { get; } = [];

    /// <summary>Gets the pipeline latencies, by their key.</summary>
    internal SortedDictionary<string, LatencyStats> Latencies { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the per element latencies, by their key.</summary>
    internal SortedDictionary<string, LatencyStats> ElementLatencies { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the latencies the elements reported, in the order they did.</summary>
    internal List<ReportedLatency> ElementReportedLatencies { get; } = [];

    /// <summary>Gets the plugins that were used.</summary>
    internal List<PluginStats> Plugins { get; } = [];

    /// <summary>Gets how many buffers were passed.</summary>
    internal ulong NumBuffers { get; private set; }

    /// <summary>Gets how many events were sent.</summary>
    internal ulong NumEvents { get; private set; }

    /// <summary>Gets how many messages were posted.</summary>
    internal ulong NumMessages { get; private set; }

    /// <summary>Gets how many queries were sent.</summary>
    internal ulong NumQueries { get; private set; }

    /// <summary>Gets how many elements were created, bins included.</summary>
    internal uint NumElements { get; private set; }

    /// <summary>Gets how many of the elements were bins.</summary>
    internal uint NumBins { get; private set; }

    /// <summary>Gets how many pads were created, ghost pads included.</summary>
    internal uint NumPads { get; private set; }

    /// <summary>Gets how many of the pads were ghost pads.</summary>
    internal uint NumGhostPads { get; private set; }

    /// <summary>Gets the last timestamp any record carried.</summary>
    internal ulong LastTs { get; private set; }

    /// <summary>Gets the mean load of the process, in tenths of a percent.</summary>
    internal uint TotalCpuLoad { get; private set; }

    /// <summary>Gets a value indicating whether the process load is known.</summary>
    internal bool HaveCpuLoad { get; private set; }

    /// <summary>Gets a value indicating whether a pipeline latency was seen.</summary>
    internal bool HaveLatency { get; private set; }

    /// <summary>Gets a value indicating whether a per element latency was seen.</summary>
    internal bool HaveElementLatency { get; private set; }

    /// <summary>Gets a value indicating whether an element reported its latency.</summary>
    internal bool HaveElementReportedLatency { get; private set; }

    /// <summary>Gets how many lines matched none of the parsers.</summary>
    internal int Foreign { get; private set; }

    /// <summary>Gets how many structures carried a name no handler knows.</summary>
    internal int Unknown { get; private set; }

    /// <summary>Gets how many payloads did not parse as a structure.</summary>
    internal int Unparsable { get; private set; }

    /// <summary>Gets how many structures reached one of the handlers.</summary>
    internal int Handled { get; private set; }

    /// <summary>
    /// Reads a log: <c>collect_stats</c>. The format is probed on the first line
    /// and the whole file is then read with the parser that probe chose.
    /// </summary>
    /// <param name="filename">The log to read.</param>
    internal void Collect(string filename)
    {
        Regex parser = _rawLog;
        bool any = false;

        foreach (string first in Reads(filename))
        {
            // A custom expression is what was asked for; an escape on the first
            // line says the log was captured off a terminal.
            parser = _customLog ?? (first.Contains('\x1b') ? _ansiLog : _rawLog);
            any = true;
            break;
        }

        if (!any)
        {
            // "empty log": the C tool warns and prints the empty report.
            return;
        }

        foreach (string line in Reads(filename))
        {
            Match match = parser.Match(line);

            if (match.Success)
            {
                Handle(match.Groups[1].Value);
            }
            else
            {
                // Every buffer the C tool reads holds at least the newline, so
                // its own emptiness test is true for all of them.
                Foreign++;
            }
        }
    }

    /// <summary>
    /// Reads the log the way the <c>fgets</c> loop of <c>collect_stats</c> does.
    /// </summary>
    /// <param name="filename">The log to read.</param>
    /// <returns>What each read of the C tool would return.</returns>
    /// <remarks>
    /// The C tool reads into 5000 bytes, so it takes at most 4999 of them at a
    /// time, the newline included, and a longer line arrives as several reads:
    /// the first is a cut structure that does not parse and the rest match no
    /// parser at all. A caps query of a video pipeline is regularly that long,
    /// so the counts of the report depend on it and the port reads the same way.
    /// The carriage return of a log that was written on Windows is dropped,
    /// which is what the text mode of the C tool does when it reads one there.
    /// </remarks>
    private static IEnumerable<string> Reads(string filename)
    {
        const int size = 4999;

        foreach (string raw in File.ReadLines(filename))
        {
            string line = raw.TrimEnd('\r');
            int length = Encoding.UTF8.GetByteCount(line);

            // The newline the C tool still has in its buffer counts towards the
            // 4999 bytes, which is why a line of exactly that length is two
            // reads there.
            if (length + 1 <= size)
            {
                yield return line;
                continue;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");

            for (int i = 0; i < bytes.Length; i += size)
            {
                yield return Encoding.UTF8
                    .GetString(bytes, i, Math.Min(size, bytes.Length - i))
                    .TrimEnd('\n');
            }
        }
    }

    /// <summary>
    /// Looks a type up by name and refuses to carry on without it.
    /// </summary>
    /// <param name="name">The name of the type.</param>
    /// <returns>The type.</returns>
    /// <exception cref="InvalidOperationException">The type is not registered.</exception>
    private static GType TypeOf(string name)
    {
        GType type = GType.FromName(name);

        if (!type.IsValid)
        {
            throw new InvalidOperationException(
                $"{name} is not a registered GType, so the field it types cannot be read");
        }

        return type;
    }

    /// <summary>Reads an unsigned field, or 0 when it is not there.</summary>
    /// <param name="structure">The structure to read.</param>
    /// <param name="field">The name of the field.</param>
    /// <returns>The value.</returns>
    private static uint Uint(Structure structure, string field) =>
        structure.GetUint(field, out uint value) ? value : 0;

    /// <summary>Reads a 64 bit unsigned field, or 0 when it is not there.</summary>
    /// <param name="structure">The structure to read.</param>
    /// <param name="field">The name of the field.</param>
    /// <returns>The value.</returns>
    private static ulong Uint64(Structure structure, string field) =>
        structure.GetUint64(field, out ulong value) ? value : 0;

    /// <summary>Reads a boolean field, or false when it is not there.</summary>
    /// <param name="structure">The structure to read.</param>
    /// <param name="field">The name of the field.</param>
    /// <returns>The value.</returns>
    private static bool Bool(Structure structure, string field) =>
        structure.GetBoolean(field, out bool value) && value;

    /// <summary>
    /// Reads a string field the way <c>g_strdup_printf</c> would print it, so
    /// that a key built from a missing field reads the same as it does in C.
    /// </summary>
    /// <param name="structure">The structure to read.</param>
    /// <param name="field">The name of the field.</param>
    /// <returns>The value, or <c>(null)</c>.</returns>
    private static string Printed(Structure structure, string field) =>
        structure.GetString(field) ?? "(null)";

    /// <summary>
    /// Folds one latency into a table: <c>update_latency_table</c>.
    /// </summary>
    /// <param name="table">The table to fold into.</param>
    /// <param name="key">The key of the row.</param>
    /// <param name="time">The latency that was measured.</param>
    /// <param name="ts">When it was measured.</param>
    private static void UpdateLatencyTable(
        IDictionary<string, LatencyStats> table,
        string key,
        ulong time,
        ulong ts)
    {
        if (!table.TryGetValue(key, out LatencyStats? stats))
        {
            table[key] = new LatencyStats
            {
                Name = key,
                Count = 1,
                Total = time,
                Min = time,
                Max = time,
                FirstLatencyTs = ts,
            };

            return;
        }

        stats.Count++;
        stats.Total += time;

        if (stats.Min > time)
        {
            stats.Min = time;
        }

        if (stats.Max < time)
        {
            stats.Max = time;
        }
    }

    /// <summary>
    /// Turns one payload into a structure and hands it to its handler: the body
    /// of the read loop of <c>collect_stats</c>.
    /// </summary>
    /// <param name="data">The text of the structure.</param>
    private void Handle(string data)
    {
        using Structure? structure = Structure.FromString(data, out _);

        if (structure is null)
        {
            Unparsable++;
            return;
        }

        string name = structure.GetName();

        switch (name)
        {
            case "new-pad":
                NewPadStats(structure);
                break;

            case "new-element":
                NewElementStats(structure);
                break;

            case "buffer":
                DoBufferStats(structure);
                break;

            case "event":
                DoEventStats(structure);
                break;

            case "message":
                DoMessageStats(structure);
                break;

            case "query":
                DoQueryStats(structure);
                break;

            case "thread-rusage":
                DoThreadRusageStats(structure);
                break;

            case "proc-rusage":
                DoProcRusageStats(structure);
                break;

            case "latency":
                DoLatencyStats(structure);
                break;

            case "element-latency":
                DoElementLatencyStats(structure);
                break;

            case "element-reported-latency":
                DoElementReportedLatency(structure);
                break;

            case "factory-used":
                DoFactoryUsed(structure);
                break;

            default:
                // The templates the tracer logs once per record type. The C tool
                // has a TODO about reading them and skips them until then.
                if (!name.EndsWith(".class", StringComparison.Ordinal))
                {
                    Unknown++;
                }

                return;
        }

        Handled++;
    }

    /// <summary>Reads an element by index, the way <c>get_element_stats</c> does.</summary>
    /// <param name="ix">The index of the element.</param>
    /// <returns>The element, or <see langword="null"/>.</returns>
    internal ElementStats? ElementByIndex(uint ix) =>
        ix != NoIndex && ix < Elements.Count ? Elements[(int)ix] : null;

    /// <summary>Reads a pad by index, the way <c>get_pad_stats</c> does.</summary>
    /// <param name="ix">The index of the pad.</param>
    /// <returns>The pad, or <see langword="null"/>.</returns>
    private PadStats? GetPadStats(uint ix) =>
        ix != NoIndex && ix < Pads.Count ? Pads[(int)ix] : null;

    /// <summary>
    /// Reads the statistics of a thread, creating them when the thread is new:
    /// <c>get_thread_stats</c>.
    /// </summary>
    /// <param name="id">The id of the thread.</param>
    /// <returns>The statistics.</returns>
    private ThreadStats GetThreadStats(ulong id)
    {
        if (!Threads.TryGetValue(id, out ThreadStats? stats))
        {
            stats = new ThreadStats();
            Threads[id] = stats;
        }

        return stats;
    }
}
