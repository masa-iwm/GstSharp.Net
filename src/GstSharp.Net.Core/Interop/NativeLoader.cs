using System.Reflection;
using System.Runtime.InteropServices;

namespace Gst.Interop;

/// <summary>
/// Finds and loads the native GStreamer and GLib libraries.
/// </summary>
/// <remarks>
/// <para>
/// Every binding assembly registers this loader for its own
/// <see cref="LibraryImportAttribute"/> stubs from a module initialiser, so
/// that the logical names (<c>GLib</c>, <c>GObject</c>, <c>Gst</c>, ...) are
/// mapped to the real file names of the platform.
/// </para>
/// <para>
/// On Windows the first module that is loaded decides the installation: its
/// flavor and directory are pinned and every other module is loaded from there,
/// because the MSVC and the MinGW build cannot be mixed inside one process.
/// </para>
/// </remarks>
public static class NativeLoader
{
    private static readonly object Sync = new();
    private static readonly HashSet<Assembly> RegisteredAssemblies = [];
    private static readonly Dictionary<string, nint> LoadedModules = new(StringComparer.Ordinal);
    private static readonly DllImportResolver Resolver = Resolve;
    private static readonly IPlatformProbe Probe = new SystemPlatformProbe();

    private static string? _configuredPath;
    private static GstFlavor? _configuredFlavor;
    private static bool _pinned;
    private static string? _pinnedDirectory;
    private static GstFlavor _pinnedFlavor;

    /// <summary>
    /// Gets the directory the native modules are loaded from, or
    /// <see langword="null"/> when nothing has been loaded yet or the operating
    /// system found the libraries on its own search path.
    /// </summary>
    public static string? ResolvedDirectory
    {
        get
        {
            lock (Sync)
            {
                return _pinnedDirectory;
            }
        }
    }

    /// <summary>
    /// Gets the flavor of the installation that is in use, or
    /// <see langword="null"/> when nothing has been loaded yet or the platform
    /// is not Windows.
    /// </summary>
    public static GstFlavor? ResolvedFlavor
    {
        get
        {
            lock (Sync)
            {
                return _pinned && OperatingSystem.IsWindows() ? _pinnedFlavor : null;
            }
        }
    }

    /// <summary>
    /// Registers the loader for <paramref name="assembly"/>. Calling it more
    /// than once for the same assembly does nothing.
    /// </summary>
    /// <param name="assembly">The assembly whose native imports should use this loader.</param>
    public static void EnsureRegistered(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        lock (Sync)
        {
            if (RegisteredAssemblies.Add(assembly))
            {
                NativeLibrary.SetDllImportResolver(assembly, Resolver);
            }
        }
    }

    /// <summary>
    /// Tells the loader where the native libraries are, before the first one is
    /// loaded.
    /// </summary>
    /// <param name="nativeSearchPath">
    /// The directory that holds the native libraries (on Windows the <c>bin</c>
    /// directory of the installation), or <see langword="null"/> to keep
    /// searching automatically.
    /// </param>
    /// <param name="windowsFlavor">
    /// The flavor to use on Windows, or <see langword="null"/> to detect it.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// A module has already been loaded from a different installation.
    /// </exception>
    public static void Configure(string? nativeSearchPath, GstFlavor? windowsFlavor)
    {
        lock (Sync)
        {
            if (_pinned)
            {
                bool samePath = nativeSearchPath is null || SameDirectory(nativeSearchPath, _pinnedDirectory);
                bool sameFlavor = windowsFlavor is null || !OperatingSystem.IsWindows() || windowsFlavor == _pinnedFlavor;

                if (samePath && sameFlavor)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "The native GStreamer libraries have already been loaded from " +
                    $"\"{_pinnedDirectory ?? "the process search path"}\", so the loader cannot be " +
                    "reconfigured. Call NativeLoader.Configure before the first native call.");
            }

            _configuredPath = nativeSearchPath;
            _configuredFlavor = windowsFlavor;
        }
    }

    /// <summary>
    /// Loads one native module, or returns the handle of the module if it is
    /// already loaded.
    /// </summary>
    /// <param name="logicalName">The logical name of the module, for example <c>Gst</c>.</param>
    /// <returns>The operating system handle of the module.</returns>
    /// <exception cref="GstNativeLoadException">The module could not be found.</exception>
    public static nint Load(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);

        lock (Sync)
        {
            if (LoadedModules.TryGetValue(logicalName, out nint cached))
            {
                return cached;
            }

            if (!NativeNames.TryGet(logicalName, out NativeNameEntry entry))
            {
                throw new GstNativeLoadException(
                    $"\"{logicalName}\" is not one of the native modules that GstSharp.Net knows about.");
            }

            nint handle = OperatingSystem.IsWindows()
                ? LoadWindows(logicalName, entry)
                : LoadUnix(logicalName, entry);

            LoadedModules[logicalName] = handle;
            return handle;
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Names that are not ours have to fall back to the default resolution.
        return NativeNames.TryGet(libraryName, out _) ? Load(libraryName) : nint.Zero;
    }

    private static nint LoadWindows(string logicalName, NativeNameEntry entry)
    {
        List<string> attempts = [];

        if (_pinned)
        {
            string pinnedFile = entry.Windows(_pinnedFlavor);

            if (_pinnedDirectory is not null)
            {
                // Only this directory: falling back to the search path could
                // pull the module out of a second installation, and two copies
                // of GLib in one process do not end well.
                string path = Path.Combine(_pinnedDirectory, pinnedFile);
                if (NativeLibrary.TryLoad(path, out nint pinnedHandle))
                {
                    return pinnedHandle;
                }

                attempts.Add($"{path} (the installation that is already in use)");
                throw new GstNativeLoadException(logicalName, attempts);
            }

            if (NativeLibrary.TryLoad(pinnedFile, out nint searchPathHandle))
            {
                return searchPathHandle;
            }

            attempts.Add($"{pinnedFile} (the process search path)");
            throw new GstNativeLoadException(logicalName, attempts);
        }

        foreach (NativeInstall install in NativeInstallPlanner.EnumerateWindows(Probe, _configuredPath, _configuredFlavor))
        {
            string file = entry.Windows(install.Flavor);
            string path = install.Directory is null ? file : Path.Combine(install.Directory, file);

            if (NativeLibrary.TryLoad(path, out nint handle))
            {
                _pinned = true;
                _pinnedFlavor = install.Flavor;
                _pinnedDirectory = install.Directory;
                AddToSearchPath(install.Directory);
                return handle;
            }

            attempts.Add($"{path} ({install.Source})");
        }

        throw new GstNativeLoadException(logicalName, attempts);
    }

    private static nint LoadUnix(string logicalName, NativeNameEntry entry)
    {
        bool isMacOs = OperatingSystem.IsMacOS();
        string file = isMacOs ? entry.MacOs : entry.Linux;
        List<string> attempts = [];

        foreach (string? directory in NativeInstallPlanner.EnumerateUnixDirectories(Probe, _configuredPath, isMacOs))
        {
            string path = directory is null ? file : Path.Combine(directory, file);

            if (NativeLibrary.TryLoad(path, out nint handle))
            {
                if (!_pinned)
                {
                    _pinned = true;
                    _pinnedDirectory = directory;
                }

                return handle;
            }

            attempts.Add(directory is null ? $"{path} (the library search path)" : path);
        }

        throw new GstNativeLoadException(logicalName, attempts);
    }

    /// <summary>
    /// Puts the installation directory on the search path of the process, so
    /// that the DLLs which GStreamer loads on its own can find their
    /// dependencies.
    /// </summary>
    /// <param name="directory">The pinned directory, may be <see langword="null"/>.</param>
    /// <remarks>
    /// The core libraries are loaded by absolute path, which makes Windows
    /// resolve their siblings next to them. Plugins are different: GStreamer
    /// loads them itself, from the plugin directory, and the dependencies they
    /// pull in live in the <c>bin</c> directory. Without this the plugin loader
    /// reports "unable to find a DLL dependency" for every plugin.
    /// </remarks>
    private static void AddToSearchPath(string? directory)
    {
        if (directory is null || !OperatingSystem.IsWindows())
        {
            return;
        }

        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string entry in current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (SameDirectory(entry.Trim(), directory))
            {
                return;
            }
        }

        Environment.SetEnvironmentVariable(
            "PATH",
            current.Length == 0 ? directory : directory + Path.PathSeparator + current);
    }

    private static bool SameDirectory(string left, string? right)
    {
        if (right is null)
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            comparison);
    }
}
