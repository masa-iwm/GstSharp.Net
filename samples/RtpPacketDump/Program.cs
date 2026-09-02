// The RTP sample: it reads the header of every RTP packet a payloader
// produced, and then builds, re-reads and lists a compound RTCP packet.
//
// Usage: RtpPacketDump          (no arguments, headless, bounded)
//
// What it demonstrates:
//
//   * Gst.Rtp.RTPBuffer over a buffer that came out of an appsink: the
//     sequence number, the timestamp, the SSRC, the payload type, the marker
//     bit and the payload length of a real rtpL16pay packet.
//   * Gst.Rtp.RTCPBuffer and Gst.Rtp.RTCPPacket: a sender report and an SDES
//     item written into a fresh RTCP buffer, and the same buffer walked back
//     packet by packet through GetFirstPacket and MoveToNext.
//
// The lifetime rules it follows, all of them from docs/ownership.md, section
// "RTP mapped structures":
//
//   * RTPBuffer and RTCPBuffer are the plain C structures, not scopes. Each one
//     here is a local variable, mapped once and unmapped exactly once, and it
//     is never copied: it is not passed to a helper, not stored in a field and
//     not captured in a lambda. The RTCP half is written and read in a single
//     method for that reason -- AddPacket writes the address of the RTCPBuffer
//     variable into the packet, and Unmap resizes the buffer through it, so a
//     copy in another stack frame would be a dangling one.
//   * The Gst.Buffer wrapper the mapping came from stays alive until after the
//     unmap. The library keeps a bare pointer to it and takes no reference, so
//     every mapping below sits inside the scope of a "using" declaration of
//     its buffer: the disposal at the end of that scope is a use that comes
//     after the unmap, which is what keeps the collector away from it.
//   * An RTCPPacket is valid only while the RTCPBuffer it was taken from is
//     mapped, so no packet outlives the method that mapped it.
//   * An RTP header accessor on a structure that was never mapped crashes the
//     process, so nothing is read before MapBuffer answered true.
//
// How to run:
//
//   dotnet run --project samples/RtpPacketDump
//
// It needs audiotestsrc, audioconvert, appsink (gst-plugins-base) and
// rtpL16pay (the rtp plugin of gst-plugins-good). A missing one is reported by
// name and exits with 2.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Gst;
using Gst.App;
using Gst.GLib;
using Gst.Interop;
using Gst.Rtp;

return Dump.Run(args);

internal static class Dump
{
    /// <summary>The pipeline the sample runs.</summary>
    private const string Description =
        "audiotestsrc num-buffers=20 ! audioconvert ! "
        + "audio/x-raw,format=S16BE,channels=1,rate=8000 ! rtpL16pay ! appsink name=sink";

    /// <summary>The name of the sink in <see cref="Description"/>.</summary>
    private const string SinkName = "sink";

    /// <summary>The MTU of the RTCP buffer the run builds.</summary>
    private const uint Mtu = 1400;

    /// <summary>The elements <see cref="Description"/> needs.</summary>
    private static readonly string[] Required =
        ["audiotestsrc", "audioconvert", "rtpL16pay", "appsink"];

    /// <summary>How long the whole run may take.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>How long one pull of the sink waits before the bus is read.</summary>
    private static readonly ClockTime Slice = ClockTime.FromMilliseconds(100);

    /// <summary>The canonical name the SDES item carries.</summary>
    private static ReadOnlySpan<byte> Cname => "rtppacketdump@gstsharp"u8;

    /// <summary>
    /// Runs the pipeline, dumps every RTP header and then the RTCP compound.
    /// </summary>
    /// <param name="arguments">The command line of the process.</param>
    /// <returns>0 on success, 2 when an element is missing, 1 on any other failure.</returns>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The sample turns every failure into a message and a non zero exit code.")]
    internal static int Run(string[] arguments)
    {
        try
        {
            if (arguments.Length != 0)
            {
                Console.Error.WriteLine("Usage: RtpPacketDump");
                Console.Error.WriteLine("       the sample takes no arguments.");
                return 2;
            }

            // One entry point per binding assembly this sample uses, rather
            // than GstSharp.Initialize: only a call into GstSharp.Net.App runs
            // the module initialiser that puts GstAppSink into the type
            // registry, without which the cast of the sink below is silently
            // null. GstRtp.Initialize says the same for GstSharp.Net.Rtp; here
            // the first RTPBuffer.MapBuffer would run that module initialiser
            // anyway, but nothing would say so, and a cast to a payloader base
            // class added later would be the silently null one.
            GstRtp.Initialize();
            GstApp.Initialize();

            Console.WriteLine($"version:     {GstSharp.NativeVersion.Description}");
            Console.WriteLine($"flavor:      {NativeLoader.ResolvedFlavor?.ToString() ?? "not applicable"}");
            Console.WriteLine($"directory:   {NativeLoader.ResolvedDirectory ?? "the process search path"}");
            Console.WriteLine($"pipeline:    {Description}");

            if (MissingElement() is string missing)
            {
                Console.Error.WriteLine(
                    $"RtpPacketDump: {missing} is not installed. Install gst-plugins-base and the "
                    + "rtp plugin of gst-plugins-good.");
                return 2;
            }

            if (Global.ParseLaunch(Description) is not Pipeline pipeline)
            {
                Console.Error.WriteLine("RtpPacketDump: the description did not produce a pipeline.");
                return 1;
            }

            using (pipeline)
            {
                if (pipeline.GetByName(SinkName) is not AppSink sink)
                {
                    Console.Error.WriteLine($"RtpPacketDump: \"{SinkName}\" is not an appsink.");
                    return 1;
                }

                // The sink and the bus are interned wrappers, shared with every
                // other lookup of the same object, so neither is disposed here.
                // The pipeline is the sanctioned exception: this code built it
                // and sets it back to NULL before releasing it. See
                // docs/ownership.md.
                Bus bus = pipeline.GetBus();
                Session session = new();

                int status = Pull(pipeline, bus, sink, session);
                if (status != 0)
                {
                    return status;
                }

                if (session.Packets == 0)
                {
                    Console.Error.WriteLine("RtpPacketDump: the pipeline produced no RTP packet.");
                    return 1;
                }

                return Rtcp(session);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"RtpPacketDump: {exception}");
            return 1;
        }
        finally
        {
            GstSharp.DrainPendingReleases();
        }
    }

    /// <summary>
    /// Looks for the elements the description names.
    /// </summary>
    /// <returns>The name of the first element that is not installed, or <see langword="null"/>.</returns>
    private static string? MissingElement()
    {
        foreach (string name in Required)
        {
            using ElementFactory? factory = ElementFactory.Find(name);

            if (factory is null)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the pipeline and reads every RTP packet the sink hands out.
    /// </summary>
    /// <param name="pipeline">The pipeline to run.</param>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <param name="sink">The sink to pull from.</param>
    /// <param name="session">What the run remembers of the packets it saw.</param>
    /// <returns>0 when the sink reached the end of the stream, 1 otherwise.</returns>
    /// <remarks>
    /// No main loop and no callback: a bounded wait per sample, the bus polled
    /// for whatever went wrong, and an overall deadline, so that the sample can
    /// never hang a CI job.
    /// </remarks>
    private static int Pull(Pipeline pipeline, Bus bus, AppSink sink, Session session)
    {
        try
        {
            if (pipeline.SetState(State.Playing) == StateChangeReturn.Failure)
            {
                Console.Error.WriteLine("RtpPacketDump: the pipeline refused to go to PLAYING.");
                return Drain(bus);
            }

            Stopwatch elapsed = Stopwatch.StartNew();

            while (elapsed.Elapsed < Timeout)
            {
                using Sample? sample = sink.TryPullSample(Slice);

                if (sample is null)
                {
                    if (sink.IsEos())
                    {
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"eos:         after {elapsed.Elapsed.TotalSeconds:F2} s"));
                        return CheckBus(bus);
                    }

                    if (CheckBus(bus) != 0)
                    {
                        return 1;
                    }

                    continue;
                }

                if (!Read(sample, session))
                {
                    return 1;
                }
            }

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"RtpPacketDump: no end of stream within {Timeout.TotalSeconds:F0} s."));
            return 1;
        }
        finally
        {
            pipeline.SetState(State.Null);
        }
    }

    /// <summary>
    /// Prints the header of one RTP packet.
    /// </summary>
    /// <param name="sample">The sample the sink handed out.</param>
    /// <param name="session">What the run remembers of the packets it saw.</param>
    /// <returns><see langword="true"/> unless the buffer could not be mapped.</returns>
    /// <remarks>
    /// The mapped structure is a local of this method and is read here: it is
    /// deliberately not handed to a printing helper, because passing it would
    /// copy it. The buffer is a <c>using</c> declaration of this method, so its
    /// disposal -- the use that keeps it reachable -- comes after the unmap.
    /// </remarks>
    private static bool Read(Sample sample, Session session)
    {
        using Gst.Buffer? buffer = sample.GetBuffer();

        if (buffer is null)
        {
            return true;
        }

        if (!RTPBuffer.MapBuffer(buffer, MapFlags.Read, out RTPBuffer rtp))
        {
            Console.Error.WriteLine("RtpPacketDump: a buffer of the sink is not an RTP packet.");
            return false;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"rtp:         seq={rtp.GetSeq(),5}  ts={rtp.GetTimestamp(),10}  ssrc=0x{rtp.GetSsrc():X8}  "
            + $"pt={rtp.GetPayloadType(),3}  marker={(rtp.GetMarker() ? "1" : "0")}  "
            + $"payload={rtp.GetPayloadLen()} bytes"));

        session.Add(rtp.GetTimestamp(), rtp.GetSsrc(), rtp.GetPayloadLen());

        rtp.Unmap();
        return true;
    }

    /// <summary>
    /// Builds a compound RTCP packet out of what the RTP run saw, and reads it
    /// back packet by packet.
    /// </summary>
    /// <param name="session">What the run remembers of the packets it saw.</param>
    /// <returns>0 when the compound was built and read back, 1 otherwise.</returns>
    /// <remarks>
    /// Everything happens in this one method on purpose. The packets borrow the
    /// address of the <c>RTCPBuffer</c> variable, and <c>Unmap</c> resizes the
    /// buffer through it, so neither the structure nor a packet may leave this
    /// frame.
    /// </remarks>
    private static int Rtcp(Session session)
    {
        using Gst.Buffer buffer = RTCPBuffer.New(Mtu);

        // A write only mapping is refused for RTCP, so the compound is built
        // through a read/write one.
        if (!RTCPBuffer.MapBuffer(buffer, MapFlags.Read | MapFlags.Write, out RTCPBuffer rtcp))
        {
            Console.Error.WriteLine("RtpPacketDump: the RTCP buffer could not be mapped for writing.");
            return 1;
        }

        if (!rtcp.AddPacket(RTCPType.Sr, out RTCPPacket sr))
        {
            Console.Error.WriteLine("RtpPacketDump: the sender report did not fit into the MTU.");
            rtcp.Unmap();
            return 1;
        }

        // The sender report says what this sender sent: the SSRC and the RTP
        // timestamp of the last packet the run saw, and the packet and octet
        // counts it added up.
        sr.SrSetSenderInfo(session.Ssrc, Ntp(), session.Timestamp, session.Packets, session.Octets);

        if (!rtcp.AddPacket(RTCPType.Sdes, out RTCPPacket sdes)
            || !sdes.SdesAddItem(session.Ssrc)
            || !sdes.SdesAddEntry(RTCPSDESType.Cname, Cname))
        {
            Console.Error.WriteLine("RtpPacketDump: the SDES item did not fit into the MTU.");
            rtcp.Unmap();
            return 1;
        }

        if (!rtcp.Unmap())
        {
            Console.Error.WriteLine("RtpPacketDump: the written RTCP buffer could not be unmapped.");
            return 1;
        }

        // A second, read only mapping: the compound is a finished buffer now,
        // and this is how a receiver would look at it.
        if (!RTCPBuffer.MapBuffer(buffer, MapFlags.Read, out RTCPBuffer reread))
        {
            Console.Error.WriteLine("RtpPacketDump: the RTCP buffer could not be mapped for reading.");
            return 1;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"rtcp:        {reread.GetPacketCount()} packets, ssrc=0x{session.Ssrc:X8}, "
            + $"ts={session.Timestamp}, packets={session.Packets}, octets={session.Octets}"));

        if (reread.GetFirstPacket(out RTCPPacket packet))
        {
            do
            {
                // The length of an RTCP packet is the header field RFC 3550
                // defines: the size in 32 bit words, not counting the first
                // one, which is (length + 1) * 4 bytes on the wire.
                ushort words = packet.GetLength();

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"packet:      type={packet.GetPacketType(),-5} length={words} words "
                    + $"({(words + 1) * 4} bytes)"));
            }
            while (packet.MoveToNext());
        }

        if (!reread.Unmap())
        {
            Console.Error.WriteLine("RtpPacketDump: the read RTCP buffer could not be unmapped.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// The current wall clock as the 64 bit NTP timestamp a sender report
    /// carries: seconds since 1900 in the high half, the fraction of a second
    /// in the low half.
    /// </summary>
    /// <returns>The NTP timestamp of now.</returns>
    private static ulong Ntp()
    {
        // DateTimeOffset and not DateTime: Gst and Gst.GLib both declare a
        // DateTime of their own, so the name is ambiguous in this file.
        TimeSpan since = DateTimeOffset.UtcNow - new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ulong seconds = (ulong)since.TotalSeconds;
        ulong fraction = (ulong)((since.TotalSeconds - seconds) * 4294967296.0);

        return (seconds << 32) | (fraction & 0xFFFFFFFF);
    }

    /// <summary>
    /// Prints whatever error the bus holds.
    /// </summary>
    /// <param name="bus">The bus to look at.</param>
    /// <returns>0 when nothing failed, 1 when an error was posted.</returns>
    private static int CheckBus(Bus bus)
    {
        using Message? message = bus.PopFiltered(MessageType.Error);

        if (message is null)
        {
            return 0;
        }

        PrintError(message);
        return 1;
    }

    /// <summary>
    /// Prints everything the bus already holds after a failed state change.
    /// </summary>
    /// <param name="bus">The bus of the pipeline.</param>
    /// <returns>1, because this is only reached after a failure.</returns>
    private static int Drain(Bus bus)
    {
        while (bus.PopFiltered(MessageType.Error) is Message message)
        {
            using (message)
            {
                PrintError(message);
            }
        }

        return 1;
    }

    /// <summary>
    /// Prints an error message together with the element that posted it.
    /// </summary>
    /// <param name="message">The error message.</param>
    private static void PrintError(Message message)
    {
        (GException error, string? debug) = message.ParseError();

        Console.Error.WriteLine($"error:       {message.SourceName ?? "?"}: {error.Message}");
        Console.Error.WriteLine($"debug:       {debug ?? "none"}");
    }

    /// <summary>
    /// What the RTP half of the run saw, and what the sender report of the RTCP
    /// half is built from.
    /// </summary>
    private sealed class Session
    {
        /// <summary>Gets the RTP timestamp of the last packet.</summary>
        internal uint Timestamp { get; private set; }

        /// <summary>Gets the SSRC of the last packet.</summary>
        internal uint Ssrc { get; private set; }

        /// <summary>Gets how many packets were read.</summary>
        internal uint Packets { get; private set; }

        /// <summary>Gets how many payload bytes were read.</summary>
        internal uint Octets { get; private set; }

        /// <summary>
        /// Adds one packet.
        /// </summary>
        /// <param name="timestamp">The RTP timestamp of the packet.</param>
        /// <param name="ssrc">The SSRC of the packet.</param>
        /// <param name="payload">The payload length of the packet.</param>
        internal void Add(uint timestamp, uint ssrc, uint payload)
        {
            Timestamp = timestamp;
            Ssrc = ssrc;
            Packets++;
            Octets += payload;
        }
    }
}
