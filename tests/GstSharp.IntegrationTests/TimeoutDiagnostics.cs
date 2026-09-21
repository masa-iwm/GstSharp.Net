using Gst;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What a wait that ran out says about the pipeline it was waiting for.
/// </summary>
/// <remarks>
/// <para>
/// A bounded wait that expires reports a timeout and nothing else, which
/// cannot separate the two shapes of failure the asynchronous facts of
/// <c>GstPlay</c> and <c>GstTranscoder</c> have seen on the build machines: a
/// pipeline that never reached its state, and a pipeline that reached it while
/// the messages that say so were never delivered. Queued messages and a
/// pipeline in <c>PLAYING</c> say the delivery failed; an empty bus and a
/// pipeline short of <c>PLAYING</c> says the pipeline stalled.
/// </para>
/// <para>
/// Everything here runs on the failure path only. It drains the buses it is
/// given, which a passing test must never see, so a caller has to build the
/// text inside the branch that failed rather than as the message of an
/// assertion that usually holds.
/// </para>
/// </remarks>
internal static class TimeoutDiagnostics
{
    /// <summary>How many messages one bus listing reports at most.</summary>
    private const int MostMessages = 32;

    /// <summary>How much of the structure of one message is reported.</summary>
    private const int MostStructure = 96;

    /// <summary>
    /// Describes what a pipeline and its buses were doing when a wait ran out.
    /// </summary>
    /// <param name="waited">How long the wait lasted.</param>
    /// <param name="pipeline">The pipeline that was waited for, if it is reachable.</param>
    /// <param name="apiBus">The bus the wait expected its messages on, if there is one.</param>
    /// <returns>The evidence, as one line.</returns>
    /// <remarks>
    /// The wrappers handed in are objects, which <c>docs/ownership.md</c> says
    /// no consumer disposes; the messages this pops are mini objects, which it
    /// owns and disposes.
    /// </remarks>
    internal static string Describe(TimeSpan waited, Element? pipeline, Bus? apiBus)
    {
        string state = DescribeState(pipeline);
        string api = DescribeQueue(apiBus);
        string element = DescribeQueue(pipeline?.GetBus());

        return FormattableString.Invariant($"waited {waited}; pipeline: {state}; ")
            + FormattableString.Invariant($"api bus: {api}; pipeline bus: {element}");
    }

    /// <summary>
    /// Reads the state of a pipeline without waiting for a pending change.
    /// </summary>
    /// <param name="pipeline">The pipeline to ask, if it is reachable.</param>
    /// <returns>The reading, as one phrase.</returns>
    private static string DescribeState(Element? pipeline)
    {
        if (pipeline is null)
        {
            return "unreachable";
        }

        StateChangeReturn answer = pipeline.GetState(out State state, out State pending, ClockTime.Zero);

        return FormattableString.Invariant(
            $"{pipeline.GetName()} {answer}, state {state}, pending {pending}");
    }

    /// <summary>
    /// Drains a bus and lists what was queued on it.
    /// </summary>
    /// <param name="bus">The bus to drain, if there is one.</param>
    /// <returns>The listing, as one phrase.</returns>
    private static string DescribeQueue(Bus? bus)
    {
        if (bus is null)
        {
            return "unreachable";
        }

        List<string> queued = [];
        while (queued.Count < MostMessages)
        {
            using Message? message = bus.Pop();
            if (message is null)
            {
                break;
            }

            queued.Add(DescribeMessage(message));
        }

        return queued.Count == 0
            ? "empty"
            : FormattableString.Invariant($"{queued.Count} queued [{string.Join("; ", queued)}]");
    }

    /// <summary>
    /// Names one message. The type alone does not identify the messages of an
    /// API bus, which all carry the application type, so the payload that does
    /// identify them is reported with it.
    /// </summary>
    /// <param name="message">The message to name.</param>
    /// <returns>The name, as one phrase.</returns>
    private static string DescribeMessage(Message message)
    {
        // The structure of a message is a boxed value, so the wrapper owns a
        // copy and has to release it.
        using Structure? payload = message.GetStructure();
        if (payload is null)
        {
            return message.Type.ToString();
        }

        string written = payload.ToString();
        if (written.Length > MostStructure)
        {
            written = string.Concat(written.AsSpan(0, MostStructure), "...");
        }

        return FormattableString.Invariant($"{message.Type} {written}");
    }
}
