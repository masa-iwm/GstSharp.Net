using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.Semantic;

/// <summary>
/// How a mapped type crosses the interop boundary.
/// </summary>
internal enum MarshalKind
{
    /// <summary>No value.</summary>
    Void,

    /// <summary>Passed as-is; the raw and the public type are identical.</summary>
    Blittable,

    /// <summary><c>gboolean</c>: an <see cref="int"/> that is <c>0</c> or non-zero.</summary>
    Boolean,

    /// <summary>A <c>GType</c> handle.</summary>
    GType,

    /// <summary>A <c>GQuark</c>.</summary>
    Quark,

    /// <summary>A UTF-8 string.</summary>
    Utf8String,

    /// <summary>An OS encoded file name.</summary>
    FilenameString,

    /// <summary>A generated enumeration.</summary>
    Enum,

    /// <summary>A generated bitfield.</summary>
    Flags,

    /// <summary>An untyped pointer.</summary>
    Pointer,

    /// <summary>A <c>GObject</c> derived instance.</summary>
    GObject,

    /// <summary>A <c>GInterface</c> instance.</summary>
    Interface,

    /// <summary>A <c>GstMiniObject</c> derived instance.</summary>
    MiniObject,

    /// <summary>A boxed instance.</summary>
    Boxed,

    /// <summary>
    /// A <c>GValue</c>, projected onto the hand written
    /// <c>Gst.GObject.Value</c> struct and passed as a pointer to storage the
    /// caller owns.
    /// </summary>
    GValue,

    /// <summary>
    /// A <c>GError</c>, projected onto the hand written
    /// <c>Gst.GLib.GException</c>.
    /// </summary>
    GError,

    /// <summary>A blittable struct passed by value.</summary>
    PlainStruct,

    /// <summary>An opaque record handled behind a pointer.</summary>
    OpaqueRecord,

    /// <summary>A callback, marshalled as a function pointer.</summary>
    Callback,

    /// <summary>A C array.</summary>
    Array,

    /// <summary>A <c>GList</c>.</summary>
    GList,

    /// <summary>A <c>GSList</c>.</summary>
    GSList,

    /// <summary>A <c>GPtrArray</c>.</summary>
    GPtrArray,

    /// <summary>A <c>GArray</c>.</summary>
    GArray,

    /// <summary>A <c>GByteArray</c>.</summary>
    GByteArray,

    /// <summary>A <c>GHashTable</c>.</summary>
    GHashTable,

    /// <summary>A GType fundamental that is hand written.</summary>
    Fundamental,

    /// <summary>Not bindable, for example <c>va_list</c>.</summary>
    Unsupported,
}

/// <summary>
/// The C# projection of a gir type reference.
/// </summary>
internal sealed class MappedType
{
    /// <summary>Gets the type used in the <c>LibraryImport</c> signature.</summary>
    internal required string RawType { get; init; }

    /// <summary>Gets the type used in the public API.</summary>
    internal required string PublicType { get; init; }

    /// <summary>Gets how the value crosses the interop boundary.</summary>
    internal required MarshalKind Kind { get; init; }

    /// <summary>Gets the element type of a container.</summary>
    internal MappedType? ElementType { get; init; }

    /// <summary>Gets the key type of a <c>GHashTable</c>.</summary>
    internal MappedType? KeyType { get; init; }

    /// <summary>Gets the index of the parameter carrying the array length.</summary>
    internal int? LengthParameterIndex { get; init; }

    /// <summary>Gets a value indicating whether an array is zero terminated.</summary>
    internal bool IsZeroTerminated { get; init; }

    /// <summary>Gets the inline element count of a fixed size array.</summary>
    internal int? FixedSize { get; init; }

    /// <summary>Gets the resolved gir symbol, when the type is not built in.</summary>
    internal GirSymbol? Symbol { get; init; }
}

/// <summary>
/// Maps gir type references onto raw and public C# types.
/// </summary>
internal sealed class TypeMap
{
    private const string NativeInt = "nint";
    private const string NativeUInt = "nuint";

    private static readonly Dictionary<string, MappedType> Primitives = new(StringComparer.Ordinal)
    {
        ["none"] = Simple("void", MarshalKind.Void),
        ["gboolean"] = new MappedType { RawType = "int", PublicType = "bool", Kind = MarshalKind.Boolean },
        ["gchar"] = Simple("sbyte", MarshalKind.Blittable),
        ["guchar"] = Simple("byte", MarshalKind.Blittable),
        ["gint8"] = Simple("sbyte", MarshalKind.Blittable),
        ["guint8"] = Simple("byte", MarshalKind.Blittable),
        ["gshort"] = Simple("short", MarshalKind.Blittable),
        ["gushort"] = Simple("ushort", MarshalKind.Blittable),
        ["gint16"] = Simple("short", MarshalKind.Blittable),
        ["guint16"] = Simple("ushort", MarshalKind.Blittable),
        ["gint"] = Simple("int", MarshalKind.Blittable),
        ["guint"] = Simple("uint", MarshalKind.Blittable),
        ["gint32"] = Simple("int", MarshalKind.Blittable),
        ["guint32"] = Simple("uint", MarshalKind.Blittable),
        ["gint64"] = Simple("long", MarshalKind.Blittable),
        ["guint64"] = Simple("ulong", MarshalKind.Blittable),
        ["glong"] = Simple("System.Runtime.InteropServices.CLong", MarshalKind.Blittable),
        ["gulong"] = Simple("System.Runtime.InteropServices.CULong", MarshalKind.Blittable),
        ["gsize"] = Simple(NativeUInt, MarshalKind.Blittable),
        ["gssize"] = Simple(NativeInt, MarshalKind.Blittable),
        ["gintptr"] = Simple(NativeInt, MarshalKind.Blittable),
        ["guintptr"] = Simple(NativeUInt, MarshalKind.Blittable),
        ["gfloat"] = Simple("float", MarshalKind.Blittable),
        ["gdouble"] = Simple("double", MarshalKind.Blittable),
        ["gunichar"] = Simple("uint", MarshalKind.Blittable),
        ["gunichar2"] = Simple("ushort", MarshalKind.Blittable),
        ["gpointer"] = Simple(NativeInt, MarshalKind.Pointer),
        ["gconstpointer"] = Simple(NativeInt, MarshalKind.Pointer),
        ["utf8"] = new MappedType { RawType = NativeInt, PublicType = "string", Kind = MarshalKind.Utf8String },
        ["filename"] = new MappedType { RawType = NativeInt, PublicType = "string", Kind = MarshalKind.FilenameString },
        ["GType"] = new MappedType { RawType = NativeUInt, PublicType = "Gst.GObject.GType", Kind = MarshalKind.GType },
        ["va_list"] = Simple(NativeInt, MarshalKind.Unsupported),
        ["long double"] = Simple("double", MarshalKind.Unsupported),
    };

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly DiagnosticBag _diagnostics;

    /// <summary>Initializes a new instance of the <see cref="TypeMap"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    internal TypeMap(Repository repository, Classifier classifier, NameMapper names, DiagnosticBag diagnostics)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _diagnostics = diagnostics;
    }

    /// <summary>Maps a gir type reference.</summary>
    /// <param name="type">The reference to map.</param>
    /// <param name="context">The namespace the reference was written in.</param>
    /// <returns>The mapping.</returns>
    internal MappedType Map(GirTypeRef type, GirNamespace? context)
    {
        if (type.IsVarArgs)
        {
            return Simple(NativeInt, MarshalKind.Unsupported);
        }

        if (type is GirArrayRef array)
        {
            return MapArray(array, context);
        }

        if (type.Name is null)
        {
            return Simple("void", MarshalKind.Void);
        }

        if (Primitives.TryGetValue(type.Name, out MappedType? primitive))
        {
            return primitive;
        }

        return MapNamed(type, type.Name, context);
    }

    /// <summary>Returns the C# type name of a generated enumeration.</summary>
    /// <param name="symbol">The enumeration symbol.</param>
    /// <returns>The fully qualified C# type name.</returns>
    internal string EnumTypeName(GirSymbol symbol) =>
        ModuleMap.ClrNamespaceOf(symbol.Namespace.Name) + "." + _names.TypeName(symbol);

    private static MappedType Simple(string type, MarshalKind kind) =>
        new() { RawType = type, PublicType = type, Kind = kind };

    private static MarshalKind ContainerKind(string name) => name switch
    {
        "GLib.List" => MarshalKind.GList,
        "GLib.SList" => MarshalKind.GSList,
        "GLib.PtrArray" => MarshalKind.GPtrArray,
        "GLib.Array" => MarshalKind.GArray,
        "GLib.ByteArray" => MarshalKind.GByteArray,
        "GLib.HashTable" => MarshalKind.GHashTable,
        _ => MarshalKind.Unsupported,
    };

    private MappedType MapArray(GirArrayRef array, GirNamespace? context)
    {
        // GLib container types are spelled as <array name="GLib.PtrArray">.
        if (array.Name is not null)
        {
            MarshalKind named = ContainerKind(array.Name);
            if (named != MarshalKind.Unsupported)
            {
                return MapContainer(array, named, context);
            }
        }

        MappedType element = array.ElementType is null
            ? Simple(NativeInt, MarshalKind.Pointer)
            : Map(array.ElementType, context);

        return new MappedType
        {
            RawType = NativeInt,
            PublicType = element.PublicType + "[]",
            Kind = MarshalKind.Array,
            ElementType = element,
            LengthParameterIndex = array.LengthParameterIndex,
            IsZeroTerminated = array.IsZeroTerminated,
            FixedSize = array.FixedSize,
        };
    }

    private MappedType MapContainer(GirTypeRef type, MarshalKind kind, GirNamespace? context)
    {
        MappedType? element = type.InnerTypes.Count > 0 ? Map(type.InnerTypes[0], context) : null;
        MappedType? key = null;
        if (kind == MarshalKind.GHashTable && type.InnerTypes.Count > 1)
        {
            key = element;
            element = Map(type.InnerTypes[1], context);
        }

        string publicType = kind switch
        {
            MarshalKind.GHashTable => $"System.Collections.Generic.IDictionary<{key?.PublicType ?? NativeInt}, {element?.PublicType ?? NativeInt}>",
            MarshalKind.GByteArray => "byte[]",
            _ => $"System.Collections.Generic.IReadOnlyList<{element?.PublicType ?? NativeInt}>",
        };

        return new MappedType
        {
            RawType = NativeInt,
            PublicType = publicType,
            Kind = kind,
            ElementType = element,
            KeyType = key,
        };
    }

    private MappedType MapNamed(GirTypeRef type, string name, GirNamespace? context)
    {
        MarshalKind container = ContainerKind(name);
        if (container != MarshalKind.Unsupported)
        {
            return MapContainer(type, container, context);
        }

        GirSymbol? symbol = _repository.Resolve(name, context);
        if (symbol is null)
        {
            _diagnostics.Warn(
                "GEN0001",
                $"Unresolved type reference '{name}' in namespace '{context?.Name ?? "?"}'; treated as an opaque pointer.");
            return Simple(NativeInt, MarshalKind.Pointer);
        }

        return MapSymbol(symbol, type);
    }

    private MappedType MapSymbol(GirSymbol symbol, GirTypeRef type)
    {
        string qualified = symbol.QualifiedName;
        switch (qualified)
        {
            case "GLib.Quark":
                return new MappedType
                {
                    RawType = "uint",
                    PublicType = "Gst.GLib.Quark",
                    Kind = MarshalKind.Quark,
                    Symbol = symbol,
                };
            case "Gst.ClockTime":
                return new MappedType
                {
                    RawType = "ulong",
                    PublicType = "Gst.ClockTime",
                    Kind = MarshalKind.Blittable,
                    Symbol = symbol,
                };
            case "GObject.Value":
                // The runtime declares Gst.GObject.Value by hand, a struct
                // whose NativeValue field is the GValue layout itself, so the
                // call crosses as a pointer into the caller's own storage:
                // nothing is allocated and nothing has to be adopted. The raw
                // side is a typed pointer rather than the `ref` of the hand
                // written import in Custom/Structure.cs, because the interop
                // generator only accepts a by-ref struct it can prove strictly
                // blittable, and a struct from a referenced assembly never is
                // (SYSLIB1051); the member pins the storage with `fixed`
                // instead, which is the same AOT-safe stub. A return travels
                // as a bare pointer; the planner overrides the raw type there.
                return new MappedType
                {
                    RawType = "Gst.GObject.GValueNative*",
                    PublicType = "Gst.GObject.Value",
                    Kind = MarshalKind.GValue,
                    Symbol = symbol,
                };
            case "GLib.Error":
                // The runtime declares Gst.GLib.GException by hand, and it is
                // a plain Exception subclass built from the three fields of a
                // GError rather than a disposable wrapper with a
                // FromNative(nint, Transfer) factory. The route every other
                // hand written GLib type takes - a RuntimeTypes entry read by
                // PlanHandle - therefore does not fit: there is nothing to
                // adopt, nothing to dispose, and the projection is a copy in
                // one direction and a temporary in the other. The special case
                // here is what keeps it out of PlanHandle, exactly as the
                // GValue one above does.
                return new MappedType
                {
                    RawType = "nint",
                    PublicType = "Gst.GLib.GException",
                    Kind = MarshalKind.GError,
                    Symbol = symbol,
                };
        }

        if (symbol.Kind == GirSymbolKind.Alias && symbol.Declaration is GirAlias alias)
        {
            MappedType target = Map(alias.Target, symbol.Namespace);
            return new MappedType
            {
                RawType = target.RawType,
                PublicType = target.PublicType,
                Kind = target.Kind,
                ElementType = target.ElementType,
                KeyType = target.KeyType,
                Symbol = symbol,
            };
        }

        string clrName = ModuleMap.ClrNamespaceOf(symbol.Namespace.Name) + "." + _names.TypeName(symbol);
        TypeKind kind = _classifier.Classify(symbol.Declaration);

        switch (kind)
        {
            case TypeKind.EnumType:
            case TypeKind.FlagsType:
                GirEnumeration enumeration = (GirEnumeration)symbol.Declaration;
                return new MappedType
                {
                    RawType = EnumFacts.ToKeyword(EnumFacts.GetUnderlyingType(enumeration, _diagnostics)),
                    PublicType = clrName,
                    Kind = kind == TypeKind.FlagsType ? MarshalKind.Flags : MarshalKind.Enum,
                    Symbol = symbol,
                };

            case TypeKind.GObjectClass:
                return Handle(clrName, MarshalKind.GObject, symbol);

            case TypeKind.Interface:
                return Handle(clrName, MarshalKind.Interface, symbol);

            case TypeKind.MiniObject:
                return Handle(clrName, MarshalKind.MiniObject, symbol);

            case TypeKind.Boxed:
                return Handle(clrName, MarshalKind.Boxed, symbol);

            case TypeKind.Callback:
                return Handle(clrName, MarshalKind.Callback, symbol);

            case TypeKind.PlainStruct:
                // Passed by value unless the gir spelled a pointer.
                return new MappedType
                {
                    RawType = type.IsPointer ? NativeInt : clrName,
                    PublicType = clrName,
                    Kind = MarshalKind.PlainStruct,
                    Symbol = symbol,
                };

            case TypeKind.Fundamental:
                return Handle(clrName, MarshalKind.Fundamental, symbol);

            default:
                return Handle(clrName, MarshalKind.OpaqueRecord, symbol);
        }
    }

    private static MappedType Handle(string publicType, MarshalKind kind, GirSymbol symbol) =>
        new() { RawType = NativeInt, PublicType = publicType, Kind = kind, Symbol = symbol };
}
