using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst.GLib;

/// <summary>
/// The per user directories GLib resolves for an application.
/// </summary>
/// <remarks>
/// They follow the conventions of the platform - the XDG base directories on
/// Linux, the known folders on Windows - and GLib caches each of them, so the
/// value a process reads never changes while it runs.
/// </remarks>
public static partial class UserDirectories
{
    /// <summary>
    /// Gets the directory user specific non essential data is cached in.
    /// </summary>
    /// <value>The cache directory of the user, as an absolute path.</value>
    /// <remarks>
    /// This is <c>g_get_user_cache_dir</c>. GStreamer's own tools build their
    /// paths below it - <c>gst-discoverer-1.0</c> keeps its serialised
    /// discoveries in <c>gstreamer-1.0/discoverer</c> under this directory -
    /// and an application that wants to sit next to them joins its own
    /// subdirectory to it. The string belongs to GLib and is never released.
    /// </remarks>
    public static string CacheDir =>
        GMarshal.PtrToStringUtf8(GetUserCacheDir()) ?? string.Empty;

    /// <summary>The <c>g_get_user_cache_dir</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_get_user_cache_dir")]
    private static partial nint GetUserCacheDir();
}
