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
    /// <remarks>
    /// A <c>&lt;interface&gt;</c> is emitted as a C# interface and therefore
    /// carries the conventional <c>I</c> prefix. Every consumer of the name
    /// goes through this method, so the type map and the interface emitter
    /// cannot disagree about it.
    /// </remarks>
    internal string TypeName(GirSymbol symbol)
    {
        if (_overlays.TryGetRename(symbol.QualifiedName, out string? renamed))
        {
            return renamed;
        }

        string name = EscapeIdentifier(ToPascalCase(symbol.Name));
        return symbol.Kind == GirSymbolKind.Interface ? "I" + name : name;
    }

    /// <summary>Maps the C# name of a callable.</summary>
    /// <param name="callable">The callable to name.</param>
    /// <returns>The C# member name.</returns>
    /// <remarks>
    /// A callable that <c>shadows</c> another one takes the shadowed name, so
    /// that <c>gst_bus_add_watch_full</c> is emitted as <c>AddWatch</c>.
    /// </remarks>
    internal string CallableName(GirCallable callable)
    {
        if (callable.CIdentifier is { } identifier && _overlays.TryGetRename(identifier, out string? renamed))
        {
            return renamed;
        }

        return EscapeIdentifier(ToPascalCase(SkipRules.EffectiveGirName(callable)));
    }

    /// <summary>Maps the C# name of a property.</summary>
    /// <param name="declarationNamespace">The gir namespace of the declaring type.</param>
    /// <param name="owner">The declaring type.</param>
    /// <param name="property">The property to name.</param>
    /// <returns>The C# property name.</returns>
    internal string PropertyName(GirNamespace declarationNamespace, GirTypeDeclaration owner, GirProperty property) =>
        _overlays.TryGetRename(
            declarationNamespace.Name + "." + owner.Name + ":" + property.Name,
            out string? renamed)
            ? renamed
            : EscapeIdentifier(ToPascalCase(property.Name));

    /// <summary>Maps the C# name of the event of a signal.</summary>
    /// <param name="declarationNamespace">The gir namespace of the declaring type.</param>
    /// <param name="owner">The declaring type.</param>
    /// <param name="signal">The signal to name.</param>
    /// <returns>The C# event name, for example <c>PadAdded</c> for <c>pad-added</c>.</returns>
    /// <remarks>
    /// The rename key is the GObject spelling of a signal,
    /// <c>Gst.Element::pad-added</c>, which can collide neither with the key of
    /// a property (<c>Gst.Element:name</c>) nor with the one of a member.
    /// </remarks>
    internal string SignalName(GirNamespace declarationNamespace, GirTypeDeclaration owner, GirSignal signal) =>
        _overlays.TryGetRename(
            declarationNamespace.Name + "." + owner.Name + "::" + signal.Name,
            out string? renamed)
            ? renamed
            : EscapeIdentifier(ToPascalCase(signal.Name));

    /// <summary>Maps the C# name of a parameter or a local.</summary>
    /// <param name="girName">The verbatim gir name.</param>
    /// <returns>The C# identifier.</returns>
    internal static string ParameterName(string girName) => EscapeIdentifier(ToCamelCase(girName));

    /// <summary>Maps the C# name of a field that is part of the API.</summary>
    /// <param name="fieldNamespace">The gir namespace of the declaring type.</param>
    /// <param name="owner">The declaring type.</param>
    /// <param name="field">The field to name.</param>
    /// <returns>The C# field name.</returns>
    internal string FieldName(GirNamespace fieldNamespace, GirTypeDeclaration owner, GirField field) =>
        _overlays.TryGetRename(fieldNamespace.Name + "." + owner.Name + "." + field.Name, out string? renamed)
            ? renamed
            : EscapeIdentifier(ToPascalCase(field.Name));

    /// <summary>
    /// Maps the C# name of a field that is private to the C implementation.
    /// Such a field only exists because it takes up space in the layout.
    /// </summary>
    /// <param name="fieldNamespace">The gir namespace of the declaring type.</param>
    /// <param name="owner">The declaring type.</param>
    /// <param name="field">The field to name.</param>
    /// <returns>The C# field name.</returns>
    internal string PrivateFieldName(GirNamespace fieldNamespace, GirTypeDeclaration owner, GirField field) =>
        "_" + ToCamelCase(FieldName(fieldNamespace, owner, field));

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
