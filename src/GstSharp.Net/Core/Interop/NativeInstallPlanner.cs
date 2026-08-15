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
/// <param name="Source">A human readable description of where the candidate came from.</param>
internal readonly record struct NativeInstall(GstFlavor Flavor, string? Directory, string Source);

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

    private static readonly string[] Msys2Prefixes = ["ucrt64", "mingw64", "clang64"];
    private static readonly string[] Msys2RootNames = ["msys64", "msys32"];

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

        void Add(GstFlavor flavor, string? directory, string source)
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
                result.Add(new NativeInstall(flavor, directory, source));
            }
        }

        // 1. What the application configured explicitly.
        if (!string.IsNullOrEmpty(configuredPath))
        {
            foreach (GstFlavor flavor in FlavorsOf(probe, configuredPath, configuredFlavor))
            {
                Add(flavor, configuredPath, "the configured native search path");
            }
        }

        // 2. The environment variables of the official installers.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            string variable = $"GSTREAMER_1_0_ROOT_{FlavorToken(flavor).ToUpperInvariant()}_{architecture.ToUpperInvariant()}";
            string? root = probe.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(root))
            {
                Add(flavor, JoinWindows(root, "bin"), $"the {variable} environment variable");
            }
        }

        // 3. The uninstall entries of the official installers.
        foreach (NativeInstall candidate in RegistryCandidates(probe, architecture))
        {
            Add(candidate.Flavor, candidate.Directory, candidate.Source);
        }

        // 4. The directories the official installers use by default.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            string directoryName = $"{FlavorToken(flavor).ToLowerInvariant()}_{architecture}";

            if (!string.IsNullOrEmpty(localAppData))
            {
                string candidate = JoinWindows(localAppData, "Programs", "gstreamer", "1.0", directoryName, "bin");
                if (HasAnchor(probe, candidate, flavor))
                {
                    Add(flavor, candidate, "the default per user installation directory");
                }
            }

            string machineWide = JoinWindows(@"C:\gstreamer", "1.0", directoryName, "bin");
            if (HasAnchor(probe, machineWide, flavor))
            {
                Add(flavor, machineWide, "the default machine wide installation directory");
            }
        }

        // 5. MSYS2, which ships the MinGW flavor.
        foreach (NativeInstall candidate in Msys2Candidates(probe, localAppData))
        {
            Add(candidate.Flavor, candidate.Directory, candidate.Source);
        }

        // 6. Whatever the operating system finds on the search path.
        foreach (GstFlavor flavor in PreferredFlavors(configuredFlavor))
        {
            Add(flavor, null, "the process search path");
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

            candidates.Add(new NativeInstall(flavor, directory, $"the registry entry \"{install.DisplayName}\""));
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
