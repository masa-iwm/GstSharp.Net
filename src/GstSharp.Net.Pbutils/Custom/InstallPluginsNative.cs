using System.Runtime.InteropServices;

namespace Gst.Pbutils;

/// <summary>
/// Raw entry points of <c>libgstpbutils-1.0</c> that the hand written plugin
/// installer glue needs.
/// </summary>
/// <remarks>
/// <c>gst_install_plugins_async</c> is imported by hand because its callback
/// runs exactly once when the call answers
/// <see cref="InstallPluginsReturn.StartedOk"/> and never otherwise, so the
/// state has to be released at the call site on every other return. No
/// generated shape keys on the value a call answered.
/// </remarks>
internal static unsafe partial class InstallPluginsNative
{
    /// <summary>Asks the installer helper for a set of plugins.</summary>
    /// <param name="details">The NULL terminated vector of installer details.</param>
    /// <param name="ctx">The context, or <c>0</c>.</param>
    /// <param name="func">The function that is called with the result.</param>
    /// <param name="userData">The state of <paramref name="func"/>.</param>
    /// <returns>Whether an external installer could be started.</returns>
    [LibraryImport("GstPbutils", EntryPoint = "gst_install_plugins_async")]
    internal static partial InstallPluginsReturn InstallPluginsAsync(
        nint* details,
        nint ctx,
        nint func,
        nint userData);
}
