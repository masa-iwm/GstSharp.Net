using System.Diagnostics;
using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The asynchronous callback scope against the running library:
/// <c>gst_call_async</c> and <c>gst_object_call_async</c> invoke the delegate
/// exactly once on a thread of the shared pool, and the trampoline releases the
/// <c>GCHandle</c> that carried it.
/// </summary>
/// <remarks>
/// Both entry points arrived in 1.28, so the whole class is gated on the shared
/// probe rather than on one of its own.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class CallAsyncTests
{
    /// <summary>
    /// How long the handle is given to be released. The trampoline frees it
    /// after the delegate returned, on the pool thread, so the release is not
    /// ordered against the assertion and is polled for.
    /// </summary>
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(5);

    [RequiresGStreamerFact(28)]
    public void GlobalCallAsyncInvokesTheDelegateOnceAndReleasesIt()
    {
        using ManualResetEventSlim done = new(initialState: false);
        int calls = 0;

        WeakReference weak = Invoke();
        Assert.True(done.Wait(CollectionTimeout), "the asynchronous call never ran");
        Assert.Equal(1, Volatile.Read(ref calls));
        AssertCollected(weak);

        // A local method keeps the delegate out of the frame of the test, so
        // that nothing but the GCHandle of the binding refers to it.
        WeakReference Invoke()
        {
            // The delegate has to capture something: a lambda that captures
            // nothing is cached in a static field by the compiler and would
            // never be collectable.
            CallAsyncFunc func = () =>
            {
                Interlocked.Increment(ref calls);
                done.Set();
            };

            WeakReference reference = new(func);
            Global.CallAsync(func);
            return reference;
        }
    }

    [RequiresGStreamerFact(28)]
    public void ObjectCallAsyncPassesTheObjectAndReleasesTheDelegate()
    {
        using Pipeline pipeline = Pipeline.New("call-async");
        using ManualResetEventSlim done = new(initialState: false);
        int calls = 0;
        string? seen = null;

        WeakReference weak = Invoke();
        Assert.True(done.Wait(CollectionTimeout), "the asynchronous call never ran");
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.Equal("call-async", seen);
        AssertCollected(weak);

        WeakReference Invoke()
        {
            ObjectCallAsyncFunc func = @object =>
            {
                seen = @object.Name;
                Interlocked.Increment(ref calls);
                done.Set();
            };

            WeakReference reference = new(func);
            pipeline.CallAsync(func);
            return reference;
        }
    }

    /// <summary>
    /// Waits for the target of a weak reference to be collected, which is what
    /// says that the trampoline released the handle: a live
    /// <see cref="System.Runtime.InteropServices.GCHandle"/> would keep it
    /// alive for the life of the process.
    /// </summary>
    /// <param name="weak">The reference to the delegate.</param>
    private static void AssertCollected(WeakReference weak)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < CollectionTimeout)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!weak.IsAlive)
            {
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("the callback state was still reachable, so its handle was never freed");
    }
}
