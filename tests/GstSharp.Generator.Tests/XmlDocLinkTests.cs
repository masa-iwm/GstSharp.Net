using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What a Markdown link of a gir becomes in the documentation that ships.
/// </summary>
/// <remarks>
/// A gir writes its cross references the way gi-docgen renders them, and every
/// one of them is a relative file link to the documentation build, which
/// reports it as broken. Each target says what it means: a web address, a page
/// of the GStreamer documentation below <c>additional/</c>, an anchor into a
/// page this documentation is not part of, or a C name that has no page here
/// at all. A link the gir did not write whole is left alone, because a guess
/// about what it meant would be worse than the sentence itself.
/// </remarks>
public sealed class XmlDocLinkTests
{
    [Fact]
    public void AWebAddressBecomesAnAnchor()
    {
        Assert.Equal(
            """
            /// <summary>See the <a href="https://example.org/spec.html">specification</a>.</summary>

            """,
            Write("See the [specification](https://example.org/spec.html)."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AGStreamerPageBecomesTheAddressItIsPublishedUnder()
    {
        Assert.Equal(
            """
            /// <summary>The <a href="https://gstreamer.freedesktop.org/documentation/additional/design/synchronisation.html#running-time">running time</a> of the segment.</summary>

            """,
            Write("The [running time](additional/design/synchronisation.md#running-time) of the segment."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnAnchorIntoAnotherPageKeepsItsTextAndLosesTheLink()
    {
        Assert.Equal(
            """
            /// <summary>The rules of the Overlaps section are respected.</summary>

            """,
            Write("The rules of the [Overlaps](#overlaps-and-autotransitions) section are respected."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACNameIsWrittenBesideTheTextTheGirChoseForIt()
    {
        Assert.Equal(
            """
            /// <summary>A single sinkpad (<c>GST_PAD_SINK</c>) is requested.</summary>

            """,
            Write("A single [sinkpad](GST_PAD_SINK) is requested."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ACNameThatIsAlreadyTheTextIsWrittenOnce()
    {
        Assert.Equal(
            """
            /// <summary>Call <c>gst_segment_clip</c> first.</summary>

            """,
            Write("Call [gst_segment_clip](gst_segment_clip) first."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ALinkBrokenAcrossTwoLinesIsOneLinkAndKeepsTheBreak()
    {
        // The lines the gir wrote are the lines that are emitted: the break
        // inside the text of the link stays where it was.
        Assert.Equal(
            """
            /// <summary>
            /// Elements that synchronize buffer <a href="https://gstreamer.freedesktop.org/documentation/additional/design/synchronisation.html#running-time">running
            /// times</a> on the clock.
            /// </summary>

            """,
            Write(
                """
                Elements that synchronize buffer [running
                times](additional/design/synchronisation.md#running-time) on the clock.
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheTextOfALinkIsEscapedLikeEverythingElse()
    {
        Assert.Equal(
            """
            /// <summary>The a &amp; b (<c>GST_A_&amp;_B</c>) flag is set.</summary>

            """,
            Write("The [a & b](GST_A_&_B) flag is set."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AMalformedLinkIsLeftExactlyAsTheGirWroteIt()
    {
        // No closing parenthesis, a bracket inside the text and whitespace
        // inside the target: none of the three is a link the gir wrote whole.
        Assert.Equal(
            """
            /// <summary>See [the manual](http://example.org and [a [b]](c) and [d](e f).</summary>

            """,
            Write("See [the manual](http://example.org and [a [b]](c) and [d](e f)."),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ASampleKeepsItsBracketsBecauseTheyAreProgramText()
    {
        Assert.Equal(
            """
            /// <summary>Reads an element.</summary>
            /// <remarks>
            /// <para>
            /// <code>
            /// value = table[i](arg);
            /// </code>
            /// </para>
            /// </remarks>

            """,
            Write(
                """
                Reads an element.

                ```c
                value = table[i](arg);
                ```
                """),
            StringComparer.Ordinal);
    }

    private static string Write(string doc)
    {
        CodeWriter writer = new();
        XmlDocWriter.Write(writer, doc, "The fallback summary.");
        return writer.ToSource();
    }
}
