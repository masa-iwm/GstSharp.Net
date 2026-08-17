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
/// from it. Its <c>(nint, Transfer)</c> constructor is <c>protected</c>, which
/// is what lets a binding module outside this repository attach its wrappers to
/// the generated hierarchy; everything else about the class — the factory, the
/// type function, the class-struct mirrors — stays internal. Instances still
/// never come from application code: only a factory or the type registry knows
/// what ownership the native call transferred.
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

    /// <summary>The suffix of the holder of the functions an enumeration declares.</summary>
    internal const string EnumHolderSuffix = "Extensions";

    private readonly Repository _repository;
    private readonly Classifier _classifier;
    private readonly NameMapper _names;
    private readonly SurfaceBuilder _surfaces;
    private readonly Overlays _overlays;
    private readonly EmissionCensus _census;
    private readonly DiagnosticBag _diagnostics;
    private readonly List<RegistryEntry> _registry;
    private readonly Dictionary<string, List<string>> _inherited;

    /// <summary>Initializes a new instance of the <see cref="ClassEmitter"/> class.</summary>
    /// <param name="repository">The loaded gir repository.</param>
    /// <param name="classifier">The type classifier.</param>
    /// <param name="names">The name mapper.</param>
    /// <param name="surfaces">The member builder.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="diagnostics">The diagnostic sink.</param>
    /// <param name="registry">Receives the types that the module registers.</param>
    /// <param name="inherited">
    /// The members of every class the run has emitted so far, keyed by
    /// qualified gir name. The table is shared by every module, because a class
    /// of one module derives from a class of another one.
    /// </param>
    internal ClassEmitter(
        Repository repository,
        Classifier classifier,
        NameMapper names,
        SurfaceBuilder surfaces,
        Overlays overlays,
        EmissionCensus census,
        DiagnosticBag diagnostics,
        List<RegistryEntry> registry,
        Dictionary<string, List<string>> inherited)
    {
        _repository = repository;
        _classifier = classifier;
        _names = names;
        _surfaces = surfaces;
        _overlays = overlays;
        _census = census;
        _diagnostics = diagnostics;
        _registry = registry;
        _inherited = inherited;
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

    /// <summary>
    /// Emits the functions that a gir declares inside an enumeration, for
    /// example <c>gst_state_get_name</c> inside <c>&lt;enumeration
    /// name="State"&gt;</c>.
    /// </summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated files, ordered by relative path.</returns>
    /// <remarks>
    /// An enumeration is a C# <c>enum</c> and carries no members of its own, so
    /// the functions become static methods of a holder named after it. They are
    /// plain static methods rather than extension methods on the enumeration:
    /// several of them take no value of the enumeration at all
    /// (<c>gst_format_get_by_nick</c> returns one), so a <c>this</c> receiver
    /// would only fit some of them.
    /// </remarks>
    internal IReadOnlyList<GeneratedFile> EmitEnumFunctions(ModuleInfo module, GirNamespace ns)
    {
        List<GeneratedFile> files = [];
        foreach (GirEnumeration enumeration in ns.AllEnumerations)
        {
            if (EmitEnumFunctions(module, ns, enumeration) is { } file)
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
        string globalName = module.GlobalTypeName;
        PlanningContext context = new(module, ns, TypeKind.Unknown, OwnerType: null);
        GirRecord holder = new() { Name = globalName, Functions = ns.Functions };
        TypeSurface surface = _surfaces.Build(
            holder,
            context,
            CallableForm.StaticMethod,
            [globalName],
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
        writer.WriteLine("public static unsafe partial class " + globalName);
        writer.OpenBlock();
        WriteMembers(writer, surface, module, first: true);
        writer.CloseBlock();

        _census.Emitted(module.GirNamespace, "class");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + globalName + ".cs", writer.ToSource());
    }

    private GeneratedFile? EmitEnumFunctions(ModuleInfo module, GirNamespace ns, GirEnumeration enumeration)
    {
        if (enumeration.Functions.Count == 0
            || !enumeration.IsIntrospectable
            || _overlays.IsSkipped(ns.Name + "." + enumeration.Name))
        {
            return null;
        }

        GirSymbol symbol = new(ns, enumeration.Name, GirSymbolKind.Enumeration, enumeration);
        string typeName = _names.TypeName(symbol);
        string holderName = typeName + EnumHolderSuffix;

        // The functions belong to no instance, so the holder is only a
        // namespace for them: nothing of the enumeration is in scope.
        PlanningContext context = new(module, ns, TypeKind.Unknown, OwnerType: null);
        GirRecord holder = new() { Name = holderName, Functions = enumeration.Functions };
        TypeSurface surface = _surfaces.Build(
            holder,
            context,
            CallableForm.StaticMethod,
            [holderName],
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
            "/// <summary>The functions the gir declares inside <c>" + CTypeOf(enumeration) + "</c>.</summary>");
        writer.WriteLine("public static unsafe partial class " + holderName);
        writer.OpenBlock();
        WriteMembers(writer, surface, module, first: true);
        writer.CloseBlock();

        _census.Emitted(module.GirNamespace, "enum holder");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + holderName + ".cs", writer.ToSource());
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
    /// <param name="interfaceType">
    /// The C# interface the members extend, when they are emitted into the
    /// extension class of a gir interface. Its signals become a pair of
    /// extension methods instead of an event.
    /// </param>
    internal static void WriteMembers(
        CodeWriter writer,
        TypeSurface surface,
        ModuleInfo module,
        bool first,
        string cType = "",
        string? interfaceType = null)
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
            if (interfaceType is null)
            {
                SignalEmitter.WriteSignal(writer, signal, module, cType);
            }
            else
            {
                SignalEmitter.WriteInterfaceSignal(writer, signal, module, cType, interfaceType);
            }
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
            "The <c>" + property.Property.Name + "</c> property.",
            Arrival(property));
        XmlDocWriter.WriteObsolete(writer, Deprecation(property));

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

    /// <summary>
    /// Returns the gir element whose deprecation the property carries.
    /// </summary>
    /// <remarks>
    /// A property is nothing but a call of its accessors, so a deprecated
    /// accessor makes the property deprecated as well: without the attribute the
    /// body would use an obsolete member and the generated file would not
    /// compile under warnings as errors. Only the first deprecation found is
    /// used, because a second <c>[Obsolete]</c> attribute is an error of its
    /// own, and the gir property comes first when it carries one.
    /// </remarks>
    /// <param name="property">The property being written.</param>
    /// <returns>The element to take the <c>[Obsolete]</c> attribute from.</returns>
    private static GirNode Deprecation(PropertyEmission property)
    {
        if (property.Property.IsDeprecated)
        {
            return property.Property;
        }

        if (property.Getter.Callable.IsDeprecated)
        {
            return property.Getter.Callable;
        }

        if (property.Setter is { Callable.IsDeprecated: true })
        {
            return property.Setter.Callable;
        }

        return property.Property;
    }

    /// <summary>
    /// Returns the gir element whose <c>version</c> attribute says which
    /// GStreamer the property needs.
    /// </summary>
    /// <remarks>
    /// The newest of the three, because a property is nothing but a call of its
    /// accessors: the member is there once the gir property, the getter and the
    /// setter are all there, and the newest of them is when that happens. A
    /// read only property has no setter to wait for.
    /// </remarks>
    /// <param name="property">The property being written.</param>
    /// <returns>The element to take the version from.</returns>
    private static GirNode Arrival(PropertyEmission property)
    {
        GirNode newest = property.Property;

        if (Availability.IsNewer(property.Getter.Callable.Version, newest.Version))
        {
            newest = property.Getter.Callable;
        }

        if (property.Setter is { } setter && Availability.IsNewer(setter.Callable.Version, newest.Version))
        {
            newest = setter.Callable;
        }

        return newest;
    }

    private static string CTypeOf(GirClass declaration) =>
        declaration.CType is { Length: > 0 } cType ? cType : declaration.Name;

    private static string CTypeOf(GirEnumeration declaration) =>
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
        _inherited[qualifiedName] = members;

        List<string> interfaces = [];
        foreach (string implemented in declaration.Implements)
        {
            // An interface of another generated module counts too:
            // GstAppSink implements GstURIHandler, which lives in Gst.
            if (_repository.Resolve(implemented, ns) is
                    { Kind: GirSymbolKind.Interface, Declaration: GirInterface { IsIntrospectable: true } } implementedSymbol
                && ModuleMap.Find(implementedSymbol.Namespace.Name) is { IsGenerated: true } implementedModule
                && !_overlays.IsSkipped(implementedSymbol.QualifiedName))
            {
                interfaces.Add(implementedModule.ClrNamespace + "." + _names.TypeName(implementedSymbol));
            }
        }

        interfaces.Sort(StringComparer.Ordinal);

        CodeWriter writer = new();
        WriteHeader(writer, module, ns);
        writer.WriteLine();
        XmlDocWriter.Write(writer, declaration.Doc, "The <c>" + CTypeOf(declaration) + "</c> class.", declaration);
        XmlDocWriter.WriteObsolete(writer, declaration);

        string modifiers = declaration.IsAbstract ? "public abstract " : "public ";
        writer.WriteLine(
            modifiers + "unsafe partial class " + typeName + " : "
            + string.Join(", ", new[] { baseType.Name }.Concat(interfaces)));
        writer.OpenBlock();

        WriteWrapperConstructor(writer, typeName, CTypeOf(declaration));

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
            _registry.Add(new RegistryEntry(module.ClrNamespace + "." + typeName, declaration.IsDeprecated));
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

    /// <summary>
    /// Writes the constructor that attaches a wrapper of a derived native type
    /// to this one.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="typeName">The C# name of the type.</param>
    /// <param name="cType">The C name of the type.</param>
    /// <remarks>
    /// <para>
    /// It is <c>protected</c> rather than <c>internal</c>, and that is
    /// deliberate public surface: it is where a binding module written against
    /// the package attaches its own wrappers, so that
    /// <c>Gst.Controller.TimedValueControlSource</c> really is a
    /// <c>Gst.ControlSource</c> and the generated members that take one accept
    /// it. Nothing else about a generated class is open — the factory, the type
    /// function and the class-struct mirrors stay internal — so the constructor
    /// carries the whole of the contract, which is why the documentation on it
    /// repeats what the runtime bases say and points at the module guide.
    /// </para>
    /// <para>
    /// Every generated class is non-sealed, so <c>protected</c> is always
    /// legal here. The records — mini objects, boxed values and opaque records
    /// — are sealed and keep their <c>internal</c> constructors.
    /// </para>
    /// </remarks>
    private static void WriteWrapperConstructor(CodeWriter writer, string typeName, string cType)
    {
        writer.WriteLine("/// <summary>Wraps a native <c>" + cType + "</c>.</summary>");
        writer.WriteLine("/// <param name=\"handle\">The native instance.</param>");
        writer.WriteLine(
            "/// <param name=\"transfer\">How ownership of <paramref name=\"handle\"/> is transferred.</param>");
        writer.WriteLine("/// <remarks>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// This is where a binding module attaches its own wrappers: derive from");
        writer.WriteLine("/// this class to wrap a native type that derives from");
        writer.WriteLine("/// <c>" + cType + "</c> and has no binding of its own here. See");
        writer.WriteLine(
            "/// <see href=\"https://github.com/masa-iwm/GstSharp.Net/blob/main/docs/modules.md\">docs/modules.md</see>.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// <b>Call it from a type-registry factory, never from application code.</b>");
        writer.WriteLine("/// GObject wrappers are interned, and wrapping a handle that a live wrapper");
        writer.WriteLine("/// already holds throws. Expose a <c>CreateWrapper</c> to");
        writer.WriteLine("/// <see cref=\"Gst.Interop.ModuleTypeEntry\"/>, keep the constructor out of");
        writer.WriteLine("/// your public surface, and write your own factories through");
        writer.WriteLine("/// <see cref=\"Gst.GObject.Object.FromNative{T}(nint, Gst.Interop.Transfer)\"/>.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// <para>");
        writer.WriteLine("/// <b>Pass the transfer the C function documented.</b> The wrapper owns one");
        writer.WriteLine("/// reference either way, so getting it wrong leaks the object or releases a");
        writer.WriteLine("/// reference that was never handed over.");
        writer.WriteLine("/// </para>");
        writer.WriteLine("/// </remarks>");
        writer.WriteLine("protected " + typeName + "(nint handle, Gst.Interop.Transfer transfer)");
        writer.WriteLine("    : base(handle, transfer)");
        writer.OpenBlock();
        writer.CloseBlock();
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

        // The two roots of the hierarchy are hand written in the runtime
        // library and carry no generated members.
        switch (symbol.QualifiedName)
        {
            case "GObject.InitiallyUnowned":
                return new BaseType("Gst.GObject.InitiallyUnowned", null);

            case "GObject.Object":
                return new BaseType("Gst.GObject.Object", null);
        }

        // Everything else has to be a class this run already emitted, whether
        // it belongs to this module or to one that was generated before it:
        // GstAppSink derives from GstBaseSink, which lives in GstBase.
        if (ModuleMap.Find(symbol.Namespace.Name) is not { IsGenerated: true }
            || _classifier.Classify(symbol.Declaration) != TypeKind.GObjectClass
            || !_inherited.ContainsKey(symbol.QualifiedName))
        {
            return null;
        }

        return new BaseType(
            ModuleMap.ClrNamespaceOf(symbol.Namespace.Name) + "." + _names.TypeName(symbol),
            symbol.QualifiedName);
    }

    /// <summary>The base class of a generated class.</summary>
    /// <param name="Name">The C# type name.</param>
    /// <param name="InModule">The qualified gir name when the base class is generated too.</param>
    private readonly record struct BaseType(string Name, string? InModule);
}
