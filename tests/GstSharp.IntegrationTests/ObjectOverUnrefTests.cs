using System.Runtime.InteropServices;
using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The detector of an over-unref: with
/// <c>GSTSHARP_DETECT_OVER_UNREF=1</c> set, a GObject that dies while the
/// binding still holds its toggle reference is reported through the exception
/// trap, from inside the unref that killed it, instead of crashing the next
/// release. With the switch off nothing is installed and nothing is reported.
/// </summary>
[Collection(GstCollection.Name)]
public sealed partial class ObjectOverUnrefTests
{
    /// <summary>
    /// Dropping a reference that nobody owned kills the object underneath the
    /// toggle reference of its wrapper. The weak notification observes that
    /// while the instance can still be read, names it, and takes the
    /// bookkeeping of the release over, so that disposing the wrapper
    /// afterwards never touches the corpse.
    /// </summary>
    [Fact]
    public void AnObjectThatDiesUnderItsToggleReferenceIsReported()
    {
        List<Exception> failures = [];
        using FailureLog log = new(failures);

        bool detecting = OverUnrefDetector.Enabled;
        OverUnrefDetector.Enabled = true;

        Element element;
        nint handle;

        try
        {
            element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "over-unref"));

            // Read once, before the object dies: the wrapper answers with an
            // exception from then on.
            handle = element.Handle;
        }
        finally
        {
            OverUnrefDetector.Enabled = detecting;
        }

        // The reference the wrapper owns is the only one left, so this one is
        // the over-unref: it runs the dispose chain of the object while the
        // toggle reference is installed. The notification runs inside this
        // call, on this thread.
        RawUnref(handle);

        lock (failures)
        {
            Exception failure = Assert.Single(failures);
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Contains("over-unref", failure.Message, StringComparison.Ordinal);
            Assert.Contains("GstFakeSink", failure.Message, StringComparison.Ordinal);
            Assert.Contains(nameof(ObjectOverUnrefTests), failure.Message, StringComparison.Ordinal);
        }

        // The wrapper outlived its object and is fenced off: it reads as
        // disposed, it refuses to hand its dangling handle out, and it is out
        // of the interning table, so an object that reuses the address gets a
        // wrapper of its own.
        Assert.True(element.IsDisposed);
        ObjectDisposedException refused = Assert.Throws<ObjectDisposedException>(() => element.Handle);
        Assert.Contains("destroyed underneath", refused.Message, StringComparison.Ordinal);
        Assert.Null(Gst.GObject.Object.TryGetInterned(handle));
        Assert.False(Gst.GObject.Object.HasDisposedInterned(handle));

        // The toggle reference is marked released already, so this performs no
        // bookkeeping and calls nothing on the dead object.
        element.Dispose();

        lock (failures)
        {
            Assert.Single(failures);
        }
    }

    /// <summary>
    /// The dispose chain of a live object fires the weak notification too, so
    /// running it by hand is the one false positive of the detector — and the
    /// proof that the weak notification is installed at all while the switch
    /// is on, which is what the switched-off case below is measured against.
    /// </summary>
    [Fact]
    public void RunningTheDisposeChainOfAWatchedObjectIsReported()
    {
        List<Exception> failures = [];
        using FailureLog log = new(failures);

        bool detecting = OverUnrefDetector.Enabled;
        OverUnrefDetector.Enabled = true;

        Element element;
        nint handle;

        try
        {
            element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "run-dispose-on"));
            handle = element.Handle;
        }
        finally
        {
            OverUnrefDetector.Enabled = detecting;
        }

        // The toggle reference keeps the object alive across this, but the
        // dispose chain clears the weak notifications, which fires them.
        RawRunDispose(handle);

        lock (failures)
        {
            Exception failure = Assert.Single(failures);
            Assert.Contains("over-unref", failure.Message, StringComparison.Ordinal);
        }

        // The detector cannot tell this from a real death, so the wrapper is
        // fenced off and its object is leaked, with the toggle reference still
        // on it. That is the cost of the false positive, and the reason the
        // wrapper is not disposed here: Dispose would do nothing anyway.
        Assert.True(element.IsDisposed);
    }

    /// <summary>
    /// With the switch off no weak notification is installed, so the same
    /// dispose chain that is reported above passes unobserved and the wrapper
    /// stays usable.
    /// </summary>
    [Fact]
    public void TheDeathOfAnObjectIsNotObservedWhileTheSwitchIsOff()
    {
        List<Exception> failures = [];
        using FailureLog log = new(failures);

        bool detecting = OverUnrefDetector.Enabled;
        OverUnrefDetector.Enabled = false;

        Element element;
        nint handle;

        try
        {
            element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "run-dispose-off"));
            handle = element.Handle;
        }
        finally
        {
            OverUnrefDetector.Enabled = detecting;
        }

        // Nothing was installed on this object, so nothing observes its
        // dispose chain. The object stays alive: the toggle reference holds it.
        RawRunDispose(handle);

        lock (failures)
        {
            Assert.Empty(failures);
        }

        Assert.False(element.IsDisposed);
        Assert.Equal(handle, element.Handle);

        // The release removes the toggle reference only — removing a weak
        // reference that was never installed would warn in GLib and would say
        // the detector charges for itself while it is off.
        element.Dispose();

        lock (failures)
        {
            Assert.Empty(failures);
        }
    }

    /// <summary>
    /// A wrapper that was built while the switch was on removes its weak
    /// reference when it is released, whatever the switch says by then. Turning
    /// the detector off in between must not leave one behind, and a wrapper
    /// built while it was off must not remove one that was never installed —
    /// GLib warns about either, and the trap would carry nothing about it, so
    /// the ordinary release of a watched wrapper is walked here end to end.
    /// </summary>
    [Fact]
    public void AWatchedWrapperTakesItsWeakReferenceBackWhenItIsReleased()
    {
        List<Exception> failures = [];
        using FailureLog log = new(failures);

        bool detecting = OverUnrefDetector.Enabled;
        OverUnrefDetector.Enabled = true;

        Element element;

        try
        {
            element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "released-watched"));
        }
        finally
        {
            // Off again before the release: whether the weak reference is
            // removed is the wrapper's own business, not the switch's.
            OverUnrefDetector.Enabled = false;
        }

        try
        {
            // Removes the weak reference and then the toggle reference, which
            // kills the object. The death is the ordinary one, so nothing is
            // reported.
            element.Dispose();

            Assert.True(element.IsDisposed);

            lock (failures)
            {
                Assert.Empty(failures);
            }
        }
        finally
        {
            OverUnrefDetector.Enabled = detecting;
        }
    }

    /// <summary>
    /// Drops a reference without going through the binding, the way native code
    /// that unrefs one time too many does.
    /// </summary>
    /// <param name="instance">The object to unref.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    private static partial void RawUnref(nint instance);

    /// <summary>
    /// Runs the dispose chain of an object that is still referenced, the way
    /// an owner that breaks its cycles by hand does.
    /// </summary>
    /// <param name="instance">The object to dispose.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_run_dispose")]
    private static partial void RawRunDispose(nint instance);

    /// <summary>
    /// Collects what the exception trap reports for as long as it is alive.
    /// </summary>
    private sealed class FailureLog : IDisposable
    {
        private readonly List<Exception> _failures;

        internal FailureLog(List<Exception> failures)
        {
            _failures = failures;
            ExceptionTrap.UnhandledException += OnFailure;
        }

        public void Dispose() => ExceptionTrap.UnhandledException -= OnFailure;

        private void OnFailure(Exception exception)
        {
            lock (_failures)
            {
                _failures.Add(exception);
            }
        }
    }
}
