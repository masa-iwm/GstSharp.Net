using System.Globalization;
using System.Text;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// Turns verbatim gir names into C# identifiers.
/// </summary>
/// <remarks>
/// The gir layer keeps every name exactly as the XML spelled it; all naming
/// decisions live here so that they can be overridden from
/// <c>girs/overlays/fixups.json</c>.
/// </remarks>
internal sealed class NameMapper
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private readonly Overlays _overlays;

    /// <summary>Initializes a new instance of the <see cref="NameMapper"/> class.</summary>
    /// <param name="overlays">The overlay configuration holding the renames.</param>
    internal NameMapper(Overlays overlays) => _overlays = overlays;

    /// <summary>
    /// Converts a <c>snake_case</c> or <c>kebab-case</c> gir name to
    /// <c>PascalCase</c>. Names that are already Pascal cased are preserved.
    /// </summary>
    /// <param name="girName">The verbatim gir name.</param>
    /// <returns>The Pascal cased name.</returns>
    internal static string ToPascalCase(string girName)
    {
        if (girName.Length == 0)
        {
            return girName;
        }

        StringBuilder builder = new(girName.Length);
        bool capitalize = true;
        foreach (char c in girName)
        {
            if (c is '_' or '-' or ' ')
            {
                capitalize = true;
                continue;
            }

            builder.Append(capitalize ? char.ToUpper(c, CultureInfo.InvariantCulture) : c);
            capitalize = false;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts a gir name to <c>camelCase</c>, for parameters and locals.
    /// </summary>
    /// <param name="girName">The verbatim gir name.</param>
    /// <returns>The camel cased name.</returns>
    internal static string ToCamelCase(string girName)
    {
        string pascal = ToPascalCase(girName);
        if (pascal.Length == 0)
        {
            return pascal;
        }

        return char.ToLower(pascal[0], CultureInfo.InvariantCulture) + pascal[1..];
    }

    /// <summary>
    /// Escapes C# keywords and identifiers that cannot start with their first
    /// character.
    /// </summary>
    /// <param name="name">The candidate identifier.</param>
    /// <returns>A legal C# identifier.</returns>
    internal static string EscapeIdentifier(string name)
    {
        if (name.Length == 0)
        {
            return "_";
        }

        if (CSharpKeywords.Contains(name))
        {
            return "@" + name;
        }

        // Enum members such as GST_VIDEO_AFD_16_9_TOP_ALIGNED start with a digit
        // once the prefix is stripped. Prefixing with an underscore is the
        // documented, deterministic rule.
        return char.IsAsciiDigit(name[0]) ? "_" + name : name;
    }

    /// <summary>Maps the C# name of a type declaration.</summary>
    /// <param name="symbol">The declared type.</param>
    /// <returns>The C# type name.</returns>
    internal string TypeName(GirSymbol symbol) =>
        _overlays.TryGetRename(symbol.QualifiedName, out string? renamed)
            ? renamed
            : EscapeIdentifier(ToPascalCase(symbol.Name));

    /// <summary>Maps the C# name of an enumeration or bitfield member.</summary>
    /// <param name="enumeration">The declaring enumeration.</param>
    /// <param name="enumerationNamespace">The gir namespace of the enumeration.</param>
    /// <param name="member">The member to name.</param>
    /// <returns>The C# member name.</returns>
    internal string EnumMemberName(GirEnumeration enumeration, GirNamespace enumerationNamespace, GirEnumMember member)
    {
        string key = enumerationNamespace.Name + "." + enumeration.Name + "." + member.Name;
        if (_overlays.TryGetRename(key, out string? renamed))
        {
            return renamed;
        }

        string source = member.Name.Length > 0 ? member.Name : member.Nick ?? string.Empty;
        return EscapeIdentifier(ToPascalCase(source));
    }
}
