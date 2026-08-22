using System.Diagnostics;
using Gst.Pbutils;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The failure half of <c>gst_install_plugins_async</c>, which is the half a
/// test can assert: the call answers something other than
/// <see cref="InstallPluginsReturn.StartedOk"/> when no installer helper is
/// configured, the delegate is never invoked, and the binding releases the
/// state it allocated for it at the call site.
/// </summary>
/// <remarks>
/// A successful installation is deliberately not asserted. It would start an
/// external program, need a main loop to report through, and depend on what the
/// machine has installed. The test therefore does nothing at all on a machine
/// that has an installer helper.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class InstallPluginsAsyncTests
{
    /// <summary>How long the state is given to become collectable.</summary>
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void AFailedRequestNeverCallsBackAndReleasesTheDelegate()
    {
        // The premise of the test is that no helper can be started, which is
        // what makes the call answer a failure, free the state synchronously
        // and never invoke the delegate. A machine that has one configured
        // (GST_INSTALL_PLUGINS_HELPER, or a distribution helper on the search
        // path) would instead start an external installer that nothing here
        // iterates a main context for, so the request is not made at all.
        if (PbutilsGlobal.InstallPluginsSupported())
        {
            return;
        }

        int calls = 0;
        InstallPluginsReturn result;
        WeakReference weak = Request(out result);

        // No helper was started, so nothing will ever report a result. Which
        // failure is answered depends on the machine, so only StartedOk is
        // ruled out.
        Assert.NotEqual(InstallPluginsReturn.StartedOk, result);

        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < CollectionTimeout)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (!weak.IsAlive)
            {
                Assert.Equal(0, Volatile.Read(ref calls));
                return;
            }

            Thread.Sleep(20);
        }

        Assert.Fail("the callback state was still reachable, so its handle was never freed");

        WeakReference Request(out InstallPluginsReturn answered)
        {
            InstallPluginsResultFunc func = _ => Interlocked.Increment(ref calls);
            WeakReference reference = new(func);
            answered = PbutilsGlobal.InstallPluginsAsync(
                ["gstreamer|1.0|gstsharp-net-tests|a plugin that does not exist|decoder-audio/x-nonesuch"],
                ctx: null,
                func);

            return reference;
        }
    }
}
