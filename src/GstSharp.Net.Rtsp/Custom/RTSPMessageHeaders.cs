using System.Runtime.InteropServices;

namespace Gst.Rtsp;

/// <content>
/// The header serialisation of a message, whose output the C call writes into
/// a <c>GString</c> the caller owns.
/// </content>
/// <remarks>
/// <c>gst_rtsp_message_append_headers</c> takes a <c>GString</c>, which is a
/// growable buffer of GLib and has no place in a managed signature: the caller
/// would have to allocate one, keep it alive across the call and free it
/// afterwards. The member below owns that buffer for the length of the call
/// and appends what it holds to a
/// <see cref="System.Text.StringBuilder"/> instead, which is the same role on
/// this side of the boundary.
/// </remarks>
public sealed partial class RTSPMessage
{
    /// <summary>
    /// Appends the headers of this message to <paramref name="builder"/>, in
    /// the form they take on the wire.
    /// </summary>
    /// <param name="builder">The builder to append to.</param>
    /// <returns>
    /// <see cref="RTSPResult.Ok"/>, which is what
    /// <c>gst_rtsp_message_append_headers</c> answers once it has appended.
    /// Its <see cref="RTSPResult.Einval"/> branch guards a null message and a
    /// null buffer, and neither can arrive here: reading the handle of a
    /// disposed wrapper throws <see cref="ObjectDisposedException"/> first,
    /// and the buffer belongs to this member. The value is passed on rather
    /// than dropped because it is the C contract, not because a caller has a
    /// second answer to expect.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_rtsp_message_append_headers</c>. Every header is written
    /// as <c>Name: value</c> followed by CRLF, in the order the message holds
    /// them, so a message with no header appends nothing at all. The blank line
    /// that separates the headers from the body is <b>not</b> written: this is
    /// the header block on its own, and whoever assembles the request adds the
    /// terminator.
    /// </para>
    /// <para>
    /// Nothing already in <paramref name="builder"/> is touched, which is what
    /// makes the call composable with the request line that precedes it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ObjectDisposedException">This wrapper was disposed.</exception>
    public unsafe RTSPResult AppendHeaders(System.Text.StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The empty initialiser is what makes the GString a buffer to append
        // to rather than one that already holds something.
        nint storage = GStringNew(null);
        if (storage == 0)
        {
            throw new InvalidOperationException("g_string_new returned no string.");
        }

        try
        {
            int nativeResult = GstRtspMessageAppendHeaders(Handle, storage);

            // Reading Handle is the last use of this wrapper, so without this
            // the collector may finalize it while the call is still running.
            System.GC.KeepAlive(this);

            GStringNative* text = (GStringNative*)storage;
            if (text->Str is not null && text->Len > 0)
            {
                builder.Append(
                    System.Text.Encoding.UTF8.GetString(
                        new System.ReadOnlySpan<byte>(text->Str, checked((int)text->Len))));
            }

            return (RTSPResult)nativeResult;
        }
        finally
        {
            // TRUE releases the character buffer with the string, which is
            // what makes this the whole of the cleanup.
            _ = GStringFree(storage, 1);
        }
    }

    /// <summary>
    /// The layout of a <c>GString</c>, which is public in GLib and is read
    /// here rather than gone through the accessor-less API of the type.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct GStringNative
    {
        /// <summary>The characters, which are NUL terminated as well.</summary>
        public byte* Str;

        /// <summary>How many characters <see cref="Str"/> holds.</summary>
        public nuint Len;

        /// <summary>How many it has room for.</summary>
        public nuint AllocatedLen;
    }

    /// <summary>The <c>gst_rtsp_message_append_headers</c> entry point.</summary>
    [LibraryImport("GstRtsp", EntryPoint = "gst_rtsp_message_append_headers")]
    private static partial int GstRtspMessageAppendHeaders(nint msg, nint str);

    /// <summary>The <c>g_string_new</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_string_new")]
    private static unsafe partial nint GStringNew(byte* init);

    /// <summary>The <c>g_string_free</c> entry point.</summary>
    [LibraryImport("GLib", EntryPoint = "g_string_free")]
    private static partial nint GStringFree(nint str, int freeSegment);
}
