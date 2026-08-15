using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Emits the <c>&lt;class&gt;</c> declarations of a gir namespace, one file per
/// class, plus the holder of the functions that belong to no type.
/// </summary>
/// <remarks>
/// <para>
/// A generated class is <c>partial</c> and not sealed, so that hand written
/// glue can extend it and so that the wrapper of a native subtype can derive
/// from it. Its constructor is <c>internal</c>: instances always come from a
/// generated factory or from the type registry, never from user code, because
/// only those know what ownership the native call transferred.
/// </para>
/// <para>
/// An abstract gir class stays abstract in C#, and carries a private concrete
/// subclass that the type registry instantiates. A registry entry without a
/// factory is not valid (see <c>Gst.Interop.ModuleTypeEntry</c>), and without
/// one the lookup of, say, a <c>GstFakeSrc</c> would walk past
/// <c>GstElement</c> and end up as a bare <c>Gst.GObject.Object</c>.
/// </para>
/// </remarks>
internal sealed class ClassEmitter
{
    /// <summary>The name of the concrete subclass of an abstract wrapper.</summary>
    internal const string ConcreteName = "Concrete";

    /// <summary>The name of the holder of the functions that belong to no type.</summary>
    internal const string GlobalName = "Global";

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly SurfaceBuilder _surfaces;
    private readonly Overlays _overlays;
    private readonly EmissionCensus _census;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<string> _registry;
    private readonly Dictionary<string, List<string>> _inherited = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="ClassEmitter"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="surfaces">The member builder.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    /// <param name="registry">Receives the types that the module registers.</param>
    internal ClassEmitter(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        SurfaceBuilder surfaces,
        Overlays overlays,
        EmissionCensus census,
        DiagnosticBag diagnostics,
        List<string> registry)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _surfaces = surfaces;
        _overlays = overlays;
        _census = census;
        _diagnostics = diagnostics;
        _registry = registry;
    }

    /// <summary>Emits every generated class of one module.</summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated files, ordered by relative path.</returns>
    internal IReadOnlyList<GeneratedFile> Emit(ModuleInfo module, GirNamespace ns)
    {
        List<GeneratedFile> files = [];

        // A class is emitted after its base class, so that the names of the
        // inherited members are known when its own members are planned.
        HashSet<string> done = new(StringComparer.Ordinal);
        foreach (GirClass declaration in Ordered(ns))
        {
            if (!done.Add(declaration.Name))
            {
                continue;
            }

            if (Emit(module, ns, declaration) is { } file)
            {
                files.Add(file);
            }
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return files;
    }

    /// <summary>Emits the functions of a namespace that belong to no type.</summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated file, or <see langword="null"/> when there is nothing to emit.</returns>
    internal GeneratedFile? EmitGlobal(ModuleInfo module, GirNamespace ns)
    {
        PlanningContext context = new(module, ns, TypeKind.Unknown, OwnerType: null);
        GirRecord holder = new() { Name = GlobalName, Functions = ns.Functions };
        TypeSurface surface = _surfaces.Build(
            holder,
            context,
            CallableForm.StaticMethod,
            [GlobalName],
            [],
            includeProperties: false);

        if (surface.IsEmpty)
        {
            return null;
        }

        CodeWriter writer = new();
        WriteHeader(writer, module, ns);
        writer.WriteLine();
        writer.WriteLine(
            "/// <summary>The functions of the <c>" + ns.Name + "</c> namespace that belong to no type.</summary>");
        writer.WriteLine("public static unsafe partial class " + GlobalName);
        writer.OpenBlock();
        WriteMembers(writer, surface, module, first: true);
        writer.CloseBlock();

        _census.Emitted(module.GirNamespace, "class");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + GlobalName + ".cs", writer.ToSource());
    }

    /// <summary>Writes the header every generated file of a module starts with.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    internal static void WriteHeader(CodeWriter writer, ModuleInfo module, GirNamespace ns)
    {
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("using System;");
        writer.WriteLine("using System.Runtime.InteropServices;");
        writer.WriteLine();
        writer.WriteLine("namespace " + module.ClrNamespace + ";");
    }

    /// <summary>Writes the members and the entry points of a type.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="surface">The members to write.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="first">Whether the first member opens the type body.</param>
    /// <param name="cType">The C type of the declaring type, for the documentation of its signals.</param>
    internal static void WriteMembers(
        CodeWriter writer,
        TypeSurface surface,
        ModuleInfo module,
        bool first,
        string cType = "")
    {
        bool leading = first;
        foreach (MarshalPlan member in surface.Members)
        {
            if (!leading)
            {
                writer.WriteLine();
            }

            leading = false;
            CallableRenderer.WriteMember(writer, member);
        }

        foreach (PropertyEmission property in surface.Properties)
        {
            if (!leading)
            {
                writer.WriteLine();
            }

            leading = false;
            WriteProperty(writer, property);
        }

        // The events come after the members that are already bound, and before
        // the imports, so that the public surface of a type stays together.
        foreach (SignalEmission signal in surface.Signals)
        {
            if (!leading)
            {
                writer.WriteLine();
            }

            leading = false;
            SignalEmitter.WriteSignal(writer, signal, module, cType);
        }

        foreach (MarshalPlan member in surface.Members)
        {
            writer.WriteLine();
            CallableRenderer.WriteImport(writer, member, module.NativeLibrary);
        }
    }

    private static void WriteProperty(CodeWriter writer, PropertyEmission property)
    {
        XmlDocWriter.Write(
            writer,
            property.Property.Doc,
            "The <c>" + property.Property.Name + "</c> property.");
        XmlDocWriter.WriteObsolete(writer, property.Property);

        string modifiers = "public " + (property.IsNew ? "new " : string.Empty);
        if (property.Setter is null)
        {
            writer.WriteLine(modifiers + property.Type + " " + property.Name + " => " + property.Getter.Name + "();");
            return;
        }

        writer.WriteLine(modifiers + property.Type + " " + property.Name);
        writer.OpenBlock();
        writer.WriteLine("get => " + property.Getter.Name + "();");
        writer.WriteLine("set => " + property.Setter.Name + "(value);");
        writer.CloseBlock();
    }

    private static string CTypeOf(GirClass declaration) =>
        declaration.CType is { Length: > 0 } cType ? cType : declaration.Name;

    private GeneratedFile? Emit(ModuleInfo module, GirNamespace ns, GirClass declaration)
    {
        string qualifiedName = ns.Name + "." + declaration.Name;
        if (!declaration.IsIntrospectable
            || _overlays.IsSkipped(qualifiedName)
            || _classifier.Classify(declaration) != TypeKind.GObjectClass)
        {
            return null;
        }

        GirSymbol symbol = new(ns, declaration.Name, GirSymbolKind.Class, declaration);
        string typeName = _names.TypeName(symbol);
        if (ResolveBase(ns, declaration) is not { } baseType)
        {
            _diagnostics.Warn(
                "GEN0008",
                $"Class '{qualifiedName}' derives from '{declaration.Parent}', which is not generated; the class is skipped.");
            return null;
        }

        List<string> reserved = [.. SurfaceBuilder.WrapperNames, .. SurfaceBuilder.ObjectNames, typeName, ConcreteName];
        List<string> inherited =
            baseType.InModule is { } parentName && _inherited.TryGetValue(parentName, out List<string>? baseMembers)
                ? [.. baseMembers]
                : [];

        PlanningContext context = new(module, ns, TypeKind.GObjectClass, module.ClrNamespace + "." + typeName);
        TypeSurface surface = _surfaces.Build(
            declaration,
            context,
            CallableForm.InstanceMethod,
            reserved,
            inherited,
            includeProperties: true,
            includeSignals: true);

        List<string> members = [.. inherited];
        members.AddRange(surface.MemberKeys);
        _inherited[declaration.Name] = members;

        List<string> interfaces = [];
        foreach (string implemented in declaration.Implements)
        {
            if (_repository.Resolve(implemented, ns) is { Kind: GirSymbolKind.Interface } implementedSymbol
                && string.Equals(implementedSymbol.Namespace.Name, ns.Name, StringComparison.Ordinal)
                && !_overlays.IsSkipped(implementedSymbol.QualifiedName))
            {
                interfaces.Add(module.ClrNamespace + "." + _names.TypeName(implementedSymbol));
            }
        }

        interfaces.Sort(StringComparer.Ordinal);

        CodeWriter writer = new();
        WriteHeader(writer, module, ns);
        writer.WriteLine();
        XmlDocWriter.Write(writer, declaration.Doc, "The <c>" + CTypeOf(declaration) + "</c> class.");
        XmlDocWriter.WriteObsolete(writer, declaration);

        string modifiers = declaration.IsAbstract ? "public abstract " : "public ";
        writer.WriteLine(
            modifiers + "unsafe partial class " + typeName + " : "
            + string.Join(", ", new[] { baseType.Name }.Concat(interfaces)));
        writer.OpenBlock();

        writer.WriteLine("/// <summary>Wraps a native <c>" + CTypeOf(declaration) + "</c>.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance.</param>");
        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine("internal " + typeName + "(nint handle, Gst.Interop.Transfer transfer)");
        writer.WriteLine("    : base(handle, transfer)");
        writer.OpenBlock();
        writer.CloseBlock();

        WriteMembers(writer, surface, module, first: false, CTypeOf(declaration));

        bool hidesBase = baseType.InModule is not null;
        writer.WriteLine();
        WriteTypeFunction(writer, module, declaration.GlibGetType, CTypeOf(declaration), hidesBase);

        writer.WriteLine();
        WriteFactory(writer, typeName, declaration.IsAbstract, hidesBase);

        if (declaration.IsAbstract)
        {
            writer.WriteLine();
            WriteConcrete(writer, typeName, CTypeOf(declaration));
        }

        writer.CloseBlock();

        if (declaration.GlibGetType is { Length: > 0 })
        {
            _registry.Add(module.ClrNamespace + "." + typeName);
        }

        _census.Emitted(module.GirNamespace, "class");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + typeName + ".cs", writer.ToSource());
    }

    /// <summary>Writes the import of the <c>glib:get-type</c> function of a type.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="getType">The <c>glib:get-type</c> entry point.</param>
    /// <param name="cType">The C type name, for the documentation.</param>
    /// <param name="hidesBase">
    /// Whether the base class carries the same member. Every generated class has
    /// its own type function, so a derived one hides the one it inherits and has
    /// to say so.
    /// </param>
    internal static void WriteTypeFunction(
        CodeWriter writer,
        ModuleInfo module,
        string? getType,
        string cType,
        bool hidesBase)
    {
        writer.WriteLine(
            "/// <summary>Returns the <c>GType</c> that GObject registered <c>" + cType + "</c> under.</summary>");
        writer.WriteLine("/// <returns>The type of the instances of this wrapper.</returns>");
        writer.WriteLine("[LibraryImport(\"" + module.NativeLibrary + "\", EntryPoint = \"" + getType + "\")]");
        writer.WriteLine("internal static " + (hidesBase ? "new " : string.Empty) + "partial nuint GetGType();");
    }

    /// <summary>Writes the factory that the type registry calls.</summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="typeName">The C# name of the type.</param>
    /// <param name="isAbstract">Whether the wrapper itself cannot be instantiated.</param>
    /// <param name="hidesBase">Whether the base class carries the same member.</param>
    internal static void WriteFactory(CodeWriter writer, string typeName, bool isAbstract, bool hidesBase)
    {
        writer.WriteLine("/// <summary>Creates the wrapper of a native instance, for the type registry.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance.</param>");
        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine("/// <returns>The new wrapper.</returns>");
        writer.WriteLine(
            "internal static " + (hidesBase ? "new " : string.Empty)
            + "object CreateWrapper(nint handle, Gst.Interop.Transfer transfer) => new "
            + (isAbstract ? ConcreteName : typeName) + "(handle, transfer);");
    }

    private static void WriteConcrete(CodeWriter writer, string typeName, string cType)
    {
        writer.WriteLine("/// <summary>");
        writer.WriteLine("/// The wrapper of a native type that derives from <c>" + cType + "</c> and has no");
        writer.WriteLine("/// generated binding of its own, for example an element of a plugin.");
        writer.WriteLine("/// </summary>");
        writer.WriteLine("private sealed class " + ConcreteName + " : " + typeName);
        writer.OpenBlock();
        writer.WriteLine("/// <summary>Wraps a native instance.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance.</param>");
        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine("internal " + ConcreteName + "(nint handle, Gst.Interop.Transfer transfer)");
        writer.WriteLine("    : base(handle, transfer)");
        writer.OpenBlock();
        writer.CloseBlock();
        writer.CloseBlock();
    }

    /// <summary>
    /// Orders the classes of a namespace so that a base class comes before every
    /// class that derives from it.
    /// </summary>
    /// <param name="ns">The namespace to order.</param>
    /// <returns>The classes, in emission order.</returns>
    private static IReadOnlyList<GirClass> Ordered(GirNamespace ns)
    {
        Dictionary<string, GirClass> byName = new(StringComparer.Ordinal);
        foreach (GirClass declaration in ns.Classes)
        {
            byName.TryAdd(declaration.Name, declaration);
        }

        List<GirClass> ordered = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        foreach (GirClass declaration in ns.Classes)
        {
            Visit(declaration, 0);
        }

        return ordered;

        void Visit(GirClass declaration, int depth)
        {
            if (depth > 32 || !visited.Add(declaration.Name))
            {
                return;
            }

            if (declaration.Parent is { } parent && byName.TryGetValue(parent, out GirClass? baseClass))
            {
                Visit(baseClass, depth + 1);
            }

            ordered.Add(declaration);
        }
    }

    private BaseType? ResolveBase(GirNamespace ns, GirClass declaration)
    {
        if (declaration.Parent is not { Length: > 0 } parent)
        {
            return null;
        }

        GirSymbol? symbol = _repository.Resolve(parent, ns);
        if (symbol is null)
        {
            return null;
        }

        if (string.Equals(symbol.Namespace.Name, ns.Name, StringComparison.Ordinal))
        {
            return _classifier.Classify(symbol.Declaration) == TypeKind.GObjectClass
                ? new BaseType(
                    ModuleMap.ClrNamespaceOf(ns.Name) + "." + _names.TypeName(symbol),
                    symbol.Name)
                : null;
        }

        return symbol.QualifiedName switch
        {
            "GObject.InitiallyUnowned" => new BaseType("Gst.GObject.InitiallyUnowned", null),
            "GObject.Object" => new BaseType("Gst.GObject.Object", null),
            _ => null,
        };
    }

    /// <summary>The base class of a generated class.</summary>
    /// <param name="Name">The C# type name.</param>
    /// <param name="InModule">The gir name when the base class is generated too.</param>
    private readonly record struct BaseType(string Name, string? InModule);
}
