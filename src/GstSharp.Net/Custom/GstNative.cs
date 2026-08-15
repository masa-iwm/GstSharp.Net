using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written part of
/// the binding needs.
/// </summary>
/// <remarks>
/// Every signature is blittable: strings are passed as <c>byte*</c> and encoded
/// by <see cref="Gst.Interop.GMarshal"/>, and <c>gboolean</c> is an
/// <see cref="int"/>. The generated bindings bring their own imports; only what
/// <c>GstSharp.Initialize</c> and <see cref="MiniObject"/> need lives here.
/// </remarks>
internal static unsafe partial class GstNative
{
    /// <summary>
    /// Initialises GStreamer, reporting failures through a <c>GError</c>
    /// instead of terminating the process.
    /// </summary>
    /// <param name="argc">
    /// The number of entries in <paramref name="argv"/>, or <c>null</c> when no
    /// command line is passed.
    /// </param>
    /// <param name="argv">
    /// The address of the argument vector, whose first entry is the program
    /// name, or <c>null</c>. GStreamer removes the arguments it understands
    /// from the vector.
    /// </param>
    /// <param name="error">Receives the <c>GError</c> of a failed call.</param>
    /// <returns>Non zero when GStreamer was initialised.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_init_check")]
    internal static partial int InitCheck(int* argc, byte*** argv, nint* error);

    [LibraryImport("Gst", EntryPoint = "gst_version")]
    internal static partial void GetVersion(out uint major, out uint minor, out uint micro, out uint nano);

    [LibraryImport("Gst", EntryPoint = "gst_version_string")]
    internal static partial nint VersionString();

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_ref")]
    internal static partial nint MiniObjectRef(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_unref")]
    internal static partial void MiniObjectUnref(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_is_writable")]
    internal static partial int MiniObjectIsWritable(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_make_writable")]
    internal static partial nint MiniObjectMakeWritable(nint miniObject);
}
