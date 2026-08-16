using System.Collections.Frozen;

namespace Gst.Interop;

/// <summary>
/// The file names of one native module on every supported platform.
/// </summary>
internal readonly struct NativeNameEntry
{
    internal NativeNameEntry(string linux, string macOs, string windowsMsvc, string windowsMinGW)
    {
        Linux = linux;
        MacOs = macOs;
        WindowsMsvc = windowsMsvc;
        WindowsMinGW = windowsMinGW;
    }

    /// <summary>Gets the SONAME used on Linux and other ELF platforms.</summary>
    internal string Linux { get; }

    /// <summary>Gets the file name used on macOS.</summary>
    internal string MacOs { get; }

    /// <summary>Gets the file name of the MSVC build on Windows.</summary>
    internal string WindowsMsvc { get; }

    /// <summary>Gets the file name of the MinGW build on Windows.</summary>
    internal string WindowsMinGW { get; }

    /// <summary>
    /// Gets the Windows file name of one flavor.
    /// </summary>
    /// <param name="flavor">The flavor of the installation.</param>
    /// <returns>The file name to load.</returns>
    internal string Windows(GstFlavor flavor) => flavor == GstFlavor.Msvc ? WindowsMsvc : WindowsMinGW;
}

/// <summary>
/// Maps the logical library names that the bindings use in
/// <see cref="System.Runtime.InteropServices.LibraryImportAttribute"/> to the
/// real file names of every platform.
/// </summary>
/// <remarks>
/// The table lives in the runtime library, not in the generated assemblies, so
/// that a single <see cref="NativeLoader"/> owns the whole name space and can
/// keep every module on the same installation.
/// </remarks>
internal static class NativeNames
{
    private static readonly FrozenDictionary<string, NativeNameEntry> Table = Build();

    /// <summary>
    /// Gets the logical name of the module that decides the flavor of a Windows
    /// installation.
    /// </summary>
    internal const string AnchorLogicalName = "Gst";

    /// <summary>
    /// Looks the file names of a logical module name up.
    /// </summary>
    /// <param name="logicalName">The logical name, for example <c>GObject</c>.</param>
    /// <param name="entry">The file names of the module.</param>
    /// <returns><see langword="true"/> when the name belongs to the bindings.</returns>
    internal static bool TryGet(string logicalName, out NativeNameEntry entry) =>
        Table.TryGetValue(logicalName, out entry);

    /// <summary>
    /// Gets the file names that prove that a directory holds a GStreamer
    /// installation of one flavor.
    /// </summary>
    /// <param name="flavor">The flavor to probe for.</param>
    /// <returns>The file names, in the order in which they should be probed.</returns>
    internal static string[] WindowsAnchors(GstFlavor flavor) =>
    [
        WindowsAnchor(flavor),
        Table["GLib"].Windows(flavor),
    ];

    /// <summary>
    /// Gets the file name of the GStreamer library itself, which is what a
    /// directory has to contain to be a GStreamer installation rather than just
    /// a directory that happens to hold a copy of GLib.
    /// </summary>
    /// <param name="flavor">The flavor to probe for.</param>
    /// <returns>The file name of the GStreamer library.</returns>
    internal static string WindowsAnchor(GstFlavor flavor) => Table[AnchorLogicalName].Windows(flavor);

    /// <summary>
    /// Gets the file name of the GStreamer library on the platforms that have
    /// no flavors, which is what a directory has to contain to be a GStreamer
    /// installation.
    /// </summary>
    /// <param name="isMacOs"><see langword="true"/> for macOS, <see langword="false"/> for Linux.</param>
    /// <returns>The file name of the GStreamer library.</returns>
    internal static string UnixAnchor(bool isMacOs) =>
        isMacOs ? Table[AnchorLogicalName].MacOs : Table[AnchorLogicalName].Linux;

    private static FrozenDictionary<string, NativeNameEntry> Build()
    {
        Dictionary<string, NativeNameEntry> table = new(StringComparer.Ordinal)
        {
            ["GLib"] = new(
                "libglib-2.0.so.0",
                "libglib-2.0.0.dylib",
                "glib-2.0-0.dll",
                "libglib-2.0-0.dll"),
            ["GObject"] = new(
                "libgobject-2.0.so.0",
                "libgobject-2.0.0.dylib",
                "gobject-2.0-0.dll",
                "libgobject-2.0-0.dll"),
            ["GModule"] = new(
                "libgmodule-2.0.so.0",
                "libgmodule-2.0.0.dylib",
                "gmodule-2.0-0.dll",
                "libgmodule-2.0-0.dll"),
            ["GstBase"] = new(
                "libgstbase-1.0.so.0",
                "libgstbase-1.0.0.dylib",
                "gstbase-1.0-0.dll",
                "libgstbase-1.0-0.dll"),
            ["GstApp"] = new(
                "libgstapp-1.0.so.0",
                "libgstapp-1.0.0.dylib",
                "gstapp-1.0-0.dll",
                "libgstapp-1.0-0.dll"),
            ["GstVideo"] = new(
                "libgstvideo-1.0.so.0",
                "libgstvideo-1.0.0.dylib",
                "gstvideo-1.0-0.dll",
                "libgstvideo-1.0-0.dll"),
            ["GstAudio"] = new(
                "libgstaudio-1.0.so.0",
                "libgstaudio-1.0.0.dylib",
                "gstaudio-1.0-0.dll",
                "libgstaudio-1.0-0.dll"),
            ["GstPbutils"] = new(
                "libgstpbutils-1.0.so.0",
                "libgstpbutils-1.0.0.dylib",
                "gstpbutils-1.0-0.dll",
                "libgstpbutils-1.0-0.dll"),
            ["GstSdp"] = new(
                "libgstsdp-1.0.so.0",
                "libgstsdp-1.0.0.dylib",
                "gstsdp-1.0-0.dll",
                "libgstsdp-1.0-0.dll"),
            ["GstWebRTC"] = new(
                "libgstwebrtc-1.0.so.0",
                "libgstwebrtc-1.0.0.dylib",
                "gstwebrtc-1.0-0.dll",
                "libgstwebrtc-1.0-0.dll"),
            [AnchorLogicalName] = new(
                "libgstreamer-1.0.so.0",
                "libgstreamer-1.0.0.dylib",
                "gstreamer-1.0-0.dll",
                "libgstreamer-1.0-0.dll"),
        };

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
