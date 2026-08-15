using System.Diagnostics;
using Gst;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Waits for messages on a bus the way an application without a main loop
/// does: by polling it with a short timeout until the message it waits for
/// arrives or the wait runs out.
/// </summary>
/// <remarks>
/// Every wait is bounded. A test that hangs reports nothing, so a pipeline that
/// never reaches its end has to fail the test instead of blocking the suite.
/// </remarks>
internal static class BusPump
{
    /// <summary>The interval of a single poll.</summary>
    private static readonly ClockTime Slice = ClockTime.FromMilliseconds(100);

    /// <summary>
    /// Waits for the first message of one of the given types.
    /// </summary>
    /// <param name="bus">The bus to poll.</param>
    /// <param name="types">The types to wait for.</param>
    /// <param name="timeout">How long to wait in total.</param>
    /// <returns>
    /// The message, which the caller owns and has to dispose, or
    /// <see langword="null"/> when none arrived in time.
    /// </returns>
    internal static Message? WaitFor(Bus bus, MessageType types, TimeSpan timeout)
    {
        Stopwatch elapsed = Stopwatch.StartNew();

        while (elapsed.Elapsed < timeout)
        {
            if (bus.TimedPopFiltered(Slice, types) is Message message)
            {
                return message;
            }
        }

        return null;
    }
}
