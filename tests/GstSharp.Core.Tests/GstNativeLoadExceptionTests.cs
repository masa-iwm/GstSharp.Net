using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The diagnostics of a failed load.
/// </summary>
public class GstNativeLoadExceptionTests
{
    [Fact]
    public void TheMessageListsEveryCandidate()
    {
        string[] attempts =
        [
            @"C:\gstreamer\1.0\msvc_x86_64\bin\gstreamer-1.0-0.dll (the registry entry ""GStreamer 1.0 (MSVC x86_64)"")",
            @"libgstreamer-1.0-0.dll (the process search path)",
        ];

        GstNativeLoadException exception = new("Gst", attempts);

        Assert.Equal("Gst", exception.LogicalName);
        Assert.Equal(attempts, exception.AttemptedPaths);

        foreach (string attempt in attempts)
        {
            Assert.Contains(attempt, exception.Message, StringComparison.Ordinal);
        }

        Assert.Contains("NativeLoader.Configure", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ItIsADllNotFoundException()
    {
        GstNativeLoadException exception = new("Gst", []);

        Assert.IsAssignableFrom<DllNotFoundException>(exception);
        Assert.Contains("No installation candidate", exception.Message, StringComparison.Ordinal);
    }
}
