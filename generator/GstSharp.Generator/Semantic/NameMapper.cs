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
    private readonly DiagnosticBag _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="NameMapper"/> class.</summary>
    /// <param name="overlays">The overlay configuration holding the renames.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal NameMapper(Overlays overlays, DiagnosticBag diagnostics)
    {
        _overlays = overlays;
        _diagnostics = diagnostics;
    }

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

        // A derived name can start with a digit, as gst_video_scaler_2d does.
        // Prefixing with an underscore is the deterministic rule that keeps such
        // a name legal. It does not apply to enumeration members: an underscore
        // in front of a number says nothing about what the member means, so
        // EnumMemberName demands a rename from the overlays instead of escaping.
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

    /// <summary>Maps the C# name of a parameter of a virtual method.</summary>
    /// <param name="overlayKey">
    /// The key of the slot, in the <c>Ns.Class::vfunc</c> spelling.
    /// </param>
    /// <param name="girName">The verbatim gir name of the parameter.</param>
    /// <returns>The C# identifier.</returns>
    /// <remarks>
    /// The name of a parameter of a virtual method is public surface, because
    /// the <c>OnX</c> method it becomes can be called with named arguments.
    /// Several of them are abbreviations the camel casing cannot expand —
    /// <c>incaps</c> is two words run together, <c>buf</c> is one word cut
    /// short — so the <c>rename</c> overlay addresses them one by one, at
    /// <c>Ns.Class::vfunc#parameter</c>.
    /// </remarks>
    internal string VirtualMethodParameterName(string overlayKey, string girName) =>
        _overlays.TryGetRename(overlayKey + "#" + girName, out string? renamed)
            ? renamed
            : ParameterName(girName);

    /// <summary>
    /// The suffix that a public field of a value projected record carries when
    /// the field is projected onto a bare pointer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record that marshals by value spells every pointer field as an
    /// <c>nint</c>, because that is all the layout can say: the address of a
    /// string, of a mini object, of a boxed record or of a block of bytes is one
    /// pointer wide and nothing more is known about it at the point the struct
    /// is laid out. The name a field derives from the gir is the name the typed
    /// accessor wants - <c>string? Nick</c> over <c>const gchar *nick</c>,
    /// <c>Gst.Memory? Memory</c> over <c>GstMemory *memory</c>,
    /// <c>ReadOnlySpan&lt;byte&gt; Data</c> over the mapped block - and a
    /// property cannot carry the name of a field of the same type. Emitting the
    /// raw address under the derived name therefore spends the one name the
    /// accessor needs on the projection that says the least, and a released
    /// surface cannot take it back: adding <c>Nick</c> later is a source and
    /// binary break, while adding it beside an existing <c>NickPtr</c> is
    /// additive and can be done in any 1.28.x.
    /// </para>
    /// <para>
    /// The rule is driven by the projected type and not by an entry in
    /// <c>girs/overlays/fixups.json</c>, so a gir refresh that adds a pointer
    /// field to a value type gets the suffix without anyone remembering to ask
    /// for it. It reaches public fields of value projected records alone: a
    /// field that is private to the C implementation is named by
    /// <see cref="PrivateFieldName"/>, and an inline array is storage rather
    /// than an address. A boxed record, a mini object and a record behind a
    /// pointer are wrapped rather than projected by value, and their fields
    /// reach the surface as get only properties over an internal mirror; the
    /// mirror spells a pointer field as a plain <c>nint</c> under the name the
    /// gir gives it, and no property is emitted for one, so nothing competes
    /// for the name and the suffix has nothing to do there. An explicit rename
    /// still wins, which is the escape hatch for a field the suffix reads badly
    /// on.
    /// </para>
    /// </remarks>
    internal const string PointerFieldSuffix = "Ptr";

    /// <summary>Maps the C# name of a field that is part of the API.</summary>
    /// <param name="fieldNamespace">The gir namespace of the declaring type.</param>
    /// <param name="owner">The declaring type.</param>
    /// <param name="field">The field to name.</param>
    /// <param name="barePointer">
    /// <see langword="true"/> when the field is a public field of a value
    /// projected record that lands on a bare pointer, which appends
    /// <see cref="PointerFieldSuffix"/>.
    /// </param>
    /// <returns>The C# field name.</returns>
    internal string FieldName(
        GirNamespace fieldNamespace,
        GirTypeDeclaration owner,
        GirField field,
        bool barePointer = false)
    {
        if (_overlays.TryGetRename(
                fieldNamespace.Name + "." + owner.Name + "." + field.Name,
                out string? renamed))
        {
            return renamed;
        }

        string name = EscapeIdentifier(ToPascalCase(field.Name));
        return barePointer ? name + PointerFieldSuffix : name;
    }

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
    /// <remarks>
    /// A member whose gir name starts with a digit has no derived C# name that
    /// says anything: <c>GST_RTSP_VERSION_1_0</c> would be spelled
    /// <c>_10</c>, which reads as ten and whose value is 16. Such a member is
    /// named in <c>girs/overlays/fixups.json</c>, and a run that meets one
    /// without a rename fails with <c>GEN0016</c> rather than emitting the
    /// escape. The escaped name is still returned so that the rest of the run
    /// works on legal C#; nothing is written, because the error stops the run
    /// before the files reach the disk.
    /// </remarks>
    internal string EnumMemberName(GirEnumeration enumeration, GirNamespace enumerationNamespace, GirEnumMember member)
    {
        string key = enumerationNamespace.Name + "." + enumeration.Name + "." + member.Name;
        if (_overlays.TryGetRename(key, out string? renamed))
        {
            return renamed;
        }

        string source = member.Name.Length > 0 ? member.Name : member.Nick ?? string.Empty;
        string derived = ToPascalCase(source);
        if (derived.Length > 0 && char.IsAsciiDigit(derived[0]))
        {
            _diagnostics.Error(
                "GEN0016",
                $"Enumeration member '{key}' derives the C# name '{derived}', which starts with a digit. "
                + $"Add a rename for the key '{key}' to girs/overlays/fixups.json that spells out what the "
                + "leading number means; the generator does not escape such a name with an underscore.");
        }

        return EscapeIdentifier(derived);
    }
}
