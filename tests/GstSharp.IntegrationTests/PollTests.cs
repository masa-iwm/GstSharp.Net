using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The two hand written factories of <see cref="Poll"/> against the library
/// that is installed: what they build, what a timer set does with a wait, and
/// the release the caller owes.
/// </summary>
/// <remarks>
/// <c>gst_poll_new</c> and <c>gst_poll_new_timer</c> are marked <c>(skip)</c>
/// in the C and therefore declared <c>introspectable="0"</c> in the gir, so
/// <see cref="Poll.New"/> and <see cref="Poll.NewTimer"/> are written by hand
/// in <c>src/GstSharp.Net/Custom/Poll.cs</c>. They are the only way managed
/// code makes a set, and the set they answer is released by
/// <see cref="Poll.Free"/> and by nothing else: there is no finalizer.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class PollTests
{
    /// <summary>
    /// A timer set takes the wakeup that was written into its control socket,
    /// which is what makes a timeout cancellable: the wait answers at once
    /// rather than after the timeout, and reading the control clears it again.
    /// </summary>
    [Fact]
    public void ATimerSetTakesTheWakeupThatWasWrittenToIt()
    {
        Poll set = Poll.NewTimer();
        try
        {
            Assert.True(set.WriteControl());

            // The timeout is finite so that a set that never reports the
            // wakeup fails the test instead of holding the run.
            Assert.Equal(1, set.Wait(new ClockTime(2_000_000_000)));
            Assert.True(set.ReadControl());
        }
        finally
        {
            set.Free();
        }
    }

    /// <summary>
    /// A controllable set answers a descriptor of its own, which is the pair
    /// <see cref="Poll.New"/> exists for.
    /// </summary>
    [Fact]
    public void AControllableSetCarriesItsOwnDescriptor()
    {
        Poll set = Poll.New(controllable: true);
        try
        {
            Assert.NotEqual(-1, set.GetReadGpollfd().Fd);
            Assert.True(set.SetControllable(true));
        }
        finally
        {
            set.Free();
        }
    }
}
