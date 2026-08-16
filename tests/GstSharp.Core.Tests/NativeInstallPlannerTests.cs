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
    private const string UnixApplicationDirectory = "/opt/recorder";

    [Fact]
    public void EnvironmentVariableComesFirst()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_X86_64", @"C:\gstreamer\1.0\msvc_x86_64")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(GstFlavor.Msvc, candidates[0].Flavor);
        Assert.Equal(@"C:\gstreamer\1.0\msvc_x86_64\bin", candidates[0].Directory);
        Assert.Equal(GstInstallOrigin.EnvironmentVariable, candidates[0].Origin);
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
        string bin = LocalAppData + @"\Programs\gstreamer\1.0\mingw_x86_64\bin";

        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("LOCALAPPDATA", LocalAppData)
            .WithRegistryEntry(
                "GStreamer 1.0 (MinGW x86_64) version 1.28.6",
                LocalAppData + @"\Programs\gstreamer\1.0\mingw_x86_64\")
            .WithInstallation(bin, GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
        Assert.Equal(bin, candidates[0].Directory);
        Assert.Equal(GstInstallOrigin.Registry, candidates[0].Origin);
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
        Assert.Equal(GstInstallOrigin.ConfiguredSearchPath, candidates[0].Origin);

        // The flavor of the configured directory is taken from what is in it.
        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
    }

    /// <summary>
    /// An MSYS2 prefix that is on the search path is found by the search path
    /// scan, which runs before the MSYS2 stage, so it is offered exactly once.
    /// </summary>
    [Fact]
    public void Msys2OnThePathIsFoundOnceByThePathScan()
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
        Assert.Equal(GstInstallOrigin.PathDirectory, found.Origin);
        Assert.Contains(@"C:\src\msys64\ucrt64\bin", found.Source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Putting an installation in front of the search path is how the Windows
    /// tools of GStreamer are pointed at one, so the scan of the search path
    /// runs before every other implicit stage. A directory that only holds a
    /// copy of GLib is not one.
    /// </summary>
    [Fact]
    public void ThePathScanBeatsTheEnvironmentVariableAndSkipsGLibOnlyDirectories()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"C:\tools\pango\bin")
            .WithGLibOnly(@"C:\tools\pango\bin")
            .WithPathEntry(@"D:\gst-mingw\bin")
            .WithInstallation(@"D:\gst-mingw\bin", GstFlavor.MinGW)
            .WithEnvironment("GSTREAMER_1_0_ROOT_MSVC_X86_64", @"C:\gstreamer\1.0\msvc_x86_64")
            .WithInstallation(@"C:\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(@"D:\gst-mingw\bin", candidates[0].Directory);
        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
        Assert.Equal(GstInstallOrigin.PathDirectory, candidates[0].Origin);

        Assert.Equal(@"C:\gstreamer\1.0\msvc_x86_64\bin", candidates[1].Directory);
        Assert.Equal(GstInstallOrigin.EnvironmentVariable, candidates[1].Origin);

        Assert.DoesNotContain(
            candidates,
            candidate => string.Equals(candidate.Directory, @"C:\tools\pango\bin", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The position on the search path is the priority the user expressed, so
    /// it decides between directories; the flavor preference only decides
    /// inside one of them.
    /// </summary>
    [Fact]
    public void ThePathOrderOutranksTheFlavorPreference()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"D:\gst-mingw\bin")
            .WithPathEntry(@"D:\gst-msvc\bin")
            .WithInstallation(@"D:\gst-mingw\bin", GstFlavor.MinGW)
            .WithInstallation(@"D:\gst-msvc\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(@"D:\gst-mingw\bin", candidates[0].Directory);
        Assert.Equal(GstFlavor.MinGW, candidates[0].Flavor);
        Assert.Equal(@"D:\gst-msvc\bin", candidates[1].Directory);
        Assert.Equal(GstFlavor.Msvc, candidates[1].Flavor);
        Assert.All(
            candidates.Take(2),
            candidate => Assert.Equal(GstInstallOrigin.PathDirectory, candidate.Origin));

        // The bare file name stays the last resort behind all of it.
        Assert.Null(candidates[^1].Directory);
        Assert.Equal(GstInstallOrigin.ProcessSearchPath, candidates[^1].Origin);
    }

    [Fact]
    public void MsvcWinsInsideOnePathDirectory()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"D:\gst-both\bin")
            .WithInstallation(@"D:\gst-both\bin", GstFlavor.MinGW)
            .WithInstallation(@"D:\gst-both\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(@"D:\gst-both\bin", candidates[0].Directory);
        Assert.Equal(GstFlavor.Msvc, candidates[0].Flavor);
        Assert.Equal(@"D:\gst-both\bin", candidates[1].Directory);
        Assert.Equal(GstFlavor.MinGW, candidates[1].Flavor);
    }

    [Fact]
    public void TheConfiguredFlavorRemovesThePathDirectoriesOfTheOtherOne()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithPathEntry(@"D:\gst-mingw\bin")
            .WithPathEntry(@"D:\gst-msvc\bin")
            .WithInstallation(@"D:\gst-mingw\bin", GstFlavor.MinGW)
            .WithInstallation(@"D:\gst-msvc\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates =
            NativeInstallPlanner.EnumerateWindows(probe, null, GstFlavor.Msvc);

        Assert.All(candidates, candidate => Assert.Equal(GstFlavor.Msvc, candidate.Flavor));
        Assert.Equal(@"D:\gst-msvc\bin", candidates[0].Directory);
        Assert.DoesNotContain(
            candidates,
            candidate => string.Equals(candidate.Directory, @"D:\gst-mingw\bin", StringComparison.OrdinalIgnoreCase));
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
            .WithInstallation(LocalAppData + @"\Programs\gstreamer\1.0\msvc_x86_64\bin", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(
            LocalAppData + @"\Programs\gstreamer\1.0\msvc_x86_64\bin",
            candidates[0].Directory);
        Assert.Equal(GstInstallOrigin.DefaultInstallDirectory, candidates[0].Origin);
    }

    [Fact]
    public void AnEmptyMachineStillFallsBackToTheSearchPath()
    {
        FakePlatformProbe probe = new();

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Null(candidate.Directory));
        Assert.All(candidates, candidate => Assert.Equal(GstInstallOrigin.ProcessSearchPath, candidate.Origin));
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

    /// <summary>
    /// An application that ships GStreamer next to itself has to be found after
    /// every installed copy, so that a machine wide installation still wins, but
    /// before the process search path, which is the last resort.
    /// </summary>
    [Fact]
    public void TheBundledRuntimeSitsBetweenMsys2AndTheSearchPath()
    {
        string bundle = FakePlatformProbe.BundledDirectory("win-x64");

        FakePlatformProbe probe = new FakePlatformProbe()
            .WithEnvironment("MSYSTEM_PREFIX", @"C:\msys64\ucrt64")
            .WithInstallation(@"C:\msys64\ucrt64\bin", GstFlavor.MinGW)
            .WithBundledRuntime("win-x64", GstFlavor.Msvc)
            .WithBundledRuntime("win-x64", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        int msys2 = LastIndexOf(candidates, candidate => candidate.Origin == GstInstallOrigin.Msys2);
        int msvc = IndexOf(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime && candidate.Flavor == GstFlavor.Msvc);
        int minGW = IndexOf(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime && candidate.Flavor == GstFlavor.MinGW);
        int searchPath = IndexOf(candidates, candidate => candidate.Origin == GstInstallOrigin.ProcessSearchPath);

        Assert.True(msys2 >= 0 && msys2 < msvc, "The MSYS2 installation has to come first.");
        Assert.True(msvc < minGW, "MSVC is the preferred flavor of the bundled runtime.");
        Assert.True(minGW < searchPath, "The process search path stays the last resort.");

        Assert.Equal(bundle, candidates[msvc].Directory);
        Assert.Equal(bundle, candidates[minGW].Directory);
        Assert.Contains("bundled runtime", candidates[msvc].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void ABundledRuntimeOfOneFlavorIsFound()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithBundledRuntime("win-x64", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        NativeInstall found = Assert.Single(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);

        Assert.Equal(GstFlavor.MinGW, found.Flavor);
        Assert.Equal(FakePlatformProbe.BundledDirectory("win-x64"), found.Directory);
    }

    [Fact]
    public void AMissingBundledRuntimeIsNotACandidate()
    {
        FakePlatformProbe probe = new();

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.DoesNotContain(candidates, candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);
    }

    [Fact]
    public void ABundledRuntimeWithoutGstreamerIsNotACandidate()
    {
        // A bundle directory that only holds a copy of GLib, as a dependency of
        // something else entirely, is not a GStreamer installation and must not
        // pin one.
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithGLibOnly(FakePlatformProbe.BundledDirectory("win-x64"));

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.DoesNotContain(candidates, candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);
    }

    /// <summary>
    /// The runtime identifier of the process can be more specific than the one
    /// the published tree is named after, so both are probed.
    /// </summary>
    [Fact]
    public void ASpecificRuntimeIdentifierFallsBackToThePortableOne()
    {
        FakePlatformProbe probe = new FakePlatformProbe
        {
            RuntimeIdentifier = "win10-x64",
        }
            .WithBundledRuntime("win-x64", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        NativeInstall found = Assert.Single(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);

        Assert.Equal(FakePlatformProbe.BundledDirectory("win-x64"), found.Directory);
    }

    [Fact]
    public void TheRuntimeIdentifierOfTheProcessIsProbedFirst()
    {
        FakePlatformProbe probe = new FakePlatformProbe
        {
            RuntimeIdentifier = "win10-x64",
        }
            .WithBundledRuntime("win10-x64", GstFlavor.Msvc)
            .WithBundledRuntime("win-x64", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        string[] bundled = [.. candidates
            .Where(candidate => candidate.Origin == GstInstallOrigin.BundledRuntime)
            .Select(candidate => candidate.Directory!)];

        Assert.Equal(
            [FakePlatformProbe.BundledDirectory("win10-x64"), FakePlatformProbe.BundledDirectory("win-x64")],
            bundled);
    }

    [Fact]
    public void ThePortableRuntimeIdentifierIsOnlyProbedOnce()
    {
        // The default runtime identifier of the fake machine is the portable
        // one, so the architecture derived candidate is the very same directory.
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithBundledRuntime("win-x64", GstFlavor.Msvc);

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        Assert.Single(candidates, candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);
    }

    [Fact]
    public void TheConfiguredFlavorRemovesTheOtherBundledOne()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithBundledRuntime("win-x64", GstFlavor.Msvc)
            .WithBundledRuntime("win-x64", GstFlavor.MinGW);

        IReadOnlyList<NativeInstall> candidates =
            NativeInstallPlanner.EnumerateWindows(probe, null, GstFlavor.MinGW);

        NativeInstall found = Assert.Single(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);

        Assert.Equal(GstFlavor.MinGW, found.Flavor);
    }

    /// <summary>
    /// NuGet puts the native assets of a package under
    /// <c>runtimes/{rid}/native</c>, so that layout is probed as well.
    /// </summary>
    [Fact]
    public void TheNativeLayoutOfTheBundleIsProbedAfterTheBinLayout()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithBundledRuntime("win-x64", GstFlavor.Msvc)
            .WithBundledRuntime("win-x64", GstFlavor.Msvc, "native");

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        string[] bundled = [.. candidates
            .Where(candidate => candidate.Origin == GstInstallOrigin.BundledRuntime)
            .Select(candidate => candidate.Directory!)];

        Assert.Equal(
            [FakePlatformProbe.BundledDirectory("win-x64"), FakePlatformProbe.BundledDirectory("win-x64", "native")],
            bundled);
    }

    [Fact]
    public void TheNativeLayoutIsFoundWhenThereIsNoBinDirectory()
    {
        FakePlatformProbe probe = new FakePlatformProbe()
            .WithBundledRuntime("win-x64", GstFlavor.MinGW, "native");

        IReadOnlyList<NativeInstall> candidates = NativeInstallPlanner.EnumerateWindows(probe, null, null);

        NativeInstall found = Assert.Single(
            candidates,
            candidate => candidate.Origin == GstInstallOrigin.BundledRuntime);

        Assert.Equal(FakePlatformProbe.BundledDirectory("win-x64", "native"), found.Directory);
    }

    [Theory]
    [InlineData(Architecture.X64, "win-x64")]
    [InlineData(Architecture.Arm64, "win-arm64")]
    [InlineData(Architecture.X86, "win-x86")]
    public void RuntimeIdentifiersUseTheLowerCaseSdkTokens(Architecture architecture, string expected) =>
        Assert.Equal(expected, NativeInstallPlanner.WindowsRuntimeIdentifier(architecture));

    [Theory]
    [InlineData(Architecture.X64, false, "linux-x64")]
    [InlineData(Architecture.Arm64, false, "linux-arm64")]
    [InlineData(Architecture.X86, false, "linux-x86")]
    [InlineData(Architecture.X64, true, "osx-x64")]
    [InlineData(Architecture.Arm64, true, "osx-arm64")]
    public void UnixRuntimeIdentifiersNameTheOperatingSystem(
        Architecture architecture,
        bool isMacOs,
        string expected) =>
        Assert.Equal(expected, NativeInstallPlanner.UnixRuntimeIdentifier(architecture, isMacOs));

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

    [Fact]
    public void TheBundledRuntimeIsFoundOnLinux()
    {
        FakePlatformProbe probe = UnixProbe("linux-x64")
            .WithUnixInstallation(UnixBundledDirectory("linux-x64", "lib"), isMacOs: false);

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: false);

        Assert.Equal([null, UnixBundledDirectory("linux-x64", "lib")], directories);
    }

    [Fact]
    public void TheNativeLayoutOfTheBundleIsFoundOnLinux()
    {
        FakePlatformProbe probe = UnixProbe("linux-x64")
            .WithUnixInstallation(UnixBundledDirectory("linux-x64", "native"), isMacOs: false);

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: false);

        Assert.Equal(UnixBundledDirectory("linux-x64", "native"), directories[^1]);
    }

    /// <summary>
    /// The bundled tree is the last resort on macOS too: the framework and the
    /// package managers of the machine come first.
    /// </summary>
    [Fact]
    public void TheBundledRuntimeComesAfterTheKnownDirectoriesOnMacOs()
    {
        FakePlatformProbe probe = UnixProbe("osx-arm64", Architecture.Arm64)
            .WithUnixInstallation(UnixBundledDirectory("osx-arm64", "lib"), isMacOs: true);

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: true);

        Assert.Equal(UnixBundledDirectory("osx-arm64", "lib"), directories[^1]);
        Assert.Equal("/usr/local/lib", directories[^2]);
        Assert.Contains("/Library/Frameworks/GStreamer.framework/Versions/1.0/lib", directories);
    }

    [Fact]
    public void ABundledUnixTreeWithoutGstreamerIsNotACandidate()
    {
        FakePlatformProbe probe = UnixProbe("linux-x64")
            .WithUnixGLibOnly(UnixBundledDirectory("linux-x64", "lib"), isMacOs: false);

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: false);

        Assert.Equal([null], directories);
    }

    [Fact]
    public void ASpecificUnixRuntimeIdentifierFallsBackToThePortableOne()
    {
        FakePlatformProbe probe = UnixProbe("ubuntu.22.04-x64")
            .WithUnixInstallation(UnixBundledDirectory("linux-x64", "lib"), isMacOs: false);

        IReadOnlyList<string?> directories =
            NativeInstallPlanner.EnumerateUnixDirectories(probe, null, isMacOs: false);

        Assert.Equal([null, UnixBundledDirectory("linux-x64", "lib")], directories);
    }

    [Theory]
    [InlineData(Architecture.X64, "x86_64")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.X86, "x86")]
    public void ArchitectureTokensMatchTheInstallerNames(Architecture architecture, string expected) =>
        Assert.Equal(expected, NativeInstallPlanner.ArchitectureToken(architecture));

    /// <summary>
    /// Creates a machine whose application directory is a Unix one.
    /// </summary>
    /// <param name="runtimeIdentifier">The runtime identifier of the process.</param>
    /// <param name="architecture">The architecture of the operating system.</param>
    /// <returns>The probe.</returns>
    private static FakePlatformProbe UnixProbe(
        string runtimeIdentifier,
        Architecture architecture = Architecture.X64) =>
        new()
        {
            ApplicationBaseDirectory = UnixApplicationDirectory,
            RuntimeIdentifier = runtimeIdentifier,
            OSArchitecture = architecture,
        };

    private static string UnixBundledDirectory(string runtimeIdentifier, string layout) =>
        $"{UnixApplicationDirectory}/runtimes/{runtimeIdentifier}/{layout}";

    private static int IndexOf(IReadOnlyList<NativeInstall> candidates, Func<NativeInstall, bool> predicate)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (predicate(candidates[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static int LastIndexOf(IReadOnlyList<NativeInstall> candidates, Func<NativeInstall, bool> predicate)
    {
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            if (predicate(candidates[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
