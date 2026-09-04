using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points of <c>libgstreamer-1.0</c> that the hand written part of
/// the binding needs.
/// </summary>
/// <remarks>
/// Every signature is blittable: strings are passed as <c>byte*</c> and encoded
/// by <see cref="Gst.Interop.GMarshal"/>, and <c>gboolean</c> is an
/// <see cref="int"/>. The generated bindings bring their own imports; what
/// lives here is what <c>GstSharp.Initialize</c> and <see cref="MiniObject"/>
/// need, plus <see cref="MiniObjectRef"/>, which a generated member that
/// consumes a mini object argument calls to mint the reference the callee
/// takes over.
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

    /// <summary>
    /// Registers and answers the type of a <c>GstParamSpecFraction</c>, which
    /// is how the binding learns the type without reaching for a data symbol.
    /// </summary>
    [LibraryImport("Gst", EntryPoint = "gst_param_spec_fraction_get_type")]
    internal static partial nuint ParamSpecFractionGetType();

    /// <summary>
    /// Registers and answers the type of a <c>GstParamSpecArray</c>.
    /// </summary>
    [LibraryImport("Gst", EntryPoint = "gst_param_spec_array_get_type")]
    internal static partial nuint ParamSpecArrayGetType();

    /// <summary>
    /// Creates empty caps, which is what a slot answers when it has to answer
    /// caps and the managed override answered none: negotiation fails on it
    /// rather than on a NULL pointer the caller does not check for.
    /// </summary>
    /// <returns>The new caps, with one reference the caller takes over.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_caps_new_empty")]
    internal static partial nint CapsNewEmpty();

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_ref")]
    internal static partial nint MiniObjectRef(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_unref")]
    internal static partial void MiniObjectUnref(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_is_writable")]
    internal static partial int MiniObjectIsWritable(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_make_writable")]
    internal static partial nint MiniObjectMakeWritable(nint miniObject);

    /// <summary>
    /// Describes an element class, for the <c>class_init</c> of a managed
    /// subclass. The strings are copied by GStreamer.
    /// </summary>
    /// <param name="elementClass">The class being initialised.</param>
    /// <param name="longname">The human readable name of the element.</param>
    /// <param name="classification">
    /// The classification, a slash separated list such as
    /// <c>Source/Network</c>.
    /// </param>
    /// <param name="description">What the element does.</param>
    /// <param name="author">Who wrote it.</param>
    [LibraryImport("Gst", EntryPoint = "gst_element_class_set_metadata")]
    internal static partial void ElementClassSetMetadata(
        nint elementClass,
        byte* longname,
        byte* classification,
        byte* description,
        byte* author);

    /// <summary>
    /// Adds a pad template to an element class, taking a reference of its own
    /// and sinking a floating one.
    /// </summary>
    /// <param name="elementClass">The class being initialised.</param>
    /// <param name="padTemplate">The template to add.</param>
    [LibraryImport("Gst", EntryPoint = "gst_element_class_add_pad_template")]
    internal static partial void ElementClassAddPadTemplate(nint elementClass, nint padTemplate);

    /// <summary>
    /// Looks a pad template of an element class up by name.
    /// </summary>
    /// <param name="elementClass">The class to search.</param>
    /// <param name="name">The name of the template.</param>
    /// <returns>The template, which the class keeps owning, or null.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_element_class_get_pad_template")]
    internal static partial nint ElementClassGetPadTemplate(nint elementClass, byte* name);
}
