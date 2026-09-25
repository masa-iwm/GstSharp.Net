using Gst;
using Gst.Base;
using Gst.GObject;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A managed sink whose render override prerolls itself, the way the decklink
/// sinks do: it waits on the clock, which releases PREROLL_LOCK, and then calls
/// <see cref="BaseSink.DoPreroll"/> to catch a PLAYING to PAUSED change made
/// while it waited.
/// </summary>
/// <remarks>
/// The lock is already held inside <see cref="OnRender"/>, so the override
/// calls <see cref="BaseSink.DoPreroll"/> directly and never takes it.
/// </remarks>
internal sealed class RenderPrerollSink : BaseSink
{
    /// <summary>The <c>GType</c> name, unique in the process.</summary>
    internal const string GTypeName = "GstSharpTestRenderPrerollSink";

    /// <summary>No arming: the render prerolls on its buffer and returns.</summary>
    private const int Plain = 0;

    /// <summary>The next render waits on the clock before it prerolls.</summary>
    private const int ClockThenPreroll = 1;

    /// <summary>The next render prerolls on no object at all.</summary>
    private const int NullPreroll = 2;

    /// <summary>The pad template, built before the registration.</summary>
    private static readonly PadTemplate SinkTemplate = NewSinkTemplate();

    private static readonly SubclassType Definition = DefineSubclass(
        GTypeName,
        ConfigureClass,
        RenderOverride);

    private int _armed;
    private int _plainOk;
    private int _plainFailed;
    private int _armedResult;
    private int _clockResult;

    /// <summary>Creates the sink, with synchronisation on so that it can wait on the clock.</summary>
    internal RenderPrerollSink()
        : base(Definition.NewInstance())
    {
        SetSync(true);
    }

    /// <summary>Gets the event the armed render sets before it waits on the clock.</summary>
    internal ManualResetEventSlim Waiting { get; } = new(false);

    /// <summary>Gets the event the armed render sets once its preroll returned.</summary>
    internal ManualResetEventSlim Returned { get; } = new(false);

    /// <summary>Gets how many plain renders prerolled with <see cref="FlowReturn.Ok"/>.</summary>
    internal int PlainOk => Volatile.Read(ref _plainOk);

    /// <summary>Gets how many plain renders prerolled with anything else.</summary>
    internal int PlainFailed => Volatile.Read(ref _plainFailed);

    /// <summary>Gets what the preroll of the last armed render returned.</summary>
    internal FlowReturn ArmedResult => (FlowReturn)Volatile.Read(ref _armedResult);

    /// <summary>Gets what the clock wait of the last armed render returned.</summary>
    internal ClockReturn ClockResult => (ClockReturn)Volatile.Read(ref _clockResult);

    /// <summary>Makes the next render wait on the clock and then preroll.</summary>
    internal void ArmClockWait() => Arm(ClockThenPreroll);

    /// <summary>Makes the next render preroll on <see langword="null"/>.</summary>
    internal void ArmNullPreroll() => Arm(NullPreroll);

    /// <inheritdoc/>
    protected override FlowReturn OnRender(Gst.Buffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        FlowReturn result;
        switch (Interlocked.Exchange(ref _armed, Plain))
        {
            case ClockThenPreroll:
                Waiting.Set();

                // A running time an hour away: nothing but the unschedule of a
                // state change ends the wait, and PLAYING to PAUSED does that
                // (gstbasesink.c:5805-5808) once it holds the lock this wait
                // released.
                ClockReturn clock = WaitClock(ClockTime.FromSeconds(3600), out _);
                Volatile.Write(ref _clockResult, (int)clock);
                result = DoPreroll(buffer);
                Finish(result);
                return result;

            case NullPreroll:
                result = DoPreroll(null);
                Finish(result);
                return result;

            default:
                result = DoPreroll(buffer);
                Interlocked.Increment(ref result == FlowReturn.Ok ? ref _plainOk : ref _plainFailed);
                return result;
        }
    }

    /// <summary>Describes the class, and gives it the pad it needs.</summary>
    /// <param name="config">The class being initialised.</param>
    private static void ConfigureClass(ClassConfig config)
    {
        config.SetMetadata(
            "GstSharp render preroll sink",
            "Sink/Testing",
            "Prerolls from inside its render override",
            "GstSharp.Net integration tests");

        config.AddPadTemplate(SinkTemplate);
    }

    /// <summary>Builds the sink pad template of the class.</summary>
    /// <returns>The template, which lives for the process.</returns>
    private static PadTemplate NewSinkTemplate()
    {
        using Caps caps = Caps.NewAny();

        return PadTemplate.New("sink", PadDirection.Sink, PadPresence.Always, caps)
            ?? throw new InvalidOperationException("The sink pad template could not be created.");
    }

    private void Arm(int mode)
    {
        Waiting.Reset();
        Returned.Reset();
        Volatile.Write(ref _armed, mode);
    }

    private void Finish(FlowReturn result)
    {
        Volatile.Write(ref _armedResult, (int)result);
        Returned.Set();
    }
}
