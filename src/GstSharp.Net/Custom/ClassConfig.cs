using Gst.Interop;

namespace Gst.GObject;

/// <summary>
/// The class of a managed subclass while it is being initialised: what the
/// element is called, and which pads it can have.
/// </summary>
/// <remarks>
/// <para>
/// An instance is handed to the <c>configureClass</c> delegate of
/// <c>DefineSubclass</c>, from inside <c>class_init</c>, and it is only usable
/// for the length of that call. Everything on it is a class level statement
/// that GStreamer expects to be made exactly once, before any instance exists:
/// the metadata that <c>gst-inspect</c> prints, and the pad templates that
/// <c>GstBaseSrc</c>, <c>GstBaseSink</c> and <c>GstBaseTransform</c> create
/// their pads from.
/// </para>
/// <para>
/// <b>Build pad templates before the registration, and only add them here.</b>
/// <c>class_init</c> runs while GObject holds its type lock, and the wrapper
/// interning table of this binding is a lock that is taken <em>around</em>
/// native calls — <c>Gst.GObject.Object</c>'s constructor holds it across
/// <c>g_object_ref_sink</c> and <c>g_object_add_toggle_ref</c>. Creating a
/// wrapper from inside <c>class_init</c> would therefore take that lock while
/// holding the type lock, which is the reverse of the order every other path
/// takes them in, and two threads doing both at once would deadlock. This
/// facade never creates a wrapper: <see cref="SetMetadata"/> passes strings and
/// <see cref="AddPadTemplate"/> passes the handle of a template that already
/// exists. See <c>docs/subclassing.md</c> §5.5 and §9, risk 2.
/// </para>
/// <para>
/// The facade lives with the <c>Gst</c> glue rather than in the GObject layer
/// of the runtime because everything it does is a <c>GstElementClass</c>
/// operation. A module that wants to configure something of its own adds its
/// own facade next to this one.
/// </para>
/// </remarks>
public sealed unsafe class ClassConfig
{
    private readonly nint _elementClass;

    /// <summary>Wraps the class that is being initialised.</summary>
    /// <param name="elementClass">The <c>GstElementClass</c> under construction.</param>
    internal ClassConfig(nint elementClass) => _elementClass = elementClass;

    /// <summary>
    /// Describes the element, which is what <c>gst-inspect-1.0</c> and
    /// <see cref="Gst.Element.GetMetadata(string)"/> report.
    /// </summary>
    /// <param name="longname">The human readable name, for example <c>Counter source</c>.</param>
    /// <param name="classification">
    /// A slash separated classification, for example <c>Source/Testing</c>.
    /// The vocabulary is the one of the GStreamer plugin writer's guide.
    /// </param>
    /// <param name="description">One sentence on what the element does.</param>
    /// <param name="author">
    /// Who wrote it, conventionally <c>Name &lt;mail@example.com&gt;</c>.
    /// </param>
    /// <remarks>
    /// This is <c>gst_element_class_set_metadata</c>, which copies the strings.
    /// </remarks>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument contains a null character.</exception>
    public void SetMetadata(string longname, string classification, string description, string author)
    {
        ArgumentNullException.ThrowIfNull(longname);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(author);

        Span<byte> longnameBuffer = stackalloc byte[GMarshal.StackBufferSize];
        Span<byte> classificationBuffer = stackalloc byte[GMarshal.StackBufferSize];
        Span<byte> descriptionBuffer = stackalloc byte[GMarshal.StackBufferSize];
        Span<byte> authorBuffer = stackalloc byte[GMarshal.StackBufferSize];

        using Utf8Scope longnameScope = GMarshal.StackUtf8(longname, longnameBuffer);
        using Utf8Scope classificationScope = GMarshal.StackUtf8(classification, classificationBuffer);
        using Utf8Scope descriptionScope = GMarshal.StackUtf8(description, descriptionBuffer);
        using Utf8Scope authorScope = GMarshal.StackUtf8(author, authorBuffer);

        GstNative.ElementClassSetMetadata(
            _elementClass,
            longnameScope.Pointer,
            classificationScope.Pointer,
            descriptionScope.Pointer,
            authorScope.Pointer);
    }

    /// <summary>
    /// Adds a pad template to the class, which is how an element declares the
    /// pads it can have and the caps they accept.
    /// </summary>
    /// <param name="template">
    /// The template to add. It has to exist before the registration is made:
    /// building one here would create a wrapper from inside <c>class_init</c>,
    /// which is what the remarks of this class rule out.
    /// </param>
    /// <remarks>
    /// <para>
    /// A <c>GstBaseSrc</c> subclass needs a template named <c>src</c>, a
    /// <c>GstBaseSink</c> subclass one named <c>sink</c> and a
    /// <c>GstBaseTransform</c> subclass both: their instance init creates the
    /// pad from the template of the class, and an element whose class has none
    /// fails to be built at all. <c>DefineSubclass</c> checks for them once the
    /// class has been initialised.
    /// </para>
    /// <para>
    /// This is <c>gst_element_class_add_pad_template</c>, which takes a
    /// reference of its own. <paramref name="template"/> keeps the one the
    /// wrapper holds, so the same template can be added to several classes and
    /// stays usable afterwards.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The template was disposed.</exception>
    public void AddPadTemplate(Gst.PadTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        GstNative.ElementClassAddPadTemplate(_elementClass, template.Handle);
        GC.KeepAlive(template);
    }
}
