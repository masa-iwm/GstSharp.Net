using Gst.GLib;
using Gst.Interop;
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
}
