namespace Gst;

/// <summary>
/// The combinations of <see cref="BufferCopyFlags"/> that the C headers define
/// as convenience constants.
/// </summary>
/// <remarks>
/// <para>
/// <c>GST_BUFFER_COPY_METADATA</c> and <c>GST_BUFFER_COPY_ALL</c> are
/// preprocessor macros in <c>gst/gstbuffer.h</c>, not members of the
/// <c>GstBufferCopyFlags</c> enumeration, so they do not appear in the
/// introspection data and the generated <see cref="BufferCopyFlags"/> does not
/// carry them. A C# enumeration cannot be extended by a second declaration —
/// <c>partial</c> does not apply to enumerations — so the composites live here,
/// next to the type they belong to rather than inside it.
/// </para>
/// <para>
/// The values mirror the header exactly, including the part that is easy to get
/// wrong: <see cref="BufferCopyFlags.Merge"/> is <b>not</b> part of
/// <see cref="All"/>. Merging the memory of the source into memory the
/// destination already holds is a separate request, and the copy functions of
/// GStreamer do not make it for you.
/// </para>
/// </remarks>
public static class BufferCopy
{
    /// <summary>
    /// Everything about a buffer except its memory: the flags, the five
    /// timestamp and offset fields, and the metadata attached to it.
    /// </summary>
    /// <remarks>
    /// <c>GST_BUFFER_COPY_METADATA</c>, value 7.
    /// </remarks>
    public const BufferCopyFlags Metadata =
        BufferCopyFlags.Flags | BufferCopyFlags.Timestamps | BufferCopyFlags.Meta;

    /// <summary>
    /// Everything about a buffer, its memory included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GST_BUFFER_COPY_ALL</c>, value 15: <see cref="Metadata"/> plus
    /// <see cref="BufferCopyFlags.Memory"/>.
    /// </para>
    /// <para>
    /// The memory is referenced, not duplicated, unless a block is marked
    /// <c>NO_SHARE</c>: the copy and the original share the same bytes, and
    /// neither of them is writable while both live. Add
    /// <see cref="BufferCopyFlags.Deep"/> to force a real copy of the bytes.
    /// </para>
    /// </remarks>
    public const BufferCopyFlags All = Metadata | BufferCopyFlags.Memory;
}
