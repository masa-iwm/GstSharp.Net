using System.Globalization;
using System.Text;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Turns gir documentation into C# XML documentation comments.
/// </summary>
/// <remarks>
/// The conversion stays deliberately small: gtk-doc markup is not translated,
/// the text is only XML escaped and split into a summary and remarks. Every
/// emitted member gets a summary, because the shipping projects compile with
/// <c>GenerateDocumentationFile</c> and warnings as errors.
/// </remarks>
internal static class XmlDocWriter
{
    private const int InlineLimit = 88;

    /// <summary>
    /// Writes a <c>&lt;summary&gt;</c> element, plus a <c>&lt;remarks&gt;</c>
    /// element when the gir documentation has more than one paragraph.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallbackSummary">
    /// The summary used when there is no documentation. It is generator
    /// authored XML documentation markup and is emitted verbatim.
    /// </param>
    internal static void Write(CodeWriter writer, string? doc, string fallbackSummary)
    {
        IReadOnlyList<IReadOnlyList<string>> paragraphs = SplitParagraphs(doc);
        if (paragraphs.Count == 0)
        {
            writer.WriteLine("/// <summary>" + fallbackSummary + "</summary>");
            return;
        }

        WriteSummary(writer, paragraphs[0]);
        if (paragraphs.Count > 1)
        {
            writer.WriteLine("/// <remarks>");
            for (int i = 1; i < paragraphs.Count; i++)
            {
                WriteParagraph(writer, paragraphs[i]);
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
    internal static void WriteParam(CodeWriter writer, string name, string? doc, string fallback) =>
        WriteElement(writer, "param name=\"" + name + "\"", "param", doc, fallback);

    /// <summary>
    /// Writes a <c>&lt;returns&gt;</c> element.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="doc">The gir documentation, if any.</param>
    /// <param name="fallback">
    /// The description used when there is no documentation. It is generator
    /// authored XML documentation markup and is emitted verbatim.
    /// </param>
    internal static void WriteReturns(CodeWriter writer, string? doc, string fallback) =>
        WriteElement(writer, "returns", "returns", doc, fallback);

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
    private static void WriteElement(
        CodeWriter writer,
        string openTag,
        string closeTag,
        string? doc,
        string fallback)
    {
        IReadOnlyList<IReadOnlyList<string>> paragraphs = SplitParagraphs(doc);
        if (paragraphs.Count == 0)
        {
            writer.WriteLine("/// <" + openTag + ">" + fallback + "</" + closeTag + ">");
            return;
        }

        // Only the first paragraph is kept: a parameter description has no
        // place for the <para> elements that the summary uses.
        IReadOnlyList<string> first = paragraphs[0];
        if (first.Count == 1 && first[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <" + openTag + ">" + Escape(first[0]) + "</" + closeTag + ">");
            return;
        }

        writer.WriteLine("/// <" + openTag + ">");
        foreach (string line in first)
        {
            WriteTextLine(writer, line);
        }

        writer.WriteLine("/// </" + closeTag + ">");
    }

    private static void WriteSummary(CodeWriter writer, IReadOnlyList<string> paragraph)
    {
        if (paragraph.Count == 1 && paragraph[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <summary>" + Escape(paragraph[0]) + "</summary>");
            return;
        }

        writer.WriteLine("/// <summary>");
        foreach (string line in paragraph)
        {
            WriteTextLine(writer, line);
        }

        writer.WriteLine("/// </summary>");
    }

    private static void WriteParagraph(CodeWriter writer, IReadOnlyList<string> paragraph)
    {
        if (paragraph.Count == 1 && paragraph[0].Length <= InlineLimit)
        {
            writer.WriteLine("/// <para>" + Escape(paragraph[0]) + "</para>");
            return;
        }

        writer.WriteLine("/// <para>");
        foreach (string line in paragraph)
        {
            WriteTextLine(writer, line);
        }

        writer.WriteLine("/// </para>");
    }

    private static void WriteTextLine(CodeWriter writer, string line) =>
        writer.WriteLine(line.Length == 0 ? "///" : "/// " + Escape(line));

    private static IReadOnlyList<IReadOnlyList<string>> SplitParagraphs(string? doc)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return [];
        }

        string normalized = doc.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        List<IReadOnlyList<string>> paragraphs = [];
        List<string> current = [];
        foreach (string rawLine in normalized.Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    paragraphs.Add(current);
                    current = [];
                }

                continue;
            }

            current.Add(line);
        }

        if (current.Count > 0)
        {
            paragraphs.Add(current);
        }

        return paragraphs;
    }
}
