using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// What the chain-up of a slot answered, keyed by the name the gir gives the
/// slot.
/// </summary>
/// <remarks>
/// A probe composes one of these and writes every chain-up answer through
/// <see cref="Record{T}"/>, which hands the answer straight back. An override
/// that does nothing else therefore changes no behaviour: what it answers is
/// what the class below it answered, which for an empty slot is the value the
/// library reads an empty one as.
/// </remarks>
internal sealed class ChainUpRecord
{
    private readonly Dictionary<string, object> _answers = [];

    /// <summary>Gets what each slot answered, as of the moment of the read.</summary>
    internal IReadOnlyDictionary<string, object> Answers
    {
        get
        {
            lock (_answers)
            {
                return new Dictionary<string, object>(_answers, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>Records what a chain-up answered and hands it back.</summary>
    /// <typeparam name="T">The type the slot answers.</typeparam>
    /// <param name="slot">The name the gir gives the slot.</param>
    /// <param name="answer">What the class below the override answered.</param>
    /// <returns>The answer, unchanged.</returns>
    /// <remarks>
    /// A slot runs on a streaming thread, so the dictionary is written under a
    /// lock of its own.
    /// </remarks>
    internal T Record<T>(string slot, T answer)
        where T : notnull
    {
        lock (_answers)
        {
            _answers[slot] = answer;
        }

        return answer;
    }

    /// <summary>Asserts that a slot answered, and what it answered.</summary>
    /// <param name="slot">The name the gir gives the slot.</param>
    /// <param name="expected">The answer the C default reads as.</param>
    internal void AssertAnswered(string slot, object expected)
    {
        IReadOnlyDictionary<string, object> answers = Answers;

        Assert.True(
            answers.ContainsKey(slot),
            $"The {slot} slot was not reached; the answers were: {string.Join(", ", answers.Keys)}.");

        Assert.Equal(expected, answers[slot]);
    }
}

/// <summary>
/// Runs a stretch of a test with the exception trap watched, which is how a
/// slot that threw says so: the trampoline swallows the exception at the
/// callback boundary and reports it here instead.
/// </summary>
internal static class TrapWatch
{
    /// <summary>
    /// Runs the body and asserts that nothing was reported through the trap.
    /// </summary>
    /// <param name="body">What to run while the trap is watched.</param>
    /// <remarks>
    /// The body has to carry the transition to NULL with it: <c>close</c> and
    /// <c>stop</c> fire on the way there, so unsubscribing before it would let
    /// a throwing chain-up pass unnoticed.
    /// </remarks>
    internal static void NothingIsReported(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);

        List<Exception> reported = [];

        void OnFailure(Exception exception)
        {
            lock (reported)
            {
                reported.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            body();
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (reported)
        {
            Assert.Empty(reported);
        }
    }
}
