using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gst;
using Gst.Net;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Who releases the state of the callback that
/// <see cref="Clock.IdWaitAsync"/> hands to GStreamer.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_clock_id_wait_async</c> assigns the callback, its state and the
/// destroy notification onto the clock entry immediately before it dispatches
/// to the clock, and it returns through three earlier exits without ever having
/// seen the destroy notification. The binding releases the state itself on all
/// three, which is what the first test measures; the second measures that it
/// does <em>not</em> do so when the entry took the state over, because a double
/// release would be a use after free rather than a leak.
/// </para>
/// <para>
/// Of the three failing exits only <c>GST_CLOCK_ERROR</c> is reachable through
/// the public API. <c>GST_CLOCK_BADTIME</c> needs an entry whose time is
/// invalid, and both <c>gst_clock_new_single_shot_id</c> and
/// <c>gst_clock_single_shot_id_reinit</c> refuse one;
/// <c>GST_CLOCK_UNSUPPORTED</c> needs a clock class with no <c>wait_async</c>,
/// and every instantiable clock derives from <c>GstSystemClock</c>, which has
/// one. <c>GST_CLOCK_ERROR</c> is the exit taken when the entry lost its clock,
/// and an entry holds nothing but a weak reference to the clock it was made
/// from, so outliving that clock is all it takes.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class ClockWaitAsyncTests
{
    /// <summary>
    /// The entry of a clock that is gone answers <see cref="ClockReturn.Error"/>
    /// without running the callback and without running the destroy
    /// notification, and the binding releases the state of the callback for it.
    /// </summary>
    /// <remarks>
    /// The clock is an <see cref="NtpClock"/> because it has to be a clock this
    /// test alone owns: <c>SystemClock.Obtain</c> hands out the singleton of the
    /// process, which never dies. Nothing answers on the port it is pointed at,
    /// which is what <c>GstNetTests</c> already relies on — constructing the
    /// clock is not an error and the synchronisation simply never happens.
    /// </remarks>
    [Fact]
    public void AWaitAsyncThatFailsReleasesTheStateOfTheCallback()
    {
        nint id;
        using (NtpClock clock = NtpClock.New("gstsharp-wait-async", "127.0.0.1", 5678, ClockTime.Zero))
        {
            id = clock.NewSingleShotId(ClockTime.Zero);
        }

        // The wrapper released the last reference of the clock above, so the
        // weak reference the entry holds is empty and gst_clock_id_wait_async
        // takes its invalid_entry exit.
        (WeakReference state, ClockReturn result) = WaitAsyncOnALostClock(id);

        Assert.Equal(ClockReturn.Error, result);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(state.IsAlive);

        Clock.IdUnref(id);
    }

    /// <summary>
    /// A wait that was accepted leaves the state with the entry, which releases
    /// it when the entry is freed.
    /// </summary>
    /// <remarks>
    /// Unscheduling wakes the waiting thread of the system clock, which then
    /// drops the reference it took on the entry; the release of the state is
    /// therefore not synchronous with the call here, and the collection is
    /// retried until it happens.
    /// </remarks>
    [Fact]
    public void AWaitAsyncThatWasAcceptedLeavesTheStateWithTheEntry()
    {
        using Clock clock = SystemClock.Obtain();
        nint id = clock.NewSingleShotId(
            ClockTime.FromNanoseconds(clock.GetTime().Nanoseconds + ClockTime.FromSeconds(3600).Nanoseconds));

        (WeakReference state, ClockReturn result) = WaitAsyncOnALiveClock(id);

        Assert.Equal(ClockReturn.Ok, result);

        // The entry owns the state now, so it is still there.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(state.IsAlive);

        Clock.IdUnschedule(id);
        Clock.IdUnref(id);

        Assert.True(CollectUntilGone(state), "The entry never released the state of the callback.");
    }

    /// <summary>
    /// Schedules a callback on an entry whose clock is gone.
    /// </summary>
    /// <param name="id">The entry to schedule on.</param>
    /// <returns>A weak reference to the callback, and what the call answered.</returns>
    /// <remarks>
    /// The callback is created in a frame of its own and captures a local, so
    /// that neither the test frame nor the delegate cache of the compiler keeps
    /// it alive when the collection below decides whether anything else did.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference State, ClockReturn Result) WaitAsyncOnALostClock(nint id)
    {
        int calls = 0;
        ClockCallback callback = (_, _, _) =>
        {
            calls++;
            return true;
        };

        WeakReference weak = new(callback);
        ClockReturn result = Clock.IdWaitAsync(id, callback);

        // The invalid_entry exit is taken before the callback is ever read.
        Assert.Equal(0, calls);
        return (weak, result);
    }

    /// <summary>
    /// Schedules a callback on an entry of a clock that is alive.
    /// </summary>
    /// <param name="id">The entry to schedule on.</param>
    /// <returns>A weak reference to the callback, and what the call answered.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference State, ClockReturn Result) WaitAsyncOnALiveClock(nint id)
    {
        int calls = 0;
        ClockCallback callback = (_, _, _) =>
        {
            calls++;
            return true;
        };

        WeakReference weak = new(callback);
        return (weak, Clock.IdWaitAsync(id, callback));
    }

    /// <summary>
    /// Collects until the state is gone, or until the wait is over.
    /// </summary>
    /// <param name="state">The state to watch.</param>
    /// <returns><see langword="true"/> when the state was released.</returns>
    private static bool CollectUntilGone(WeakReference state)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!state.IsAlive)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }
}
