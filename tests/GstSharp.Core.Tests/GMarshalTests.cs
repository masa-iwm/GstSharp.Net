using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The string conversions that hold without a GStreamer installation.
/// </summary>
public class GMarshalTests
{
    /// <summary>
    /// A C string ends at the first null byte, so a managed string that
    /// contains one would arrive in native code truncated at that point. Half a
    /// caps description or half a property name is refused rather than passed
    /// on.
    /// </summary>
    [Fact]
    public void StackUtf8RejectsAnEmbeddedNull()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(Encode);
        Assert.Equal("value", error.ParamName);

        static void Encode()
        {
            Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
            using Utf8Scope scope = GMarshal.StackUtf8("video/x-raw\0,format=NV12", buffer);
        }
    }

    /// <summary>
    /// The same rule for the strings that are copied into a buffer native code
    /// takes over.
    /// </summary>
    [Fact]
    public void StringToUtf8PtrRejectsAnEmbeddedNull()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => GMarshal.StringToUtf8Ptr("--gst-debug\0=3"));

        Assert.Equal("value", error.ParamName);
    }

    /// <summary>
    /// A null string is not a string with a null in it: it stays the way to say
    /// "no string at all".
    /// </summary>
    [Fact]
    public unsafe void NullIsStillAllowed()
    {
        Assert.Equal(nint.Zero, GMarshal.StringToUtf8Ptr(null));

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(null, buffer);
        Assert.True(scope.Pointer is null);
    }
}
