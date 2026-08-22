using System.Globalization;
using System.Xml.Linq;
using GstSharp.Generator.GirParsing.Model;

namespace GstSharp.Generator.GirParsing;

/// <summary>
/// Faithful <c>.gir</c> reader. It maps the XML onto the POCO model without
/// interpreting anything: no naming, no filtering, no type resolution.
/// </summary>
internal static class GirReader
{
    /// <summary>Reads a gir file from disk.</summary>
    /// <param name="path">Path of the gir file.</param>
    /// <returns>The parsed repository.</returns>
    internal static GirRepository ReadFile(string path)
    {
        // GStreamer gir files carry no <?xml?> declaration and start with a
        // comment; XDocument accepts that.
        XDocument document = XDocument.Load(path, LoadOptions.None);
        return ReadDocument(document, path);
    }

    /// <summary>Reads gir markup from a string, for tests and fixtures.</summary>
    /// <param name="xml">The gir markup.</param>
    /// <param name="filePath">A display path recorded on the repository.</param>
    /// <returns>The parsed repository.</returns>
    internal static GirRepository ReadXml(string xml, string filePath = "<memory>") =>
        ReadDocument(XDocument.Parse(xml), filePath);

    private static GirRepository ReadDocument(XDocument document, string filePath)
    {
        XElement root = document.Root
            ?? throw new InvalidDataException(FormattableString.Invariant($"The gir file {filePath} has no root element."));
        if (root.Name != GirXml.Core + "repository")
        {
            throw new InvalidDataException(
                FormattableString.Invariant($"The file {filePath} is not a gir file; its root element is {root.Name}."));
        }

        return new GirRepository
        {
            FilePath = filePath,
            SchemaVersion = Attr(root, "version"),
            Includes =
            [
                .. Children(root, "include").Select(static e => new GirInclude(
                    Attr(e, "name") ?? string.Empty,
                    Attr(e, "version") ?? string.Empty)),
            ],
            Packages = [.. Children(root, "package").Select(static e => Attr(e, "name") ?? string.Empty)],
            CIncludes = [.. root.Elements(GirXml.C + "include").Select(static e => Attr(e, "name") ?? string.Empty)],
            Namespaces = [.. Children(root, "namespace").Select(ReadNamespace)],
        };
    }

    private static GirNamespace ReadNamespace(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            Version = Attr(element, "version") ?? string.Empty,
            SharedLibrary = Attr(element, "shared-library"),
            IdentifierPrefixes = SplitList(CAttr(element, "identifier-prefixes")),
            SymbolPrefixes = SplitList(CAttr(element, "symbol-prefixes")),
            Aliases = [.. Children(element, "alias").Select(ReadAlias)],
            Classes = [.. Children(element, "class").Select(ReadClass)],
            Records = [.. Children(element, "record").Select(ReadRecord)],
            Interfaces = [.. Children(element, "interface").Select(ReadInterface)],
            Unions = ReadUnions(element),
            Enumerations = [.. Children(element, "enumeration").Select(static e => ReadEnumeration(e, isBitfield: false))],
            Bitfields = [.. Children(element, "bitfield").Select(static e => ReadEnumeration(e, isBitfield: true))],
            Callbacks = [.. Children(element, "callback").Select(static e => ReadCallback(e, isFieldSlot: false))],
            Constants = [.. Children(element, "constant").Select(ReadConstant)],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
        };

    private static GirAlias ReadAlias(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            CType = CAttr(element, "type"),
            Target = ReadTypeOf(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirConstant ReadConstant(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            Value = Attr(element, "value") ?? string.Empty,
            CType = CAttr(element, "type"),
            Type = ReadTypeOf(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirEnumeration ReadEnumeration(XElement element, bool isBitfield) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            IsBitfield = isBitfield,
            CType = CAttr(element, "type"),
            GlibTypeName = GlibAttr(element, "type-name"),
            GlibGetType = GlibAttr(element, "get-type"),
            ErrorDomain = GlibAttr(element, "error-domain"),
            Members = [.. Children(element, "member").Select(ReadEnumMember)],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirEnumMember ReadEnumMember(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            Value = Attr(element, "value") ?? string.Empty,
            CIdentifier = CAttr(element, "identifier"),
            Nick = GlibAttr(element, "nick"),
            GlibName = GlibAttr(element, "name"),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
        };

    private static GirClass ReadClass(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            Parent = Attr(element, "parent"),
            IsAbstract = Flag(element, "abstract"),
            GlibTypeStruct = GlibAttr(element, "type-struct"),
            IsFundamental = GlibFlag(element, "fundamental"),
            Implements = [.. Children(element, "implements").Select(static e => Attr(e, "name") ?? string.Empty)],
            CType = CAttr(element, "type"),
            CSymbolPrefix = CAttr(element, "symbol-prefix"),
            GlibTypeName = GlibAttr(element, "type-name"),
            GlibGetType = GlibAttr(element, "get-type"),
            Fields = [.. Children(element, "field").Select(ReadField)],
            Constructors = [.. Children(element, "constructor").Select(static e => ReadFunction(e, GirCallableKind.Constructor))],
            Methods = [.. Children(element, "method").Select(static e => ReadFunction(e, GirCallableKind.Method))],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
            VirtualMethods = [.. Children(element, "virtual-method").Select(ReadVirtualMethod)],
            Signals = [.. element.Elements(GirXml.Glib + "signal").Select(ReadSignal)],
            Properties = [.. Children(element, "property").Select(ReadProperty)],
            Unions = ReadUnions(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirRecord ReadRecord(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            IsDisguised = Flag(element, "disguised"),
            IsOpaque = Flag(element, "opaque"),
            IsPointer = Flag(element, "pointer"),
            GlibIsGTypeStructFor = GlibAttr(element, "is-gtype-struct-for"),
            CType = CAttr(element, "type"),
            CSymbolPrefix = CAttr(element, "symbol-prefix"),
            GlibTypeName = GlibAttr(element, "type-name"),
            GlibGetType = GlibAttr(element, "get-type"),
            Fields = [.. Children(element, "field").Select(ReadField)],
            Constructors = [.. Children(element, "constructor").Select(static e => ReadFunction(e, GirCallableKind.Constructor))],
            Methods = [.. Children(element, "method").Select(static e => ReadFunction(e, GirCallableKind.Method))],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
            Unions = ReadUnions(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirInterface ReadInterface(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            GlibTypeStruct = GlibAttr(element, "type-struct"),
            Prerequisites = [.. Children(element, "prerequisite").Select(static e => Attr(e, "name") ?? string.Empty)],
            CType = CAttr(element, "type"),
            CSymbolPrefix = CAttr(element, "symbol-prefix"),
            GlibTypeName = GlibAttr(element, "type-name"),
            GlibGetType = GlibAttr(element, "get-type"),
            Fields = [.. Children(element, "field").Select(ReadField)],
            Methods = [.. Children(element, "method").Select(static e => ReadFunction(e, GirCallableKind.Method))],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
            VirtualMethods = [.. Children(element, "virtual-method").Select(ReadVirtualMethod)],
            Signals = [.. element.Elements(GirXml.Glib + "signal").Select(ReadSignal)],
            Properties = [.. Children(element, "property").Select(ReadProperty)],
            Doc = Doc(element),
            Version = Attr(element, "version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    /// <summary>
    /// Reads the unions declared inside one element, remembering how many
    /// <c>&lt;field&gt;</c> siblings precede each of them.
    /// </summary>
    /// <param name="element">The declaring element.</param>
    /// <returns>The unions, in document order.</returns>
    /// <remarks>
    /// The fields and the unions of a structure end up in two separate lists,
    /// so the count of the fields in front of a union is what is left of the
    /// document order, and it is what says where the union sits in the C
    /// layout.
    /// </remarks>
    private static IReadOnlyList<GirUnion> ReadUnions(XElement element)
    {
        List<GirUnion> unions = [];
        int fields = 0;
        foreach (XElement child in element.Elements())
        {
            if (child.Name == GirXml.Core + "field")
            {
                fields++;
            }
            else if (child.Name == GirXml.Core + "union")
            {
                unions.Add(ReadUnion(child, fields));
            }
        }

        return unions;
    }

    private static GirUnion ReadUnion(XElement element, int fieldIndex) =>
        new()
        {
            FieldIndex = fieldIndex,
            Name = Attr(element, "name") ?? string.Empty,
            CType = CAttr(element, "type"),
            CSymbolPrefix = CAttr(element, "symbol-prefix"),
            GlibTypeName = GlibAttr(element, "type-name"),
            GlibGetType = GlibAttr(element, "get-type"),
            Fields = [.. Children(element, "field").Select(ReadField)],
            Records = [.. Children(element, "record").Select(ReadRecord)],
            Constructors = [.. Children(element, "constructor").Select(static e => ReadFunction(e, GirCallableKind.Constructor))],
            Methods = [.. Children(element, "method").Select(static e => ReadFunction(e, GirCallableKind.Method))],
            Functions = [.. Children(element, "function").Select(static e => ReadFunction(e, GirCallableKind.Function))],
            Doc = Doc(element),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirField ReadField(XElement element)
    {
        XElement? callback = element.Element(GirXml.Core + "callback");
        return new GirField
        {
            Name = Attr(element, "name") ?? string.Empty,
            IsWritable = Flag(element, "writable"),
            IsReadable = FlagOrTrue(element, "readable"),
            IsPrivate = Flag(element, "private"),
            Bits = IntAttr(element, "bits"),
            Type = callback is null ? ReadTypeOf(element) : null,
            Callback = callback is null ? null : ReadCallback(callback, isFieldSlot: true),
            Doc = Doc(element),
            Version = Attr(element, "version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };
    }

    private static GirProperty ReadProperty(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            IsWritable = Flag(element, "writable"),
            IsReadable = FlagOrTrue(element, "readable"),
            IsConstructOnly = Flag(element, "construct-only"),
            IsConstruct = Flag(element, "construct"),
            Getter = Attr(element, "getter"),
            Setter = Attr(element, "setter"),
            DefaultValue = Attr(element, "default-value"),
            Transfer = ParseTransfer(Attr(element, "transfer-ownership")),
            Type = ReadTypeOf(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirFunction ReadFunction(XElement element, GirCallableKind kind) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            FunctionKind = kind,
            CIdentifier = CAttr(element, "identifier"),
            MovedTo = Attr(element, "moved-to"),
            Shadows = Attr(element, "shadows"),
            ShadowedBy = Attr(element, "shadowed-by"),
            Throws = Flag(element, "throws"),
            GetsProperty = GlibAttr(element, "get-property"),
            SetsProperty = GlibAttr(element, "set-property"),
            InstanceParameter = ReadInstanceParameter(element),
            Parameters = ReadParameters(element),
            ReturnValue = ReadReturnValue(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirVirtualMethod ReadVirtualMethod(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            Invoker = Attr(element, "invoker"),
            Throws = Flag(element, "throws"),
            InstanceParameter = ReadInstanceParameter(element),
            Parameters = ReadParameters(element),
            ReturnValue = ReadReturnValue(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirCallback ReadCallback(XElement element, bool isFieldSlot) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            CType = CAttr(element, "type"),
            IsFieldSlot = isFieldSlot,
            Throws = Flag(element, "throws"),
            Parameters = ReadParameters(element),
            ReturnValue = ReadReturnValue(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirSignal ReadSignal(XElement element) =>
        new()
        {
            Name = Attr(element, "name") ?? string.Empty,
            When = Attr(element, "when"),
            IsDetailed = Flag(element, "detailed"),
            IsAction = Flag(element, "action"),
            NoRecurse = Flag(element, "no-recurse"),
            NoHooks = Flag(element, "no-hooks"),
            Parameters = ReadParameters(element),
            ReturnValue = ReadReturnValue(element),
            Doc = Doc(element),
            DocDeprecated = DocDeprecated(element),
            Version = Attr(element, "version"),
            IsDeprecated = Flag(element, "deprecated"),
            DeprecatedVersion = Attr(element, "deprecated-version"),
            IsIntrospectable = FlagOrTrue(element, "introspectable"),
        };

    private static GirInstanceParameter? ReadInstanceParameter(XElement callable)
    {
        XElement? parameters = callable.Element(GirXml.Core + "parameters");
        XElement? element = parameters?.Element(GirXml.Core + "instance-parameter");
        if (element is null)
        {
            return null;
        }

        return new GirInstanceParameter
        {
            Name = Attr(element, "name") ?? string.Empty,
            Transfer = ParseTransfer(Attr(element, "transfer-ownership")),
            IsNullable = Flag(element, "nullable") || Flag(element, "allow-none"),
            Type = ReadTypeOf(element),
            Doc = Doc(element),
        };
    }

    private static IReadOnlyList<GirParameter> ReadParameters(XElement callable)
    {
        XElement? parameters = callable.Element(GirXml.Core + "parameters");
        return parameters is null ? [] : [.. Children(parameters, "parameter").Select(ReadParameter)];
    }

    private static GirParameter ReadParameter(XElement element)
    {
        bool allowNone = Flag(element, "allow-none");
        bool nullable = element.Attribute("nullable") is { } attribute ? IsSet(attribute.Value) : allowNone;

        return new GirParameter
        {
            Name = Attr(element, "name") ?? string.Empty,
            Direction = ParseDirection(Attr(element, "direction")),
            Transfer = ParseTransfer(Attr(element, "transfer-ownership")),
            IsNullable = nullable,
            IsAllowNone = allowNone,
            IsOptional = Flag(element, "optional"),
            IsCallerAllocates = Flag(element, "caller-allocates"),
            IsSkip = Flag(element, "skip"),
            ClosureIndex = IntAttr(element, "closure"),
            DestroyIndex = IntAttr(element, "destroy"),
            Scope = ParseScope(Attr(element, "scope")),
            IsVarArgs = element.Element(GirXml.Core + "varargs") is not null,
            Type = ReadTypeOf(element),
            Doc = Doc(element),
        };
    }

    private static GirReturnValue ReadReturnValue(XElement callable)
    {
        XElement? element = callable.Element(GirXml.Core + "return-value");
        if (element is null)
        {
            return new GirReturnValue();
        }

        return new GirReturnValue
        {
            Transfer = ParseTransfer(Attr(element, "transfer-ownership")),
            IsNullable = Flag(element, "nullable"),
            IsSkip = Flag(element, "skip"),
            Type = ReadTypeOf(element),
            Doc = Doc(element),
        };
    }

    private static GirTypeRef ReadTypeOf(XElement owner)
    {
        foreach (XElement child in owner.Elements())
        {
            if (IsTypeElement(child))
            {
                return ReadTypeElement(child);
            }
        }

        return new GirTypeRef();
    }

    private static GirTypeRef ReadTypeElement(XElement element)
    {
        if (element.Name == GirXml.Core + "varargs")
        {
            return new GirTypeRef { IsVarArgs = true };
        }

        List<GirTypeRef> inner = [];
        foreach (XElement child in element.Elements())
        {
            if (IsTypeElement(child))
            {
                inner.Add(ReadTypeElement(child));
            }
        }

        if (element.Name == GirXml.Core + "array")
        {
            return new GirArrayRef
            {
                Name = Attr(element, "name"),
                CType = CAttr(element, "type"),
                InnerTypes = inner,
                LengthParameterIndex = IntAttr(element, "length"),
                IsZeroTerminated = Flag(element, "zero-terminated"),
                FixedSize = IntAttr(element, "fixed-size"),
            };
        }

        return new GirTypeRef
        {
            Name = Attr(element, "name"),
            CType = CAttr(element, "type"),
            InnerTypes = inner,
        };
    }

    private static GirTransfer ParseTransfer(string? value) => value switch
    {
        "none" => GirTransfer.None,
        "container" => GirTransfer.Container,
        "full" => GirTransfer.Full,
        "floating" => GirTransfer.Floating,
        _ => GirTransfer.Unspecified,
    };

    private static GirDirection ParseDirection(string? value) => value switch
    {
        "out" => GirDirection.Out,
        "inout" => GirDirection.InOut,
        _ => GirDirection.In,
    };

    private static GirScope ParseScope(string? value) => value switch
    {
        "call" => GirScope.Call,
        "async" => GirScope.Async,
        "notified" => GirScope.Notified,
        "forever" => GirScope.Forever,
        _ => GirScope.Unspecified,
    };

    private static bool IsTypeElement(XElement element) =>
        element.Name == GirXml.Core + "type"
        || element.Name == GirXml.Core + "array"
        || element.Name == GirXml.Core + "varargs";

    private static IEnumerable<XElement> Children(XElement element, string name) =>
        element.Elements(GirXml.Core + name);

    private static string? Doc(XElement element) => element.Element(GirXml.Core + "doc")?.Value;

    private static string? DocDeprecated(XElement element) =>
        element.Element(GirXml.Core + "doc-deprecated")?.Value;

    private static string? Attr(XElement element, string name) => element.Attribute(name)?.Value;

    private static string? CAttr(XElement element, string name) => element.Attribute(GirXml.C + name)?.Value;

    private static string? GlibAttr(XElement element, string name) => element.Attribute(GirXml.Glib + name)?.Value;

    private static bool Flag(XElement element, string name) =>
        element.Attribute(name) is { } attribute && IsSet(attribute.Value);

    private static bool GlibFlag(XElement element, string name) =>
        element.Attribute(GirXml.Glib + name) is { } attribute && IsSet(attribute.Value);

    private static bool FlagOrTrue(XElement element, string name) =>
        element.Attribute(name) is not { } attribute || IsSet(attribute.Value);

    private static bool IsSet(string value) =>
        string.Equals(value, "1", StringComparison.Ordinal)
        || string.Equals(value, "true", StringComparison.Ordinal);

    private static int? IntAttr(XElement element, string name) =>
        element.Attribute(name) is { } attribute
        && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
