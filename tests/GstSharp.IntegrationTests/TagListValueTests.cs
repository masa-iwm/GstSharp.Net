using Gst;
using Gst.GObject;
using Xunit;
using Xunit.Abstractions;

namespace GstSharp.IntegrationTests;

/// <summary>
/// Covers <see cref="TagList.GetValueIndex"/>: the generic reader that reaches
/// the tags whose type has no typed getter, and the values past the first one.
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
}
