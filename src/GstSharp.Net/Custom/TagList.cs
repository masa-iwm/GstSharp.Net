using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Gst.Interop;

namespace Gst;

/// <content>
/// The generic readers of a tag, for the tags whose type has no typed getter,
/// and the writer that puts a value under one.
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
/// There are two readers because a tag may carry more than one value.
/// <see cref="GetValueIndex(string, uint)"/> reads the value at one index and
/// <see cref="CopyValue(string)"/> reads the whole tag merged into one value,
/// which are the two questions the C API has separate calls for.
/// </para>
/// <para>
/// The other half is <see cref="AddValue(TagMergeMode, string, in Gst.GObject.Value)"/>,
/// which is what makes a tag list writable from managed code at all: every
/// setter of C is variadic and the generator emits none of them, so before it a
/// tag list could be read but never filled. The typed conveniences —
/// <see cref="AddString(TagMergeMode, string, string)"/> and its neighbours —
/// build the value and hand it on, and they are named after the getter of the
/// same type.
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

    /// <summary>
    /// Stores a value under a tag, combining it with what the tag already
    /// carries the way <paramref name="mode"/> says.
    /// </summary>
    /// <param name="mode">
    /// What to do about the values the tag already has.
    /// <see cref="TagMergeMode.Replace"/> and
    /// <see cref="TagMergeMode.ReplaceAll"/> drop them,
    /// <see cref="TagMergeMode.Append"/> and
    /// <see cref="TagMergeMode.Prepend"/> add to them, and
    /// <see cref="TagMergeMode.Keep"/> and <see cref="TagMergeMode.KeepAll"/>
    /// leave a tag that already has a value alone.
    /// </param>
    /// <param name="tag">The name of the tag, for example <c>title</c>.</param>
    /// <param name="value">
    /// The value to store, which has to hold the type the tag was registered
    /// with. <b>The list takes a copy of it</b>, so the caller keeps owning what
    /// it passed and still has to dispose it.
    /// </param>
    /// <remarks>
    /// <para>
    /// This is <c>gst_tag_list_add_value</c>, the one writer of a tag list that
    /// is not variadic and therefore the one a binding can offer. It is the
    /// mirror of <see cref="GetValueIndex(string, uint)"/>: what goes in is a
    /// <see cref="Gst.GObject.Value"/> of the caller's, what stays behind is a
    /// copy of it that the list owns, and neither side can release what the
    /// other holds.
    /// </para>
    /// <para>
    /// <b>The list has to be writable</b>, which means that this wrapper holds
    /// the only reference to it — <see cref="MiniObject.IsWritable"/> says so.
    /// A fresh <see cref="NewEmpty"/> is writable; a list that arrived on a
    /// <c>GST_MESSAGE_TAG</c> is shared with whoever posted it and is not, so it
    /// is <see cref="Copy"/> that has to be written. In C the same misuse is a
    /// <c>g_return_if_fail</c> on the console and a write that silently did not
    /// happen.
    /// </para>
    /// <para>
    /// <b>The tag has to be registered</b>, because that registration is what
    /// says which type the tag holds and how several of its values merge.
    /// <see cref="Global.TagExists(string)"/> asks the question and
    /// <c>gst_tag_register</c> answers it for a tag of an application's own. An
    /// unregistered tag is a <c>g_warning</c> and a silent no-op in C, so it is
    /// refused here before the call rather than after it, and so is a value
    /// whose type is not the one the tag was registered with:
    /// <c>gst_tag_list_add_value</c> does not convert. The one exception is the
    /// one C makes, a <c>GstValueList</c>, which stands for several values of
    /// the tag at once.
    /// </para>
    /// <para>
    /// <b><see cref="TagMergeMode.ReplaceAll"/> replaces this tag, not the whole
    /// list.</b> The variadic <c>gst_tag_list_add</c> clears every field first;
    /// <c>gst_tag_list_add_value</c> does not, so here the two replacing modes
    /// do the same thing. Whether <see cref="TagMergeMode.Append"/> and
    /// <see cref="TagMergeMode.Prepend"/> accumulate depends on the tag as well:
    /// only a tag that was registered with a merge function — <c>artist</c> and
    /// <c>title</c> are, <c>bitrate</c> is not — collects several values, and on
    /// the others appending keeps what is already there.
    /// </para>
    /// <code>
    /// using Gst.TagList tags = Gst.TagList.NewEmpty();
    /// tags.AddString(Gst.TagMergeMode.Replace, "title", "Take Five");
    /// tags.AddString(Gst.TagMergeMode.Append, "artist", "Dave Brubeck");
    /// tags.AddUint(Gst.TagMergeMode.Replace, "track-number", 3);
    /// </code>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or <paramref name="value"/>
    /// holds nothing or holds something other than the type of the tag.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddValue(TagMergeMode mode, string tag, in Gst.GObject.Value value)
    {
        ArgumentNullException.ThrowIfNull(tag);

        // GST_TAG_MODE_IS_VALID, which the C call answers with a warning and a
        // write that did not happen.
        if (mode is <= TagMergeMode.Undefined or >= TagMergeMode.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "The merge mode is not one of the modes a tag list knows.");
        }

        // The handle is read first, so that a disposed wrapper throws before
        // anything is encoded.
        nint list = Handle;

        if (!IsWritable)
        {
            throw new InvalidOperationException(
                $"The tag list is not writable, so \"{tag}\" cannot be added: somebody else holds a " +
                "reference to it and writing would change what they see. This is the " +
                "gst_mini_object_is_writable guard of gst_tag_list_add_value. Write a Copy() of the " +
                "list instead.");
        }

        // An unregistered tag has neither a type nor a merge function, and the C
        // call answers it with a warning on the console and a list that did not
        // change. A silent no-op is not an answer a caller can act on.
        if (!Global.TagExists(tag))
        {
            throw new ArgumentException(
                $"\"{tag}\" is not a registered tag, so nothing can be stored under it. " +
                "Register it with gst_tag_register first, or use one of the tags GStreamer registers.",
                nameof(tag));
        }

        if (value.IsEmpty)
        {
            throw new ArgumentException(
                $"An empty value cannot be stored under the \"{tag}\" tag: it has no type, and a tag " +
                "only accepts a value of the type it was registered with.",
                nameof(value));
        }

        Gst.GObject.GType tagType = Global.TagGetType(tag);

        // G_VALUE_HOLDS (value, info->type) || GST_VALUE_HOLDS_LIST (value),
        // which is what gst_tag_list_add_value_internal demands. It converts
        // nothing, so a value of another type is refused rather than coerced.
        if (!value.Type.IsA(tagType) && !value.Type.IsA(ValueListType))
        {
            throw new ArgumentException(
                $"The \"{tag}\" tag holds {tagType.Name} and the value holds {value.Type.Name}. " +
                "gst_tag_list_add_value stores the value as it is and converts nothing, so it has to " +
                "be of the type of the tag, or a GstValueList of several of them.",
                nameof(value));
        }

        Span<byte> buffer = stackalloc byte[GMarshal.StackBufferSize];
        using Utf8Scope scope = GMarshal.StackUtf8(tag, buffer);

        GstTagListAddValue(list, (int)mode, scope.Pointer, ref Unsafe.AsRef(in value).NativeValue);

        // The handle was read before the call, so nothing keeps this wrapper
        // alive across it on its own.
        GC.KeepAlive(this);
    }

    /// <summary>
    /// Stores a string under a tag, for the tags whose type is
    /// <c>G_TYPE_STRING</c> — <c>title</c>, <c>artist</c>, <c>album</c> and
    /// their neighbours.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag, for example <c>title</c>.</param>
    /// <param name="value">The text to store, which the list copies.</param>
    /// <remarks>
    /// The value is built, handed to
    /// <see cref="AddValue(TagMergeMode, string, in Gst.GObject.Value)"/> and
    /// released again, so every rule that call states holds here: the list must
    /// be writable, the tag must be registered, and it must be a tag that holds
    /// a string. <see cref="GetString(string, out string?)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tag"/> or <paramref name="value"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a string.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddString(TagMergeMode mode, string tag, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.String);
        content.SetString(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores a 32 bit integer under a tag, for the tags whose type is
    /// <c>G_TYPE_INT</c>.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetInt(string, out int)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>gint</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddInt(TagMergeMode mode, string tag, int value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.Int);
        content.SetInt(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores an unsigned 32 bit integer under a tag, for the tags whose type is
    /// <c>G_TYPE_UINT</c> — <c>track-number</c>, <c>bitrate</c> and their
    /// neighbours.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag, for example <c>track-number</c>.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetUint(string, out uint)"/> reads it back.
    /// The spelling follows the getter, which is the spelling of the C call.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>guint</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddUint(TagMergeMode mode, string tag, uint value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.UInt);
        content.SetUInt(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores a 64 bit integer under a tag, for the tags whose type is
    /// <c>G_TYPE_INT64</c>.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetInt64(string, out long)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>gint64</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddInt64(TagMergeMode mode, string tag, long value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.Int64);
        content.SetInt64(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores an unsigned 64 bit integer under a tag, for the tags whose type is
    /// <c>G_TYPE_UINT64</c> — <c>duration</c> among them.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag, for example <c>duration</c>.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetUint64(string, out ulong)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>guint64</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddUint64(TagMergeMode mode, string tag, ulong value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.UInt64);
        content.SetUInt64(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores a double precision number under a tag, for the tags whose type is
    /// <c>G_TYPE_DOUBLE</c> — the replay gain tags among them.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag, for example <c>replaygain-track-gain</c>.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetDouble(string, out double)"/> reads it
    /// back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>gdouble</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddDouble(TagMergeMode mode, string tag, double value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.Double);
        content.SetDouble(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores a single precision number under a tag, for the tags whose type is
    /// <c>G_TYPE_FLOAT</c> — of which GStreamer registers none, so these are
    /// tags of an application's own.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag.</param>
    /// <param name="value">The number to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetFloat(string, out float)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>gfloat</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddFloat(TagMergeMode mode, string tag, float value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.Float);
        content.SetFloat(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Stores a boolean under a tag, for the tags whose type is
    /// <c>G_TYPE_BOOLEAN</c>.
    /// </summary>
    /// <param name="mode">What to do about the values the tag already has.</param>
    /// <param name="tag">The name of the tag.</param>
    /// <param name="value">The flag to store.</param>
    /// <remarks>
    /// This is <see cref="AddString(TagMergeMode, string, string)"/> for the
    /// other type, and <see cref="GetBoolean(string, out bool)"/> reads it back.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="tag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="mode"/> is not one of the merge modes.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tag"/> is not a registered tag, or is not a tag that
    /// holds a <c>gboolean</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The list is not writable.</exception>
    /// <exception cref="ObjectDisposedException">The wrapper was disposed.</exception>
    public void AddBoolean(TagMergeMode mode, string tag, bool value)
    {
        using Gst.GObject.Value content = Gst.GObject.Value.New(Gst.GObject.GType.Boolean);
        content.SetBoolean(value);
        AddValue(mode, tag, content);
    }

    /// <summary>
    /// Gets <c>GST_TYPE_LIST</c>, the type of the value that stands for several
    /// values of one tag.
    /// </summary>
    /// <remarks>
    /// It is a fundamental type GStreamer registers while it initialises, so it
    /// is not a compile time constant and has to be asked for. The entry point
    /// resolves it out of a <c>g_once</c>, which is cheap enough not to be
    /// cached here.
    /// </remarks>
    private static Gst.GObject.GType ValueListType => new(GstValueListGetType());

    /// <summary>The <c>gst_tag_list_get_value_index</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_get_value_index")]
    private static partial nint GstTagListGetValueIndex(nint list, byte* tag, uint index);

    /// <summary>The <c>gst_tag_list_add_value</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_add_value")]
    private static partial void GstTagListAddValue(
        nint list,
        int mode,
        byte* tag,
        ref Gst.GObject.GValueNative value);

    /// <summary>The <c>gst_value_list_get_type</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_value_list_get_type")]
    private static partial nuint GstValueListGetType();

    /// <summary>The <c>gst_tag_list_copy_value</c> entry point.</summary>
    [LibraryImport("Gst", EntryPoint = "gst_tag_list_copy_value")]
    private static partial int GstTagListCopyValue(
        ref Gst.GObject.GValueNative dest,
        nint list,
        byte* tag);
}
