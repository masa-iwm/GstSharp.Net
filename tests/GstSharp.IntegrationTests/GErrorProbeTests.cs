using Gst;
using Gst.GLib;
using Gst.Interop;
using Gst.Pbutils;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Checks that a <c>GError</c> produced by the library arrives as a
/// <see cref="GException"/> with its domain, code and message intact.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class GErrorProbeTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public GErrorProbeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A pipeline description that names an element which does not exist fails
    /// with <c>GST_PARSE_ERROR_NO_SUCH_ELEMENT</c>, which is the cheapest way
    /// to get a real <c>GError</c> out of GStreamer.
    /// </summary>
    [Fact]
    public unsafe void ParseLaunchErrorBecomesAGException()
    {
        nint error = nint.Zero;
        nint pipeline;

        Span<byte> stack = stackalloc byte[128];
        using (Utf8Scope description = GMarshal.StackUtf8("no-such-element-gstsharp ! fakesink", stack))
        {
            pipeline = TestNatives.ParseLaunch(description.Pointer, &error);
        }

        // A recoverable parse error hands a pipeline back together with the
        // error, so the return value is released either way.
        if (pipeline != nint.Zero)
        {
            TestNatives.ObjectUnref(pipeline);
        }

        Assert.NotEqual(nint.Zero, error);

        GException? caught = null;
        try
        {
            GException.ThrowIfSet(ref error);
        }
        catch (GException failure)
        {
            caught = failure;
        }

        Assert.NotNull(caught);
        _output.WriteLine(FormattableString.Invariant(
            $"gst_parse_launch: domain={caught.Domain.Value} ({caught.Domain}), code={caught.Code}, message={caught.Message}"));

        Assert.NotEqual(Quark.Zero, caught.Domain);
        Assert.False(string.IsNullOrWhiteSpace(caught.Message));

        // ThrowIfSet owns the GError: it frees it and clears the pointer, so
        // the test does not have to free it a second time.
        Assert.Equal(nint.Zero, error);
    }

    /// <summary>
    /// A discovery that fails is the live case of a call that sets its
    /// <c>GError</c> and returns an object it transferred all the same. The
    /// generated member releases that object before it raises the error, and
    /// this is the path that runs it.
    /// </summary>
    /// <remarks>
    /// What the assertion can see is that the error still arrives whole and
    /// that releasing the result did not take the process with it. The release
    /// itself is not observable from outside: the handle never leaves the
    /// method, so there is no wrapper to weakly reference and no reference
    /// count to read. That the release is emitted at all is pinned in the
    /// generator tests instead.
    /// </remarks>
    [Fact]
    public void ADiscoveryThatFailsReleasesTheInformationItStillReturned()
    {
        GstPbutils.Initialize();

        using Discoverer discoverer = Discoverer.New(ClockTime.FromSeconds(5));

        // An unknown URI scheme reaches no element, so the run posts an error
        // on its bus; gst_discoverer_discover_uri reports that through the
        // GError and hands the information object over regardless.
        GException failure = Assert.Throws<GException>(
            () => discoverer.DiscoverUri("gstsharp-no-such-scheme:///nothing"));

        _output.WriteLine(FormattableString.Invariant(
            $"gst_discoverer_discover_uri: domain={failure.Domain.Value} ({failure.Domain}), code={failure.Code}, message={failure.Message}"));

        Assert.NotEqual(Quark.Zero, failure.Domain);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));

        // The same call again is the cheapest check that nothing was left in a
        // broken state by the release on the failed path.
        Assert.Throws<GException>(() => discoverer.DiscoverUri("gstsharp-no-such-scheme:///nothing"));
    }
}
