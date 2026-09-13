using System.Globalization;
using Gst;

/// <summary>
/// The sections of the report: the accumulation into bins that runs before
/// two of them are printed, and one printer per section of gst-stats.c.
/// </summary>
internal sealed partial class StatsPrinter
{
    /// <summary>
    /// Adds what an element did to the bin it is in: <c>accum_element_stats</c>.
    /// </summary>
    /// <param name="stats">The element to fold into its parent.</param>
    private void AccumElementStats(ElementStats stats)
    {
        if (stats.ParentIx == NoIndex || _stats.ElementByIndex(stats.ParentIx) is not ElementStats parent)
        {
            return;
        }

        parent.NumEvents += stats.NumEvents;
        parent.NumMessages += stats.NumMessages;
        parent.NumQueries += stats.NumQueries;

        if (parent.FirstTs == TimeNone)
        {
            parent.FirstTs = stats.FirstTs;
        }
        else if (stats.FirstTs != TimeNone)
        {
            parent.FirstTs = Math.Min(parent.FirstTs, stats.FirstTs);
        }

        if (parent.LastTs == TimeNone)
        {
            parent.LastTs = stats.LastTs;
        }
        else if (stats.LastTs != TimeNone)
        {
            parent.LastTs = Math.Max(parent.LastTs, stats.LastTs);
        }
    }

    /// <summary>
    /// Folds every bin into the bin above it, deepest first: the
    /// <c>process_leaf_bins</c> loop of <c>print_stats</c>.
    /// </summary>
    private void AccumBinStats()
    {
        Dictionary<uint, ElementStats> accumBins = [];

        for (uint i = 0; i < _stats.NumElements && i < _stats.Elements.Count; i++)
        {
            if (_stats.Elements[(int)i] is { IsBin: true } stats)
            {
                accumBins[i] = stats;
            }
        }

        while (accumBins.Count > 0)
        {
            List<uint> leaves = [];

            foreach ((uint index, ElementStats stats) in accumBins)
            {
                bool hasChildBin = false;

                foreach (ElementStats candidate in accumBins.Values)
                {
                    if (candidate.ParentIx == index)
                    {
                        hasChildBin = true;
                        break;
                    }
                }

                if (!hasChildBin)
                {
                    AccumElementStats(stats);
                    leaves.Add(index);
                }
            }

            if (leaves.Count == 0)
            {
                // A bin that is its own ancestor would keep the C tool in this
                // loop for ever. It cannot happen in a log a pipeline wrote.
                break;
            }

            foreach (uint index in leaves)
            {
                accumBins.Remove(index);
            }
        }
    }

    /// <summary>Prints one thread and its pads: <c>print_thread_stats</c>.</summary>
    /// <param name="id">The id of the thread.</param>
    /// <param name="stats">What is known about the thread.</param>
    /// <param name="pads">Every pad, in the order the report lists them.</param>
    private void PrintThreadStats(ulong id, ThreadStats stats, List<PadStats> pads)
    {
        bool any = false;

        foreach (PadStats pad in pads)
        {
            if (pad.ThreadId == id && pad.NumBuffers != 0)
            {
                any = true;
                break;
            }
        }

        // A thread that drove no pad, a pipeline thread for example, is skipped.
        if (!any)
        {
            return;
        }

        Line(Text($"Thread {Pointer(id)} Statistics:"));

        if (stats.TThread != TimeNone)
        {
            Line(Text($"  Time: {ClockTimeFormat.Format(stats.TThread)}"));
            Line(Text($"  Avg CPU load: {CpuLoad(stats.CpuLoad),4} %"));
        }

        Line("  Pad Statistics:");

        foreach (PadStats pad in pads)
        {
            if (pad.ThreadId == id && pad.NumBuffers != 0)
            {
                PrintPadStats(pad);
            }
        }
    }

    /// <summary>Prints one pad: <c>print_pad_stats</c>.</summary>
    /// <param name="stats">The pad to print.</param>
    private void PrintPadStats(PadStats stats)
    {
        long running = unchecked((long)stats.LastTs - (long)stats.FirstTs);
        ElementStats? element = _stats.ElementByIndex(stats.ParentIx);
        string fullname = Truncate($"{element?.Name ?? string.Empty}.{stats.Name}", 30);

        // The three printfs of the C tool, which make one line.
        string line =
            Text($"    {(stats.Direction == PadDirection.Src ? '>' : '<')} {fullname,-30}") +
            Text($": buffers {stats.NumBuffers,7} (live {stats.NumLive,5},dec {stats.NumDecodeOnly,5},") +
            Text($"dis {stats.NumDiscont,5},res {stats.NumResync,5},cor {stats.NumCorrupted,5},") +
            Text($"mar {stats.NumMarker,5},hdr {stats.NumHeader,5},gap {stats.NumGap,5},") +
            Text($"drop {stats.NumDroppable,5},dlt {stats.NumDelta,5}),");

        line += stats.MinSize == stats.MaxSize
            ? Text($" size (min/avg/max) ......./{stats.AvgSize,7}/.......,")
            : Text($" size (min/avg/max) {stats.MinSize,7}/{stats.AvgSize,7}/{stats.MaxSize,7},");

        // The C tool multiplies two guints here, so the product wraps at four
        // gigabytes, and it divides by a running time of zero when the pad saw
        // exactly one buffer.
        double bytes = unchecked(stats.NumBuffers * stats.AvgSize);
        double bytesPerSecond = bytes * 1000000000.0 / running;

        line += Text($" time {ClockTimeFormat.FormatDiff(running)}, bytes/sec {Double(bytesPerSecond)}");

        Line(line);
    }

    /// <summary>Prints one element or bin: <c>print_element_stats</c>.</summary>
    /// <param name="stats">The element to print.</param>
    private void PrintElementStats(ElementStats stats)
    {
        // A temporary element that never did anything is skipped.
        if (stats.FirstTs == TimeNone)
        {
            return;
        }

        string fullname = Truncate($"{stats.TypeName}:{stats.Name}", 45);
        string line = Text($"  {fullname,-45}:");

        line += stats.RecvBuffers != 0
            ? Text($" buffers in/out {stats.RecvBuffers,7}")
            : Text($" buffers in/out {"-",7}");

        line += stats.SentBuffers != 0
            ? Text($"/{stats.SentBuffers,7}")
            : Text($"/{"-",7}");

        line += stats.RecvBytes != 0
            ? Text($" bytes in/out {stats.RecvBytes,12}")
            : Text($" bytes in/out {"-",12}");

        line += stats.SentBytes != 0
            ? Text($"/{stats.SentBytes,12}")
            : Text($"/{"-",12}");

        line +=
            Text($" first activity {ClockTimeFormat.Format(stats.FirstTs)}, ") +
            Text($" ev/msg/qry sent {stats.NumEvents,5}/{stats.NumMessages,5}/{stats.NumQueries,5}");

        Line(line);
    }

    /// <summary>Prints one latency table: <c>print_latency_stats</c> over its list.</summary>
    /// <param name="table">The table to print.</param>
    private void PrintLatencies(SortedDictionary<string, LatencyStats> table)
    {
        // The C tool reads the values out of a hash table, whose order is
        // undefined, and sorts them by their first timestamp with a stable sort.
        // The keys order what the hash left unordered here.
        List<LatencyStats> list = [.. table.Values];

        StableSort(list, CompareLatenciesByFirstTs);

        foreach (LatencyStats stats in list)
        {
            Line(
                Text($"\t{stats.Name}: mean={ClockTimeFormat.Format(stats.Total / stats.Count)}") +
                Text($" min={ClockTimeFormat.Format(stats.Min)}") +
                Text($" max={ClockTimeFormat.Format(stats.Max)}"));
        }
    }

    /// <summary>Prints the plugins and their factories: the tail of <c>print_stats</c>.</summary>
    private void PrintPlugins()
    {
        List<PluginStats> plugins = [.. _stats.Plugins];

        plugins.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        _writer.Write("Plugins used: ");

        for (int i = 0; i < plugins.Count; i++)
        {
            _writer.Write(i == 0 ? plugins[i].Name : ";" + plugins[i].Name);
        }

        Line();

        for (int f = 0; f < StatsCollector.FactoryTypes.Count; f++)
        {
            string type = StatsCollector.FactoryTypes[f];
            bool first = true;

            _writer.Write(Text($"{char.ToUpperInvariant(type[0])}{type[1..]}s: "));

            foreach (PluginStats plugin in plugins)
            {
                List<string> factories = plugin.Factories[f];

                if (factories.Count == 0)
                {
                    continue;
                }

                _writer.Write(first ? plugin.Name + ":" : ";" + plugin.Name + ":");
                first = false;

                List<string> sorted = [.. factories];

                sorted.Sort(string.CompareOrdinal);

                for (int j = 0; j < sorted.Count; j++)
                {
                    _writer.Write(j == 0 ? sorted[j] : "," + sorted[j]);
                }
            }

            Line();
        }
    }
}
