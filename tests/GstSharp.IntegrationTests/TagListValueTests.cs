using Gst;
using Gst.GObject;
using Gst.Interop;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers the generic value surface of a tag list: the two readers —
/// <see cref="TagList.GetValueIndex"/>, which reaches the tags whose type has
/// no typed getter and the values past the first one, and
/// <see cref="TagList.CopyValue"/>, which merges a tag that carries several
/// values into one — and the writer
/// <see cref="TagList.AddValue(TagMergeMode, string, in Value)"/> with the typed
/// conveniences built on it.
/// </summary>
/// <remarks>
/// The reading tests build their list from its text form, which was the only way
/// to fill one before the writer existed. The writing tests start from
/// <see cref="TagList.NewEmpty"/> instead, because the claims they make are
/// about what the writer puts there: a value that is copied rather than taken
/// over, a merge mode that is obeyed, and every misuse that C answers with a
/// warning on the console answered with an exception before the call.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TagListValueTests
{
    /// <summary>A tag of type <c>gint</c>, of which GStreamer registers none.</summary>
    private const string IntTag = "gstsharp-test-int";

    /// <summary>A tag of type <c>gint64</c>, of which GStreamer registers none.</summary>
    private const string Int64Tag = "gstsharp-test-int64";

    /// <summary>A tag of type <c>gboolean</c>, of which GStreamer core registers none.</summary>
    private const string BooleanTag = "gstsharp-test-boolean";

    /// <summary>A tag of type <c>gfloat</c>, of which GStreamer registers none.</summary>
    private const string FloatTag = "gstsharp-test-float";

    private readonly ITestOutputHelper _output;

    /// <summary>Initialises one test.</summary>
    /// <param name="output">The output of the test.</param>
    public TagListValueTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A tag of a type the typed getters know is readable here as well, and the
    /// value is a copy that outlives the list.
    /// </summary>
    [Fact]
    public void TagsAreReadableAsValues()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, title=(string)Hello, track-number=(uint)3;"));

        using Value title = tags.GetValueIndex("title", 0);
        Assert.False(title.IsEmpty);
        Assert.Equal(GType.String, title.Type);
        Assert.Equal("Hello", title.GetString());
        Assert.Equal("Hello", Global.ValueSerialize(title));

        using Value track = tags.GetValueIndex("track-number", 0);
        Assert.False(track.IsEmpty);
        Assert.Equal(3u, track.GetUInt());
        Assert.Equal("3", Global.ValueSerialize(track));
    }

    /// <summary>
    /// A tag list holds more than one value per tag, and every one of them is
    /// reachable by index.
    /// </summary>
    [Fact]
    public void EveryValueOfATagIsReachableByIndex()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, artist=(string){ \"first\", \"second\" };"));

        Assert.Equal(2u, tags.GetTagSize("artist"));

        using Value first = tags.GetValueIndex("artist", 0);
        using Value second = tags.GetValueIndex("artist", 1);

        _output.WriteLine($"artist[0]={first.GetString()} artist[1]={second.GetString()}");

        Assert.Equal("first", first.GetString());
        Assert.Equal("second", second.GetString());
    }

    /// <summary>
    /// A tag the list does not carry and an index past the last value are both
    /// the empty value, the way a missing field of a structure is.
    /// </summary>
    [Fact]
    public void AMissingTagAndAnIndexPastTheEndAreEmpty()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, title=(string)Hello;"));

        using Value missing = tags.GetValueIndex("album", 0);
        Assert.True(missing.IsEmpty);

        using Value pastTheEnd = tags.GetValueIndex("title", 1);
        Assert.True(pastTheEnd.IsEmpty);

        Assert.Equal(0u, tags.GetTagSize("album"));
    }

    /// <summary>
    /// The value is a copy: it still holds its content after the list that
    /// carried it is gone.
    /// </summary>
    [Fact]
    public void TheValueOutlivesTheList()
    {
        Value title;

        using (TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, title=(string)Hello;")))
        {
            title = tags.GetValueIndex("title", 0);
        }

        using (title)
        {
            Assert.Equal("Hello", title.GetString());
        }
    }

    /// <summary>
    /// A tag that carries several values is merged into one by
    /// <see cref="TagList.CopyValue"/>, which is the difference from reading it
    /// by index.
    /// </summary>
    [Fact]
    public void SeveralValuesOfATagAreMergedIntoOne()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, artist=(string){ \"first\", \"second\" };"));

        using Value merged = tags.CopyValue("artist");
        Assert.False(merged.IsEmpty);
        Assert.Equal(GType.String, merged.Type);

        string? text = merged.GetString();
        _output.WriteLine($"merged artist={text}");

        Assert.NotNull(text);
        Assert.Contains("first", text, StringComparison.Ordinal);
        Assert.Contains("second", text, StringComparison.Ordinal);

        // The index reader answers the other question and still answers it.
        using Value byIndex = tags.GetValueIndex("artist", 0);
        Assert.Equal("first", byIndex.GetString());
        Assert.NotEqual(byIndex.GetString(), text);
    }

    /// <summary>
    /// A tag with a single value reads the same either way.
    /// </summary>
    [Fact]
    public void OneValuedTagsReadTheSameEitherWay()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, title=(string)Hello, track-number=(uint)3;"));

        using Value copiedTitle = tags.CopyValue("title");
        using Value indexedTitle = tags.GetValueIndex("title", 0);
        Assert.Equal(indexedTitle.GetString(), copiedTitle.GetString());

        using Value copiedTrack = tags.CopyValue("track-number");
        Assert.Equal(GType.UInt, copiedTrack.Type);
        Assert.Equal(3u, copiedTrack.GetUInt());
    }

    /// <summary>
    /// A tag the list does not carry copies as the empty value, the way it
    /// reads as one by index.
    /// </summary>
    [Fact]
    public void AMissingTagCopiesAsTheEmptyValue()
    {
        using TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, title=(string)Hello;"));

        using Value missing = tags.CopyValue("album");
        Assert.True(missing.IsEmpty);
    }

    /// <summary>
    /// The copy owns its content, so it outlives the list as the indexed read
    /// does.
    /// </summary>
    [Fact]
    public void TheCopiedValueOutlivesTheList()
    {
        Value artist;

        using (TagList tags = Assert.IsAssignableFrom<TagList>(
            TagList.NewFromString("taglist, artist=(string){ \"first\", \"second\" };")))
        {
            artist = tags.CopyValue("artist");
        }

        using (artist)
        {
            Assert.Contains("second", artist.GetString() ?? string.Empty, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The round trip an application performs: fill a fresh list with the typed
    /// writers and read every value back with the generated getter of the same
    /// type.
    /// </summary>
    [Fact]
    public void TagsWrittenByTheTypedWritersAreReadBackByTheTypedGetters()
    {
        RegisterTestTags();

        using TagList tags = TagList.NewEmpty();

        // A list nobody else holds is writable, which is what the writer needs.
        Assert.True(tags.IsWritable);

        tags.AddString(TagMergeMode.Replace, "title", "Take Five");
        tags.AddUint(TagMergeMode.Replace, "track-number", 3u);
        tags.AddUint64(TagMergeMode.Replace, "duration", 301_000_000_000ul);
        tags.AddDouble(TagMergeMode.Replace, "replaygain-track-gain", -7.5);
        tags.AddInt(TagMergeMode.Replace, IntTag, -42);
        tags.AddInt64(TagMergeMode.Replace, Int64Tag, -9_000_000_000L);
        tags.AddBoolean(TagMergeMode.Replace, BooleanTag, true);
        tags.AddFloat(TagMergeMode.Replace, FloatTag, 0.25f);

        _output.WriteLine(tags.ToString());

        Assert.False(tags.IsEmpty());

        Assert.True(tags.GetString("title", out string? title));
        Assert.Equal("Take Five", title);

        Assert.True(tags.GetUint("track-number", out uint track));
        Assert.Equal(3u, track);

        Assert.True(tags.GetUint64("duration", out ulong duration));
        Assert.Equal(301_000_000_000ul, duration);

        Assert.True(tags.GetDouble("replaygain-track-gain", out double gain));
        Assert.Equal(-7.5, gain);

        Assert.True(tags.GetInt(IntTag, out int number));
        Assert.Equal(-42, number);

        Assert.True(tags.GetInt64(Int64Tag, out long wide));
        Assert.Equal(-9_000_000_000L, wide);

        Assert.True(tags.GetBoolean(BooleanTag, out bool flag));
        Assert.True(flag);

        Assert.True(tags.GetFloat(FloatTag, out float ratio));
        Assert.Equal(0.25f, ratio);
    }

    /// <summary>
    /// The generic writer stores a value the caller built, and the list takes a
    /// copy of it: the caller still owns and still reads what it passed.
    /// </summary>
    [Fact]
    public void AValueBuiltByHandIsCopiedIntoTheListAndReadBackByIndex()
    {
        using TagList tags = TagList.NewEmpty();

        using (Value artist = Value.New(GType.String))
        {
            artist.SetString("Dave Brubeck");
            tags.AddValue(TagMergeMode.Replace, "artist", artist);

            // gst_tag_list_add_value copies, so the value of the test is still
            // the value of the test.
            Assert.Equal("Dave Brubeck", artist.GetString());
        }

        Assert.Equal(1u, tags.GetTagSize("artist"));

        using Value read = tags.GetValueIndex("artist", 0);
        Assert.False(read.IsEmpty);
        Assert.Equal(GType.String, read.Type);
        Assert.Equal("Dave Brubeck", read.GetString());
    }

    /// <summary>
    /// The merge mode is obeyed: appending collects a second value under the
    /// same tag, and replacing leaves one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accumulating is a property of the tag rather than of the call — only a
    /// tag registered with a merge function collects several values, and
    /// <c>artist</c> is one of those.
    /// </para>
    /// <para>
    /// <see cref="TagMergeMode.ReplaceAll"/> replaces the one tag here and not
    /// the whole list, which is where <c>gst_tag_list_add_value</c> differs from
    /// the variadic <c>gst_tag_list_add</c>: the variadic call clears every
    /// field first and this one never does. The title written in between is what
    /// measures that.
    /// </para>
    /// </remarks>
    [Fact]
    public void AppendingCollectsSeveralValuesAndReplaceAllLeavesOne()
    {
        using TagList tags = TagList.NewEmpty();

        tags.AddString(TagMergeMode.Append, "artist", "first");
        Assert.Equal(1u, tags.GetTagSize("artist"));

        tags.AddString(TagMergeMode.Append, "artist", "second");
        Assert.Equal(2u, tags.GetTagSize("artist"));

        using (Value first = tags.GetValueIndex("artist", 0))
        using (Value second = tags.GetValueIndex("artist", 1))
        {
            Assert.Equal("first", first.GetString());
            Assert.Equal("second", second.GetString());
        }

        tags.AddString(TagMergeMode.Replace, "title", "Take Five");

        // Keep leaves a tag that already carries something alone.
        tags.AddString(TagMergeMode.Keep, "title", "ignored");
        Assert.True(tags.GetString("title", out string? kept));
        Assert.Equal("Take Five", kept);

        tags.AddString(TagMergeMode.ReplaceAll, "artist", "only");
        Assert.Equal(1u, tags.GetTagSize("artist"));

        using (Value only = tags.GetValueIndex("artist", 0))
        {
            Assert.Equal("only", only.GetString());
        }

        // The other tag survived, so ReplaceAll replaced the tag and not the
        // list.
        Assert.True(tags.GetString("title", out string? title));
        Assert.Equal("Take Five", title);
    }

    /// <summary>
    /// A tag nobody registered is refused before the call, because the C call
    /// answers it with a warning on the console and a list that did not change.
    /// </summary>
    [Fact]
    public void AnUnregisteredTagIsRefusedRatherThanIgnored()
    {
        using TagList tags = TagList.NewEmpty();

        Assert.False(Global.TagExists("gstsharp-no-such-tag"));

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => tags.AddString(TagMergeMode.Replace, "gstsharp-no-such-tag", "value"));

        _output.WriteLine(failure.Message);

        Assert.Equal("tag", failure.ParamName);
        Assert.True(tags.IsEmpty());
    }

    /// <summary>
    /// A value of a type the tag was not registered with is refused, with both
    /// type names in the message.
    /// </summary>
    /// <remarks>
    /// <c>gst_tag_list_add_value_internal</c> demands
    /// <c>G_VALUE_HOLDS (value, info-&gt;type)</c> and transforms nothing, so
    /// the number below cannot become the text the tag holds. In C the mismatch
    /// is a warning and a write that did not happen.
    /// </remarks>
    [Fact]
    public void AValueOfAnotherTypeThanTheTagIsRefused()
    {
        using TagList tags = TagList.NewEmpty();

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => tags.AddUint(TagMergeMode.Replace, "title", 3u));

        _output.WriteLine(failure.Message);

        Assert.Equal("value", failure.ParamName);
        Assert.Contains("gchararray", failure.Message, StringComparison.Ordinal);
        Assert.Contains("guint", failure.Message, StringComparison.Ordinal);
        Assert.True(tags.IsEmpty());
    }

    /// <summary>
    /// An empty value has no type, so there is nothing for the tag to hold.
    /// </summary>
    [Fact]
    public void AnEmptyValueCannotBeStoredUnderATag()
    {
        using TagList tags = TagList.NewEmpty();

        Value empty = default;

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => tags.AddValue(TagMergeMode.Replace, "title", empty));

        Assert.Equal("value", failure.ParamName);
        Assert.True(tags.IsEmpty());
    }

    /// <summary>
    /// A merge mode outside the enumeration is refused, which in C is the
    /// <c>GST_TAG_MODE_IS_VALID</c> assertion and a write that did not happen.
    /// </summary>
    [Fact]
    public void AMergeModeOutsideTheEnumerationIsRefused()
    {
        using TagList tags = TagList.NewEmpty();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => tags.AddString(TagMergeMode.Undefined, "title", "Take Five"));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => tags.AddString(TagMergeMode.Count, "title", "Take Five"));

        Assert.True(tags.IsEmpty());
    }

    /// <summary>
    /// A list somebody else holds a reference to is not writable, and writing it
    /// is an exception rather than a warning on the console. A copy is the way
    /// out, and the message says so.
    /// </summary>
    [Fact]
    public void ASharedTagListRefusesToBeWritten()
    {
        using TagList tags = TagList.NewEmpty();
        tags.AddString(TagMergeMode.Replace, "title", "Take Five");

        // The second reference is what makes the list unwritable, the way a
        // posted GST_MESSAGE_TAG holds the list it carries.
        TestNatives.MiniObjectRef(tags.Handle);

        try
        {
            Assert.False(tags.IsWritable);

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => tags.AddString(TagMergeMode.Replace, "title", "Blue Rondo"));

            _output.WriteLine(failure.Message);
            Assert.Contains("gst_mini_object_is_writable", failure.Message, StringComparison.Ordinal);

            // The write did not happen.
            Assert.True(tags.GetString("title", out string? unchanged));
            Assert.Equal("Take Five", unchanged);
        }
        finally
        {
            // The reference the test took, released before the wrapper releases
            // its own.
            TestNatives.MiniObjectUnref(tags.Handle);
        }

        Assert.True(tags.IsWritable);

        // A copy is writable, which is the way out the message points at.
        using TagList copy = tags.Copy();
        Assert.True(copy.IsWritable);

        copy.AddString(TagMergeMode.Replace, "title", "Blue Rondo");

        Assert.True(copy.GetString("title", out string? changed));
        Assert.Equal("Blue Rondo", changed);

        Assert.True(tags.GetString("title", out string? original));
        Assert.Equal("Take Five", original);
    }

    /// <summary>
    /// Registers the tags the typed writers need and GStreamer does not bring:
    /// there is no core tag of type <c>gint</c>, <c>gint64</c>, <c>gboolean</c>
    /// or <c>gfloat</c>.
    /// </summary>
    /// <remarks>
    /// A second registration of the same name and type is a no-op inside
    /// GStreamer, so this may run once per test and in any order.
    /// </remarks>
    private static unsafe void RegisterTestTags()
    {
        Register(IntTag, GType.Int);
        Register(Int64Tag, GType.Int64);
        Register(BooleanTag, GType.Boolean);
        Register(FloatTag, GType.Float);

        static void Register(string name, GType type)
        {
            Span<byte> nameBuffer = stackalloc byte[GMarshal.StackBufferSize];
            Span<byte> blurbBuffer = stackalloc byte[GMarshal.StackBufferSize];

            using Utf8Scope nameScope = GMarshal.StackUtf8(name, nameBuffer);
            using Utf8Scope blurbScope = GMarshal.StackUtf8(
                "a tag registered by the GstSharp.Net test suite",
                blurbBuffer);

            TestNatives.TagRegister(
                nameScope.Pointer,
                (int)TagFlag.Meta,
                type.Value,
                nameScope.Pointer,
                blurbScope.Pointer,
                nint.Zero);
        }
    }
}
