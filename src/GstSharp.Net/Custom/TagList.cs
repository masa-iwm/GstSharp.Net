using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The generic readers of a tag, for the tags whose type has no typed getter.
/// </content>
/// <remarks>
/// <para>
/// The generated surface of a tag list covers the types GStreamer has an
/// accessor for — <see cref="GetString(string, out string?)"/>,
/// <see cref="GetSampleIndex(string, uint, out Gst.Sample?)"/> and their
/// neighbours. A tag of any other type could be counted with
/// <see cref="GetTagSize(string)"/> but not read, which is the gap this closes;
/// it is the same gap that <see cref="Structure.GetValue(string)"/> closes for
/// a structure, and the shape is deliberately the same.
/// </para>
/// <para>
/// There are two of them because a tag may carry more than one value.
/// <see cref="GetValueIndex(string, uint)"/> reads the value at one index and
/// <see cref="CopyValue(string)"/> reads the whole tag merged into one value,
/// which are the two questions the C API has separate calls for.
/// </para>
/// </remarks>
public sealed unsafe partial class TagList
{
    /// <summary>
    /// Reads one of the values stored for a tag, as a value of the caller's
    /// own.
    /// </summary>
    /// <param name="tag">The name of the tag, for example <c>title</c>.</param>
    /// <param name="index">
    /// Which of the values to read, counted from zero. A tag list may hold more
    /// than one value per tag; <see cref="GetTagSize(string)"/> says how many.
    /// </param>
    /// <returns>
    /// A copy of the value, which the caller has to dispose. A tag the list
    /// does not carry, and an index past its last value, both produce an empty
    /// value — <see cref="Gst.GObject.Value.IsEmpty"/> is
    /// <see langword="true"/> and disposing it does nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_tag_list_get_value_index</c>, which hands out the value
    /// the list owns, borrowed. A <see cref="Gst.GObject.Value"/> owns what it
    /// holds and unsets it when it is disposed, so what this returns is a copy
    /// taken through <c>g_value_copy</c> — the same shape
    /// <see cref="Structure.GetValue(string)"/> has, and what makes the result
    /// safe to keep after the list is gone.
    /// </para>
    /// <para>
    /// <b>This is the generic tag reader.</b> The typed getters answer for the
    /// types they know; a tag whose type is a fraction, a date, an enumeration
    /// or anything else a plugin registers is read here and printed with
    /// <see cref="Global.ValueSerialize"/>:
    /// </para>
    /// <code>
    /// for (uint i = 0; i &lt; tagList.GetTagSize(tag); i++)
    /// {
    ///     using Gst.GObject.Value value = tagList.GetValueIndex(tag, i);
    ///     Console.WriteLine($"{Global.TagGetNick(tag)}: {Global.ValueSerialize(value)}");
    /// }
    /// </code>
    /// <para>
    /// A missing tag is not an error, for the reason it is not one on a
    /// structure: a tag list is a bag of optional entries rather than a type
    /// with a fixed shape.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.GObject.Value GetValueIndex(string tag, uint index)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // The handle is read first, so that a disposed wrapper throws before
        // anything is encoded.
        nint list = Handle;

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(tag, buffer);

        // Null for a tag that is not there and for an index past the end, which
        // CopyFrom maps onto the empty value.
        nint borrowed = GstTagListGetValueIndex(list, scope.Pointer, index);
        Gst.GObject.Value value = Gst.GObject.Value.CopyFrom(borrowed);

        // The copy is taken while the list is still known to be alive.
        GC.KeepAlive(this);
        return value;
    }

    /// <summary>
    /// Reads a tag as one value, merging the values of a tag that carries
    /// several.
    /// </summary>
    /// <param name="tag">The name of the tag, for example <c>title</c>.</param>
    /// <returns>
    /// The merged value, which the caller has to dispose. A tag the list does
    /// not carry produces an empty value — <see cref="Gst.GObject.Value.IsEmpty"/>
    /// is <see langword="true"/> and disposing it does nothing.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This is <c>gst_tag_list_copy_value</c>, and the difference from
    /// <see cref="GetValueIndex(string, uint)"/> is what it does with a tag
    /// that has more than one value: it applies the merge function the tag was
    /// registered with, so several artists come back as one string rather than
    /// as the first of several. A tag with a single value gives the same answer
    /// either way.
    /// </para>
    /// <para>
    /// <b>This is the reader for printing a tag, and the index one is the
    /// reader for walking it.</b> The C tools use each in its place —
    /// <c>gst-launch-1.0</c> prints the tags of a table of contents entry with
    /// this call and walks the tags of a <c>GST_MESSAGE_TAG</c> by index — and
    /// the choice is about whether the values of a tag are one fact or several.
    /// </para>
    /// <para>
    /// The value is written into storage of the caller's and is owned by the
    /// caller, so unlike the borrowed pointer of
    /// <see cref="GetValueIndex(string, uint)"/> nothing is copied on the way
    /// out.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public Gst.GObject.Value CopyValue(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // The handle is read first, so that a disposed wrapper throws before
        // anything is encoded.
        nint list = Handle;

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(tag, buffer);

        Gst.GObject.Value value = default;

        // The call leaves the value untouched when the tag is not there, and a
        // value that was never initialised is the empty one.
        _ = GstTagListCopyValue(ref value.NativeValue, list, scope.Pointer);

        // The value was filled while the list is still known to be alive.
        GC.KeepAlive(this);
        return value;
    }

    /// <summary>The <c>gst_tag_list_get_value_index</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_get_value_index")]
    private static partial nint GstTagListGetValueIndex(nint list, byte* tag, uint index);

    /// <summary>The <c>gst_tag_list_copy_value</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_copy_value")]
    private static partial int GstTagListCopyValue(
        ref Gst.GObject.GValueNative dest,
        nint list,
        byte* tag);
}
