using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The generic reader of a tag, for the tags whose type has no typed getter.
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

    /// <summary>The <c>gst_tag_list_get_value_index</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_get_value_index")]
    private static partial nint GstTagListGetValueIndex(nint list, byte* tag, uint index);
}
