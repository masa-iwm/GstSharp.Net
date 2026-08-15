namespace Gst.Interop;

/// <summary>
/// The build flavor of a GStreamer installation on Windows.
/// </summary>
/// <remarks>
/// The two official Windows builds use different file names for the very same
/// libraries (<c>gstreamer-1.0-0.dll</c> versus
/// <c>libgstreamer-1.0-0.dll</c>) and are not interchangeable, so
/// <see cref="NativeLoader"/> pins one flavor for the whole process.
/// </remarks>
public enum GstFlavor
{
    /// <summary>
    /// The MSVC build, which is also what the official
    /// <c>gstreamer-1.0-msvc-x86_64</c> installer ships.
    /// </summary>
    Msvc,

    /// <summary>
    /// The MinGW build, which also covers the MSYS2 packages.
    /// </summary>
    MinGW,
}
