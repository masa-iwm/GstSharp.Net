using Gst.GLib;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// A <see cref="MainLoop"/> run on a thread of its own, and the
/// <see cref="GLibSynchronizationContext"/> that dispatches onto it.
/// </summary>
/// <remarks>
/// <para>
/// This is the arrangement an application uses when it wants
/// <c>await</c> continuations to land on one known thread: a private
/// <see cref="MainContext"/>, a thread that makes it thread default and runs a
/// loop on it, and a synchronization context that posts into it. It is the same
/// shape the runtime's own Gio dispatcher has, and the tests below check the
/// two things that shape promises — that work reaches the loop thread and not
/// the caller's, and that quitting lets the thread finish.
/// </para>
/// <para>
/// The loop is only known to be dispatching once something has come back from
/// it, so every test starts with one posted round trip. Without it
/// <see cref="GLibSynchronizationContext.Send"/> races: it hands the work to
/// <c>g_main_context_invoke_full</c>, which runs it on the calling thread when
/// that thread can acquire the context, and a context nobody is running yet can
/// be acquired by anybody.
/// </para>
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class MainLoopTests
{
    /// <summary>How long anything here is allowed to take.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public MainLoopTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Posted work runs on the loop thread, never inline on the caller's, and
    /// it runs in the order it was posted.
    /// </summary>
    [Fact]
    public void PostedWorkRunsOnTheLoopThread()
    {
        using LoopThread loop = LoopThread.Start();

        GLibSynchronizationContext dispatcher = new(loop.Context);
        int callerThread = Environment.CurrentManagedThreadId;

        List<int> order = [];
        int[] observed = new int[3];
        using CountdownEvent done = new(3);

        for (int i = 0; i < 3; i++)
        {
            int index = i;

            dispatcher.Post(
                state =>
                {
                    observed[index] = Environment.CurrentManagedThreadId;
                    order.Add((int)state!);
                    done.Signal();
                },
                index);
        }

        Assert.True(done.Wait(Timeout));

        Assert.Equal([0, 1, 2], order);
        Assert.All(observed, thread => Assert.Equal(loop.ThreadId, thread));
        Assert.DoesNotContain(callerThread, observed);

        _output.WriteLine($"caller {callerThread}, loop {loop.ThreadId}");
    }

    /// <summary>
    /// Sent work runs on the loop thread as well, and the caller waits for it.
    /// </summary>
    [Fact]
    public void SentWorkRunsOnTheLoopThreadAndTheCallerWaits()
    {
        using LoopThread loop = LoopThread.Start();

        GLibSynchronizationContext dispatcher = new(loop.Context);

        int ran = 0;
        int thread = 0;

        dispatcher.Send(
            _ =>
            {
                // A send that had not completed by the time it returned would
                // let the assertions below run against the initial values.
                Thread.Sleep(50);
                thread = Environment.CurrentManagedThreadId;
                Volatile.Write(ref ran, 1);
            },
            state: null);

        Assert.Equal(1, Volatile.Read(ref ran));
        Assert.Equal(loop.ThreadId, thread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, thread);
    }

    /// <summary>
    /// An exception thrown by sent work reaches the caller, which is what makes
    /// <see cref="GLibSynchronizationContext.Send"/> a call and not a post.
    /// </summary>
    [Fact]
    public void ASentFailureReachesTheCaller()
    {
        using LoopThread loop = LoopThread.Start();

        GLibSynchronizationContext dispatcher = new(loop.Context);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => dispatcher.Send(_ => throw new InvalidOperationException("from the loop"), state: null));

        Assert.Equal("from the loop", thrown.Message);

        // The loop survived it and is still dispatching.
        using ManualResetEventSlim after = new(initialState: false);
        dispatcher.Post(_ => after.Set(), state: null);
        Assert.True(after.Wait(Timeout));
    }

    /// <summary>
    /// Work sent from the loop thread itself runs inline rather than deadlocking
    /// on a loop that is waiting for it.
    /// </summary>
    [Fact]
    public void SendFromTheLoopThreadRunsInline()
    {
        using LoopThread loop = LoopThread.Start();

        GLibSynchronizationContext dispatcher = new(loop.Context);

        int inner = 0;
        using ManualResetEventSlim done = new(initialState: false);

        dispatcher.Post(
            _ =>
            {
                dispatcher.Send(_ => Volatile.Write(ref inner, Environment.CurrentManagedThreadId), state: null);
                done.Set();
            },
            state: null);

        Assert.True(done.Wait(Timeout));
        Assert.Equal(loop.ThreadId, Volatile.Read(ref inner));
    }

    /// <summary>
    /// Quitting stops the loop and lets its thread finish.
    /// </summary>
    [Fact]
    public void QuittingEndsTheLoopThread()
    {
        LoopThread loop = LoopThread.Start();

        Assert.True(loop.Loop.IsRunning);

        loop.Loop.Quit();

        Assert.True(loop.Thread.Join(Timeout));
        Assert.False(loop.Loop.IsRunning);
        Assert.Null(loop.Error);

        // Disposing after the thread is gone is the normal order, and it is what
        // the other tests do through the using.
        loop.Dispose();
    }

    /// <summary>
    /// A main loop running on a thread of its own, with the private context it
    /// runs.
    /// </summary>
    private sealed class LoopThread : IDisposable
    {
        private readonly MainContext _context;
        private readonly MainLoop _loop;
        private readonly Thread _thread;
        private int _threadId;

        private LoopThread(MainContext context, MainLoop loop, Thread thread)
        {
            _context = context;
            _loop = loop;
            _thread = thread;
        }

        /// <summary>Gets the private context the loop runs.</summary>
        internal MainContext Context => _context;

        /// <summary>Gets the loop.</summary>
        internal MainLoop Loop => _loop;

        /// <summary>Gets the thread that runs it.</summary>
        internal Thread Thread => _thread;

        /// <summary>Gets the managed identifier of that thread.</summary>
        internal int ThreadId => Volatile.Read(ref _threadId);

        /// <summary>Gets whatever the loop thread failed with, if anything.</summary>
        internal Exception? Error { get; private set; }

        /// <summary>
        /// Starts a loop on a thread of its own and waits until it dispatches.
        /// </summary>
        /// <returns>The running loop.</returns>
        internal static LoopThread Start()
        {
            MainContext context = MainContext.New();
            MainLoop loop = new(context);

            Thread thread = new(static state => ((LoopThread)state!).Run())
            {
                IsBackground = true,
                Name = "GstSharp main loop test",
            };

            LoopThread started = new(context, loop, thread);
            thread.Start(started);

            // One round trip proves that the loop owns the context and is
            // dispatching; see the remarks of the class.
            GLibSynchronizationContext dispatcher = new(context);
            using ManualResetEventSlim ready = new(initialState: false);
            dispatcher.Post(_ => ready.Set(), state: null);

            if (!ready.Wait(Timeout))
            {
                loop.Quit();
                thread.Join(Timeout);
                loop.Dispose();
                context.Dispose();
                throw new TimeoutException("The main loop never dispatched.");
            }

            return started;
        }

        /// <summary>
        /// Stops the loop, waits for its thread and releases both wrappers.
        /// </summary>
        public void Dispose()
        {
            if (_loop.IsRunning)
            {
                _loop.Quit();
            }

            _thread.Join(Timeout);
            _loop.Dispose();
            _context.Dispose();
        }

        private void Run()
        {
            Volatile.Write(ref _threadId, Environment.CurrentManagedThreadId);

            // This is what makes the context the one an operation started here
            // captures, exactly as the Gio dispatcher of the runtime does it.
            _context.PushThreadDefault();

            try
            {
                _loop.Run();
            }
            catch (Exception exception)
            {
                Error = exception;
            }
            finally
            {
                _context.PopThreadDefault();
            }
        }
    }
}
