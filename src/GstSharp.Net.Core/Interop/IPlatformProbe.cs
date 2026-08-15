using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Gst.Interop;

/// <summary>
/// One entry of the Windows uninstall registry that could describe a GStreamer
/// installation.
/// </summary>
/// <param name="DisplayName">The <c>DisplayName</c> value of the entry.</param>
/// <param name="InstallLocation">The <c>InstallLocation</c> value of the entry.</param>
internal readonly record struct RegistryInstall(string DisplayName, string InstallLocation);

/// <summary>
/// The bits of the machine that <see cref="NativeInstallPlanner"/> looks at.
/// </summary>
/// <remarks>
/// The interface exists so that the ordering rules can be unit tested against a
/// fake machine, without a GStreamer installation and without touching the real
/// registry.
/// </remarks>
internal interface IPlatformProbe
{
    /// <summary>Gets the architecture of the operating system.</summary>
    Architecture OSArchitecture { get; }

    /// <summary>Reads an environment variable.</summary>
    /// <param name="name">The name of the variable.</param>
    /// <returns>The value, or <see langword="null"/> when it is not set.</returns>
    string? GetEnvironmentVariable(string name);

    /// <summary>Gets the entries of the <c>PATH</c> environment variable.</summary>
    /// <returns>The directories on the search path.</returns>
    IReadOnlyList<string> GetPathEntries();

    /// <summary>Enumerates the installed programs of the Windows registry.</summary>
    /// <returns>Every uninstall entry that has a display name.</returns>
    IReadOnlyList<RegistryInstall> EnumerateRegistryInstalls();

    /// <summary>Tests whether a directory exists.</summary>
    /// <param name="path">The directory to test.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    bool DirectoryExists(string path);

    /// <summary>Tests whether a file exists.</summary>
    /// <param name="path">The file to test.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    bool FileExists(string path);
}

/// <summary>
/// The <see cref="IPlatformProbe"/> that looks at the real machine.
/// </summary>
internal sealed class SystemPlatformProbe : IPlatformProbe
{
    private static readonly string[] UninstallKeys =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <inheritdoc/>
    public Architecture OSArchitecture => RuntimeInformation.OSArchitecture;

    /// <inheritdoc/>
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);

    /// <inheritdoc/>
    public IReadOnlyList<string> GetPathEntries()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return [];
        }

        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <inheritdoc/>
    public IReadOnlyList<RegistryInstall> EnumerateRegistryInstalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        List<RegistryInstall> result = [];
        CollectWindowsInstalls(result);
        return result;
    }

    /// <inheritdoc/>
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc/>
    public bool FileExists(string path) => File.Exists(path);

    [SupportedOSPlatform("windows")]
    private static void CollectWindowsInstalls(List<RegistryInstall> result)
    {
        foreach (string subKey in UninstallKeys)
        {
            Collect(Registry.CurrentUser, subKey, result);
            Collect(Registry.LocalMachine, subKey, result);
        }
    }

    [SupportedOSPlatform("windows")]
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "A registry that cannot be read is a probe result, never a fatal error.")]
    private static void Collect(RegistryKey root, string subKeyPath, List<RegistryInstall> result)
    {
        try
        {
            using RegistryKey? key = root.OpenSubKey(subKeyPath);
            if (key is null)
            {
                return;
            }

            foreach (string name in key.GetSubKeyNames())
            {
                using RegistryKey? entry = key.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is not string displayName)
                {
                    continue;
                }

                string location = entry.GetValue("InstallLocation") as string ?? string.Empty;
                result.Add(new RegistryInstall(displayName, location));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A key that cannot be opened simply does not contribute candidates.
        }
    }
}
