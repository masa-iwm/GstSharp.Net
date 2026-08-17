using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// <see cref="NativeLoader.RegisterLibrary"/>, the half of the module SPI that
/// lets a binding module outside this repository name the library it imports
/// from.
/// </summary>
/// <remarks>
/// Nothing here loads a native library. Registration only writes to the name
/// table of the loader, which is what makes these tests runnable on a machine
/// without GStreamer; that the registered name then resolves out of the pinned
/// installation is asserted by the integration suite.
/// </remarks>
public sealed class NativeLoaderRegistrationTests
{
    private static NativeLibraryNames Names(string stem) => new()
    {
        Linux = $"lib{stem}.so.0",
        MacOs = $"lib{stem}.0.dylib",
        WindowsMsvc = $"{stem}-0.dll",
        WindowsMinGW = $"lib{stem}-0.dll",
    };

    /// <summary>
    /// A module may not redirect one of the libraries this repository binds,
    /// because every module in the process shares one installation and one name
    /// space.
    /// </summary>
    [Theory]
    [InlineData("Gst")]
    [InlineData("GLib")]
    [InlineData("GObject")]
    [InlineData("GstBase")]
    public void TheBuiltInNamesCannotBeShadowed(string logicalName)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => NativeLoader.RegisterLibrary(logicalName, Names("gstshadow")));

        Assert.Equal("logicalName", error.ParamName);
    }

    /// <summary>
    /// Two assemblies that import from the same library both register it, and
    /// the second registration has to be a no-op rather than a conflict.
    /// </summary>
    [Fact]
    public void RegisteringTheSameLibraryTwiceDoesNothing()
    {
        NativeLoader.RegisterLibrary("GstSharpTestIdempotent", Names("gstsharptestidempotent"));
        NativeLoader.RegisterLibrary("GstSharpTestIdempotent", Names("gstsharptestidempotent"));
    }

    /// <summary>
    /// One logical name stands for one library in a process, so a second answer
    /// is refused instead of silently winning or silently losing.
    /// </summary>
    [Fact]
    public void RegisteringTheSameNameForADifferentLibraryIsRefused()
    {
        NativeLoader.RegisterLibrary("GstSharpTestConflict", Names("gstsharptestconflict"));

        Assert.Throws<InvalidOperationException>(
            () => NativeLoader.RegisterLibrary("GstSharpTestConflict", Names("gstsharptestother")));
    }

    /// <summary>
    /// The directory is the installation the loader pinned, so a file name that
    /// carries one is refused rather than combined into a path that reaches
    /// outside it.
    /// </summary>
    [Theory]
    [InlineData("/usr/lib/libgstsharptest.so.0", "libgstsharptest.0.dylib", "gstsharptest-0.dll", "libgstsharptest-0.dll")]
    [InlineData("libgstsharptest.so.0", "../libgstsharptest.0.dylib", "gstsharptest-0.dll", "libgstsharptest-0.dll")]
    [InlineData("libgstsharptest.so.0", "libgstsharptest.0.dylib", @"C:\gst\gstsharptest-0.dll", "libgstsharptest-0.dll")]
    [InlineData("libgstsharptest.so.0", "libgstsharptest.0.dylib", "gstsharptest-0.dll", "")]
    public void AFileNameThatIsAPathIsRefused(string linux, string macOs, string msvc, string minGW)
    {
        Assert.Throws<ArgumentException>(() => NativeLoader.RegisterLibrary(
            "GstSharpTestPath",
            new NativeLibraryNames
            {
                Linux = linux,
                MacOs = macOs,
                WindowsMsvc = msvc,
                WindowsMinGW = minGW,
            }));
    }

    /// <summary>
    /// A name that nobody registered stays unknown, and the failure says what to
    /// do about it.
    /// </summary>
    [Fact]
    public void AnUnregisteredNameIsNotLoadable()
    {
        GstNativeLoadException error = Assert.Throws<GstNativeLoadException>(
            () => NativeLoader.Load("GstSharpTestUnregistered"));

        Assert.Contains("RegisterLibrary", error.Message, StringComparison.Ordinal);
    }
}
