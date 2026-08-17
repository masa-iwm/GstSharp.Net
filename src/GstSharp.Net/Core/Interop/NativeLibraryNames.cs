namespace Gst.Interop;

/// <summary>
/// The file name of one native library on every platform the bindings run on.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="NativeLoader.RegisterLibrary"/> takes: a binding
/// module outside this repository names the library it imports from once, and
/// the loader then resolves it out of the same GStreamer installation as the
/// core libraries. The four names are the four spellings the same library has —
/// the ELF <c>SONAME</c>, the macOS file name, and the two Windows builds, which
/// differ only in whether the <c>lib</c> prefix survived.
/// </para>
/// <para>
/// Every name is a bare file name. The directory is the loader's business: it
/// is the installation that was pinned when the first module was loaded, and
/// naming a directory here would defeat that.
/// </para>
/// <example>
/// <code>
/// NativeLoader.RegisterLibrary("GstController", new NativeLibraryNames
/// {
///     Linux = "libgstcontroller-1.0.so.0",
///     MacOs = "libgstcontroller-1.0.0.dylib",
///     WindowsMsvc = "gstcontroller-1.0-0.dll",
///     WindowsMinGW = "libgstcontroller-1.0-0.dll",
/// });
/// </code>
/// </example>
/// </remarks>
public sealed class NativeLibraryNames
{
    /// <summary>
    /// Gets the <c>SONAME</c> used on Linux and the other ELF platforms, for
    /// example <c>libgstcontroller-1.0.so.0</c>.
    /// </summary>
    public required string Linux { get; init; }

    /// <summary>
    /// Gets the file name used on macOS, for example
    /// <c>libgstcontroller-1.0.0.dylib</c>.
    /// </summary>
    public required string MacOs { get; init; }

    /// <summary>
    /// Gets the file name of the MSVC build on Windows, for example
    /// <c>gstcontroller-1.0-0.dll</c>. The MSVC build drops the <c>lib</c>
    /// prefix.
    /// </summary>
    public required string WindowsMsvc { get; init; }

    /// <summary>
    /// Gets the file name of the MinGW build on Windows, for example
    /// <c>libgstcontroller-1.0-0.dll</c>. The MinGW build keeps the <c>lib</c>
    /// prefix.
    /// </summary>
    public required string WindowsMinGW { get; init; }
}
