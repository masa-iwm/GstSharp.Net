using Gst;

/// <summary>
/// The twelve tracer records gst-stats.c knows, and the fields it reads out
/// of each of them: the handlers <c>collect_stats</c> dispatches to.
/// </summary>
internal sealed partial class StatsCollector
{
    /// <summary>Records a pad: <c>new_pad_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void NewPadStats(Structure s)
    {
        uint ix = Uint(s, "ix");
        bool isGhostPad = Bool(s, "is-ghostpad");

        PadStats stats = new()
        {
            Name = s.GetString("name") ?? string.Empty,
            TypeName = s.GetString("type") ?? string.Empty,
            Index = ix,
            IsGhostPad = isGhostPad,
            Direction = s.GetEnum("pad-direction", _padDirectionType, out int direction)
                ? (PadDirection)direction
                : PadDirection.Unknown,
            MinSize = uint.MaxValue,
            FirstTs = TimeNone,
            LastTs = TimeNone,
            NextTs = TimeNone,
            ThreadId = Uint64(s, "thread-id"),
            ParentIx = Uint(s, "parent-ix"),
        };

        if (isGhostPad)
        {
            NumGhostPads++;
        }

        NumPads++;

        while (Pads.Count <= ix)
        {
            Pads.Add(null);
        }

        Pads[(int)ix] = stats;
    }

    /// <summary>Records an element: <c>new_element_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void NewElementStats(Structure s)
    {
        uint ix = Uint(s, "ix");
        bool isBin = Bool(s, "is-bin");

        ElementStats stats = new()
        {
            Index = ix,
            Name = s.GetString("name") ?? string.Empty,
            TypeName = s.GetString("type") ?? string.Empty,
            IsBin = isBin,
            FirstTs = TimeNone,
            ParentIx = Uint(s, "parent-ix"),
        };

        if (isBin)
        {
            NumBins++;
        }

        NumElements++;

        while (Elements.Count <= ix)
        {
            Elements.Add(null);
        }

        Elements[(int)ix] = stats;
    }

    /// <summary>Folds a buffer into the statistics of a pad: <c>do_pad_stats</c>.</summary>
    /// <param name="stats">The pad the buffer went over.</param>
    /// <param name="elemIx">The element the pad belongs to.</param>
    /// <param name="size">How large the buffer was.</param>
    /// <param name="ts">When it went over the pad.</param>
    /// <param name="bufferTs">The timestamp of the buffer.</param>
    /// <param name="bufferDur">The duration of the buffer.</param>
    /// <param name="bufferFlags">The flags of the buffer.</param>
    private void DoPadStats(
        PadStats stats,
        uint elemIx,
        uint size,
        ulong ts,
        ulong bufferTs,
        ulong bufferDur,
        BufferFlags bufferFlags)
    {
        if (stats.ParentIx == NoIndex)
        {
            stats.ParentIx = elemIx;
        }

        if (stats.ThreadId != 0)
        {
            GetThreadStats(stats.ThreadId);
        }

        // The running mean of the C tool, which rounds down once per buffer.
        ulong avgSize = ((ulong)stats.AvgSize * stats.NumBuffers) + size;
        stats.NumBuffers++;
        stats.AvgSize = (uint)(avgSize / stats.NumBuffers);

        // The else of the C tool is kept: a pad that saw one buffer therefore
        // keeps a maximum of 0, which is what the report then prints.
        if (size < stats.MinSize)
        {
            stats.MinSize = size;
        }
        else if (size > stats.MaxSize)
        {
            stats.MaxSize = size;
        }

        if (stats.LastTs == TimeNone)
        {
            stats.FirstTs = ts;
        }

        stats.LastTs = ts;

        if ((bufferFlags & BufferFlags.Live) != 0)
        {
            stats.NumLive++;
        }

        if ((bufferFlags & BufferFlags.DecodeOnly) != 0)
        {
            stats.NumDecodeOnly++;
        }

        if ((bufferFlags & BufferFlags.Discont) != 0)
        {
            stats.NumDiscont++;
        }

        if ((bufferFlags & BufferFlags.Resync) != 0)
        {
            stats.NumResync++;
        }

        if ((bufferFlags & BufferFlags.Corrupted) != 0)
        {
            stats.NumCorrupted++;
        }

        if ((bufferFlags & BufferFlags.Marker) != 0)
        {
            stats.NumMarker++;
        }

        if ((bufferFlags & BufferFlags.Header) != 0)
        {
            stats.NumHeader++;
        }

        if ((bufferFlags & BufferFlags.Gap) != 0)
        {
            stats.NumGap++;
        }

        if ((bufferFlags & BufferFlags.Droppable) != 0)
        {
            stats.NumDroppable++;
        }

        if ((bufferFlags & BufferFlags.DeltaUnit) != 0)
        {
            stats.NumDelta++;
        }

        stats.NextTs = bufferTs != TimeNone && bufferDur != TimeNone
            ? bufferTs + bufferDur
            : TimeNone;
    }

    /// <summary>Folds a buffer into the two elements it travelled between: <c>do_element_stats</c>.</summary>
    /// <param name="stats">The element that sent the buffer.</param>
    /// <param name="peerStats">The element that received it.</param>
    /// <param name="size">How large the buffer was.</param>
    /// <param name="ts">When it travelled.</param>
    private static void DoElementStats(ElementStats stats, ElementStats peerStats, uint size, ulong ts)
    {
        stats.SentBuffers++;
        peerStats.RecvBuffers++;
        stats.SentBytes += size;
        peerStats.RecvBytes += size;

        if (stats.FirstTs == TimeNone)
        {
            stats.FirstTs = ts;
        }

        if (peerStats.FirstTs == TimeNone)
        {
            // The nanosecond keeps the receiver behind the sender in the report,
            // which two elements that first moved on the same buffer would
            // otherwise be tied on.
            peerStats.FirstTs = ts + 1;
        }
    }

    /// <summary>Reads a <c>buffer</c> record: <c>do_buffer_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoBufferStats(Structure s)
    {
        NumBuffers++;

        ulong ts = Uint64(s, "ts");
        uint padIx = Uint(s, "pad-ix");
        uint elemIx = Uint(s, "element-ix");
        uint peerElemIx = Uint(s, "peer-element-ix");
        uint size = Uint(s, "buffer-size");
        BufferFlags bufferFlags = s.GetFlags("buffer-flags", _bufferFlagsType, out uint flags)
            ? (BufferFlags)flags
            : 0;
        ulong bufferPts = s.GetUint64("buffer-pts", out ulong pts) ? pts : TimeNone;
        ulong bufferDur = s.GetUint64("buffer-duration", out ulong duration) ? duration : TimeNone;

        LastTs = Math.Max(LastTs, ts);

        if (GetPadStats(padIx) is not PadStats padStats)
        {
            return;
        }

        if (ElementByIndex(elemIx) is not ElementStats elemStats)
        {
            return;
        }

        if (ElementByIndex(peerElemIx) is not ElementStats peerElemStats)
        {
            return;
        }

        DoPadStats(padStats, elemIx, size, ts, bufferPts, bufferDur, bufferFlags);

        if (padStats.Direction == PadDirection.Src)
        {
            DoElementStats(elemStats, peerElemStats, size, ts);
        }
        else
        {
            DoElementStats(peerElemStats, elemStats, size, ts);
        }
    }

    /// <summary>Reads an <c>event</c> record: <c>do_event_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoEventStats(Structure s)
    {
        NumEvents++;

        ulong ts = Uint64(s, "ts");
        uint padIx = Uint(s, "pad-ix");
        uint elemIx = Uint(s, "element-ix");

        LastTs = Math.Max(LastTs, ts);

        if (GetPadStats(padIx) is null)
        {
            return;
        }

        // A reconfigure event travels over a pad that has no parent yet, which
        // is why a missing element is not an error here.
        if (ElementByIndex(elemIx) is ElementStats elemStats)
        {
            elemStats.NumEvents++;
        }
    }

    /// <summary>Reads a <c>message</c> record: <c>do_message_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoMessageStats(Structure s)
    {
        NumMessages++;

        ulong ts = Uint64(s, "ts");
        uint elemIx = Uint(s, "element-ix");

        LastTs = Math.Max(LastTs, ts);

        if (ElementByIndex(elemIx) is ElementStats elemStats)
        {
            elemStats.NumMessages++;
        }
    }

    /// <summary>Reads a <c>query</c> record: <c>do_query_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoQueryStats(Structure s)
    {
        NumQueries++;

        ulong ts = Uint64(s, "ts");
        uint elemIx = Uint(s, "element-ix");

        LastTs = Math.Max(LastTs, ts);

        if (ElementByIndex(elemIx) is ElementStats elemStats)
        {
            elemStats.NumQueries++;
        }
    }

    /// <summary>Reads a <c>thread-rusage</c> record: <c>do_thread_rusage_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoThreadRusageStats(Structure s)
    {
        ulong ts = Uint64(s, "ts");
        ulong threadId = Uint64(s, "thread-id");
        uint cpuLoad = Uint(s, "average-cpuload");
        ulong tthread = Uint64(s, "time");

        ThreadStats threadStats = GetThreadStats(threadId);
        threadStats.CpuLoad = cpuLoad;
        threadStats.TThread = tthread;
        LastTs = Math.Max(LastTs, ts);
    }

    /// <summary>Reads a <c>proc-rusage</c> record: <c>do_proc_rusage_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoProcRusageStats(Structure s)
    {
        ulong ts = Uint64(s, "ts");

        TotalCpuLoad = Uint(s, "average-cpuload");
        LastTs = Math.Max(LastTs, ts);
        HaveCpuLoad = true;
    }

    /// <summary>Reads a <c>latency</c> record: <c>do_latency_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoLatencyStats(Structure s)
    {
        ulong time = Uint64(s, "time");
        ulong ts = Uint64(s, "ts");

        LastTs = Math.Max(LastTs, ts);

        string key =
            $"{Printed(s, "src-element-id")}.{Printed(s, "src-element")}.{Printed(s, "src")}" +
            $"|{Printed(s, "sink-element-id")}.{Printed(s, "sink-element")}.{Printed(s, "sink")}";

        UpdateLatencyTable(Latencies, key, time, ts);
        HaveLatency = true;
    }

    /// <summary>Reads an <c>element-latency</c> record: <c>do_element_latency_stats</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoElementLatencyStats(Structure s)
    {
        ulong time = Uint64(s, "time");
        ulong ts = Uint64(s, "ts");

        LastTs = Math.Max(LastTs, ts);

        string key = $"{Printed(s, "element-id")}.{Printed(s, "element")}.{Printed(s, "src")}";

        UpdateLatencyTable(ElementLatencies, key, time, ts);
        HaveElementLatency = true;
    }

    /// <summary>Reads an <c>element-reported-latency</c> record: <c>do_element_reported_latency</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoElementReportedLatency(Structure s)
    {
        ulong min = Uint64(s, "min");
        ulong max = Uint64(s, "max");
        ulong ts = Uint64(s, "ts");

        LastTs = Math.Max(LastTs, ts);

        ElementReportedLatencies.Add(new ReportedLatency
        {
            Element = $"{Printed(s, "element-id")}.{Printed(s, "element")}",
            Ts = ts,
            Min = min,
            Max = max,
        });

        HaveElementReportedLatency = true;
    }

    /// <summary>Reads a <c>factory-used</c> record: <c>do_factory_used</c>.</summary>
    /// <param name="s">The structure of the record.</param>
    private void DoFactoryUsed(Structure s)
    {
        string? factory = s.GetString("factory");
        string? factoryType = s.GetString("factory-type");
        string? pluginName = s.GetString("plugin");

        if (string.Equals(pluginName, "staticelements", StringComparison.Ordinal))
        {
            return;
        }

        if (string.IsNullOrEmpty(pluginName))
        {
            pluginName = "built-in";
        }

        int f = 0;

        while (f < FactoryTypes.Count && !string.Equals(factoryType, FactoryTypes[f], StringComparison.Ordinal))
        {
            f++;
        }

        if (f == FactoryTypes.Count)
        {
            return;
        }

        PluginStats? plugin = null;

        foreach (PluginStats candidate in Plugins)
        {
            if (string.Equals(candidate.Name, pluginName, StringComparison.Ordinal))
            {
                plugin = candidate;
                break;
            }
        }

        if (plugin is null)
        {
            plugin = new PluginStats(pluginName);
            Plugins.Add(plugin);
        }

        if (!string.IsNullOrEmpty(factory) && !plugin.Factories[f].Contains(factory))
        {
            plugin.Factories[f].Add(factory);
        }
    }
}
