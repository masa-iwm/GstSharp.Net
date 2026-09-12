using Gst;
using Xunit;

namespace GstSharp.IntegrationTests;

/// <summary>
/// <see cref="Global.FormatsContains"/> against the library that is
/// installed: the list it is handed is zero terminated in C, and this is where
/// that shows.
/// </summary>
[Collection(GstCollection.Name)]
public sealed class FormatsContainsTests
{
    /// <summary>A format that is in the list is found.</summary>
    [Fact]
    public void AFormatInTheListIsFound()
    {
        Assert.True(Global.FormatsContains([Format.Time, Format.Bytes], Format.Time));
        Assert.True(Global.FormatsContains([Format.Time, Format.Bytes], Format.Bytes));
    }

    /// <summary>A format that is not in the list is not found.</summary>
    [Fact]
    public void AFormatOutsideTheListIsNotFound()
    {
        Assert.False(Global.FormatsContains([Format.Time, Format.Bytes], Format.Default));
    }

    /// <summary>
    /// An empty list finds nothing: what the call is handed is the terminator
    /// alone.
    /// </summary>
    [Fact]
    public void AnEmptyListFindsNothing()
    {
        Assert.False(Global.FormatsContains([], Format.Time));
    }

    /// <summary>
    /// The search stops at the first <see cref="Format.Undefined"/> element,
    /// because that value is the terminator of the list in C and not a format.
    /// Nothing behind one is looked at.
    /// </summary>
    [Fact]
    public void TheSearchStopsAtAnUndefinedElement()
    {
        Assert.False(
            Global.FormatsContains([Format.Time, Format.Undefined, Format.Bytes], Format.Bytes));

        Assert.True(
            Global.FormatsContains([Format.Time, Format.Undefined, Format.Bytes], Format.Time));
    }

    /// <summary>
    /// Searching for <see cref="Format.Undefined"/> always answers
    /// <see langword="false"/>: it is the value the walk stops on.
    /// </summary>
    [Fact]
    public void TheUndefinedFormatIsNeverFound()
    {
        Assert.False(Global.FormatsContains([Format.Undefined], Format.Undefined));
        Assert.False(Global.FormatsContains([Format.Time], Format.Undefined));
    }

    /// <summary>
    /// A list longer than the stack buffer takes the pooled path, terminator
    /// and all.
    /// </summary>
    [Fact]
    public void ALongListTakesThePooledPath()
    {
        Format[] formats = new Format[128];
        for (int index = 0; index < formats.Length; index++)
        {
            formats[index] = Format.Bytes;
        }

        formats[100] = Format.Percent;

        Assert.True(Global.FormatsContains(formats, Format.Percent));
        Assert.False(Global.FormatsContains(formats, Format.Time));
    }
}
