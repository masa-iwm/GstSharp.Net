using System.Diagnostics.CodeAnalysis;
using Gst.Interop;

namespace Gst;

/// <summary>
/// What <c>GstSharp.Initialize</c> should do.
/// </summary>
/// <remarks>
/// Every property may stay at its default: an application that has GStreamer
/// installed in one of the usual places and does not need to pass command line
/// options to it calls <c>GstSharp.Initialize()</c> without any options at all.
/// </remarks>
public sealed class GstSharpOptions
{
    /// <summary>
    /// Gets or sets the directory that holds the native libraries, or
    /// <see langword="null"/> to let <see cref="NativeLoader"/> find them.
    /// </summary>
    /// <remarks>
    /// On Windows this is the <c>bin</c> directory of the installation, for
    /// example <c>C:\msys64\ucrt64\bin</c>; on Linux and macOS it is the
    /// directory that holds the shared objects. It is only needed for
    /// installations that are not registered anywhere and are not on the search
    /// path of the process.
    /// </remarks>
    public string? NativeSearchPath { get; set; }

    /// <summary>
    /// Gets or sets the build flavor to load on Windows, or
    /// <see langword="null"/> to detect it.
    /// </summary>
    /// <remarks>
    /// The MSVC and the MinGW build use different file names for the same
    /// libraries and cannot be mixed inside one process, so the loader pins one
    /// of them. Setting this restricts the search to installations of that
    /// flavor. It is ignored on every other operating system.
    /// </remarks>
    public GstFlavor? WindowsFlavor { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <c>gst_init</c> should be
    /// skipped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is for applications that initialise GStreamer themselves, for
    /// example because they already do it from native code in the same process.
    /// The rest of <c>GstSharp.Initialize</c> still runs: the loader is
    /// configured, the type registry is frozen, and the version of the library
    /// is read, which means that the native library is loaded either way.
    /// </para>
    /// <para>
    /// <c>gst_init</c> has to have run by the time <c>GstSharp.Initialize</c>
    /// is called, not merely somewhere later. Freezing the registry calls the
    /// <c>get_type</c> function of every registered type, and those of some
    /// GStreamer libraries expect an initialised library and complain to the
    /// GLib log when they do not get one.
    /// </para>
    /// </remarks>
    public bool SkipNativeInit { get; set; }

    /// <summary>
    /// Gets or sets the command line arguments to hand to <c>gst_init</c>, for
    /// example <c>--gst-debug-level=3</c>, without the name of the program.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name of the program is prepended by <c>GstSharp.Initialize</c>,
    /// because GStreamer passes the vector to the option parser of GLib, which
    /// always takes the first entry for the name of the program.
    /// </para>
    /// <para>
    /// GStreamer removes the arguments it understands from the vector. That is
    /// not reflected back into this array, so an application that also parses
    /// the command line itself has to filter the GStreamer options out on its
    /// own.
    /// </para>
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "The array mirrors the argument vector that is handed to gst_init, and the option " +
            "object is a plain settings bag that is read once.")]
    public string[]? InitArgs { get; set; }
}
