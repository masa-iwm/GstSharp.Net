using System.Runtime.InteropServices;
using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed sink that runs in pull mode on a thread of its own, the shape
/// <c>gstaudiobasesink.c:2296-2346</c> has: pull a buffer, take PREROLL_LOCK,
/// look at the flush, preroll, release the lock.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OnActivatePull"/> does not chain up, because the default of the
/// base class starts a loop of its own on the pad task. The thread pulls
/// without the stream lock of the pad, which this binding has no accessor for.
/// </para>
/// <para>
/// <c>GstBaseSink</c> refuses pull mode unless <c>can_activate_pull</c> is
/// set, and a bare subclass has no member that sets it; the fixture writes the
/// field at the offset of the generated mirror, which only a test may do.
/// </para>
/// </remarks>
internal sealed class PullThreadSink : BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestPullThreadSink";

    /// <summary>The number of bytes asked for per pull.</summary>
    private const uint PullSize = 64;

    /// <summary>The pad template, built before the registration.</summary>
    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        ActivatePullOverride,
        UnlockOverride);

    private readonly List<string> _log = [];

    private Thread? _thread;
    private int _inside;
    private int _prerolled;
    private int _joined = -1;

    /// <summary>Creates the sink, with pull mode allowed and synchronisation off.</summary>
    internal PullThreadSink()
        : base(Definition.NewInstance())
    {
        SetSync(false);

        BaseSinkOwnFieldsRaw probe = default;
        int offset = InstanceLayout.OffsetOf(ref probe, ref probe.CanActivatePull);
        Marshal.WriteInt32(Handle + BaseSinkOwnFieldsRaw.OwnOffset + offset, 1);
    }

    /// <summary>Gets the event the thread sets when it ends.</summary>
    internal ManualResetEventSlim Exited { get; } = new(false);

    /// <summary>Gets a value indicating whether the thread is inside <see cref="BaseSink.DoPreroll"/>.</summary>
    internal bool InsideDoPreroll => Volatile.Read(ref _inside) != 0;

    /// <summary>Gets how many calls of <see cref="BaseSink.DoPreroll"/> returned <see cref="FlowReturn.Ok"/>.</summary>
    internal int Prerolled => Volatile.Read(ref _prerolled);

    /// <summary>
    /// Gets 1 when the deactivation joined the thread in time, 0 when it did
    /// not, and -1 before any deactivation.
    /// </summary>
    internal int Joined => Volatile.Read(ref _joined);

    /// <summary>Gets what the sink has seen, oldest first.</summary>
    internal IReadOnlyList<string> Log
    {
        get
        {
            lock (_log)
            {
                return _log.ToArray();
            }
        }
    }

    /// <summary>Forgets what the sink has seen so far.</summary>
    internal void ClearLog()
    {
        lock (_log)
        {
            _log.Clear();
        }
    }

    /// <inheritdoc/>
    protected override bool OnActivatePull(bool active)
    {
        if (active)
        {
            Record("activate-pull:true");
            Exited.Reset();
            _thread = new Thread(Loop) { IsBackground = true, Name = "pull-thread-sink" };
            _thread.Start();
            return true;
        }

        // The base class set the flush before this call, on this very thread
        // (gstbasesink.c:4936-4938), so the thread leaves DoPreroll or its pull
        // with Flushing, and the flag reads set here without the lock.
        Record("activate-pull:false flushing=" + (IsFlushing ? "yes" : "no"));
        bool joined = _thread is null || _thread.Join(TimeSpan.FromSeconds(10));
        Volatile.Write(ref _joined, joined ? 1 : 0);
        _thread = null;
        return joined;
    }

    /// <inheritdoc/>
    protected override bool OnUnlock()
    {
        // The only blocking call of the thread outside the pull is DoPreroll,
        // which the flush the base class sets next wakes; there is nothing of
        // its own to wake.
        Record("unlock");
        return true;
    }

    /// <summary>Describes the class, and gives it the pad it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp pull thread sink",
            "Sink/Testing",
            "Pulls and prerolls on a thread of its own",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }

    /// <summary>Builds the sink pad template of the class.</summary>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewSinkTemplate()
    {
        // ANY caps take the path of gst_base_sink_negotiate_pull that needs no
        // set_caps round trip.
        using Caps caps = Caps.NewAny();

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }

    private void Loop()
    {
        using Pad pad = GetStaticPad("sink")
            ?? throw new InvalidOperationException("The sink has no sink pad.");
        ulong offset = 0;

        while (true)
        {
            FlowReturn pulled = pad.PullRange(offset, PullSize, out Gst.Buffer? buffer);
            if (pulled != FlowReturn.Ok || buffer is null)
            {
                buffer?.Dispose();
                Record("pull:" + pulled);
                break;
            }

            using (buffer)
            {
                offset += buffer.GetSize();

                PrerollLock();
                if (IsFlushing)
                {
                    PrerollUnlock();
                    Record("flushing");
                    break;
                }

                Volatile.Write(ref _inside, 1);
                FlowReturn result = DoPreroll(buffer);
                Volatile.Write(ref _inside, 0);
                PrerollUnlock();

                if (result != FlowReturn.Ok)
                {
                    Record("preroll:" + result);
                    break;
                }

                Interlocked.Increment(ref _prerolled);
            }
        }

        Record("exit");
        Exited.Set();
    }

    private void Record(string entry)
    {
        lock (_log)
        {
            _log.Add(entry);
        }
    }
}
