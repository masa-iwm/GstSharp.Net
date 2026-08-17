using System.Runtime.InteropServices;

namespace Gst;

/// <summary>
/// Raw entry points that the hand written message glue needs.
/// </summary>
/// <remarks>
/// <para>
/// <c>gst_message_parse_error</c>, <c>gst_message_parse_warning</c> and
/// <c>gst_message_parse_info</c> hand a <c>GError</c> out through a
/// <c>GError**</c>. The generator does not emit those signatures, because a
/// <c>GError</c> that is a result rather than a failure of the call has no
/// place in the throwing convention of the generated bindings: an error
/// message is a value the application inspects, not an exception the binding
/// raises. All three entry points therefore belong on the skip list of
/// <c>girs/overlays/fixups.json</c> once the function emitter covers them, so
/// that there is a single way to read them.
/// </para>
/// <para>
/// <c>g_error_free</c> is imported here as well: the <c>GError</c> of both
/// calls is a copy that the caller owns, and the runtime library keeps its own
/// import of it internal.
/// </para>
/// <para>
/// <c>gst_message_parse_property_notify</c> is here for a third reason: two of
/// its three out parameters are borrowed — a <c>const gchar **</c> and a
/// <c>const GValue **</c> that both point into the structure of the message —
/// and the generator emits no signature that hands a pointer out without
/// saying who owns it. The hand written wrapper answers the question by
/// copying: the name becomes a managed string and the value a
/// <see cref="Gst.GObject.Value"/> of the caller's own.
/// </para>
/// <para>
/// <c>gst_message_new_application</c> is here for a fourth: its
/// <c>structure</c> parameter is <c>transfer-ownership="full"</c>, and the
/// generator emits no call that takes a wrapper over, because handing the only
/// value of a wrapper to the library would let both of them release it. See
/// <see cref="Message.NewApplication"/> and <c>docs/ownership.md</c>.
/// </para>
/// <para>
/// <c>gst_message_copy</c> is imported for a different reason, the one
/// <c>gst_buffer_copy</c> has: the gir marks it <c>introspectable="0"</c>, so
/// the generator skips it and no overlay can bring it back. It is a static
/// inline function of <c>gst/gstmessage.h</c> that forwards to
/// <c>gst_mini_object_copy</c>, and the library is built with the inline
/// functions disabled and exports it as a real symbol, which is what makes it
/// importable at all. Should a build ever be met that does not export it,
/// <c>gst_mini_object_copy</c> is the fallback entry point that the inline
/// version calls.
/// </para>
/// </remarks>
internal static unsafe partial class MessageNative
{
    /// <summary>
    /// Extracts the <c>GError</c> and the debug string of a
    /// <see cref="MessageType.Error"/> message.
    /// </summary>
    /// <param name="message">The message to read.</param>
    /// <param name="error">Receives a copy of the error, owned by the caller.</param>
    /// <param name="debug">Receives the debug string, owned by the caller.</param>
    [LibraryImport("Gst", EntryPoint = "gst_message_parse_error")]
    internal static partial void ParseError(nint message, nint* error, nint* debug);

    /// <summary>
    /// Extracts the <c>GError</c> and the debug string of a
    /// <see cref="MessageType.Warning"/> message.
    /// </summary>
    /// <param name="message">The message to read.</param>
    /// <param name="error">Receives a copy of the error, owned by the caller.</param>
    /// <param name="debug">Receives the debug string, owned by the caller.</param>
    [LibraryImport("Gst", EntryPoint = "gst_message_parse_warning")]
    internal static partial void ParseWarning(nint message, nint* error, nint* debug);

    /// <summary>
    /// Extracts the <c>GError</c> and the debug string of a
    /// <see cref="MessageType.Info"/> message.
    /// </summary>
    /// <param name="message">The message to read.</param>
    /// <param name="error">Receives a copy of the error, owned by the caller.</param>
    /// <param name="debug">Receives the debug string, owned by the caller.</param>
    [LibraryImport("Gst", EntryPoint = "gst_message_parse_info")]
    internal static partial void ParseInfo(nint message, nint* error, nint* debug);

    /// <summary>
    /// Extracts the object, the property name and the property value of a
    /// <see cref="MessageType.PropertyNotify"/> message.
    /// </summary>
    /// <param name="message">The message to read.</param>
    /// <param name="object">
    /// Receives the object whose property changed, borrowed from the message.
    /// </param>
    /// <param name="propertyName">
    /// Receives the name of the property, borrowed from the message.
    /// </param>
    /// <param name="propertyValue">
    /// Receives the new value of the property, borrowed from the message, or
    /// <c>0</c> when the watch was installed without values.
    /// </param>
    [LibraryImport("Gst", EntryPoint = "gst_message_parse_property_notify")]
    internal static partial void ParsePropertyNotify(
        nint message,
        nint* @object,
        nint* propertyName,
        nint* propertyValue);

    /// <summary>
    /// Creates a copy of a message: its type, source, timestamp, sequence
    /// number and payload are copied.
    /// </summary>
    /// <param name="message">The message to copy.</param>
    /// <returns>
    /// The copy, which the caller owns, or <c>0</c> when the copy failed.
    /// </returns>
    [LibraryImport("Gst", EntryPoint = "gst_message_copy")]
    internal static partial nint Copy(nint message);

    /// <summary>
    /// Creates a message that carries whatever an application wants to send
    /// through the bus of its own pipeline.
    /// </summary>
    /// <param name="src">The object to post it as, which may be <c>0</c>.</param>
    /// <param name="structure">
    /// The payload, which the call takes over.
    /// </param>
    /// <returns>The message, which the caller owns.</returns>
    [LibraryImport("Gst", EntryPoint = "gst_message_new_application")]
    internal static partial nint NewApplication(nint src, nint structure);

    /// <summary>
    /// Releases a <c>GError</c> that the caller owns.
    /// </summary>
    /// <param name="error">The error to release, never <c>0</c>.</param>
    [LibraryImport("GLib", EntryPoint = "g_error_free")]
    internal static partial void ErrorFree(nint error);
}
