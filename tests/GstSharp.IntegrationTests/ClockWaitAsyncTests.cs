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
/// to the clock. Three exits are taken before that assignment and never see the
/// destroy notification, so the binding releases the state itself on them,
/// which is what the first test measures. Every return of the dispatch leaves
/// the state with the entry, whether it succeeded or not, and the binding must
/// not touch it there, because a second release would be a use after free
/// rather than a leak. The other two tests measure that, once on the return
/// that succeeded and once on a return that failed.
/// </para>
/// <para>
/// Of the three exits before the assignment only <c>GST_CLOCK_ERROR</c> is
/// reachable through the public API. <c>GST_CLOCK_BADTIME</c> needs an entry
/// whose time is invalid, and both <c>gst_clock_new_single_shot_id</c> and
/// <c>gst_clock_single_shot_id_reinit</c> refuse one;
/// <c>GST_CLOCK_UNSUPPORTED</c> needs a clock class with no <c>wait_async</c>,
/// and every instantiable clock derives from <c>GstSystemClock</c>, which has
/// one. <c>GST_CLOCK_ERROR</c> is the exit taken when the entry lost its clock,
/// and an entry holds nothing but a weak reference to the clock it was made
/// from, so outliving that clock is all it takes. That same
/// <c>GST_CLOCK_ERROR</c> is answered from behind the assignment as well, by a
/// clock that could not start its waiting thread, which is why the binding
/// tells the two apart by the clock of the entry rather than by the result.
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
    /// <para>
    /// Unscheduling wakes the waiting thread of the clock, which then drops the
    /// reference it took on the entry; the release of the state is therefore
    /// not synchronous with the call here, and the collection is retried until
    /// it happens.
    /// </para>
    /// <para>
    /// The clock is one this test owns rather than the system clock of the
    /// process, because the waiting thread of a clock only ever looks at the
    /// first entry of its list: an entry that is unscheduled behind an entry
    /// that is still waiting is not touched until that one is dealt with, and
    /// the system clock of the process carries entries other tests left on it.
    /// An unshared clock has a list of its own, where the entry of this test is
    /// the first one.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWaitAsyncThatWasAcceptedLeavesTheStateWithTheEntry()
    {
        using NtpClock clock = NtpClock.New("gstsharp-wait-async-ok", "127.0.0.1", 5678, ClockTime.Zero);
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
    /// An entry that was unscheduled before the wait answers
    /// <see cref="ClockReturn.Unscheduled"/> from behind the assignment, so the
    /// state stays with the entry and is released when the entry is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unscheduling an entry sets its status, which the <c>wait_async</c> of
    /// the system clock reads after <c>gst_clock_id_wait_async</c> has already
    /// written the callback, its state and the destroy notification onto it.
    /// The refusal therefore looks like the two exits in front of the
    /// assignment from the result alone, and is the opposite of them: releasing
    /// the state here would free it a second time when the entry is freed.
    /// </para>
    /// <para>
    /// The entry is never added to the list of the clock on this path, so
    /// nothing takes a reference on it and the release is synchronous with the
    /// unreference below, unlike the accepted wait above.
    /// </para>
    /// </remarks>
    [Fact]
    public void AWaitAsyncOnAnUnscheduledEntryLeavesTheStateWithTheEntry()
    {
        using NtpClock clock = NtpClock.New("gstsharp-wait-async-unscheduled", "127.0.0.1", 5678, ClockTime.Zero);
        nint id = clock.NewSingleShotId(
            ClockTime.FromNanoseconds(clock.GetTime().Nanoseconds + ClockTime.FromSeconds(3600).Nanoseconds));

        Clock.IdUnschedule(id);

        (WeakReference state, ClockReturn result) = WaitAsyncOnALiveClock(id);

        Assert.Equal(ClockReturn.Unscheduled, result);

        // The entry owns the state even though the wait was refused, because
        // the refusal came from the clock and not from the exits in front of
        // the assignment.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.True(state.IsAlive, "The binding released a state that the entry owns.");

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
