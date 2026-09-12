using System;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The guard on the members of a bus that need the <c>GstPoll</c> a bus built
/// with <c>enable-async</c> off does not have.
/// </summary>
/// <remarks>
/// <para>
/// <c>enable-async</c> is construct only and write only, so no wrapper can ask
/// a bus how it was built. What the binding does instead is mark the object
/// while it builds it: <see cref="Bus.New(bool)"/> with
/// <see langword="false"/> attaches a word under the
/// <c>gstsharp-bus-no-async-delivery</c> quark, and the poll backed members
/// read it back. The marker is on the GObject rather than on the wrapper, so it
/// outlives the wrapper, which is the last test here.
/// </para>
/// <para>
/// None of these calls reaches the C, so none of them prints a GLib critical:
/// the C would install a watch that never fires, run a nested loop no
/// message wakes (gstbus.c:1217-1219), or answer NULL after a critical
/// (gstbus.c:550).
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class BusNoAsyncDeliveryTests
{
    /// <summary>
    /// Every member that reaches for the missing <c>GstPoll</c> refuses the
    /// call, and the message names the member.
    /// </summary>
    [Fact]
    public void ThePollBackedMembersRefuseABusWithoutAsynchronousDelivery()
    {
        using Bus bus = Bus.New(enableAsync: false);

        Assert.Contains(
            nameof(Bus.Poll),
            Assert.Throws<InvalidOperationException>(
                () => bus.Poll(MessageType.Any, ClockTime.FromMilliseconds(1))).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.AddWatch),
            Assert.Throws<InvalidOperationException>(
                () => bus.AddWatch(0, static (_, _) => true)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.AddSignalWatch),
            Assert.Throws<InvalidOperationException>(bus.AddSignalWatch).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.AddSignalWatchFull),
            Assert.Throws<InvalidOperationException>(() => bus.AddSignalWatchFull(0)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.TimedPop),
            Assert.Throws<InvalidOperationException>(
                () => bus.TimedPop(ClockTime.FromMilliseconds(1))).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.TimedPopFiltered),
            Assert.Throws<InvalidOperationException>(
                () => bus.TimedPopFiltered(ClockTime.FromMilliseconds(1), MessageType.Any)).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            nameof(Bus.GetPollfd),
            Assert.Throws<InvalidOperationException>(() => bus.GetPollfd()).Message,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A timeout of <see cref="ClockTime.None"/> is refused as well: the guard of
    /// the C fails for it exactly as it does for a millisecond (gstbus.c:550),
    /// so it is a critical and a null rather than a wait.
    /// </summary>
    [Fact]
    public void AnUnboundedTimedPopIsRefusedToo()
    {
        using Bus bus = Bus.New(enableAsync: false);

        Assert.Throws<InvalidOperationException>(() => bus.TimedPop(ClockTime.None));
        Assert.Throws<InvalidOperationException>(
            () => bus.TimedPopFiltered(ClockTime.None, MessageType.Any));
    }

    /// <summary>
    /// A timeout of zero is the non-blocking pop, which the C accepts on any
    /// bus, and the members that never reach the poll are untouched.
    /// </summary>
    [Fact]
    public void ANonBlockingPopAndThePlainQueueMembersStillWork()
    {
        using Bus bus = Bus.New(enableAsync: false);

        Assert.Null(bus.TimedPop(ClockTime.Zero));
        Assert.Null(bus.TimedPopFiltered(ClockTime.Zero, MessageType.Any));

        Assert.Null(bus.Pop());
        Assert.Null(bus.Peek());
        Assert.False(bus.HavePending());
    }

    /// <summary>
    /// The marker survives the wrapper: a bus handed to an element and fetched
    /// back through a wrapper of its own is still refused.
    /// </summary>
    [Fact]
    public void TheMarkerSurvivesTheWrapperItWasSetThrough()
    {
        using Element sink = ElementFactory.Make("fakesink", null)
            ?? throw new InvalidOperationException("fakesink is not installed.");

        Bus created = Bus.New(enableAsync: false);
        try
        {
            sink.SetBus(created);
        }
        finally
        {
            // The element holds a reference of its own, so dropping this
            // wrapper leaves the object alive with no wrapper interned for it:
            // the fetch below has to build a new one.
            created.Dispose();
        }

        using Bus fetched = sink.GetBus()
            ?? throw new InvalidOperationException("the element reported no bus.");

        Assert.NotSame(created, fetched);
        Assert.Throws<InvalidOperationException>(
            () => fetched.Poll(MessageType.Any, ClockTime.FromMilliseconds(1)));
    }

    /// <summary>
    /// An ordinary bus is unaffected, which is what says the marker is read
    /// rather than assumed: the absence of the word is the default.
    /// </summary>
    [Fact]
    public void ABusWithAsynchronousDeliveryIsNotRefused()
    {
        using (Bus bus = Bus.New(enableAsync: true))
        {
            Assert.Null(bus.TimedPop(ClockTime.FromMilliseconds(1)));
            Assert.NotEqual(-1, bus.GetPollfd().Fd);
        }

        using Pipeline pipeline = Pipeline.New(null);
        using Bus pipelineBus = pipeline.GetBus()
            ?? throw new InvalidOperationException("the pipeline reported no bus.");

        Assert.Null(pipelineBus.TimedPop(ClockTime.FromMilliseconds(1)));
        Assert.NotEqual(-1, pipelineBus.GetPollfd().Fd);
    }
}
