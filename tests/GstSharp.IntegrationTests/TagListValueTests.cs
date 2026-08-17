using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers the two generic readers of a tag list:
/// <see cref="TagList.GetValueIndex"/>, which reaches the tags whose type has
/// no typed getter and the values past the first one, and
/// <see cref="TagList.CopyValue"/>, which merges a tag that carries several
/// values into one.
/// </summary>
/// <remarks>
/// The list is built from its text form, which is the only way to put more than
/// one value under one tag from managed code today —
/// <c>gst_tag_list_add_value</c> is variadic-adjacent and stays on the skip
/// list.
/// </remarks>
[Collection(GstCollection.Name)]
public sealed class TagListValueTests
{
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
}
