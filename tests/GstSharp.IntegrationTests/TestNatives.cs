using System.Runtime.InteropServices;

namespace GstSharp.IntegrationTests;

/// <summary>
/// The entry points of <c>libgstreamer-1.0</c> that the tests need and the
/// binding does not offer yet.
/// </summary>
/// <remarks>
/// Everything that the binding does expose is used through the binding, so that
/// the tests exercise the product code path. What is left here is buffer
/// construction, which the function emitter does not cover yet,
/// <c>gst_parse_launch</c>, which is only used as a source of a <c>GError</c>,
/// and <c>gst_adapter_push</c>, whose transfer-full buffer argument the
/// marshaller does not support.
/// </remarks>
internal static unsafe partial class TestNatives
{
    /// <summary>Creates an empty buffer.</summary>
    /// <returns>The new buffer, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_new")]
    internal static partial nint BufferNew();

    /// <summary>Creates a buffer with preallocated memory.</summary>
    /// <param name="allocator">The allocator to use, or <c>0</c> for the default one.</param>
    /// <param name="size">The number of bytes to allocate.</param>
    /// <param name="parameters">The allocation parameters, or <c>0</c> for the defaults.</param>
    /// <returns>The new buffer, owned by the caller.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_new_allocate")]
    internal static partial nint BufferNewAllocate(nint allocator, nuint size, nint parameters);

    /// <summary>Reads the size of the memory of a buffer.</summary>
    /// <param name="buffer">The buffer to measure.</param>
    /// <returns>The number of bytes in the buffer.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_buffer_get_size")]
    internal static partial nuint BufferGetSize(nint buffer);

    /// <summary>Creates a sample out of a buffer and its caps.</summary>
    /// <param name="buffer">The buffer of the sample, or <c>0</c>.</param>
    /// <param name="caps">The caps of the sample, or <c>0</c>.</param>
    /// <param name="segment">The segment of the sample, or <c>0</c>.</param>
    /// <param name="info">The info structure of the sample, or <c>0</c>.</param>
    /// <returns>The new sample, owned by the caller.</returns>
    /// <remarks>
    /// The sample takes a reference of the buffer and copies the caps; the
    /// generator does not emit constructors of mini objects yet, so the tests
    /// that need one call the C function.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_sample_new")]
    internal static partial nint SampleNew(nint buffer, nint caps, nint segment, nint info);

    /// <summary>Builds a pipeline from its textual description.</summary>
    /// <param name="description">The description, as UTF-8.</param>
    /// <param name="error">Receives the <c>GError</c> of a failed call.</param>
    /// <returns>The pipeline, or <c>0</c> when the description was rejected.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_parse_launch")]
    internal static partial nint ParseLaunch(byte* description, nint* error);

    /// <summary>Pulls a sample out of an <c>appsink</c>, or gives up after a timeout.</summary>
    /// <param name="appsink">The sink to pull from.</param>
    /// <param name="timeout">How long to wait, in nanoseconds.</param>
    /// <returns>The sample, owned by the caller, or <c>0</c>.</returns>
    /// <remarks>
    /// The C call is the yardstick the emission of the action signal of the
    /// same name is compared against, ownership included.
    /// </remarks>
    [LibraryImport("GstApp", EntryPoint = "gst_app_sink_try_pull_sample")]
    internal static partial nint AppSinkTryPullSample(nint appsink, ulong timeout);

    /// <summary>Adds a buffer to the end of the queue of an adapter.</summary>
    /// <param name="adapter">The adapter to push into.</param>
    /// <param name="buffer">The buffer, whose reference the adapter takes over.</param>
    /// <remarks>
    /// The call is <c>transfer-ownership="full"</c> on the buffer, which is why
    /// the generator refuses the signature: the marshaller has no way to hand a
    /// wrapper over. The caller has to give it a reference of its own.
    /// </remarks>
    [LibraryImport("GstBase", EntryPoint = "gst_adapter_push")]
    internal static partial void AdapterPush(nint adapter, nint buffer);

    /// <summary>Posts a message on a bus, which takes the reference over.</summary>
    /// <param name="bus">The bus to post on.</param>
    /// <param name="message">The message, whose reference the bus takes over.</param>
    /// <returns>Non zero when the message was posted, zero when the bus is flushing.</returns>
    /// <remarks>
    /// The call is <c>transfer-ownership="full"</c> on the message, which is
    /// why the generator refuses the signature: the marshaller has no way to
    /// hand a wrapper over. Posting by hand is what lets the sync handler tests
    /// account for the reference of the poster on their own — that reference is
    /// the one a handler that answers <c>Drop</c> is responsible for, and a test
    /// that cannot see it cannot prove that it is released exactly once.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_bus_post")]
    internal static partial int BusPost(nint bus, nint message);

    /// <summary>Releases a reference of a <c>GstObject</c>.</summary>
    /// <param name="obj">The object to release.</param>
    [LibraryImport("Gst", EntryPoint = "gst_object_unref")]
    internal static partial void ObjectUnref(nint obj);

    /// <summary>Changes the state of an element that has no wrapper.</summary>
    /// <param name="element">The element to drive.</param>
    /// <param name="state">The <c>GstState</c> to reach.</param>
    /// <returns>The <c>GstStateChangeReturn</c> of the change.</returns>
    /// <remarks>
    /// The binding drives elements through <c>Gst.Element</c>, and wrapping is
    /// exactly what the construction window test must not do: it needs a live
    /// native element whose vfuncs fire while no wrapper is interned, which is
    /// the state <c>g_object_new</c> leaves an instance in.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_element_set_state")]
    internal static partial int ElementSetState(nint element, int state);

    /// <summary>Reads the state of an element that has no wrapper.</summary>
    /// <param name="element">The element to inspect.</param>
    /// <param name="state">Receives the current <c>GstState</c>.</param>
    /// <param name="pending">Receives the pending <c>GstState</c>.</param>
    /// <param name="timeout">How long to wait, in nanoseconds.</param>
    /// <returns>The <c>GstStateChangeReturn</c> of the last change.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_element_get_state")]
    internal static partial int ElementGetState(nint element, int* state, int* pending, ulong timeout);

    /// <summary>Takes a reference of a mini object.</summary>
    /// <param name="miniObject">The mini object to reference.</param>
    /// <returns>The mini object.</returns>
    /// <remarks>
    /// A second reference is what makes a mini object unwritable, which is the
    /// state the field setters of <c>Gst.Buffer</c> have to refuse. The binding
    /// keeps its own import of this internal to the module, on purpose.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_ref")]
    internal static partial nint MiniObjectRef(nint miniObject);

    /// <summary>Releases a reference of a mini object.</summary>
    /// <param name="miniObject">The mini object to release.</param>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_unref")]
    internal static partial void MiniObjectUnref(nint miniObject);

    /// <summary>
    /// Asks to be told when a mini object is destroyed, without holding a
    /// reference of its own.
    /// </summary>
    /// <param name="miniObject">The mini object to watch.</param>
    /// <param name="notify">
    /// A <c>GstMiniObjectNotify</c>, called as <c>notify(userData, object)</c>
    /// when the last reference of the object goes away.
    /// </param>
    /// <param name="userData">The state handed to the notification.</param>
    /// <remarks>
    /// This is how a test observes that something was freed rather than
    /// inferring it from a reference count. A leaked object is one whose
    /// notification never arrives, which no count read through the raw mirror
    /// can show: reading the mirror of an object that was freed is undefined,
    /// and reading the mirror of one that was not tells nothing about the rest.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_mini_object_weak_ref")]
    internal static partial void MiniObjectWeakRef(nint miniObject, nint notify, nint userData);

    /// <summary>Installs the one writer GLib hands every log message to.</summary>
    /// <param name="func">The <c>GLogWriterFunc</c> to install.</param>
    /// <param name="userData">The state handed to the writer.</param>
    /// <param name="userDataFree">A <c>GDestroyNotify</c> for the state, or <c>0</c>.</param>
    /// <remarks>
    /// The binding does not bind the GLib log at all, and it should not: this
    /// is a process-wide resource that GLib allows to be claimed once, so a
    /// library that claimed it would be taking it away from the application.
    /// A test assembly is the application. See <see cref="InitializeLogProbe"/>.
    /// </remarks>
    [LibraryImport("GLib", EntryPoint = "g_log_set_writer_func")]
    internal static partial void LogSetWriterFunc(
        delegate* unmanaged[Cdecl]<int, nint, nuint, nint, int> func,
        nint userData,
        nint userDataFree);

    /// <summary>Writes a log message the way GLib would have written it.</summary>
    /// <param name="logLevel">The <c>GLogLevelFlags</c> of the message.</param>
    /// <param name="fields">The structured fields of the message.</param>
    /// <param name="fieldCount">The number of fields.</param>
    /// <param name="userData">The state of the writer.</param>
    /// <returns>The <c>GLogWriterOutput</c> of the write.</returns>
    [LibraryImport("GLib", EntryPoint = "g_log_writer_default")]
    internal static partial int LogWriterDefault(int logLevel, nint fields, nuint fieldCount, nint userData);

    /// <summary>Renders the fields of a log message as one line of text.</summary>
    /// <param name="logLevel">The <c>GLogLevelFlags</c> of the message.</param>
    /// <param name="fields">The structured fields of the message.</param>
    /// <param name="fieldCount">The number of fields.</param>
    /// <param name="useColor">Non zero to add terminal escapes.</param>
    /// <returns>The text, which the caller frees with <c>g_free</c>.</returns>
    /// <remarks>
    /// Formatting through GLib is what keeps a mirror of <c>GLogField</c> out
    /// of the tests: the writer never has to read the array it is given.
    /// </remarks>
    [LibraryImport("GLib", EntryPoint = "g_log_writer_format_fields")]
    internal static partial nint LogWriterFormatFields(int logLevel, nint fields, nuint fieldCount, int useColor);

    /// <summary>Registers a tag, so that a tag list may carry a value under it.</summary>
    /// <param name="name">The name of the tag, which GStreamer interns.</param>
    /// <param name="flag">The <c>GstTagFlag</c> of the tag.</param>
    /// <param name="type">The <c>GType</c> the tag holds.</param>
    /// <param name="nick">A short human readable name.</param>
    /// <param name="blurb">A description of the tag.</param>
    /// <param name="mergeFunc">A <c>GstTagMergeFunc</c>, or <c>0</c> for none.</param>
    /// <remarks>
    /// GStreamer registers no tag whose type is <c>gint</c>, <c>gint64</c> or
    /// <c>gboolean</c>, so three of the typed writers of <c>Gst.TagList</c> have
    /// nothing in the built in registry to be measured against. Registering one
    /// is what the C test suite does in the same situation. A second
    /// registration of the same name and type is a no-op inside GStreamer, so
    /// the tests stay order independent and may run more than once in a process.
    /// The binding does not expose this: a library that registered tags would be
    /// changing process wide state its caller did not ask about.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_tag_register")]
    internal static partial void TagRegister(
        byte* name,
        int flag,
        nuint type,
        byte* nick,
        byte* blurb,
        nint mergeFunc);

    /// <summary>The <c>GType</c> of a <c>GstCaps</c>.</summary>
    /// <returns><c>GST_TYPE_CAPS</c>.</returns>
    /// <remarks>
    /// The generated <c>Gst.Caps.GetGType</c> is internal to the binding, and a
    /// test that wants to initialise a <c>GValue</c> to the type of a caps has
    /// to ask the library itself.
    /// </remarks>
    [LibraryImport("Gst", EntryPoint = "gst_caps_get_type")]
    internal static partial nuint CapsGetType();

    /// <summary>The <c>GType</c> of a <c>GstDiscovererInfo</c>.</summary>
    /// <returns><c>GST_TYPE_DISCOVERER_INFO</c>.</returns>
    /// <remarks>
    /// A <c>GstDiscovererInfo</c> has no public constructor: the library builds
    /// one per discovery and hands it out. A test of the
    /// <c>load-serialized-info</c> signal has to answer one, and the way the
    /// library itself makes an empty one is <c>g_object_new</c> on this type.
    /// </remarks>
    [LibraryImport("GstPbutils", EntryPoint = "gst_discoverer_info_get_type")]
    internal static partial nuint DiscovererInfoGetType();

    /// <summary>Creates an instance of a <c>GType</c> with no properties set.</summary>
    /// <param name="objectType">The type to instantiate.</param>
    /// <param name="count">The number of properties, which is zero here.</param>
    /// <param name="names">The property names, or <c>0</c>.</param>
    /// <param name="values">The property values, or <c>0</c>.</param>
    /// <returns>The new instance, owned by the caller.</returns>
    /// <remarks>
    /// The variadic <c>g_object_new</c> has no safe managed declaration, and
    /// this sibling — GLib 2.54, below the floor of the binding — takes the
    /// same arguments as an array. The binding does not expose either: building
    /// an arbitrary GObject by type is what the subclassing API is for.
    /// </remarks>
    [LibraryImport("GObject", EntryPoint = "g_object_new_with_properties")]
    internal static partial nint ObjectNewWithProperties(nuint objectType, uint count, nint names, nint values);
}
