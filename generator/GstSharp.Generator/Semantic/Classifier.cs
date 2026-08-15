using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// How a gir declaration is projected onto C#.
/// </summary>
internal enum TypeKind
{
    /// <summary>Not understood; treated as an opaque pointer.</summary>
    Unknown,

    /// <summary>An <c>&lt;enumeration&gt;</c>.</summary>
    EnumType,

    /// <summary>A <c>&lt;bitfield&gt;</c>, emitted with <c>[Flags]</c>.</summary>
    FlagsType,

    /// <summary>A <c>GObject</c> derived class.</summary>
    GObjectClass,

    /// <summary>A <c>GInterface</c>.</summary>
    Interface,

    /// <summary>A boxed type, that is a record with a <c>glib:get-type</c>.</summary>
    Boxed,

    /// <summary>A <c>GstMiniObject</c> derived type.</summary>
    MiniObject,

    /// <summary>A record whose fields are all blittable, marshalled by value.</summary>
    PlainStruct,

    /// <summary>A record that can only be handled behind a pointer.</summary>
    OpaqueRecord,

    /// <summary>A <c>&lt;callback&gt;</c>.</summary>
    Callback,

    /// <summary>A GType fundamental; hand written, never generated.</summary>
    Fundamental,

    /// <summary>A class or interface vtable struct; skipped entirely.</summary>
    GTypeStruct,

    /// <summary>A <c>&lt;union&gt;</c>; treated as opaque for now.</summary>
    Union,

    /// <summary>An <c>&lt;alias&gt;</c>.</summary>
    Alias,
}

/// <summary>
/// Assigns a <see cref="TypeKind"/> to every gir declaration.
/// </summary>
internal sealed class Classifier
{
    /// <summary>The gir namespace that owns <c>GstMiniObject</c>.</summary>
    private const string GstNamespace = "Gst";

    /// <summary>The gir name of the mini object base record.</summary>
    private const string MiniObjectName = "MiniObject";

    /// <summary>
    /// Mini objects that carry no fields in the gir, so that the first field
    /// rule cannot see them. They are opaque records with a <c>glib:get-type</c>
    /// and would otherwise be classified as boxed types.
    /// </summary>
    private static readonly HashSet<string> OpaqueMiniObjects = new(StringComparer.Ordinal)
    {
        "BufferList",
        "Context",
        "Sample",
    };

    private static readonly HashSet<string> NonBlittablePrimitives = new(StringComparer.Ordinal)
    {
        "none",
        "utf8",
        "filename",
        "va_list",
        "long double",
    };

    private readonly Repository _repository;
    private readonly DiagnosticBag _diagnostics;
    private readonly Dictionary<GirNode, TypeKind> _cache = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a new instance of the <see cref="Classifier"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal Classifier(Repository repository, DiagnosticBag diagnostics)
    {
        _repository = repository;
        _diagnostics = diagnostics;
    }

    /// <summary>Tests whether a kind is generated at all.</summary>
    /// <param name="kind">The classification.</param>
    /// <returns><see langword="true"/> when nothing is emitted for the type.</returns>
    internal static bool IsSkipped(TypeKind kind) =>
        kind is TypeKind.GTypeStruct or TypeKind.Fundamental or TypeKind.Unknown;

    /// <summary>Classifies a symbol.</summary>
    /// <param name="symbol">The symbol to classify.</param>
    /// <returns>The classification.</returns>
    internal TypeKind Classify(GirSymbol symbol) => Classify(symbol.Declaration);

    /// <summary>Classifies a declaration.</summary>
    /// <param name="declaration">The declaration to classify.</param>
    /// <returns>The classification.</returns>
    internal TypeKind Classify(GirNode declaration)
    {
        if (_cache.TryGetValue(declaration, out TypeKind cached))
        {
            return cached;
        }

        TypeKind kind = ClassifyCore(declaration);
        _cache[declaration] = kind;
        return kind;
    }

    /// <summary>
    /// Tests whether every field of a record can be laid out as a blittable C#
    /// struct field.
    /// </summary>
    /// <param name="record">The record to inspect.</param>
    /// <returns><see langword="true"/> when the record marshals by value.</returns>
    internal bool IsBlittableRecord(GirRecord record) =>
        IsBlittableRecord(record, new HashSet<GirNode>(ReferenceEqualityComparer.Instance));

    private TypeKind ClassifyCore(GirNode declaration) => declaration switch
    {
        GirEnumeration enumeration => enumeration.IsBitfield ? TypeKind.FlagsType : TypeKind.EnumType,
        GirInterface => TypeKind.Interface,
        GirCallback => TypeKind.Callback,
        GirAlias => TypeKind.Alias,
        GirUnion => TypeKind.Union,
        GirClass girClass => ClassifyClass(girClass),
        GirRecord record => ClassifyRecord(record),
        _ => TypeKind.Unknown,
    };

    private TypeKind ClassifyClass(GirClass girClass)
    {
        // GType fundamentals (GstFraction, GstIntRange, the GObject ParamSpec
        // family, ...) are hand written; the generator only records them.
        if (girClass.IsFundamental || string.Equals(girClass.GlibGetType, "intern", StringComparison.Ordinal))
        {
            return TypeKind.Fundamental;
        }

        return TypeKind.GObjectClass;
    }

    private TypeKind ClassifyRecord(GirRecord record)
    {
        if (record.GlibIsGTypeStructFor is not null)
        {
            return TypeKind.GTypeStruct;
        }

        if (IsMiniObject(record))
        {
            return TypeKind.MiniObject;
        }

        if (record.GlibGetType is not null)
        {
            return TypeKind.Boxed;
        }

        if (record.IsDisguised || record.IsOpaque || record.IsPointer)
        {
            return TypeKind.OpaqueRecord;
        }

        return IsBlittableRecord(record) ? TypeKind.PlainStruct : TypeKind.OpaqueRecord;
    }

    private bool IsMiniObject(GirRecord record)
    {
        GirNamespace? ns = _repository.GetNamespaceOf(record);
        if (ns is null)
        {
            return false;
        }

        if (string.Equals(ns.Name, GstNamespace, StringComparison.Ordinal)
            && (string.Equals(record.Name, MiniObjectName, StringComparison.Ordinal)
                || OpaqueMiniObjects.Contains(record.Name)))
        {
            return true;
        }

        // A mini object embeds GstMiniObject as its first field. The field name
        // varies (GstPromise calls it "parent"), so the type is what counts.
        if (record.Fields.Count == 0 || record.Fields[0].Type is not { } first || first.IsPointer)
        {
            return false;
        }

        GirSymbol? symbol = _repository.Resolve(first.Name, ns);
        if (symbol is null)
        {
            return false;
        }

        symbol = _repository.ResolveAlias(symbol);
        return symbol is { Kind: GirSymbolKind.Record }
            && string.Equals(symbol.Namespace.Name, GstNamespace, StringComparison.Ordinal)
            && string.Equals(symbol.Name, MiniObjectName, StringComparison.Ordinal);
    }

    private bool IsBlittableRecord(GirRecord record, HashSet<GirNode> visiting)
    {
        if (record.Fields.Count == 0 || record.Unions.Count > 0 || record.IsDisguised || record.IsOpaque)
        {
            return false;
        }

        if (!visiting.Add(record))
        {
            // A record cannot embed itself by value; a cycle means the gir
            // describes a pointer graph we cannot lay out.
            return false;
        }

        try
        {
            GirNamespace? ns = _repository.GetNamespaceOf(record);
            foreach (GirField field in record.Fields)
            {
                if (field.Bits is not null || field.Callback is not null || field.Type is null)
                {
                    return false;
                }

                if (!IsBlittableType(field.Type, ns, visiting))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            visiting.Remove(record);
        }
    }

    private bool IsBlittableType(GirTypeRef type, GirNamespace? ns, HashSet<GirNode> visiting)
    {
        if (type.IsVarArgs)
        {
            return false;
        }

        if (type is GirArrayRef array)
        {
            // Only fixed size arrays are laid out inline; everything else decays
            // to a pointer field.
            return array.FixedSize is null
                || (array.ElementType is { } element && IsBlittableType(element, ns, visiting));
        }

        if (type.IsPointer)
        {
            return true;
        }

        // Aliases such as GstClockTime and GQuark end in a built-in type and
        // stay blittable.
        string? resolvedName = _repository.ResolveAliasedName(type.Name, ns);
        if (Repository.IsPrimitive(resolvedName))
        {
            return !NonBlittablePrimitives.Contains(resolvedName!);
        }

        GirSymbol? symbol = _repository.Resolve(type.Name, ns);
        if (symbol is null)
        {
            _diagnostics.Warn(
                "GEN0001",
                $"Unresolved type reference '{type.Name ?? type.CType ?? "?"}' in namespace '{ns?.Name ?? "?"}'; treated as an opaque pointer.");
            return false;
        }

        symbol = _repository.ResolveAlias(symbol);
        return symbol switch
        {
            null => false,
            { Kind: GirSymbolKind.Enumeration } => true,
            { Kind: GirSymbolKind.Record, Declaration: GirRecord nested } => IsBlittableRecord(nested, visiting),
            _ => false,
        };
    }
}
