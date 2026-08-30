using GstSharp.Generator.Emit;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// How gtk-doc text becomes XML documentation, and in particular what happens
/// to a fenced code block.
/// </summary>
/// <remarks>
/// <para>
/// Prose is split on blank lines, which is all a paragraph of gtk-doc needs.
/// A code sample is the one construct that blank lines run through, so it is
/// read whole, written as <c>&lt;code&gt;</c> and never allowed to open a
/// summary: a summary that starts with a line of C is unreadable in every tool
/// that shows one.
/// </para>
/// <para>
/// The vendored girs carry some fifty documentations with a fence in them, and
/// exactly one of those - the <c>missing-uri</c> signal of <c>GESProject</c> -
/// is nothing but the fence, which is the fallback pinned here.
/// </para>
/// </remarks>
public sealed class XmlDocWriterTests
{
    [Fact]
    public void AFenceThatOpensTheDocumentationMovesIntoTheRemarks()
    {
        Assert.Equal(
            """
            /// <summary>Creates a widget.</summary>
            /// <remarks>
            /// <para>
            /// <code>
            /// int main (void);
            /// </code>
            /// </para>
            /// </remarks>

            """,
            Write(
                """
                ```c
                int main (void);
                ```

                Creates a widget.
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AFenceInTheMiddleKeepsItsBlankLinesAndTheOrderOfTheText()
    {
        Assert.Equal(
            """
            /// <summary>Creates a widget.</summary>
            /// <remarks>
            /// <para>
            /// <code>
            /// if (a) {
            ///
            ///   b ();
            /// }
            /// </code>
            /// </para>
            /// <para>Returns the widget.</para>
            /// </remarks>

            """,
            Write(
                """
                Creates a widget.

                ```c
                if (a) {

                  b ();
                }
                ```

                Returns the widget.
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void AnUnterminatedFenceTakesTheRestOfTheText()
    {
        Assert.Equal(
            """
            /// <summary>Creates a widget.</summary>
            /// <remarks>
            /// <para>
            /// <code>
            /// int main (void);
            ///
            /// Not a paragraph any more.
            /// </code>
            /// </para>
            /// </remarks>

            """,
            Write(
                """
                Creates a widget.

                ```c
                int main (void);

                Not a paragraph any more.
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void TheIndentationOfASampleSurvivesAndItsMarkupIsEscaped()
    {
        Assert.Equal(
            """
            /// <summary>Creates a widget.</summary>
            /// <remarks>
            /// <para>
            /// <code>
            /// void f (void)
            /// {
            ///   if (a &lt; b)
            ///     g (&amp;a);
            /// }
            /// </code>
            /// </para>
            /// </remarks>

            """,
            Write(
                """
                Creates a widget.

                ```c
                void f (void)
                {
                  if (a < b)
                    g (&a);
                }
                ```
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ADocumentationThatIsNothingButAFenceKeepsItAsItsSummary()
    {
        // There is no prose to promote, so the fence stays where it is rather
        // than leaving the member with the generator's fallback summary and no
        // trace of what the library documented. This is the shape of the
        // missing-uri signal of GESProject.
        Assert.Equal(
            """
            /// <summary>
            /// <code>
            /// int main (void);
            /// </code>
            /// </summary>

            """,
            Write(
                """
                ```c
                int main (void);
                ```
                """),
            StringComparer.Ordinal);
    }

    [Fact]
    public void ThreeBackticksInTheMiddleOfASentenceAreNotAFence()
    {
        // g_shell_unquote spells the backtick as inline markup while it
        // describes shell quoting. Reading that as an opening fence would
        // swallow the rest of the documentation into a code sample.
        Assert.Equal(
            """
            /// <summary>
            /// Double quotes allow `$`, ```, `"`, and
            /// newline to be escaped with backslash.
            /// </summary>

            """,
            Write(
                """
                Double quotes allow `$`, ```, `"`, and
                newline to be escaped with backslash.
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
