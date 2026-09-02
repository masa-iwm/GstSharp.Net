using System.Globalization;
using System.Text;
using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Turns gir documentation into C# XML documentation comments.
/// </summary>
/// <remarks>
/// The conversion stays deliberately small: gtk-doc markup is not translated,
/// the text is only XML escaped and split into a summary and remarks. The one
/// exception is the fenced code block, which is kept whole and written as
/// <c>&lt;code&gt;</c>, because splitting it on its blank lines turned a
/// sample program into unreadable prose. Every emitted member gets a summary,
/// because the shipping projects compile with
/// <c>GenerateDocumentationFile</c> and warnings as errors.
/// </remarks>
internal static class XmlDocWriter
{
    private const int InlineLimit = 88;

    /// <summary>
    /// Writes a <c>&lt;summary&gt;</c> element, plus a <c>&lt;remarks&gt;</c>
    /// element when the gir documentation has more than one paragraph, when the
    /// caller has a generator authored note to append, or when the member
    /// arrived after the supported floor.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallbackSummary">
    /// The summary used when there is no documentation. It is generator
    /// authored XML documentation markup and is emitted verbatim.
    /// </param>
    /// <param name="availability">
    /// The gir element whose <c>version</c> attribute says which GStreamer the
    /// member needs, when the member has one to report.
    /// </param>
    /// <param name="remarksNote">
    /// Generator authored lines appended to the remarks, for the parts of the
    /// contract that the gir does not state. They are emitted verbatim, after
    /// whatever the gir had to say and before the availability paragraph, which
    /// stays last.
    /// </param>
    internal static void Write(
        CodeWriter writer,
        string? doc,
        string fallbackSummary,
        GirNode? availability = null,
        IReadOnlyList<string>? remarksNote = null)
    {
        string? since = Availability.SinceVersion(availability);
        IReadOnlyList<DocUnit> paragraphs = SplitParagraphs(doc);
        if (paragraphs.Count == 0)
        {
            writer.WriteLine("/// <summary>" + fallbackSummary + "</summary>");
            if (remarksNote is not null || since is not null)
            {
                writer.WriteLine("/// <remarks>");
                if (remarksNote is not null)
                {
                    WriteNote(writer, remarksNote);
                }

                if (since is not null)
                {
                    WriteSince(writer, since);
                }

                writer.WriteLine("/// </remarks>");
            }

            return;
        }

        // A fenced code block never leads the summary: a summary that opens
        // with a line of C is unreadable in every tool that shows one. The
        // first paragraph that is not a fence becomes the summary and the
        // fences ahead of it move into the remarks, which leaves the document
        // order of everything else alone. A documentation that is nothing but
        // a fence has no prose to promote, so there the fence stays.
        int summary = 0;
        for (int i = 0; i < paragraphs.Count; i++)
        {
            if (!paragraphs[i].IsFence)
            {
                summary = i;
                break;
            }
        }

        WriteSummary(writer, paragraphs[summary]);
        if (paragraphs.Count > 1 || remarksNote is not null || since is not null)
        {
            writer.WriteLine("/// <remarks>");
            for (int i = 0; i < paragraphs.Count; i++)
            {
                if (i != summary)
                {
                    WriteParagraph(writer, paragraphs[i]);
                }
            }

            if (remarksNote is not null)
            {
                WriteNote(writer, remarksNote);
            }

            if (since is not null)
            {
                WriteSince(writer, since);
            }

            writer.WriteLine("/// </remarks>");
        }
    }

    /// <summary>
    /// Writes a <c>&lt;param&gt;</c> element for one parameter of a member.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="name">The C# name of the parameter.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallback">
    /// The description used when there is no documentation. It is generator
    /// authored XML documentation markup and is emitted verbatim.
    /// </param>
    /// <param name="note">
    /// Generator authored lines appended to the description, for the parts of
    /// the contract that the gir does not state. They are emitted verbatim.
    /// </param>
    internal static void WriteParam(
        CodeWriter writer,
        string name,
        string? doc,
        string fallback,
        IReadOnlyList<string>? note = null) =>
        WriteElement(writer, "param name=\"" + name + "\"", "param", doc, fallback, note);

    /// <summary>
    /// Writes a <c>&lt;returns&gt;</c> element.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallback">
    /// The description used when there is no documentation. It is generator
    /// authored XML documentation markup and is emitted verbatim.
    /// </param>
    /// <param name="note">
    /// Generator authored lines appended to the description, for the parts of
    /// the contract that the gir does not state. They are emitted verbatim.
    /// </param>
    internal static void WriteReturns(
        CodeWriter writer,
        string? doc,
        string fallback,
        IReadOnlyList<string>? note = null) =>
        WriteElement(writer, "returns", "returns", doc, fallback, note);

    /// <summary>
    /// Writes an <c>[Obsolete]</c> attribute when the gir marks the element as
    /// deprecated.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="node">The gir element to inspect.</param>
    internal static void WriteObsolete(CodeWriter writer, GirNode node)
    {
        if (!node.IsDeprecated)
        {
            return;
        }

        string message = node.DocDeprecated is { Length: > 0 } text
            ? CollapseToSingleLine(text)
            : "Deprecated in the native API.";
        if (node.DeprecatedVersion is { Length: > 0 } version)
        {
            message = string.Create(CultureInfo.InvariantCulture, $"{message} (deprecated since {version})");
        }

        writer.WriteLine("[Obsolete(\"" + message.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\")]");
    }

    /// <summary>Escapes the characters that are special in XML.</summary>
    /// <param name="text">The raw text.</param>
    /// <returns>The escaped text.</returns>
    internal static string Escape(string text)
    {
        StringBuilder builder = new(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&':
                    builder.Append("&amp;");
                    break;
                case '<':
                    builder.Append("&lt;");
                    break;
                case '>':
                    builder.Append("&gt;");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Collapses documentation into a single line, for attributes.</summary>
    /// <param name="text">The raw text.</param>
    /// <returns>The collapsed text.</returns>
    internal static string CollapseToSingleLine(string text)
    {
        StringBuilder builder = new(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Writes one documentation element, inline when the text is a single short
    /// line and as a block otherwise.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="openTag">The opening tag, without the angle brackets.</param>
    /// <param name="closeTag">The closing tag name.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallback">The verbatim markup used when there is none.</param>
    /// <param name="note">Generator authored lines appended to the description.</param>
    private static void WriteElement(
        CodeWriter writer,
        string openTag,
        string closeTag,
        string? doc,
        string fallback,
        IReadOnlyList<string>? note = null)
    {
        IReadOnlyList<DocUnit> paragraphs = SplitParagraphs(doc);
        if (paragraphs.Count == 0)
        {
            if (note is null)
            {
                writer.WriteLine("/// <" + openTag + ">" + fallback + "</" + closeTag + ">");
                return;
            }

            writer.WriteLine("/// <" + openTag + ">");
            writer.WriteLine("/// " + fallback);
            WriteNote(writer, note);
            writer.WriteLine("/// </" + closeTag + ">");
            return;
        }

        // Only the first paragraph is kept: a parameter description has no
        // place for the <para> elements that the summary uses. A fence there
        // is written as the code it is, in the one element this writes.
        DocUnit first = paragraphs[0];
        if (note is null && !first.IsFence && first.Lines.Count == 1 && first.Lines[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <" + openTag + ">" + Escape(first.Lines[0]) + "</" + closeTag + ">");
            return;
        }

        writer.WriteLine("/// <" + openTag + ">");
        WriteUnit(writer, first);

        if (note is not null)
        {
            WriteNote(writer, note);
        }

        writer.WriteLine("/// </" + closeTag + ">");
    }

    /// <summary>
    /// Writes the paragraph that names the GStreamer a member needs.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="since">The version the member arrived in.</param>
    /// <remarks>
    /// It is the last paragraph of the remarks, after whatever the gir had to
    /// say, because it is the generator talking about the library rather than
    /// the library talking about itself.
    /// </remarks>
    private static void WriteSince(CodeWriter writer, string since) =>
        writer.WriteLine("/// <para>Available since GStreamer " + Escape(since) + ".</para>");

    /// <summary>
    /// Writes generator authored lines into a documentation comment, verbatim.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="note">The lines to write.</param>
    /// <remarks>
    /// A note that runs to more than one paragraph separates them with
    /// <c>&lt;para&gt;</c> the way the gir documentation path does, and never
    /// with an empty line: the marker of an empty line would be a <c>///</c>
    /// with a space behind it, which is trailing whitespace that
    /// <c>.editorconfig</c> tells every editor to strip. A file that is saved
    /// once would then differ from what the generator writes, and the diff gate
    /// would fail on a change nobody made. The empty line is turned into a bare
    /// marker here as well, so that the rule holds whatever a note carries.
    /// </remarks>
    private static void WriteNote(CodeWriter writer, IReadOnlyList<string> note)
    {
        foreach (string line in note)
        {
            writer.WriteLine(line.Length == 0 ? "///" : "/// " + line);
        }
    }

    private static void WriteSummary(CodeWriter writer, DocUnit paragraph)
    {
        if (!paragraph.IsFence && paragraph.Lines.Count == 1 && paragraph.Lines[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <summary>" + Escape(paragraph.Lines[0]) + "</summary>");
            return;
        }

        writer.WriteLine("/// <summary>");
        WriteUnit(writer, paragraph);
        writer.WriteLine("/// </summary>");
    }

    private static void WriteParagraph(CodeWriter writer, DocUnit paragraph)
    {
        if (!paragraph.IsFence && paragraph.Lines.Count == 1 && paragraph.Lines[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <para>" + Escape(paragraph.Lines[0]) + "</para>");
            return;
        }

        writer.WriteLine("/// <para>");
        WriteUnit(writer, paragraph);
        writer.WriteLine("/// </para>");
    }

    /// <summary>
    /// Writes the lines of one unit, wrapped in <c>&lt;code&gt;</c> when the
    /// unit is a fenced block.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="unit">The unit to write.</param>
    /// <remarks>
    /// The fence lines themselves are gone by now; what is left is the sample
    /// as the gir wrote it, indentation and blank lines included, so that it
    /// can be read and copied. Only the characters that are special in XML are
    /// translated.
    /// </remarks>
    private static void WriteUnit(CodeWriter writer, DocUnit unit)
    {
        if (unit.IsFence)
        {
            writer.WriteLine("/// <code>");
        }

        foreach (string line in unit.Lines)
        {
            WriteTextLine(writer, line);
        }

        if (unit.IsFence)
        {
            writer.WriteLine("/// </code>");
        }
    }

    private static void WriteTextLine(CodeWriter writer, string line) =>
        writer.WriteLine(line.Length == 0 ? "///" : "/// " + Escape(line));

    /// <summary>
    /// Splits gir documentation into the units that a summary and remarks are
    /// built from: a paragraph of prose, or one fenced code block.
    /// </summary>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <returns>The units, in the order the documentation wrote them.</returns>
    /// <remarks>
    /// Prose is split on blank lines. A fenced block is not: its blank lines
    /// belong to the sample, so it is read from its opening fence to its
    /// closing one as a single unit and both fence lines are dropped. An
    /// opening fence that is never closed takes the rest of the text with it,
    /// and a fence whose body is blank contributes no unit at all. The blank
    /// lines around the sample are dropped, the ones inside it are kept.
    /// </remarks>
    private static IReadOnlyList<DocUnit> SplitParagraphs(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return [];
        }

        string normalized = doc.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        string[] lines = normalized.Split('\n');
        List<DocUnit> paragraphs = [];
        List<string> current = [];
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd();
            if (IsFenceLine(line))
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(new DocUnit(current, IsFence: false));
                    current = [];
                }

                List<string> block = [];
                int j = i + 1;
                for (; j < lines.Length && !IsFenceLine(lines[j].TrimEnd()); j++)
                {
                    block.Add(lines[j].TrimEnd());
                }

                // A blank line above or below the sample is the space the
                // author left around the fence, not part of the sample.
                while (block.Count > 0 && block[0].Length == 0)
                {
                    block.RemoveAt(0);
                }

                while (block.Count > 0 && block[^1].Length == 0)
                {
                    block.RemoveAt(block.Count - 1);
                }

                if (block.Count > 0)
                {
                    paragraphs.Add(new DocUnit(block, IsFence: true));
                }

                i = j;
                continue;
            }

            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(new DocUnit(current, IsFence: false));
                    current = [];
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(new DocUnit(current, IsFence: false));
        }

        return paragraphs;
    }

    /// <summary>Tests whether a line opens or closes a fenced code block.</summary>
    /// <param name="line">The line, already trimmed of its trailing space.</param>
    /// <returns><see langword="true"/> when the line is a fence.</returns>
    /// <remarks>
    /// Only a line that starts with the fence counts. Three backticks in the
    /// middle of a sentence are the inline markup for a backtick, which
    /// <c>g_shell_unquote</c> uses while describing shell quoting, and reading
    /// that as a fence would swallow the rest of the documentation.
    /// </remarks>
    private static bool IsFenceLine(string line) =>
        line.TrimStart().StartsWith("```", StringComparison.Ordinal);

    /// <summary>
    /// One unit of gir documentation: a paragraph of prose, or the body of a
    /// fenced code block without its fence lines.
    /// </summary>
    /// <param name="Lines">The lines of the unit.</param>
    /// <param name="IsFence">
    /// <see langword="true"/> when the lines are a code sample, which is
    /// written as <c>&lt;code&gt;</c> and never split.
    /// </param>
    private sealed record DocUnit(IReadOnlyList<string> Lines, bool IsFence);
}
