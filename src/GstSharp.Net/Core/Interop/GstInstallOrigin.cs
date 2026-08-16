namespace Gst.Interop;

/// <summary>
/// Where the GStreamer installation that <see cref="NativeLoader"/> uses was
/// found.
/// </summary>
/// <remarks>
/// The values are the stages of the Windows search, in the order in which they
/// are tried, so a larger value means that everything before it was looked at
/// and found nothing. <see cref="NativeLoader.ResolvedOrigin"/> reports the
/// stage that won.
/// </remarks>
public enum GstInstallOrigin
{
    /// <summary>
    /// The directory the application passed to
    /// <see cref="NativeLoader.Configure"/>.
    /// </summary>
    ConfiguredSearchPath,

    /// <summary>
    /// A directory of the <c>PATH</c> environment variable that holds the
    /// GStreamer library. The directory is pinned and the modules are loaded
    /// from it by absolute path, which is what separates this from
    /// <see cref="ProcessSearchPath"/>.
    /// </summary>
    PathDirectory,

    /// <summary>
    /// A <c>GSTREAMER_1_0_ROOT_*</c> environment variable, which the official
    /// installers set.
    /// </summary>
    EnvironmentVariable,

    /// <summary>
    /// An uninstall entry of the Windows registry that describes an official
    /// installation.
    /// </summary>
    Registry,

    /// <summary>
    /// One of the directories the official installers use by default, per user
    /// or machine wide.
    /// </summary>
    DefaultInstallDirectory,

    /// <summary>
    /// An MSYS2 installation, which ships the MinGW flavor.
    /// </summary>
    Msys2,

    /// <summary>
    /// The runtime tree the application ships next to itself, at
    /// <c>runtimes\{rid}\bin</c> below the application directory.
    /// </summary>
    BundledRuntime,

    /// <summary>
    /// The search path of the process, as the last resort: the module was
    /// handed to the operating system loader as a plain file name and the
    /// loader decided where it came from. No directory is pinned, unlike with
    /// <see cref="PathDirectory"/>.
    /// </summary>
    ProcessSearchPath,
}
