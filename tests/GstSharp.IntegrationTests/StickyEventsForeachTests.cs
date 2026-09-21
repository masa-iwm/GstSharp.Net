using System.Runtime.CompilerServices;
using Gst;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <c>Gst.Pad.StickyEventsForeach</c>: the walk whose function is lent one
/// reference through a slot it may keep, clear or replace.
/// </summary>
/// <remarks>
/// Every test drives a bare pad, which stores sticky events of its own as soon
/// as it is active, so nothing here needs a pipeline.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe partial class StickyEventsForeachTests
{
    /// <summary>
    /// Every sticky event is shown once, in the order the library keeps them,
    /// and the function is handed the very pad the walk was started on.
    /// </summary>
    [Fact]
    public void EveryStickyEventIsShownOnceInOrder()
    {
        using Pad pad = NewPadWithStickyEvents();
        List<EventType> seen = [];
        Pad? shown = null;

        pad.StickyEventsForeach((Pad p, ref Event? @event) =>
        {
            shown = p;
            seen.Add(@event!.Type);
            return true;
        });

        Assert.Same(pad, shown);
        Assert.Equal([EventType.StreamStart, EventType.Caps, EventType.Segment], seen);
    }

    /// <summary>
    /// A walk that leaves every slot alone leaves every reference count where
    /// it was, and the wrapper it lent is dead once the invocation is over.
    /// </summary>
    [Fact]
    public void AnUntouchedWalkLeavesEveryReferenceCountWhereItWas()
    {
        using Pad pad = NewPadWithStickyEvents();
        using Event held = Hold(pad, EventType.Caps);
        nint handle = held.Handle;
        uint before = RefCountOf(handle);
        Event? captured = null;

        pad.StickyEventsForeach((Pad _, ref Event? @event) =>
        {
            if (@event!.Type == EventType.Caps)
            {
                captured = @event;
            }

            return true;
        });

        Assert.Equal(before, RefCountOf(handle));
        Assert.NotNull(captured);
        Assert.True(captured.IsDisposed);
    }

    /// <summary>A function that answers false is not called again.</summary>
    [Fact]
    public void ReturningFalseEndsTheWalk()
    {
        using Pad pad = NewPadWithStickyEvents();
        int calls = 0;

        pad.StickyEventsForeach((Pad _, ref Event? @event) =>
        {
            calls++;
            return false;
        });

        Assert.Equal(1, calls);
    }

    /// <summary>
    /// Clearing the slot removes the event from the pad and releases the
    /// reference the pad held.
    /// </summary>
    [Fact]
    public void ClearingTheSlotRemovesTheEvent()
    {
        using Pad pad = NewPadWithStickyEvents();
        using Event held = Hold(pad, EventType.Segment);
        nint handle = held.Handle;

        Assert.Equal(2u, RefCountOf(handle));

        pad.StickyEventsForeach((Pad _, ref Event? @event) =>
        {
            if (@event!.Type == EventType.Segment)
            {
                @event = null;
            }

            return true;
        });

        Assert.Null(pad.GetStickyEvent(EventType.Segment, 0));

        using Event? streamStart = pad.GetStickyEvent(EventType.StreamStart, 0);
        using Event? caps = pad.GetStickyEvent(EventType.Caps, 0);

        Assert.NotNull(streamStart);
        Assert.NotNull(caps);
        Assert.Equal(1u, RefCountOf(handle));
    }

    /// <summary>
    /// Assigning another event replaces the entry: the new event is handed over
    /// to the library and the old one loses the reference the pad held.
    /// </summary>
    [Fact]
    public void AssigningAnotherEventReplacesIt()
    {
        using Pad pad = NewPadWithStickyEvents();
        using Event held = Hold(pad, EventType.Caps);
        nint handle = held.Handle;
        Event? replacement = null;

        Assert.Equal(2u, RefCountOf(handle));

        pad.StickyEventsForeach((Pad _, ref Event? @event) =>
        {
            if (@event!.Type != EventType.Caps)
            {
                return true;
            }

            using Caps caps = Caps.NewEmptySimple("application/x-gstsharp-replaced");
            replacement = Event.NewCaps(caps);
            @event = replacement;
            return true;
        });

        Assert.NotNull(replacement);
        Assert.True(replacement.IsDisposed);
        Assert.Equal(1u, RefCountOf(handle));

        using Event? stored = pad.GetStickyEvent(EventType.Caps, 0);

        Assert.NotNull(stored);
        stored.ParseCaps(out Caps? storedCaps);
        Assert.NotNull(storedCaps);

        using Structure structure = storedCaps.GetStructure(0);

        Assert.True(structure.HasName("application/x-gstsharp-replaced"));
    }

    /// <summary>
    /// An exception the function throws is reported through the trap and ends
    /// the walk, and the event it was shown keeps its reference count.
    /// </summary>
    [Fact]
    public void AThrowingHandlerIsTrappedAndEndsTheWalk()
    {
        using Pad pad = NewPadWithStickyEvents();
        using Event held = Hold(pad, EventType.StreamStart);
        nint handle = held.Handle;
        uint before = RefCountOf(handle);
        InvalidOperationException thrown = new("The walk of the sticky events threw.");
        int calls = 0;
        int reports = 0;

        void OnFailure(Exception exception)
        {
            // The event is process wide, so only the instance this test threw
            // says anything about this test.
            if (ReferenceEquals(exception, thrown))
            {
                Interlocked.Increment(ref reports);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;
        try
        {
            pad.StickyEventsForeach((Pad _, ref Event? @event) =>
            {
                calls++;
                throw thrown;
            });
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }

        Assert.Equal(1, calls);
        Assert.Equal(1, reports);
        Assert.Equal(before, RefCountOf(handle));
    }

    /// <summary>
    /// The state of the walk is rooted for as long as the library may call it,
    /// and released once the call returns.
    /// </summary>
    [Fact]
    public void TheDelegateSurvivesACollectionDuringTheWalk()
    {
        using Pad pad = NewPadWithStickyEvents();
        (WeakReference Captured, int Calls) walk = RunCollectingWalk(pad);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Equal(3, walk.Calls);
        Assert.False(walk.Captured.IsAlive);
    }

    /// <summary>
    /// Walks the pad with a fresh function that collects while it runs, and
    /// answers a weak reference to the object the function captured.
    /// </summary>
    /// <param name="pad">The pad to walk.</param>
    /// <returns>The weak reference and the number of invocations.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Captured, int Calls) RunCollectingWalk(Pad pad)
    {
        int[] calls = new int[1];
        WeakReference captured = new(calls);

        pad.StickyEventsForeach((Pad _, ref Event? @event) =>
        {
            if (calls[0] == 0)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            calls[0]++;
            return true;
        });

        return (captured, calls[0]);
    }

    /// <summary>Builds an active pad that carries three sticky events.</summary>
    /// <returns>The pad, which the caller disposes.</returns>
    private static Pad NewPadWithStickyEvents()
    {
        Pad pad = Pad.New("sink", PadDirection.Sink);
        try
        {
            Assert.True(pad.SetActive(true));

            using (Event streamStart = Event.NewStreamStart("sticky-events-foreach"))
            {
                Assert.Equal(FlowReturn.Ok, pad.StoreStickyEvent(streamStart));
            }

            using (Caps caps = Caps.NewEmptySimple("application/x-gstsharp-sticky"))
            using (Event capsEvent = Event.NewCaps(caps))
            {
                Assert.Equal(FlowReturn.Ok, pad.StoreStickyEvent(capsEvent));
            }

            using (Segment segment = Segment.New())
            {
                segment.Init(Format.Time);
                using Event segmentEvent = Event.NewSegment(segment);
                Assert.Equal(FlowReturn.Ok, pad.StoreStickyEvent(segmentEvent));
            }

            return pad;
        }
        catch
        {
            pad.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes a second wrapper of a sticky event, so that its reference count is
    /// visible while the pad drops or replaces its own reference.
    /// </summary>
    /// <param name="pad">The pad that stores the event.</param>
    /// <param name="type">The type of the event.</param>
    /// <returns>The wrapper, which the caller disposes.</returns>
    private static Event Hold(Pad pad, EventType type) =>
        pad.GetStickyEvent(type, 0) ?? throw new InvalidOperationException($"The pad stores no {type} event.");

    private static uint RefCountOf(nint handle) => *(uint*)(handle + sizeof(nint));
}
