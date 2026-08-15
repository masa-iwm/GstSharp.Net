using System.Runtime.InteropServices;
using Gst.Interop;

namespace GstSharp.Core.Tests;

/// <summary>
/// A machine that only exists inside the test run.
/// </summary>
internal sealed class FakePlatformProbe : IPlatformProbe
{
    private readonly Dictionary<string, string> _environment = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _pathEntries = [];
    private readonly List<RegistryInstall> _registry = [];
    private readonly HashSet<string> _directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);

    public Architecture OSArchitecture { get; set; } = Architecture.X64;

    public string? GetEnvironmentVariable(string name) =>
        _environment.TryGetValue(name, out string? value) ? value : null;

    public IReadOnlyList<string> GetPathEntries() => _pathEntries;

    public IReadOnlyList<RegistryInstall> EnumerateRegistryInstalls() => _registry;

    public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));

    public bool FileExists(string path) => _files.Contains(Normalize(path));

    internal FakePlatformProbe WithEnvironment(string name, string value)
    {
        _environment[name] = value;
        return this;
    }

    internal FakePlatformProbe WithPathEntry(string entry)
    {
        _pathEntries.Add(entry);
        return this;
    }

    internal FakePlatformProbe WithRegistryEntry(string displayName, string installLocation)
    {
        _registry.Add(new RegistryInstall(displayName, installLocation));
        return this;
    }

    internal FakePlatformProbe WithDirectory(string path)
    {
        _directories.Add(Normalize(path));
        return this;
    }

    /// <summary>
    /// Creates a GStreamer installation whose <c>bin</c> directory is
    /// <paramref name="binDirectory"/>.
    /// </summary>
    /// <param name="binDirectory">The directory that holds the DLLs.</param>
    /// <param name="flavor">The flavor of the installation.</param>
    /// <returns>This probe.</returns>
    internal FakePlatformProbe WithInstallation(string binDirectory, GstFlavor flavor)
    {
        WithDirectory(binDirectory);

        string prefix = flavor == GstFlavor.Msvc ? string.Empty : "lib";
        _files.Add(Normalize(Path.Combine(binDirectory, $"{prefix}gstreamer-1.0-0.dll")));
        _files.Add(Normalize(Path.Combine(binDirectory, $"{prefix}glib-2.0-0.dll")));
        _files.Add(Normalize(Path.Combine(binDirectory, $"{prefix}gobject-2.0-0.dll")));
        return this;
    }

    /// <summary>
    /// Adds a directory that holds a copy of GLib but no GStreamer, as an MSYS2
    /// root without the GStreamer packages does.
    /// </summary>
    /// <param name="binDirectory">The directory that holds the DLLs.</param>
    /// <returns>This probe.</returns>
    internal FakePlatformProbe WithGLibOnly(string binDirectory)
    {
        WithDirectory(binDirectory);
        _files.Add(Normalize(Path.Combine(binDirectory, "libglib-2.0-0.dll")));
        _files.Add(Normalize(Path.Combine(binDirectory, "libgobject-2.0-0.dll")));
        return this;
    }

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');
}
