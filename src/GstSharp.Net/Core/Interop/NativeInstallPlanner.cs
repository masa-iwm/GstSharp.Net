using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Gst.Interop;

/// <summary>
/// One candidate installation that <see cref="NativeLoader"/> can load the
/// native modules from.
/// </summary>
/// <param name="Flavor">The build flavor of the installation.</param>
/// <param name="Directory">
/// The directory that holds the DLLs, or <see langword="null"/> to load by
/// plain file name and let the operating system search for it.
/// </param>
/// <param name="Origin">The stage of the search that produced the candidate.</param>
/// <param name="Source">A human readable description of where the candidate came from.</param>
internal readonly record struct NativeInstall(
    GstFlavor Flavor,
    string? Directory,
    GstInstallOrigin Origin,
    string Source);

/// <summary>
/// Decides in which order GStreamer installations are tried.
/// </summary>
/// <remarks>
/// The class is pure: it only looks at the machine through
/// <see cref="IPlatformProbe"/> and never loads anything, so the ordering rules
/// can be unit tested on a machine without GStreamer.
/// </remarks>
internal static partial class NativeInstallPlanner
{
    /// <summary>
    /// Joins path segments with the Windows separator. The planner models
    /// Windows installations, so its output must use backslashes even when
    /// the planner itself runs on another OS (the pure-logic tests do).
    /// </summary>
    private static string JoinWindows(params string[] segments)
    {
        var parts = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            string trimmed = segment.TrimEnd('\\', '/');
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }
        }

        return string.Join('\\', parts);
    }

    /// <summary>
    /// Joins path segments with the separator of Linux and macOS. Only the
    /// trailing separator of a segment is removed, so a rooted first segment
    /// stays rooted.
    /// </summary>
    private static string JoinUnix(params string[] segments)
    {
        var parts = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            string trimmed = segment.TrimEnd('/');
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }
        }

        return string.Join('/', parts);
    }

    private static readonly string[] Msys2Prefixes = ["ucrt64", "mingw64", "clang64"];
    private static readonly string[] Msys2RootNames = ["msys64", "msys32"];

    /// <summary>
    /// The subdirectories of a bundled runtime tree on Windows, in the order in
    /// which they are probed: the layout of a GStreamer installation first, the
    /// layout NuGet gives the native assets of a package second.
    /// </summary>
    private static readonly string[] WindowsBundleLayouts = ["bin", "native"];

    /// <summary>
    /// The subdirectories of a bundled runtime tree on Linux and macOS, in the
    /// order in which they are probed: the library layout of the platform
    /// first, the NuGet layout second, the Windows layout last so that a tree
    /// which was laid out for all platforms at once is still found.
    /// </summary>
    private static readonly string[] UnixBundleLayouts = ["lib", "native", "bin"];

    /// <summary>
    /// Enumerates the Windows installations to try, best candidate first.
    /// </summary>
    /// <param name="probe">The machine to look at.</param>
    /// <param name="configuredPath">The directory that was passed to <see cref="NativeLoader.Configure"/>.</param>
    /// <param name="configuredFlavor">The flavor that was passed to <see cref="NativeLoader.Configure"/>.</param>
    /// <returns>The candidates, in the order in which they should be tried.</returns>
    internal static IReadOnlyList<NativeInstall> EnumerateWindows(
        IPlatformProbe probe,
        string? configuredPath,
        GstFlavor? configuredFlavor)
    {
        ArgumentNullException.ThrowIfNull(probe);

        List<NativeInstall> result = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        string architecture = ArchitectureToken(probe.OSArchitecture);
        string? localAppData = probe.GetEnvironmentVariable("LOCALAPPDATA");

        void Add(GstFlavor flavor, string? directory, GstInstallOrigin origin, string source)
        {
            if (configuredFlavor is GstFlavor requested && requested != flavor)
            {
                return;
            }

            string key = directory is null
                ? $"{flavor}|<search path>"
                : $"{flavor}|{Normalize(directory)}";

            if (seen.Add(key))
            {
                result.Add(new NativeInstall(flavor, directory, origin, source));
            }
        }

        // 1. What the application configured explicitly.
        if (!string.IsNullOrEmpty(configuredPath))
        {
            foreach (GstFlavor flavor in FlavorsOf(probe, configuredPath, configuredFlavor))
            {
                Add(flavor, configuredPath, GstInstallOrigin.ConfiguredSearchPath, "the configured native search path");
            }
        }

        // 2. The directories of the PATH environment variable, in their own
        //    order. Putting an installation in front of the search path is how
        //    the Windows tooling of GStreamer itself is pointed at one, so an
        //    explicit prepend is the strongest override the application did not
        //    configure through the API. The position on the search path is the
        //    priority the user expressed, so it outranks the flavor preference
        //    between directories; the flavor only decides inside one directory
        //    that holds both namings. Every candidate is a directory that is
        //    pinned and loaded from by absolute path, so the no mixing
        //    guarantee survives, which loading by plain file name would not.
        foreach (string entry in probe.GetPathEntries())
        {
            foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
            {
                // The GStreamer library alone decides it: dozens of unrelated
                // packages put a copy of GLib on the search path.
                if (probe.FileExists(JoinWindows(entry, NativeNames.WindowsAnchor(flavor))))
                {
                    Add(
                        flavor,
                        entry,
                        GstInstallOrigin.PathDirectory,
                        $"the directory {entry} on the process search path");
                }
            }
        }

        // 3. The environment variables of the official installers.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            string variable = $"GSTREAMER_1_0_ROOT_{FlavorToken(flavor).ToUpperInvariant()}_{architecture.ToUpperInvariant()}";
            string? root = probe.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(root))
            {
                Add(
                    flavor,
                    JoinWindows(root, "bin"),
                    GstInstallOrigin.EnvironmentVariable,
                    $"the {variable} environment variable");
            }
        }

        // 4. The uninstall entries of the official installers.
        foreach (NativeInstall candidate in RegistryCandidates(probe, architecture))
        {
            Add(candidate.Flavor, candidate.Directory, candidate.Origin, candidate.Source);
        }

        // 5. The directories the official installers use by default.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            string directoryName = $"{FlavorToken(flavor).ToLowerInvariant()}_{architecture}";

            if (!string.IsNullOrEmpty(localAppData))
            {
                string candidate = JoinWindows(localAppData, "Programs", "gstreamer", "1.0", directoryName, "bin");
                if (HasAnchor(probe, candidate, flavor))
                {
                    Add(
                        flavor,
                        candidate,
                        GstInstallOrigin.DefaultInstallDirectory,
                        "the default per user installation directory");
                }
            }

            string machineWide = JoinWindows(@"C:\gstreamer", "1.0", directoryName, "bin");
            if (HasAnchor(probe, machineWide, flavor))
            {
                Add(
                    flavor,
                    machineWide,
                    GstInstallOrigin.DefaultInstallDirectory,
                    "the default machine wide installation directory");
            }
        }

        // 6. MSYS2, which ships the MinGW flavor. A prefix that is on the
        //    search path has already been found by stage 2; what is left here
        //    is the roots that are not on it.
        foreach (NativeInstall candidate in Msys2Candidates(probe, localAppData))
        {
            Add(candidate.Flavor, candidate.Directory, candidate.Origin, candidate.Source);
        }

        // 7. The runtime tree that an application ships next to itself.
        List<string> bundled = BundledDirectories(
            probe,
            WindowsBundleLayouts,
            JoinWindows,
            WindowsRuntimeIdentifier(probe.OSArchitecture));

        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            foreach (string directory in bundled)
            {
                // The GStreamer library alone decides it, as for MSYS2: a
                // bundle directory that only happens to hold a copy of GLib is
                // not an installation and must not pin one.
                if (probe.FileExists(JoinWindows(directory, NativeNames.WindowsAnchor(flavor))))
                {
                    Add(
                        flavor,
                        directory,
                        GstInstallOrigin.BundledRuntime,
                        "the bundled runtime next to the application");
                }
            }
        }

        // 8. Whatever the operating system finds on the search path, by plain
        //    file name and without pinning a directory.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            Add(flavor, null, GstInstallOrigin.ProcessSearchPath, "the process search path");
        }

        return result;
    }

    /// <summary>
    /// Enumerates the directories to try on Linux and macOS, best candidate
    /// first. An empty entry means "load by plain file name".
    /// </summary>
    /// <param name="probe">The machine to look at.</param>
    /// <param name="configuredPath">The directory that was passed to <see cref="NativeLoader.Configure"/>.</param>
    /// <param name="isMacOs"><see langword="true"/> on macOS.</param>
    /// <returns>The directories, in the order in which they should be tried.</returns>
    /// <remarks>
    /// The runtime tree an application ships next to itself comes last, as it
    /// does on Windows: a copy that is installed on the machine is the one the
    /// plugins and the helper processes of the system use, so it wins.
    /// </remarks>
    internal static IReadOnlyList<string?> EnumerateUnixDirectories(
        IPlatformProbe probe,
        string? configuredPath,
        bool isMacOs)
    {
        ArgumentNullException.ThrowIfNull(probe);

        List<string?> result = [];

        if (!string.IsNullOrEmpty(configuredPath))
        {
            result.Add(configuredPath);
        }

        result.Add(null);

        if (isMacOs)
        {
            result.Add("/Library/Frameworks/GStreamer.framework/Versions/1.0/lib");
            result.Add("/opt/homebrew/lib");
            result.Add("/usr/local/lib");
        }

        string anchor = NativeNames.UnixAnchor(isMacOs);
        List<string> bundled = BundledDirectories(
            probe,
            UnixBundleLayouts,
            JoinUnix,
            UnixRuntimeIdentifier(probe.OSArchitecture, isMacOs));

        foreach (string directory in bundled)
        {
            // The GStreamer library alone decides it: a bundle directory that
            // only happens to hold a copy of GLib is not an installation.
            if (probe.FileExists(JoinUnix(directory, anchor)))
            {
                result.Add(directory);
            }
        }

        return result;
    }

    /// <summary>
    /// Tests whether a directory holds a GStreamer installation of one flavor.
    /// </summary>
    /// <param name="probe">The machine to look at.</param>
    /// <param name="directory">The directory to test.</param>
    /// <param name="flavor">The flavor to look for.</param>
    /// <returns><see langword="true"/> when one of the anchor files is there.</returns>
    internal static bool HasAnchor(IPlatformProbe probe, string directory, GstFlavor flavor)
    {
        foreach (string anchor in NativeNames.WindowsAnchors(flavor))
        {
            if (probe.FileExists(JoinWindows(directory, anchor)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tests whether a directory holds GStreamer itself. MSYS2 roots are only
    /// interesting when they do: a copy of GLib alone comes with dozens of
    /// unrelated packages and must not pin the installation.
    /// </summary>
    /// <param name="probe">The machine to look at.</param>
    /// <param name="directory">The directory to test.</param>
    /// <returns><see langword="true"/> when the GStreamer library is there.</returns>
    internal static bool HasGStreamer(IPlatformProbe probe, string directory) =>
        probe.FileExists(JoinWindows(directory, NativeNames.WindowsAnchor(GstFlavor.MinGW)));

    /// <summary>
    /// Gets the token that the installers use for an architecture.
    /// </summary>
    /// <param name="architecture">The architecture of the operating system.</param>
    /// <returns><c>x86_64</c>, <c>arm64</c> or <c>x86</c>.</returns>
    internal static string ArchitectureToken(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x86_64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Gets the architecture part of a runtime identifier. These are the lower
    /// case RID tokens of the SDK (<c>x64</c>), not the tokens the GStreamer
    /// installers use (<c>x86_64</c>).
    /// </summary>
    /// <param name="architecture">The architecture of the operating system.</param>
    /// <returns><c>x64</c>, <c>arm64</c> or <c>x86</c>.</returns>
    private static string RuntimeIdentifierArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Gets the portable Windows runtime identifier of an architecture.
    /// </summary>
    /// <param name="architecture">The architecture of the operating system.</param>
    /// <returns><c>win-x64</c>, <c>win-arm64</c> or <c>win-x86</c>.</returns>
    internal static string WindowsRuntimeIdentifier(Architecture architecture) =>
        $"win-{RuntimeIdentifierArchitecture(architecture)}";

    /// <summary>
    /// Gets the portable runtime identifier of Linux or macOS.
    /// </summary>
    /// <param name="architecture">The architecture of the operating system.</param>
    /// <param name="isMacOs"><see langword="true"/> on macOS.</param>
    /// <returns><c>linux-x64</c>, <c>osx-arm64</c> and so on.</returns>
    internal static string UnixRuntimeIdentifier(Architecture architecture, bool isMacOs) =>
        $"{(isMacOs ? "osx" : "linux")}-{RuntimeIdentifierArchitecture(architecture)}";

    /// <summary>
    /// Gets the directories a published application can carry its own copy of
    /// GStreamer in, best candidate first.
    /// </summary>
    /// <param name="probe">The machine to look at.</param>
    /// <param name="layouts">The subdirectories of one runtime tree to probe.</param>
    /// <param name="join">Joins path segments with the separator of the platform.</param>
    /// <param name="portableRuntimeIdentifier">
    /// The runtime identifier derived from the operating system and the
    /// architecture, which the published tree is usually named after.
    /// </param>
    /// <returns>
    /// The <c>runtimes/{rid}/{layout}</c> directories to probe, without
    /// duplicates.
    /// </returns>
    /// <remarks>
    /// The runtime identifier of the process can be more specific than the one
    /// the published tree is named after (<c>win10-x64</c> or
    /// <c>ubuntu.22.04-x64</c> against <c>win-x64</c> or <c>linux-x64</c>), so
    /// the portable form is probed as well.
    /// </remarks>
    private static List<string> BundledDirectories(
        IPlatformProbe probe,
        string[] layouts,
        Func<string[], string> join,
        string portableRuntimeIdentifier)
    {
        List<string> directories = [];
        string baseDirectory = probe.ApplicationBaseDirectory;

        if (string.IsNullOrEmpty(baseDirectory))
        {
            return directories;
        }

        List<string> runtimeIdentifiers = [];

        void AddRuntimeIdentifier(string? runtimeIdentifier)
        {
            if (!string.IsNullOrEmpty(runtimeIdentifier) &&
                !runtimeIdentifiers.Contains(runtimeIdentifier, StringComparer.OrdinalIgnoreCase))
            {
                runtimeIdentifiers.Add(runtimeIdentifier);
            }
        }

        AddRuntimeIdentifier(probe.RuntimeIdentifier);
        AddRuntimeIdentifier(portableRuntimeIdentifier);

        foreach (string runtimeIdentifier in runtimeIdentifiers)
        {
            foreach (string layout in layouts)
            {
                directories.Add(join([baseDirectory, "runtimes", runtimeIdentifier, layout]));
            }
        }

        return directories;
    }

    [GeneratedRegex(
        @"^GStreamer 1\.0 \((MSVC|MinGW) (x86_64|arm64|x86)\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallerDisplayName();

    private static IEnumerable<GstFlavor> PreferredFlavors(GstFlavor? configuredFlavor)
    {
        if (configuredFlavor is GstFlavor flavor)
        {
            yield return flavor;
            yield break;
        }

        yield return GstFlavor.Msvc;
        yield return GstFlavor.MinGW;
    }

    private static IEnumerable<GstFlavor> FlavorsOf(IPlatformProbe probe, string directory, GstFlavor? configuredFlavor)
    {
        if (configuredFlavor is GstFlavor flavor)
        {
            yield return flavor;
            yield break;
        }

        bool msvc = HasAnchor(probe, directory, GstFlavor.Msvc);
        bool minGW = HasAnchor(probe, directory, GstFlavor.MinGW);

        if (msvc)
        {
            yield return GstFlavor.Msvc;
        }

        if (minGW)
        {
            yield return GstFlavor.MinGW;
        }

        if (!msvc && !minGW)
        {
            // Try both, so that a wrong path still produces a useful error.
            yield return GstFlavor.Msvc;
            yield return GstFlavor.MinGW;
        }
    }

    private static List<NativeInstall> RegistryCandidates(IPlatformProbe probe, string architecture)
    {
        List<NativeInstall> candidates = [];

        foreach (RegistryInstall install in probe.EnumerateRegistryInstalls())
        {
            if (string.IsNullOrEmpty(install.DisplayName) || string.IsNullOrEmpty(install.InstallLocation))
            {
                continue;
            }

            Match match = InstallerDisplayName().Match(install.DisplayName);
            if (!match.Success)
            {
                continue;
            }

            if (!string.Equals(match.Groups[2].Value, architecture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            GstFlavor flavor = string.Equals(match.Groups[1].Value, "MSVC", StringComparison.OrdinalIgnoreCase)
                ? GstFlavor.Msvc
                : GstFlavor.MinGW;

            string directory = JoinWindows(install.InstallLocation, "bin");
            if (!probe.DirectoryExists(directory))
            {
                continue;
            }

            candidates.Add(new NativeInstall(
                flavor,
                directory,
                GstInstallOrigin.Registry,
                $"the registry entry \"{install.DisplayName}\""));
        }

        // Prefer MSVC when both flavors are installed. OrderBy is stable, so
        // entries of the same flavor keep their registry order.
        candidates.Sort(static (left, right) => left.Flavor.CompareTo(right.Flavor));
        return candidates;
    }

    private static List<NativeInstall> Msys2Candidates(IPlatformProbe probe, string? localAppData)
    {
        List<NativeInstall> candidates = [];

        string? prefix = probe.GetEnvironmentVariable("MSYSTEM_PREFIX");
        if (!string.IsNullOrEmpty(prefix))
        {
            string directory = JoinWindows(prefix, "bin");
            if (HasGStreamer(probe, directory))
            {
                candidates.Add(new NativeInstall(
                    GstFlavor.MinGW,
                    directory,
                    GstInstallOrigin.Msys2,
                    "the MSYSTEM_PREFIX environment variable"));
            }
        }

        List<string> roots = [];

        void AddRoot(string? root)
        {
            if (!string.IsNullOrEmpty(root) && !roots.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(root);
            }
        }

        AddRoot(@"C:\msys64");
        AddRoot(@"C:\msys32");
        if (!string.IsNullOrEmpty(localAppData))
        {
            AddRoot(JoinWindows(localAppData, "msys64"));
        }

        foreach (string entry in probe.GetPathEntries())
        {
            if (Msys2RootOf(entry) is string root)
            {
                AddRoot(root);

                // The entry itself may already be the right bin directory.
                if (HasGStreamer(probe, entry))
                {
                    candidates.Add(new NativeInstall(
                        GstFlavor.MinGW,
                        entry,
                        GstInstallOrigin.Msys2,
                        "an MSYS2 directory on the process search path"));
                }
            }
        }

        foreach (string root in roots)
        {
            foreach (string prefixName in Msys2Prefixes)
            {
                string directory = JoinWindows(root, prefixName, "bin");
                if (HasGStreamer(probe, directory))
                {
                    candidates.Add(new NativeInstall(
                        GstFlavor.MinGW,
                        directory,
                        GstInstallOrigin.Msys2,
                        $"the MSYS2 installation at {root}"));
                }
            }
        }

        return candidates;
    }

    private static string? Msys2RootOf(string pathEntry)
    {
        string[] segments = pathEntry.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            foreach (string rootName in Msys2RootNames)
            {
                if (string.Equals(segments[i], rootName, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Join('\\', segments[..(i + 1)]);
                }
            }
        }

        return null;
    }

    private static string FlavorToken(GstFlavor flavor) => flavor == GstFlavor.Msvc ? "msvc" : "mingw";

    private static string Normalize(string directory) =>
        directory.TrimEnd('\\', '/').Replace('/', '\\');
}
