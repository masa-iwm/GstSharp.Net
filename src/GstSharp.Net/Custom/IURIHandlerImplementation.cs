namespace Gst;

/// <summary>
/// What a managed element implements to be a <c>GstURIHandler</c>, the
/// interface that lets <c>gst_element_make_from_uri</c> and everything built on
/// it - <c>uridecodebin</c>, <c>playbin</c> - pick the element for a URI.
/// </summary>
/// <remarks>
/// <para>
/// The two static members answer for the <em>type</em>: GStreamer asks them
/// while the element is registered, long before any instance exists, and the
/// element factory keeps a copy of what they said. They therefore cannot depend
/// on anything an instance knows, and they have to be constants of the type.
/// </para>
/// <para>
/// The two instance members answer for one element and can be called from any
/// thread: <c>uridecodebin</c> asks on its autoplug thread, a pipeline
/// description asks on the thread that parsed it. <see cref="SetUri"/> in
/// particular is called before the element is in a pipeline, so it should
/// store the URI and no more.
/// </para>
/// <para>
/// Declaring the interface to GObject is a separate step:
/// <c>Gst.URIHandlerImplementation.For&lt;TSelf&gt;()</c> builds the entry that
/// goes into <c>SubclassOptions.Interfaces</c> when the subclass is defined.
/// Implementing this interface alone attaches nothing.
/// </para>
/// </remarks>
public interface IURIHandlerImplementation
{
    /// <summary>
    /// Gets whether elements of this type produce or consume the URIs they
    /// handle.
    /// </summary>
    /// <remarks>
    /// It has to be <see cref="URIType.Src"/> or <see cref="URIType.Sink"/>:
    /// GStreamer refuses to register an element whose handler answers
    /// <see cref="URIType.Unknown"/>.
    /// </remarks>
    static abstract URIType UriType { get; }

    /// <summary>
    /// Gets the URI protocols elements of this type handle, such as
    /// <c>file</c> or <c>rtsp</c>.
    /// </summary>
    /// <remarks>
    /// The list has to be non empty and to hold no empty entry. It is read once
    /// and pinned for the life of the process, because
    /// <c>gst_uri_handler_get_protocols</c> hands the array straight out to its
    /// callers without copying it.
    /// </remarks>
    static abstract IReadOnlyList<string> Protocols { get; }

    /// <summary>
    /// Returns the URI this element currently handles, or
    /// <see langword="null"/> when it has none.
    /// </summary>
    /// <returns>The URI, or <see langword="null"/>.</returns>
    string? GetUri();

    /// <summary>
    /// Points this element at a URI.
    /// </summary>
    /// <param name="uri">The URI, whose protocol is one of <see cref="Protocols"/>.</param>
    /// <param name="error">
    /// Set to the reason the URI was refused when the method answers
    /// <see langword="false"/>, or left <see langword="null"/> to have the
    /// runtime report a generic <c>GST_URI_ERROR_BAD_URI</c>.
    /// </param>
    /// <returns>Whether the URI was accepted.</returns>
    /// <remarks>
    /// <para>
    /// The call arrives on the caller's thread and while the element is not yet
    /// in a pipeline. Store the URI; do not open anything.
    /// </para>
    /// <para>
    /// GStreamer itself synthesises no error for a refusal, and
    /// <c>gst_element_make_from_uri</c> reads the one it was given, so the
    /// runtime always fills one in - either the one left here or a
    /// <c>GST_URI_ERROR_BAD_URI</c> naming the type.
    /// </para>
    /// </remarks>
    bool SetUri(string uri, out Gst.GLib.GException? error);
}
