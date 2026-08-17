using System.Collections.Frozen;
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
/// <para>
/// A binding module outside this repository adds its own library to the name
/// space with <see cref="RegisterLibrary"/> and is then resolved by the same
/// rules; see
/// <see href="https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md">docs/modules.md</see>.
/// </para>
/// </remarks>
public static partial class NativeLoader
{
    /// <summary>
    /// The <c>MAX_PATH</c> of the classic Windows API, which is long enough for
    /// almost every module path and is grown from when it is not.
    /// </summary>
    private const int InitialPathLength = 260;

    /// <summary>
    /// The longest path Windows accepts, and therefore the point at which
    /// growing the buffer stops.
    /// </summary>
    private const int MaximumPathLength = 32768;

    /// <summary><c>ERROR_INSUFFICIENT_BUFFER</c>.</summary>
    private const int ErrorInsufficientBuffer = 122;

    private static readonly object Sync = new();
    private static readonly HashSet<Assembly> RegisteredAssemblies = [];
    private static readonly Dictionary<string, nint> LoadedModules = new(StringComparer.Ordinal);
    private static readonly DllImportResolver Resolver = Resolve;
    private static readonly IPlatformProbe Probe = new SystemPlatformProbe();

    /// <summary>
    /// The libraries that <see cref="RegisterLibrary"/> added, on top of the
    /// closed table of <see cref="NativeNames"/>.
    /// </summary>
    /// <remarks>
    /// The table is replaced wholesale under <see cref="Sync"/> and read
    /// without any lock, because <see cref="Resolve"/> runs on whichever thread
    /// happens to make the first call through a stub of a registered assembly.
    /// </remarks>
    private static volatile FrozenDictionary<string, NativeNameEntry> _registeredLibraries =
        FrozenDictionary<string, NativeNameEntry>.Empty;

    private static string? _configuredPath;
    private static GstFlavor? _configuredFlavor;
    private static bool _pinned;
    private static string? _pinnedDirectory;
    private static GstFlavor _pinnedFlavor;
    private static GstInstallOrigin? _resolvedOrigin;
    private static string? _resolvedSourceDescription;

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
    /// Gets the stage of the search that found the installation in use, or
    /// <see langword="null"/> when nothing has been loaded yet.
    /// </summary>
    /// <remarks>
    /// Only the Windows search has stages. On Linux and macOS the loader walks
    /// a plain list of directories and the property stays
    /// <see langword="null"/>, even when the directory that won is a bundled
    /// runtime tree; use <see cref="ResolvedDirectory"/> there.
    /// </remarks>
    public static GstInstallOrigin? ResolvedOrigin
    {
        get
        {
            lock (Sync)
            {
                return _resolvedOrigin;
            }
        }
    }

    /// <summary>
    /// Gets the human readable description of where the installation in use was
    /// found, for example <c>the registry entry "GStreamer 1.0 (MSVC x86_64)"</c>,
    /// or <see langword="null"/> when nothing has been loaded yet.
    /// </summary>
    /// <remarks>
    /// It is the same text the attempts of a
    /// <see cref="GstNativeLoadException"/> carry, and it is meant for log
    /// lines and diagnostics only. Code should branch on
    /// <see cref="ResolvedOrigin"/> instead. Like that property it stays
    /// <see langword="null"/> on Linux and macOS.
    /// </remarks>
    public static string? ResolvedSourceDescription
    {
        get
        {
            lock (Sync)
            {
                return _resolvedSourceDescription;
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
    /// Adds one native library to the name space of the loader, so that a
    /// binding module outside this repository can import from it.
    /// </summary>
    /// <param name="logicalName">
    /// The logical name the module uses in its
    /// <see cref="LibraryImportAttribute"/> stubs and in its
    /// <see cref="NativeModule"/>, for example <c>GstController</c>.
    /// </param>
    /// <param name="fileNames">The file name of the library on every platform.</param>
    /// <remarks>
    /// <para>
    /// <b>The registered library is resolved from the pinned installation.</b>
    /// It goes through exactly the same search as the libraries of this
    /// repository, which on Windows means the directory and the flavor that the
    /// first module pinned and nothing else, and on Linux and macOS the same
    /// ordered walk of candidate directories that every other module takes. A
    /// module therefore cannot pull a second copy of GLib into the process
    /// through the back door.
    /// </para>
    /// <para>
    /// <b>Register before the first call through the name.</b> The natural
    /// place is the <c>[ModuleInitializer]</c> that already calls
    /// <see cref="EnsureRegistered"/>, which by construction runs before any
    /// other code of that assembly and therefore before any of its stubs can
    /// resolve. A name that is still unknown when a stub resolves is left to
    /// the default resolution of the runtime, which normally fails that call
    /// with a <see cref="DllNotFoundException"/>; nothing negative is
    /// remembered, so registering afterwards still works for the next call, but
    /// the call that raced is lost.
    /// </para>
    /// <para>
    /// <b>The built-in names cannot be shadowed.</b> <c>Gst</c>, <c>GLib</c>,
    /// <c>GObject</c> and the other names this repository ships are refused, so
    /// no module can redirect a core library somewhere else.
    /// </para>
    /// <para>
    /// Registering the same name with the same four file names again does
    /// nothing, which two assemblies that import from one library both need.
    /// Registering it with different file names is a conflict and throws,
    /// whether the library has been loaded already or not: the process resolves
    /// one file per logical name and the second answer would be a lie for
    /// whoever asked first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="fileNames"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="logicalName"/> is empty or belongs to this repository,
    /// or one of the file names is empty or is a path rather than a file name.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The name is registered already, with different file names.
    /// </exception>
    public static void RegisterLibrary(string logicalName, NativeLibraryNames fileNames)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);
        ArgumentNullException.ThrowIfNull(fileNames);

        if (NativeNames.TryGet(logicalName, out _))
        {
            throw new ArgumentException(
                $"\"{logicalName}\" is one of the native modules that GstSharp.Net ships, and the built-in " +
                "names cannot be redefined. Pick a name of your own.",
                nameof(logicalName));
        }

        NativeNameEntry entry = new(
            FileName(fileNames.Linux, nameof(NativeLibraryNames.Linux)),
            FileName(fileNames.MacOs, nameof(NativeLibraryNames.MacOs)),
            FileName(fileNames.WindowsMsvc, nameof(NativeLibraryNames.WindowsMsvc)),
            FileName(fileNames.WindowsMinGW, nameof(NativeLibraryNames.WindowsMinGW)));

        lock (Sync)
        {
            if (_registeredLibraries.TryGetValue(logicalName, out NativeNameEntry known))
            {
                if (known.Matches(entry))
                {
                    return;
                }

                throw new InvalidOperationException(
                    $"\"{logicalName}\" is registered already, with different file names. One logical name " +
                    "stands for one library in a process.");
            }

            Dictionary<string, NativeNameEntry> next = new(_registeredLibraries, StringComparer.Ordinal)
            {
                [logicalName] = entry,
            };

            _registeredLibraries = next.ToFrozenDictionary(StringComparer.Ordinal);
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

            if (!TryGetFileNames(logicalName, out NativeNameEntry entry))
            {
                throw new GstNativeLoadException(
                    $"\"{logicalName}\" is not one of the native modules that GstSharp.Net knows about. " +
                    "A binding module outside this repository adds its own library with " +
                    "NativeLoader.RegisterLibrary.");
            }

            nint handle = OperatingSystem.IsWindows()
                ? LoadWindows(logicalName, entry)
                : LoadUnix(logicalName, entry);

            LoadedModules[logicalName] = handle;
            return handle;
        }
    }

    /// <summary>
    /// Gets the file the operating system loaded one native module from.
    /// </summary>
    /// <param name="logicalName">The logical name of the module, for example <c>Gst</c>.</param>
    /// <returns>
    /// The absolute path of the module file, or <see langword="null"/> when the
    /// module has not been loaded yet, when the name is neither one of the
    /// modules of the bindings nor one a module added with
    /// <see cref="RegisterLibrary"/>, or when the platform cannot answer the
    /// question.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The path is read back from the module handle rather than remembered when
    /// the module is loaded, so that it is the truth even when the module was
    /// loaded by plain file name and the operating system picked the directory
    /// (see <see cref="GstInstallOrigin.ProcessSearchPath"/>).
    /// </para>
    /// <para>
    /// Only Windows is supported: the dynamic loaders of Linux and macOS have no
    /// portable way of turning a handle back into a path, so the method returns
    /// <see langword="null"/> there.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="logicalName"/> is empty.</exception>
    public static string? GetLoadedModulePath(string logicalName)
    {
        ArgumentException.ThrowIfNullOrEmpty(logicalName);

        nint handle;
        lock (Sync)
        {
            if (!LoadedModules.TryGetValue(logicalName, out handle))
            {
                return null;
            }
        }

        return OperatingSystem.IsWindows() ? ModuleFileName(handle) : null;
    }

    /// <summary>
    /// Asks Windows which file a loaded module came from.
    /// </summary>
    /// <param name="module">The handle of the module.</param>
    /// <returns>The path, or <see langword="null"/> when Windows refuses to tell.</returns>
    private static unsafe string? ModuleFileName(nint module)
    {
        for (int length = InitialPathLength; ; length = Math.Min(length * 2, MaximumPathLength))
        {
            char[] buffer = new char[length];
            uint written;

            fixed (char* target = buffer)
            {
                written = GetModuleFileNameW(module, target, (uint)length);
            }

            if (written == 0)
            {
                return null;
            }

            // A path that did not fit is truncated and reported as the full
            // buffer size, on every Windows version that matters.
            if (written < (uint)length)
            {
                return new string(buffer, 0, (int)written);
            }

            // The buffer was too small. Growing stops at the longest path
            // Windows accepts, so the loop always ends.
            if (length == MaximumPathLength || Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
            {
                return null;
            }
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleFileNameW", SetLastError = true)]
    private static unsafe partial uint GetModuleFileNameW(nint module, char* fileName, uint size);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Names that are neither ours nor registered have to fall back to the
        // default resolution.
        return TryGetFileNames(libraryName, out _) ? Load(libraryName) : nint.Zero;
    }

    /// <summary>
    /// Looks the file names of a logical name up, in the built-in table first
    /// and in the registered libraries afterwards.
    /// </summary>
    /// <param name="logicalName">The logical name to resolve.</param>
    /// <param name="entry">The file names of the library.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    /// <remarks>
    /// The order is what makes <see cref="RegisterLibrary"/> unable to shadow a
    /// core library even if the refusal there were ever lifted.
    /// </remarks>
    private static bool TryGetFileNames(string logicalName, out NativeNameEntry entry) =>
        NativeNames.TryGet(logicalName, out entry) ||
        _registeredLibraries.TryGetValue(logicalName, out entry);

    /// <summary>
    /// Checks that one of the four names of a registration is a usable file
    /// name.
    /// </summary>
    /// <param name="value">The name as the module gave it.</param>
    /// <param name="platform">The property it came from, for the message.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// A name that carried a directory would be combined with the pinned
    /// directory on Windows and would silently produce a path that resolves to
    /// nothing, or worse to a library outside the installation in use.
    /// </remarks>
    private static string FileName(string value, string platform)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException(
                $"The {platform} file name of a native library must not be empty.",
                nameof(NativeLibraryNames));
        }

        if (value.AsSpan().IndexOfAny('/', '\\') >= 0 || Path.IsPathRooted(value))
        {
            throw new ArgumentException(
                $"The {platform} file name of a native library must be a bare file name, not a path: " +
                $"\"{value}\". The directory is the installation the loader pinned.",
                nameof(NativeLibraryNames));
        }

        return value;
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
                _resolvedOrigin = install.Origin;
                _resolvedSourceDescription = install.Source;
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
