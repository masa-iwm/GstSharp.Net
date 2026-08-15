using System.Text;

namespace GstSharp.Generator.Emit;

/// <summary>
/// A generated source file.
/// </summary>
/// <param name="RelativePath">Path below the output root, using <c>/</c> separators.</param>
/// <param name="Content">The complete file content, with LF line endings.</param>
internal sealed record GeneratedFile(string RelativePath, string Content);

/// <summary>
/// Builds source text deterministically: four space indentation, LF line
/// endings, UTF-8 without a byte order mark and a trailing newline.
/// </summary>
internal sealed class CodeWriter
{
    private const string IndentUnit = "    ";

    /// <summary>The line ending used by every generated file.</summary>
    internal const string NewLine = "\n";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly StringBuilder _builder = new();
    private int _indent;

    /// <summary>Writes an empty line.</summary>
    internal void WriteLine() => _builder.Append(NewLine);

    /// <summary>Writes one indented line.</summary>
    /// <param name="text">The line content; must not contain line breaks.</param>
    internal void WriteLine(string text)
    {
        if (text.Length != 0)
        {
            for (int i = 0; i < _indent; i++)
            {
                _builder.Append(IndentUnit);
            }

            _builder.Append(text);
        }

        _builder.Append(NewLine);
    }

    /// <summary>Opens a brace block and increases the indentation.</summary>
    internal void OpenBlock()
    {
        WriteLine("{");
        _indent++;
    }

    /// <summary>Closes a brace block and decreases the indentation.</summary>
    internal void CloseBlock()
    {
        _indent--;
        WriteLine("}");
    }

    /// <summary>Returns the accumulated source text.</summary>
    /// <returns>The source text.</returns>
    internal string ToSource() => _builder.ToString();

    /// <summary>Writes a generated file to disk, creating directories as needed.</summary>
    /// <param name="path">The absolute path of the file.</param>
    /// <param name="content">The file content.</param>
    internal static void WriteFile(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, Utf8NoBom);
    }
}
