using System.Runtime.InteropServices;
using Gst;
using Gst.Interop;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The detector of an over-unref: a GObject that dies while the binding still
/// holds its toggle reference is reported through the exception trap, from
/// inside the unref that killed it, instead of crashing the next release.
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
        Element element = Assert.IsAssignableFrom<Element>(ElementFactory.Make("fakesink", "over-unref"));

        // Read once, before the object dies: the wrapper is not disposed, so it
        // would hand the dangling pointer out happily afterwards.
        nint handle = element.Handle;

        List<Exception> failures = [];

        void OnFailure(Exception exception)
        {
            lock (failures)
            {
                failures.Add(exception);
            }
        }

        ExceptionTrap.UnhandledException += OnFailure;

        try
        {
            // The reference the wrapper owns is the only one left, so this one
            // is the over-unref: it runs the dispose chain of the object while
            // the toggle reference is installed.
            RawUnref(handle);

            lock (failures)
            {
                Exception failure = Assert.Single(failures);
                Assert.IsType<InvalidOperationException>(failure);
                Assert.Contains("over-unref", failure.Message, StringComparison.Ordinal);
                Assert.Contains("GstFakeSink", failure.Message, StringComparison.Ordinal);
            }

            // The toggle reference is marked released already, so this performs
            // the bookkeeping only and calls nothing on the dead object.
            element.Dispose();

            lock (failures)
            {
                Assert.Single(failures);
            }
        }
        finally
        {
            ExceptionTrap.UnhandledException -= OnFailure;
        }
    }

    /// <summary>
    /// Drops a reference without going through the binding, the way native code
    /// that unrefs one time too many does.
    /// </summary>
    /// <param name="instance">The object to unref.</param>
    [LibraryImport("GObject", EntryPoint = "g_object_unref")]
    private static partial void RawUnref(nint instance);
}
