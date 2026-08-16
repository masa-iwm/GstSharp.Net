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
    /// Releases a <c>GError</c> that the caller owns.
    /// </summary>
    /// <param name="error">The error to release, never <c>0</c>.</param>
    [LibraryImport("GLib", EntryPoint = "g_error_free")]
    internal static partial void ErrorFree(nint error);
}
