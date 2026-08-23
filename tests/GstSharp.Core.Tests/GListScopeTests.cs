using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The list factories, in the half that holds without a GStreamer
/// installation: what a null or empty sequence answers, what a null element
/// costs, and that disposing a scope twice does nothing.
/// </summary>
/// <remarks>
/// This project is native free by construction — the CI job that runs it has no
/// GStreamer at all — so everything here stops before the first call into GLib.
/// The empty sequence reaches that line honestly: the spine of an empty list is
/// the null pointer and no allocation is made for it, and a null element is
/// rejected at the position it sits at, which for these fixtures is the first,
/// so nothing has been allocated by the time the throw happens. The other half
/// of the feature — a spine that is really built, walked and released, and the
/// singly linked twin — lives in
/// <c>GstSharp.IntegrationTests.ListArgumentTests</c>, because building a list
/// is <c>g_list_prepend</c> and cannot be faked.
/// </remarks>
public sealed class GListScopeTests
{
    /// <summary>
    /// <c>NULL</c> is how C spells the empty list, so a null sequence and an
    /// empty one are the same value and neither allocates anything.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANullOrEmptySequenceOfStringsIsTheNullPointer(bool singly)
    {
        using (GListScope missing = GMarshal.AllocList((IEnumerable<string>?)null, singly))
        {
            Assert.Equal(nint.Zero, missing.Head);
        }

        using GListScope empty = GMarshal.AllocList(Array.Empty<string>(), singly);
        Assert.Equal(nint.Zero, empty.Head);
    }

    /// <summary>The same for a sequence of wrappers.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANullOrEmptySequenceOfHandlesIsTheNullPointer(bool singly)
    {
        using (GListScope missing = GMarshal.AllocList((IEnumerable<Gst.GObject.Object>?)null, singly))
        {
            Assert.Equal(nint.Zero, missing.Head);
        }

        using GListScope empty = GMarshal.AllocList(Array.Empty<Gst.GObject.Object>(), singly);
        Assert.Equal(nint.Zero, empty.Head);
    }

    /// <summary>
    /// The consumed direction answers the same value, which is what makes
    /// <c>null</c> the documented way to clear the headers of an encoder or the
    /// path segments of a URI.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ANullOrEmptySequenceIsHandedOverAsTheNullPointer(bool singly)
    {
        Assert.Equal(nint.Zero, GMarshal.ConsumeList((IEnumerable<string>?)null, singly));
        Assert.Equal(nint.Zero, GMarshal.ConsumeList(Array.Empty<string>(), singly));
        Assert.Equal(nint.Zero, GMarshal.ConsumeList((IEnumerable<Gst.MiniObject>?)null, singly));
        Assert.Equal(nint.Zero, GMarshal.ConsumeList(Array.Empty<Gst.MiniObject>(), singly));
    }

    /// <summary>
    /// Disposing a scope a second time does nothing: the fields are cleared by
    /// the first call, so nothing is freed twice.
    /// </summary>
    [Fact]
    public void DisposingTwiceDoesNothing()
    {
        GListScope scope = GMarshal.AllocList(Array.Empty<string>(), singly: false);

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(nint.Zero, scope.Head);
    }

    /// <summary>
    /// A null element is refused and the message names the position, because a
    /// sequence a caller built out of a query has no other way of saying which
    /// entry is wrong.
    /// </summary>
    [Fact]
    public void ANullStringElementIsRefusedByIndex()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(Encode);

        Assert.Equal("values", error.ParamName);
        Assert.Contains("index 0", error.Message, StringComparison.Ordinal);

        static void Encode()
        {
            using GListScope scope = GMarshal.AllocList(new[] { (string)null! }, singly: false);
        }
    }

    /// <summary>The same rule on the consuming side.</summary>
    [Fact]
    public void ANullStringElementIsRefusedByIndexWhenConsumed()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GMarshal.ConsumeList(new[] { (string)null! }, singly: false));

        Assert.Equal("values", error.ParamName);
        Assert.Contains("index 0", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And on a sequence of wrappers, where the refusal happens before any
    /// handle is read.
    /// </summary>
    [Fact]
    public void ANullHandleElementIsRefusedByIndex()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(Encode);

        Assert.Equal("items", error.ParamName);
        Assert.Contains("index 0", error.Message, StringComparison.Ordinal);

        static void Encode()
        {
            using GListScope scope = GMarshal.AllocList(
                new[] { (Gst.GObject.Object)null! },
                singly: false);
        }
    }

    /// <summary>
    /// A string with a null character in it is rejected the way every other
    /// string the binding copies is, before anything native is allocated.
    /// </summary>
    [Fact]
    public void AnEmbeddedNullIsRefused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(Encode);

        Assert.Equal("value", error.ParamName);

        static void Encode()
        {
            using GListScope scope = GMarshal.AllocList(new[] { "audio/x-raw\0" }, singly: false);
        }
    }
}
