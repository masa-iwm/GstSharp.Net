using Gst.GObject;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// What a generated field accessor of a boxed record rests on: the wrapper
/// hands out its handle only while it still owns a value.
/// </summary>
/// <remarks>
/// <para>
/// The generated accessors read <c>((XRaw*)Handle)-&gt;Field</c>, exactly the
/// way every generated instance method reads the handle before a call. That is
/// the whole of the disposed check, and it belongs here rather than beside the
/// generated code: it needs no GStreamer installation, and it is the property
/// of the runtime that the generator relies on.
/// </para>
/// <para>
/// What is testable without GStreamer is the half of it that does not need a
/// value: a wrapper that holds no handle throws rather than dereference zero.
/// A wrapper that owns a value and is then disposed reaches the same state by
/// the other road, and that road runs through <c>g_boxed_free</c>, so it is
/// covered where a library exists to free into —
/// <c>RecordFieldAccessorTests.ADisposedBoxedWrapperRefusesToReadItsFields</c>
/// in the integration tests.
/// </para>
/// </remarks>
public class BoxedFieldReadTests
{
    [Fact]
    public void AWrapperWithoutAValueThrowsInsteadOfDereferencingZero()
    {
        // The wrapper never held a value, which is the state disposing leaves
        // one in: Boxed keeps no flag of its own, the null handle is the
        // disposed state. Calling Dispose here would free nothing and change
        // nothing, so the read is what the test is about.
        ProbeBoxed boxed = new();

        ObjectDisposedException error = Assert.Throws<ObjectDisposedException>(() => boxed.ReadFirstWord());
        Assert.Contains(nameof(ProbeBoxed), error.Message, StringComparison.Ordinal);
        Assert.True(boxed.IsDisposed);
    }

    /// <summary>
    /// A boxed wrapper over the null pointer. Nothing native is reachable from
    /// it: the constructor copies nothing because ownership is transferred, and
    /// disposing frees nothing because there is nothing to free.
    /// </summary>
    private sealed unsafe class ProbeBoxed() : Boxed(nint.Zero, GType.Boxed, Transfer.Full)
    {
        /// <summary>Reads the first word of the value, the way a generated accessor does.</summary>
        /// <returns>The word.</returns>
        internal int ReadFirstWord()
        {
            int value = *(int*)Handle;
            GC.KeepAlive(this);
            return value;
        }
    }
}
