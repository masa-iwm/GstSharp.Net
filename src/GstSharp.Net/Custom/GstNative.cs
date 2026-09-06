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
    /// Builds the specification of a fraction property. The result is null when
    /// the default lies outside the range, which GStreamer reports through
    /// <c>g_critical</c> alone.
    /// </summary>
    [LibraryImport("Gst", EntryPoint = "gst_param_spec_fraction")]
    internal static partial nint ParamSpecFraction(
        byte* name,
        byte* nick,
        byte* blurb,
        int minimumNumerator,
        int minimumDenominator,
        int maximumNumerator,
        int maximumDenominator,
        int defaultNumerator,
        int defaultDenominator,
        uint flags);

    /// <summary>
    /// Builds the specification of an array property. The specification of the
    /// elements is referenced and sunk, so a caller that holds a reference of
    /// its own keeps it.
    /// </summary>
    [LibraryImport("Gst", EntryPoint = "gst_param_spec_array")]
    internal static partial nint ParamSpecArray(byte* name, byte* nick, byte* blurb, nint elementSpec, uint flags);

    /// <summary>
    /// Creates empty caps, which is what a slot answers when it has to answer
    /// caps and the managed override answered none: negotiation fails on it
    /// rather than on a NULL pointer the caller does not check for.
    /// </summary>
    /// <returns>The new caps, with one reference the caller takes over.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_caps_new_empty")]
    internal static partial nint CapsNewEmpty();

    /// <summary>The <c>gst_element_factory_make</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_element_factory_make")]
    private static partial nint ElementFactoryMake(byte* factoryName, byte* name);

    /// <summary>
    /// Creates an element without a wrapper, which is what a slot answers when
    /// it has to answer an element and the managed override answered none: the
    /// consumer adds the answer to a bin and sinks it, so a real element there
    /// keeps the reference counting of the normal path while a NULL would not.
    /// </summary>
    /// <param name="factoryName">
    /// The name of the factory. Name one of the core elements: coreelements
    /// is a plugin like any other, but every GStreamer installation ships it
    /// and <c>gst_init</c> picks it up with the registry, so the answer is as
    /// unlikely as it gets to be NULL for want of a plugin.
    /// </param>
    /// <param name="name">The name of the element, or <see langword="null"/> for a generated one.</param>
    /// <returns>
    /// The new element, floating, with the one reference the caller takes
    /// over, or <see cref="nint.Zero"/> when no factory of that name is
    /// registered — which hands the slot the very null answer it is guarding
    /// against.
    /// </returns>
    internal static nint ElementFactoryMakeRaw(string factoryName, string? name)
    {
        System.Span<byte> factoryBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope factoryScope = Gst.Interop.GMarshal.StackUtf8(factoryName, factoryBuffer);
        System.Span<byte> nameBuffer = stackalloc byte[Gst.Interop.GMarshal.StackBufferSize];
        using Gst.Interop.Utf8Scope nameScope = Gst.Interop.GMarshal.StackUtf8(name, nameBuffer);

        return ElementFactoryMake(factoryScope.Pointer, nameScope.Pointer);
    }

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_ref")]
    internal static partial nint MiniObjectRef(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_unref")]
    internal static partial void MiniObjectUnref(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_is_writable")]
    internal static partial int MiniObjectIsWritable(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_make_writable")]
    internal static partial nint MiniObjectMakeWritable(nint miniObject);

    /// <summary>
    /// Creates a copy of a mini object, whatever its type is.
    /// </summary>
    /// <param name="miniObject">The object to copy.</param>
    /// <returns>
    /// The copy, which the caller owns, or <see cref="nint.Zero"/> when the
    /// type of the object has no copy function.
    /// </returns>
    /// <remarks>
    /// This is the exported entry point the per type copies of the headers
    /// forward to: <c>gst_event_copy</c>, <c>gst_sample_copy</c>,
    /// <c>gst_buffer_list_copy</c> and <c>gst_query_copy</c> are static inline
    /// functions of the C headers, and the gir marks every one of them
    /// <c>introspectable="0"</c>, so nothing generates them. The C dispatches
    /// to the copy function the type installed and answers NULL when the type
    /// installed none (gstminiobject.c:182-206); the object it answers carries
    /// the <c>GType</c> of the original and starts unshared and unlocked.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_copy")]
    internal static partial nint MiniObjectCopy(nint miniObject);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_get_qdata")]
    internal static partial nint MiniObjectGetQData(nint miniObject, uint quark);

    [LibraryImport("Gst", EntryPoint = "gst_mini_object_steal_qdata")]
    internal static partial nint MiniObjectStealQData(nint miniObject, uint quark);

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

    /// <summary>Returns the type of the <c>GstURIHandler</c> interface.</summary>
    /// <returns>The <c>GType</c> of the interface.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_uri_handler_get_type")]
    internal static partial nuint UriHandlerGetType();
}
