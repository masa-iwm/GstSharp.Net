using Gst;

/// <summary>
/// What the tool knows about one pad: <c>GstPadStats</c> of gst-stats.c.
/// </summary>
internal sealed class PadStats
{
    /// <summary>The name of the pad.</summary>
    internal string Name = string.Empty;

    /// <summary>The name of the type of the pad.</summary>
    internal string TypeName = string.Empty;

    /// <summary>The index the tracer gave the pad.</summary>
    internal uint Index;

    /// <summary>Whether the pad is a ghost pad.</summary>
    internal bool IsGhostPad;

    /// <summary>Which way buffers travel over the pad.</summary>
    internal PadDirection Direction;

    /// <summary>How many buffers went over the pad.</summary>
    internal uint NumBuffers;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_LIVE.</summary>
    internal uint NumLive;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_DECODE_ONLY.</summary>
    internal uint NumDecodeOnly;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_DISCONT.</summary>
    internal uint NumDiscont;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_RESYNC.</summary>
    internal uint NumResync;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_CORRUPTED.</summary>
    internal uint NumCorrupted;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_MARKER.</summary>
    internal uint NumMarker;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_HEADER.</summary>
    internal uint NumHeader;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_GAP.</summary>
    internal uint NumGap;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_DROPPABLE.</summary>
    internal uint NumDroppable;

    /// <summary>How many of the buffers carried GST_BUFFER_FLAG_DELTA_UNIT.</summary>
    internal uint NumDelta;

    /// <summary>The smallest buffer that went over the pad.</summary>
    internal uint MinSize = uint.MaxValue;

    /// <summary>The largest buffer that went over the pad.</summary>
    internal uint MaxSize;

    /// <summary>The mean size of the buffers that went over the pad.</summary>
    internal uint AvgSize;

    /// <summary>When the first buffer went over the pad.</summary>
    internal ulong FirstTs = ClockTime.NoneValue;

    /// <summary>When the last buffer went over the pad.</summary>
    internal ulong LastTs = ClockTime.NoneValue;

    /// <summary>When the next buffer is expected.</summary>
    internal ulong NextTs = ClockTime.NoneValue;

    /// <summary>The thread the pad is driven from.</summary>
    internal ulong ThreadId;

    /// <summary>The index of the element the pad belongs to.</summary>
    internal uint ParentIx = uint.MaxValue;
}

/// <summary>
/// What the tool knows about one element: <c>GstElementStats</c> of gst-stats.c.
/// </summary>
internal sealed class ElementStats
{
    /// <summary>The name of the element.</summary>
    internal string Name = string.Empty;

    /// <summary>The name of the type of the element.</summary>
    internal string TypeName = string.Empty;

    /// <summary>The index the tracer gave the element.</summary>
    internal uint Index;

    /// <summary>Whether the element is a bin.</summary>
    internal bool IsBin;

    /// <summary>How many buffers the element received.</summary>
    internal uint RecvBuffers;

    /// <summary>How many buffers the element sent.</summary>
    internal uint SentBuffers;

    /// <summary>How many bytes the element received.</summary>
    internal ulong RecvBytes;

    /// <summary>How many bytes the element sent.</summary>
    internal ulong SentBytes;

    /// <summary>How many events the element sent.</summary>
    internal uint NumEvents;

    /// <summary>How many messages the element posted.</summary>
    internal uint NumMessages;

    /// <summary>How many queries the element answered.</summary>
    internal uint NumQueries;

    /// <summary>When the element first did something.</summary>
    internal ulong FirstTs = ClockTime.NoneValue;

    /// <summary>When the element last did something.</summary>
    internal ulong LastTs = ClockTime.NoneValue;

    /// <summary>The index of the bin the element belongs to.</summary>
    internal uint ParentIx = uint.MaxValue;
}

/// <summary>
/// What the tool knows about one thread: <c>GstThreadStats</c> of gst-stats.c.
/// </summary>
internal sealed class ThreadStats
{
    /// <summary>How much time was spent in the thread.</summary>
    internal ulong TThread = ClockTime.NoneValue;

    /// <summary>The mean load of the thread, in tenths of a percent.</summary>
    internal uint CpuLoad;
}

/// <summary>
/// One row of a latency table: <c>GstLatencyStats</c> of gst-stats.c.
/// </summary>
internal sealed class LatencyStats
{
    /// <summary>The key of the row, which is also what is printed.</summary>
    internal string Name = string.Empty;

    /// <summary>How many latencies were counted.</summary>
    internal ulong Count;

    /// <summary>The sum of all latencies.</summary>
    internal ulong Total;

    /// <summary>The smallest latency.</summary>
    internal ulong Min;

    /// <summary>The largest latency.</summary>
    internal ulong Max;

    /// <summary>When the first of the latencies was measured.</summary>
    internal ulong FirstLatencyTs;
}

/// <summary>
/// One latency an element reported: <c>GstReportedLatency</c> of gst-stats.c.
/// </summary>
internal sealed class ReportedLatency
{
    /// <summary>The element that reported, as id and name.</summary>
    internal string Element = string.Empty;

    /// <summary>When the latency was reported.</summary>
    internal ulong Ts;

    /// <summary>The smallest latency that was reported.</summary>
    internal ulong Min;

    /// <summary>The largest latency that was reported.</summary>
    internal ulong Max;
}

/// <summary>
/// The factories one plugin contributed: <c>GstPluginStats</c> of gst-stats.c.
/// </summary>
internal sealed class PluginStats
{
    /// <summary>Initialises the row.</summary>
    /// <param name="name">The name of the plugin.</param>
    internal PluginStats(string name)
    {
        Name = name;
        Factories = new List<string>[StatsCollector.FactoryTypes.Count];

        for (int i = 0; i < Factories.Length; i++)
        {
            Factories[i] = [];
        }
    }

    /// <summary>Gets the name of the plugin.</summary>
    internal string Name { get; }

    /// <summary>
    /// Gets the factories the plugin contributed, one list per entry of
    /// <see cref="StatsCollector.FactoryTypes"/>.
    /// </summary>
    internal List<string>[] Factories { get; }
}
