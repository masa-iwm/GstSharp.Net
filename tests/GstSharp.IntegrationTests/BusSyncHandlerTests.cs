using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The hand written sync handler surface of <see cref="Bus"/>, measured against
/// the library that is actually there: who owns the message a handler drops,
/// what installing twice does, and what taking the handler off again does.
/// </summary>
/// <remarks>
/// <para>
/// The reference of the poster is the whole subject. <c>gst_bus_post</c> is
/// <c>transfer-ownership="full"</c>, and of the three replies a sync handler
/// can give only <c>GST_BUS_DROP</c> leaves that reference with the handler:
/// <c>GST_BUS_PASS</c> hands it to the queue and <c>GST_BUS_ASYNC</c> keeps it
/// in <c>gst_bus_post</c> until delivery. Managed code cannot hold a reference
/// on its own — a wrapper owns the one it took and nothing else — so the
/// binding releases the poster's reference in its trampoline, and these tests
/// are what says it does it once and only for <c>Drop</c>.
/// </para>
/// <para>
/// The messages are posted through <see cref="TestNatives.BusPost"/> rather
/// than through the binding, because the binding does not emit a consuming
/// post. That is the point here: the test holds a reference of its own, hands
/// a second one to the post, and reads the count afterwards through the raw
/// mirror of <c>GstMiniObject</c>. Reading it is safe because the reference the
/// test holds keeps the message alive whatever the binding did with the other
/// one.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed unsafe class BusSyncHandlerTests
{
    /// <summary>
    /// How many watched messages have been destroyed. The field is static
    /// because the notification is an <c>[UnmanagedCallersOnly]</c> method,
    /// which cannot close over anything; the tests of a collection do not run
    /// in parallel, and the one test that reads it sets it to zero first.
    /// </summary>
    private static int _freed;

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public BusSyncHandlerTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The regression this suite exists for: a handler that answers
    /// <see cref="BusSyncReply.Drop"/> consumes the reference of the poster.
    /// </summary>
    /// <remarks>
    /// Before the trampoline was written by hand this assertion read 2 instead
    /// of 1: the generated one wrapped the message with
    /// <c>Transfer.None</c>, which takes a reference and gives it back, and
    /// nothing ever released the one the post handed over. Every dropped
    /// message leaked, which on a sync handler that drops for a living is a
    /// leak per message of the whole pipeline.
    /// </remarks>
    [Fact]
    public unsafe void AHandlerThatDropsConsumesTheReferenceOfThePoster()
    {
        using Bus bus = Bus.New();

        int seen = 0;
        bus.SetSyncHandler((_, _) =>
        {
            Interlocked.Increment(ref seen);
            return BusSyncReply.Drop;
        });

        try
        {
            // The wrapper owns the one reference the message is created with.
            using Message message = Message.NewEos(null);
            nint handle = message.Handle;
            Assert.Equal(1, ((MiniObjectRaw*)handle)->Refcount);

            // The reference the post consumes. Without it the post would take
            // the one the wrapper owns and the wrapper would release a message
            // that is already gone.
            TestNatives.MiniObjectRef(handle);
            Assert.Equal(2, ((MiniObjectRaw*)handle)->Refcount);

            Assert.NotEqual(0, TestNatives.BusPost(bus.Handle, handle));

            Assert.Equal(1, Volatile.Read(ref seen));

            // The handler dropped, so the reference of the poster is gone and
            // the only one left is the one this test holds through the wrapper.
            // A count of 2 here is the leak.
            Assert.Equal(1, ((MiniObjectRaw*)handle)->Refcount);

            // And the message never reached the queue, which is what dropping
            // means on the other side of the contract.
            Assert.Null(bus.Pop());
        }
        finally
        {
            bus.ClearSyncHandler();
        }
    }

    /// <summary>
    /// Dropped messages are freed rather than accumulating: every one of a long
    /// run of posts is destroyed by the time the run ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the claim that a reference count cannot make. A count is read
    /// off one object that is known to be alive; a leak is a population, and
    /// the objects that make it up are exactly the ones a test must not touch
    /// afterwards. So each message is watched with
    /// <c>gst_mini_object_weak_ref</c> before it is posted and the
    /// notifications are counted: a message that was freed says so itself, and
    /// one that leaked stays silent.
    /// </para>
    /// <para>
    /// Nothing in the loop keeps a message alive. The wrapper hands its
    /// reference to the post and is disposed, and the handler drops, so the
    /// binding is the only thing left that could be holding one.
    /// </para>
    /// </remarks>
    [Fact]
    public void DroppedMessagesAreFreedRatherThanAccumulating()
    {
        const int Posts = 500;

        using Bus bus = Bus.New();

        Volatile.Write(ref _freed, 0);

        int seen = 0;
        bus.SetSyncHandler((_, _) =>
        {
            Interlocked.Increment(ref seen);
            return BusSyncReply.Drop;
        });

        try
        {
            for (int i = 0; i < Posts; i++)
            {
                Message message = Message.NewEos(null);
                nint handle = message.Handle;

                // The weak reference holds nothing, so it cannot keep the
                // message alive; it only reports the moment it is destroyed.
                TestNatives.MiniObjectWeakRef(handle, (nint)(delegate* unmanaged[Cdecl]<nint, nint, void>)&OnFreed, nint.Zero);

                // The reference the post takes over. Disposing the wrapper
                // gives back the one it created the message with.
                TestNatives.MiniObjectRef(handle);
                message.Dispose();

                Assert.NotEqual(0, TestNatives.BusPost(bus.Handle, handle));
            }

            Assert.Equal(Posts, Volatile.Read(ref seen));

            // Every message the run created is gone. A binding that did not
            // release the poster's reference reads 0 here.
            Assert.Equal(Posts, Volatile.Read(ref _freed));

            // And none of them reached the queue, which is the other half of
            // what dropping means.
            Assert.Null(bus.Pop());
            Assert.False(bus.HavePending());
        }
        finally
        {
            bus.ClearSyncHandler();
        }

        _output.WriteLine($"{Volatile.Read(ref _freed)} of {Posts} dropped messages were freed");
    }

    /// <summary>
    /// The other half of the reply: a handler that passes leaves the reference
    /// of the poster with the queue, and the message comes back out of it.
    /// </summary>
    [Fact]
    public unsafe void AHandlerThatPassesLeavesTheMessageOnTheQueue()
    {
        using Bus bus = Bus.New();

        bus.SetSyncHandler((_, _) => BusSyncReply.Pass);

        try
        {
            using Message posted = Message.NewEos(null);
            nint handle = posted.Handle;

            TestNatives.MiniObjectRef(handle);
            Assert.NotEqual(0, TestNatives.BusPost(bus.Handle, handle));

            // The queue holds the reference of the poster now, next to the one
            // of this test.
            Assert.Equal(2, ((MiniObjectRaw*)handle)->Refcount);

            using Message? popped = bus.Pop();
            Assert.NotNull(popped);
            Assert.Equal(handle, popped.Handle);
            Assert.Equal(MessageType.Eos, popped.Type);
        }
        finally
        {
            bus.ClearSyncHandler();
        }
    }

    /// <summary>
    /// Installing a handler over another one replaces it. This is the contract
    /// of GStreamer since 1.16.3, and the reason the binding does not try to
    /// guard against it.
    /// </summary>
    /// <remarks>
    /// <c>gst_bus_set_sync_handler</c> once refused to replace a handler that
    /// was installed — the documentation of the function still mentions it, in
    /// the past tense. Since 1.16.3 it swaps the handler under the object lock
    /// of the bus and releases the old one afterwards, with no complaint and no
    /// race. The binding follows the current contract, so the second install
    /// simply wins.
    /// </remarks>
    [Fact]
    public void InstallingASecondHandlerReplacesTheFirst()
    {
        using Bus bus = Bus.New();

        int first = 0;
        int second = 0;

        bus.SetSyncHandler((_, _) =>
        {
            Interlocked.Increment(ref first);
            return BusSyncReply.Drop;
        });

        try
        {
            Post(bus);
            Assert.Equal(1, Volatile.Read(ref first));

            bus.SetSyncHandler((_, _) =>
            {
                Interlocked.Increment(ref second);
                return BusSyncReply.Drop;
            });

            Post(bus);
            Post(bus);

            // The first handler saw nothing more, and no exception was raised
            // for installing over it.
            Assert.Equal(1, Volatile.Read(ref first));
            Assert.Equal(2, Volatile.Read(ref second));
            Assert.Null(bus.Pop());
        }
        finally
        {
            bus.ClearSyncHandler();
        }
    }

    /// <summary>
    /// The call the generated surface could not express: taking the handler off
    /// again, which is what an application does at teardown.
    /// </summary>
    /// <remarks>
    /// The generated <c>SetSyncHandler</c> rejected a null handler, so the C
    /// clear form was unreachable and a consumer could replace the handler but
    /// never remove it. After the clear the messages go where they would have
    /// gone with no handler installed: onto the queue.
    /// </remarks>
    [Fact]
    public void ClearingTheHandlerSendsLaterMessagesToTheQueue()
    {
        using Bus bus = Bus.New();

        int seen = 0;
        bus.SetSyncHandler((_, _) =>
        {
            Interlocked.Increment(ref seen);
            return BusSyncReply.Drop;
        });

        Post(bus);
        Assert.Equal(1, Volatile.Read(ref seen));
        Assert.Null(bus.Pop());

        bus.ClearSyncHandler();

        Post(bus);

        // The handler is gone, so it saw nothing of the second message, and the
        // message took the ordinary route.
        Assert.Equal(1, Volatile.Read(ref seen));

        using Message? popped = bus.Pop();
        Assert.NotNull(popped);
        Assert.Equal(MessageType.Eos, popped.Type);

        // Clearing a bus that has no handler is allowed and does nothing.
        bus.ClearSyncHandler();
    }

    /// <summary>
    /// A handler that throws is trapped, and the message it failed on still
    /// reaches the queue.
    /// </summary>
    /// <remarks>
    /// The zero of <c>GstBusSyncReply</c> is <c>GST_BUS_DROP</c>, so a
    /// trampoline that answers a default value on failure silently swallows
    /// messages — including the error and end-of-stream messages an application
    /// blocks on. The hand written trampoline answers
    /// <see cref="BusSyncReply.Pass"/> instead: a handler that failed has
    /// decided nothing, and it must not consume the reference of the poster
    /// either.
    /// </remarks>
    [Fact]
    public void AHandlerThatThrowsIsTrappedAndTheMessageStillArrives()
    {
        using Bus bus = Bus.New();

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            bus.SetSyncHandler((_, _) =>
                throw new InvalidOperationException("The sync handler of the test throws on purpose."));

            Post(bus);

            using Message? popped = bus.Pop();
            Assert.NotNull(popped);
            Assert.Equal(MessageType.Eos, popped.Type);
        }
        finally
        {
            bus.ClearSyncHandler();
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (failures)
        {
            Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(failures[0]);
        }
    }

    /// <summary>
    /// What a handler keeps of a message it was handed: a copy, because the
    /// wrapper it saw is disposed the moment it returns.
    /// </summary>
    /// <remarks>
    /// This is the shape the documentation of the delegate and of the signal
    /// arguments now points at. Without <see cref="Message.Copy"/> there was
    /// nothing a handler could do to keep a message, since taking a reference
    /// of one's own is not something the managed surface offers.
    /// </remarks>
    [Fact]
    public void AHandlerKeepsAMessageByCopyingIt()
    {
        using Bus bus = Bus.New();

        Message? kept = null;

        bus.SetSyncHandler((_, message) =>
        {
            kept = message?.Copy();
            return BusSyncReply.Drop;
        });

        try
        {
            using Element element = Assert.IsAssignableFrom<Element>(Global.ParseLaunch("fakesink name=probe"));

            Message message = Message.NewEos(element);
            nint handle = message.Handle;
            TestNatives.MiniObjectRef(handle);
            message.Dispose();

            Assert.NotEqual(0, TestNatives.BusPost(bus.Handle, handle));

            // The wrapper the handler saw is gone and the message it stood for
            // was dropped, and the copy is still a message of its own.
            Assert.NotNull(kept);
            Assert.Equal(MessageType.Eos, kept.Type);
            Assert.Equal("probe", kept.SourceName);

            _output.WriteLine($"the copy the handler kept reports source {kept.SourceName}");
        }
        finally
        {
            kept?.Dispose();
            bus.ClearSyncHandler();
        }
    }

    /// <summary>
    /// The subscription an application with no main loop wants: every message
    /// is delivered in the thread that posted it and the queue stays empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole point of <see cref="Bus.SubscribeSyncDrop"/>. A bus
    /// nobody reads accumulates messages for the lifetime of the pipeline, and
    /// a sync handler only avoids that if it answers
    /// <see cref="BusSyncReply.Drop"/> — which is the reply with an ownership
    /// rule attached. The subscription answers it for the handler, so the
    /// assertions here are the two halves of "nothing is left behind": every
    /// message arrived, and none of them reached the queue.
    /// </para>
    /// <para>
    /// The thread is asserted as well, because a handler that ran anywhere else
    /// would mean the message had been queued and delivered later, which is the
    /// behaviour this replaces.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASyncDropSubscriptionDeliversOnThePostersThreadAndLeavesTheQueueEmpty()
    {
        const int Posts = 100;

        using Bus bus = Bus.New();

        int seen = 0;
        int poster = Environment.CurrentManagedThreadId;
        int elsewhere = 0;

        IDisposable subscription = bus.SubscribeSyncDrop((_, message) =>
        {
            if (message?.Type == MessageType.Eos)
            {
                Interlocked.Increment(ref seen);
            }

            if (Environment.CurrentManagedThreadId != poster)
            {
                Interlocked.Increment(ref elsewhere);
            }
        });

        try
        {
            for (int i = 0; i < Posts; i++)
            {
                Post(bus);
            }

            Assert.Equal(Posts, Volatile.Read(ref seen));
            Assert.Equal(0, Volatile.Read(ref elsewhere));

            // The queue never grew, which is what the subscription is for.
            Assert.Null(bus.Pop());
            Assert.False(bus.HavePending());
        }
        finally
        {
            subscription.Dispose();
        }

        // And with the subscription gone the bus is an ordinary bus again.
        Post(bus);

        Assert.Equal(Posts, Volatile.Read(ref seen));

        using Message? popped = bus.Pop();
        Assert.NotNull(popped);
        Assert.Equal(MessageType.Eos, popped.Type);

        // Disposing twice clears nothing a second time and throws nothing.
        subscription.Dispose();

        _output.WriteLine($"{Volatile.Read(ref seen)} of {Posts} posts were delivered synchronously");
    }

    /// <summary>
    /// A subscription is the sync handler of the bus, so it replaces the one
    /// that is installed and disposing it clears whatever is installed. This is
    /// the caveat the documentation states, measured.
    /// </summary>
    [Fact]
    public void ASyncDropSubscriptionTakesTheOneSyncHandlerOfTheBus()
    {
        using Bus bus = Bus.New();

        int handler = 0;
        bus.SetSyncHandler((_, _) =>
        {
            Interlocked.Increment(ref handler);
            return BusSyncReply.Drop;
        });

        int subscribed = 0;
        IDisposable subscription = bus.SubscribeSyncDrop((_, _) => Interlocked.Increment(ref subscribed));

        Post(bus);

        // The subscription won: there is one sync handler per bus.
        Assert.Equal(0, Volatile.Read(ref handler));
        Assert.Equal(1, Volatile.Read(ref subscribed));

        subscription.Dispose();

        Post(bus);

        // And the clear did not put the first handler back, which C offers no
        // way to do.
        Assert.Equal(0, Volatile.Read(ref handler));
        Assert.Equal(1, Volatile.Read(ref subscribed));

        using Message? popped = bus.Pop();
        Assert.NotNull(popped);
    }

    /// <summary>
    /// A handler of a subscription that throws is trapped the way any sync
    /// handler is, and the message it failed on takes the ordinary route.
    /// </summary>
    [Fact]
    public void AFailingSubscriptionHandlerIsTrappedAndTheMessageStillArrives()
    {
        using Bus bus = Bus.New();

        List<Exception> failures = [];
        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        Gst.Interop.ExceptionTrap.UnhandledException += OnFailure;

        IDisposable subscription = bus.SubscribeSyncDrop((_, _) =>
            throw new InvalidOperationException("The subscription of the test throws on purpose."));

        try
        {
            Post(bus);

            using Message? popped = bus.Pop();
            Assert.NotNull(popped);
            Assert.Equal(MessageType.Eos, popped.Type);
        }
        finally
        {
            subscription.Dispose();
            Gst.Interop.ExceptionTrap.UnhandledException -= OnFailure;
        }

        lock (failures)
        {
            Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(failures[0]);
        }
    }

    /// <summary>
    /// Counts one destroyed message, as a <c>GstMiniObjectNotify</c>.
    /// </summary>
    /// <param name="userData">Unused; the count is a static field.</param>
    /// <param name="miniObject">
    /// The message that is being destroyed. It must not be touched: the
    /// notification runs while it is torn down.
    /// </param>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void OnFreed(nint userData, nint miniObject) => Interlocked.Increment(ref _freed);

    /// <summary>
    /// Posts one end-of-stream message, handing the poster's reference over.
    /// </summary>
    /// <param name="bus">The bus to post on.</param>
    private static void Post(Bus bus)
    {
        Message message = Message.NewEos(null);
        nint handle = message.Handle;

        // The wrapper is disposed without releasing anything the post needs:
        // the extra reference is what gst_bus_post takes over.
        TestNatives.MiniObjectRef(handle);
        message.Dispose();

        Assert.NotEqual(0, TestNatives.BusPost(bus.Handle, handle));
    }
}
