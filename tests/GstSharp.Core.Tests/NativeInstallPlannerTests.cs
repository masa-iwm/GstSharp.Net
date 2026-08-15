using System.Runtime.InteropServices;
using Gst.Interop;
using Xunit;

namespace GstSharp.Core.Tests;

/// <summary>
/// The ordering rules of the Windows installation search.
/// </summary>
public class NativeInstallPlannerTests
{
    private const string LocalAppData = @"C:\Users\dev\AppData\Local";

    [Fact]
    public void EnvironmentVariableComesFirst()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_X86_64", @"C:\gstreamer\1.0\msvc_x86_64")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(GstFlavor.Msvc, candidates[0].Flavor);
        Assert.Equal(@"C:\gstreamer\1.0\msvc_x86_64\bin", candidates[0].Directory);
        Assert.Contains("GSTREAMER_1_0_ROOT_MSVC_X86_64", candidates[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Arm64UsesItsOwnEnvironmentVariable()
    {
        FakePlatformProbe probe = new FakePlatformProbe
        {
            OSArchitecture = Architecture.Arm64,
        }
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_ARM64", @"C:\gstreamer\1.0\msvc_arm64");

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(@"C:\gstreamer\1.0\msvc_arm64\bin", candidates[0].Directory);
    }

    /// <summary>
    /// The per user installer of this project registers itself in HKCU only and
    /// sets no environment variable, so the registry entry has to be enough.
    /// </summary>
    [Fact]
    public void RegistryEntryIsEnough()
    {
        string bin = Path.Combine(LocalAppData, @"Programs\gstreamer\1.0\mingw_x86_64\bin");

        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("LOCALAPPDATA", LocalAppData)
            .WithRegistryEntry(
                "GStreamer 1.0 (MinGW x86_64) version 1.28.6",
                Path.Combine(LocalAppData, @"Programs\gstreamer\1.0\mingw_x86_64\"))
            .WithInstallation(bin, GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
        Assert.Equal(bin, candidates[0].Directory);
        Assert.Contains("registry", candidates[0].Source, StringComparison.Ordinal);

        // The same directory is also a well known one, but it is only tried once.
        Assert.Single(candidates, candidate => string.Equals(candidate.Directory, bin, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RegistryEntryOfAnotherArchitectureIsIgnored()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithRegistryEntry("GStreamer 1.0 (MSVC arm64) version 1.28.6", @"C:\gstreamer\1.0\msvc_arm64\")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_arm64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.DoesNotContain(candidates, candidate => candidate.Directory is not null);
    }

    [Fact]
    public void MsvcWinsWhenBothFlavorsAreInstalled()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithRegistryEntry("GStreamer 1.0 (MinGW x86_64) version 1.28.6", @"C:\gstreamer\1.0\mingw_x86_64\")
            .WithRegistryEntry("GStreamer 1.0 (MSVC x86_64) version 1.28.6", @"C:\gstreamer\1.0\msvc_x86_64\")
            .WithInstallation(@"C:\gstreamer\1.0\mingw_x86_64\bin", GstFlavor.MinGW)
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(GstFlavor.Msvc, candidates[0].Flavor);
        Assert.Equal(@"C:\gstreamer\1.0\msvc_x86_64\bin", candidates[0].Directory);
        Assert.Equal(GstFlavor.MinGW, candidates[1].Flavor);
    }

    [Fact]
    public void ConfiguredFlavorRemovesTheOtherOne()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithRegistryEntry("GStreamer 1.0 (MinGW x86_64) version 1.28.6", @"C:\gstreamer\1.0\mingw_x86_64\")
            .WithRegistryEntry("GStreamer 1.0 (MSVC x86_64) version 1.28.6", @"C:\gstreamer\1.0\msvc_x86_64\")
            .WithInstallation(@"C:\gstreamer\1.0\mingw_x86_64\bin", GstFlavor.MinGW)
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates =
            NativeInstallPlanner.EnumerateWindows(probe, null, GstFlavor.MinGW);

        Assert.All(candidates, candidate => Assert.Equal(GstFlavor.MinGW, candidate.Flavor));
        Assert.Equal(@"C:\gstreamer\1.0\mingw_x86_64\bin", candidates[0].Directory);
    }

    [Fact]
    public void ConfiguredPathComesBeforeEverythingElse()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_X86_64", @"C:\gstreamer\1.0\msvc_x86_64")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc)
            .WithInstallation(@"D:\runtime\bin", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates =
            NativeInstallPlanner.EnumerateWindows(probe, @"D:\runtime\bin", null);

        Assert.Equal(@"D:\runtime\bin", candidates[0].Directory);

        // The flavor of the configured directory is taken from what is in it.
        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
    }

    [Fact]
    public void Msys2OnThePathIsFound()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"C:\Windows\system32")
            .WithPathEntry(@"C:\src\msys64\ucrt64\bin")
            .WithInstallation(@"C:\src\msys64\ucrt64\bin", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        NativeInstall found = Assert.Single(
            candidates,
            candidate => string.Equals(candidate.Directory, @"C:\src\msys64\ucrt64\bin", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(GstFlavor.MinGW, found.Flavor);
        Assert.Contains("MSYS2", found.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Msys2RootIsProbedForEveryPrefix()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"C:\src\msys64\usr\bin")
            .WithInstallation(@"C:\src\msys64\mingw64\bin", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Contains(
            candidates,
            candidate => string.Equals(candidate.Directory, @"C:\src\msys64\mingw64\bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Msys2WithoutGstreamerIsNotACandidate()
    {
        // MSYS2 ships GLib with dozens of unrelated packages, so a root that
        // only has GLib must not pin the installation.
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"C:\src\msys64\ucrt64\bin")
            .WithGLibOnly(@"C:\src\msys64\ucrt64\bin");

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.DoesNotContain(candidates, candidate => candidate.Directory is not null);
    }

    [Fact]
    public void MsystemPrefixIsUsed()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("MSYSTEM_PREFIX", @"C:\msys64\clang64")
            .WithInstallation(@"C:\msys64\clang64\bin", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(@"C:\msys64\clang64\bin", candidates[0].Directory);
        Assert.Contains("MSYSTEM_PREFIX", candidates[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void WellKnownDirectoriesAreProbed()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("LOCALAPPDATA", LocalAppData)
            .WithInstallation(Path.Combine(LocalAppData, @"Programs\gstreamer\1.0\msvc_x86_64\bin"), GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(
            Path.Combine(LocalAppData, @"Programs\gstreamer\1.0\msvc_x86_64\bin"),
            candidates[0].Directory);
    }

    [Fact]
    public void AnEmptyMachineStillFallsBackToTheSearchPath()
    {
        FakePlatformProbe probe = new();

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Null(candidate.Directory));
        Assert.Equal(GstFlavor.Msvc, candidates[0].Flavor);
        Assert.Equal(GstFlavor.MinGW, candidates[1].Flavor);
    }

    [Fact]
    public void TheSearchPathIsAlwaysTheLastResort()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_X86_64", @"C:\gstreamer\1.0\msvc_x86_64")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Null(candidates[^1].Directory);
        Assert.Null(candidates[^2].Directory);
    }

    [Fact]
    public void UnixDirectoriesEndWithTheHomebrewPrefixOnMacOs()
    {
        FakePlatformProbe probe = new();

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: true);

        Assert.Null(directories[0]);
        Assert.Equal("/Library/Frameworks/GStreamer.framework/Versions/1.0/lib", directories[1]);
        Assert.Contains("/opt/homebrew/lib", directories);
        Assert.Contains("/usr/local/lib", directories);
    }

    [Fact]
    public void UnixUsesTheConfiguredDirectoryFirst()
    {
        FakePlatformProbe probe = new();

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, "/opt/gstreamer/lib", isMacOs: false);

        Assert.Equal("/opt/gstreamer/lib", directories[0]);
        Assert.Null(directories[1]);
        Assert.Equal(2, directories.Count);
    }

    [Theory]
    [InlineData(Architecture.X64, "x86_64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, "x86")]
    public void ArchitectureTokensMatchTheInstallerNames(Architecture architecture, string expected) =>
        Assert.Equal(expected, NativeInstallPlanner.ArchitectureToken(architecture));
}
