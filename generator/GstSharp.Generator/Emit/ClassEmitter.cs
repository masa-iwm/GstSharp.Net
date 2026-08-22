using System.Globalization;
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

        // The holder declares no instance, but it is a type all the same, so
        // it is where the inline storage of a caller allocated array goes.
        PlanningContext context = new(
            module,
            ns,
            TypeKind.Unknown,
            OwnerType: null,
            StorageOwner: module.ClrNamespace + "." + globalName);
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
        WriteHeader(writer, module, ns, surface.ParameterArrays.Count > 0);
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
        // namespace for them: nothing of the enumeration is in scope. It still
        // carries the inline storage of a caller allocated array.
        PlanningContext context = new(
            module,
            ns,
            TypeKind.Unknown,
            OwnerType: null,
            StorageOwner: module.ClrNamespace + "." + holderName);
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
        WriteHeader(writer, module, ns, surface.ParameterArrays.Count > 0);
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
    /// <param name="inlineArrays">
    /// Whether the file declares the inline storage of a caller allocated array,
    /// which needs the <c>InlineArray</c> attribute in scope.
    /// </param>
    internal static void WriteHeader(
        CodeWriter writer,
        ModuleInfo module,
        GirNamespace ns,
        bool inlineArrays = false)
    {
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("// Generated by GstSharp.Generator from " + ns.Name + "-" + ns.Version + ".gir. Do not edit.");
        writer.WriteLine();
        writer.WriteLine("#nullable enable");
        writer.WriteLine();
        writer.WriteLine("using System;");
        if (inlineArrays)
        {
            writer.WriteLine("using System.Runtime.CompilerServices;");
        }

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

        // The storage of a caller allocated array closes the public surface: it
        // is a type a caller declares a variable of, so it belongs with the
        // members that take it and before the private entry points.
        foreach (InlineArrayInfo array in surface.ParameterArrays)
        {
            if (!leading)
            {
                writer.WriteLine();
            }

            leading = false;
            RecordEmitter.WriteInlineArray(
                writer,
                "public",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Inline storage of the {array.Length} elements a call writes into the parameter this type is named after."),
                array);
        }

        HashSet<string> imported = new(StringComparer.Ordinal);
        foreach (MarshalPlan member in surface.Members)
        {
            imported.Add(member.EntryPoint);
            writer.WriteLine();
            CallableRenderer.WriteImport(writer, member, module.NativeLibrary);
        }

        // The constructor a caller allocated out parameter takes its storage
        // from is an entry point of the record's own library rather than of the
        // module the member lives in, so it is imported here beside the member
        // that calls it. One import per factory and per file: several members
        // of one type ask for the same storage, and a type that already binds
        // the constructor as a member of its own imported it above.
        foreach (BoxedStorageFactory factory in StorageFactories(surface))
        {
            if (!imported.Add(factory.EntryPoint))
            {
                continue;
            }

            writer.WriteLine();
            CallableRenderer.WriteStorageFactoryImport(writer, factory);
        }
    }

    /// <summary>
    /// Returns the storage constructors the members of a type call, ordered by
    /// entry point so that the output does not depend on the order the members
    /// happen to be planned in.
    /// </summary>
    /// <param name="surface">The type being emitted.</param>
    /// <returns>The distinct factories.</returns>
    private static IEnumerable<BoxedStorageFactory> StorageFactories(TypeSurface surface)
    {
        SortedDictionary<string, BoxedStorageFactory> factories = new(StringComparer.Ordinal);
        foreach (MarshalPlan member in surface.Members)
        {
            foreach (ArgumentPlan argument in member.Arguments)
            {
                if (argument.StorageFactory is { } factory)
                {
                    factories[factory.EntryPoint] = factory;
                }
            }
        }

        return factories.Values;
    }

    private static void WriteProperty(CodeWriter writer, PropertyEmission property)
    {
        if (property.ValueBacked is { } backed)
        {
            WriteValueProperty(writer, property, backed);
            return;
        }

        XmlDocWriter.Write(
            writer,
            property.Property.Doc,
            "The <c>" + property.Property.Name + "</c> property.",
            Arrival(property));
        XmlDocWriter.WriteObsolete(writer, Deprecation(property));

        string modifiers = "public " + (property.IsNew ? "new " : string.Empty);
        MarshalPlan getter = property.Getter
            ?? throw new InvalidOperationException("A property without a value backing needs a getter.");
        if (property.Setter is null)
        {
            writer.WriteLine(modifiers + property.Type + " " + property.Name + " => " + getter.Name + "();");
            return;
        }

        writer.WriteLine(modifiers + property.Type + " " + property.Name);
        writer.OpenBlock();
        writer.WriteLine("get => " + getter.Name + "();");
        writer.WriteLine("set => " + property.Setter.Name + "(value);");
        writer.CloseBlock();
    }

    /// <summary>
    /// Writes a property that the GObject property system backs, because the
    /// gir names no C accessor for it.
    /// </summary>
    /// <remarks>
    /// The local is called <c>holder</c> rather than <c>value</c>, which is the
    /// implicit parameter of the setter, and it is disposed by the <c>using</c>
    /// the way every <c>GValue</c> of the runtime is. The body carries no
    /// <c>GC.KeepAlive</c>: the three runtime helpers it calls each end with one
    /// over the instance, which is the last use of this wrapper in either
    /// accessor.
    /// </remarks>
    /// <param name="writer">The target writer.</param>
    /// <param name="property">The property being written.</param>
    /// <param name="backed">How its value crosses the <c>GValue</c>.</param>
    private static void WriteValueProperty(
        CodeWriter writer,
        PropertyEmission property,
        ValueBackedProperty backed)
    {
        XmlDocWriter.Write(
            writer,
            property.Property.Doc,
            "The <c>" + property.Property.Name + "</c> property.",
            Arrival(property),
            ValueRemarks(property, backed));
        writer.WriteLine(
            "/// <exception cref=\"System.ObjectDisposedException\">The wrapper was disposed.</exception>");
        writer.WriteLine("/// <exception cref=\"System.ArgumentException\">");
        if (backed.WritesValue)
        {
            // The setter goes through the property system as well, and that is
            // where a property the installed library declares read-only is
            // refused: the same exception, for the other reason.
            writer.WriteLine("/// The installed GStreamer declares no such property on this class, or");
            writer.WriteLine("/// declares it read-only.");
        }
        else
        {
            writer.WriteLine("/// The installed GStreamer declares no such property on this class.");
        }

        writer.WriteLine("/// </exception>");
        XmlDocWriter.WriteObsolete(writer, Deprecation(property));

        string name = "\"" + property.Property.Name + "\"";
        writer.WriteLine("public " + property.Type + " " + property.Name);
        writer.OpenBlock();
        writer.WriteLine("get");
        writer.OpenBlock();
        writer.WriteLine("using Gst.GObject.Value holder = GetProperty(" + name + ");");
        writer.WriteLine("return " + backed.Access.Read + ";");
        writer.CloseBlock();

        if (property.Setter is { } setter)
        {
            writer.WriteLine();
            writer.WriteLine("set => " + setter.Name + "(value);");
        }
        else if (backed.WritesValue)
        {
            writer.WriteLine();
            writer.WriteLine("set");
            writer.OpenBlock();
            writer.WriteLine("using Gst.GObject.Value holder = NewPropertyValue(" + name + ");");
            writer.WriteLine(backed.Access.Write);
            writer.WriteLine("SetPropertyValue(" + name + ", in holder);");
            writer.CloseBlock();
        }

        writer.CloseBlock();
    }

    /// <summary>
    /// The generator authored remarks of a value backed property: where its
    /// value comes from, who owns what crosses it, and whether it can be
    /// written at all.
    /// </summary>
    /// <param name="property">The property being written.</param>
    /// <param name="backed">How its value crosses the <c>GValue</c>.</param>
    /// <returns>The lines to append to the remarks.</returns>
    private static IReadOnlyList<string> ValueRemarks(PropertyEmission property, ValueBackedProperty backed)
    {
        // A property whose gir names a C setter writes through that call and
        // reads through the property system, so only half of it is value
        // backed and the note has to say which half.
        List<string> note = property.Setter is { } setter
            ?
            [
                "<para>",
                "This property has no C getter; it is read through the GObject property",
                "system (<c>g_object_get_property</c>) and written through",
                "<see cref=\"" + setter.Name + "\"/>.",
                "</para>",
            ]
            :
            [
                "<para>",
                "This property has no C accessor; it is read and written through the GObject",
                "property system (<c>g_object_get_property</c> / <c>g_object_set_property</c>).",
                "</para>",
            ];

        // Construct-only comes before the ownership note: it is a statement
        // about the member itself, and the ownership note is a statement about
        // the values that cross it.
        if (backed.IsConstructOnly)
        {
            note.Add("<para>The property is construct-only and therefore read-only here.</para>");
        }

        bool writes = backed.WritesValue;
        switch (backed.Access.Ownership)
        {
            case ValueOwnership.GObject:
                note.Add("<para>");
                note.Add("Reading hands back the interned wrapper of the object, which the binding");
                note.Add("keeps; it is not the reader's to dispose.");
                note.Add("</para>");
                if (writes)
                {
                    note.Add("<para>");
                    note.Add("Writing takes a reference of its own, so the argument stays the caller's");
                    note.Add("to dispose, and <see langword=\"null\"/> clears the property.");
                    note.Add("</para>");
                }

                break;

            case ValueOwnership.Boxed:
                note.Add("<para>");
                note.Add("Reading builds a wrapper that owns a copy of the value: dispose it when");
                note.Add("you are done with it.");
                note.Add("</para>");
                if (writes)
                {
                    note.Add("<para>");
                    note.Add("Writing copies the argument, which stays the caller's to dispose, and");
                    note.Add("<see langword=\"null\"/> clears the property.");
                    note.Add("</para>");
                }

                break;

            case ValueOwnership.MiniObject:
                note.Add("<para>");
                note.Add("Reading builds a wrapper that owns a reference of its own: dispose it");
                note.Add("when you are done with it.");
                note.Add("</para>");
                if (writes)
                {
                    note.Add("<para>");
                    note.Add("Writing takes a reference of its own, so the argument stays the caller's");
                    note.Add("to dispose, and <see langword=\"null\"/> clears the property.");
                    note.Add("</para>");
                }

                break;

            default:
                break;
        }

        return note;
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
    /// own, and the gir property comes first when it carries one. A value
    /// backed property has no getter to inspect and often no setter either, so
    /// what is left is the deprecation of the gir property itself.
    /// </remarks>
    /// <param name="property">The property being written.</param>
    /// <returns>The element to take the <c>[Obsolete]</c> attribute from.</returns>
    private static GirNode Deprecation(PropertyEmission property)
    {
        if (property.Property.IsDeprecated)
        {
            return property.Property;
        }

        if (property.Getter is { Callable.IsDeprecated: true })
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
    /// read only property has no setter to wait for, and a value backed one has
    /// no getter either: what it waits for is the property, which is the
    /// specification the accessors of the GObject property system look up.
    /// </remarks>
    /// <param name="property">The property being written.</param>
    /// <returns>The element to take the version from.</returns>
    private static GirNode Arrival(PropertyEmission property)
    {
        GirNode newest = property.Property;

        if (property.Getter is { } getter && Availability.IsNewer(getter.Callable.Version, newest.Version))
        {
            newest = getter.Callable;
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

    /// <summary>
    /// The <c>GST_TYPE_*</c> macro that names a fundamental type in C, keyed by
    /// its qualified gir name.
    /// </summary>
    /// <remarks>
    /// The gir spells the C type of the fundamental (<c>GstValueList</c>) and
    /// the entry point of its <c>GType</c>, but not the macro every C caller
    /// and every serialized caps writes, and there is no rule that derives
    /// <c>GST_TYPE_LIST</c> from <c>GstValueList</c>. Naming it in the
    /// documentation is what connects the holder to the caps a reader is
    /// holding, so the handful of macros are listed here.
    /// </remarks>
    private static readonly Dictionary<string, string> FundamentalMacros = new(StringComparer.Ordinal)
    {
        ["Gst.ValueArray"] = "GST_TYPE_ARRAY",
        ["Gst.ValueList"] = "GST_TYPE_LIST",
        ["Gst.ValueUniqueList"] = "GST_TYPE_UNIQUE_LIST",
    };

    /// <summary>
    /// Emits the functions a <c>glib:fundamental="1"</c> class declares, as a
    /// static holder named after it.
    /// </summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="declaration">The fundamental class.</param>
    /// <returns>The generated file, or <see langword="null"/> when there is nothing to emit.</returns>
    /// <remarks>
    /// <para>
    /// A fundamental <c>GType</c> is not a class: it has no instance structure,
    /// no <c>GObject</c> above it and nothing to derive a wrapper from, which
    /// is why the classifier stops at it. Its functions are still ordinary
    /// entry points, and every one of them operates on a <c>GValue</c> that
    /// holds a value of the type, so they bind exactly like the functions a gir
    /// declares inside an enumeration: a static holder that is only a namespace
    /// for them.
    /// </para>
    /// <para>
    /// Without this the members were dropped with the type, before the census
    /// ever saw them, so a caller had no binding and <c>skip-report.md</c> had
    /// no line either. That blind spot is what the holder closes: what is not
    /// bound is now skipped for a reason that is written down.
    /// </para>
    /// </remarks>
    private GeneratedFile? EmitFundamental(ModuleInfo module, GirNamespace ns, GirClass declaration)
    {
        if (declaration.Functions.Count == 0)
        {
            return null;
        }

        GirSymbol symbol = new(ns, declaration.Name, GirSymbolKind.Class, declaration);
        string typeName = _names.TypeName(symbol);

        // Nothing of the fundamental is in scope: the holder only names the
        // functions, exactly as the holder of an enumeration does.
        PlanningContext context = new(module, ns, TypeKind.Unknown, OwnerType: null);
        GirRecord holder = new() { Name = typeName, Functions = declaration.Functions };
        TypeSurface surface = _surfaces.Build(
            holder,
            context,
            CallableForm.StaticMethod,
            [typeName],
            [],
            includeProperties: false);

        if (surface.IsEmpty)
        {
            return null;
        }

        CodeWriter writer = new();
        WriteHeader(writer, module, ns, surface.ParameterArrays.Count > 0);
        writer.WriteLine();
        XmlDocWriter.Write(
            writer,
            declaration.Doc,
            "The functions the gir declares inside <c>" + CTypeOf(declaration) + "</c>.",
            declaration,
            FundamentalRemarks(ns, declaration));
        XmlDocWriter.WriteObsolete(writer, declaration);
        writer.WriteLine("public static unsafe partial class " + typeName);
        writer.OpenBlock();
        WriteMembers(writer, surface, module, first: true);
        writer.CloseBlock();

        _census.Emitted(module.GirNamespace, "value container");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + typeName + ".cs", writer.ToSource());
    }

    /// <summary>
    /// Returns the remarks of a fundamental holder: where the container of that
    /// type lives, because there is no wrapper of it to hold.
    /// </summary>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <param name="declaration">The fundamental class.</param>
    /// <returns>The remarks lines.</returns>
    private static IReadOnlyList<string> FundamentalRemarks(GirNamespace ns, GirClass declaration)
    {
        string qualifiedName = ns.Name + "." + declaration.Name;
        string type = FundamentalMacros.TryGetValue(qualifiedName, out string? macro)
            ? "<c>" + macro + "</c>"
            : "<c>" + CTypeOf(declaration) + "</c>";

        List<string> lines =
        [
            "<para>",
            "Functions of the fundamental type " + type + ".",
            "A value of a fundamental type has no wrapper of its own: it lives inside",
            "a <see cref=\"Gst.GObject.Value\"/>, which is what every function here takes.",
            "Such a value comes from an <c>Init</c> call on a zeroed value, from a call",
            "that fills one, or from a field of a caps or a structure, and is disposed",
            "like any other value.",
            "</para>",
        ];

        if (string.Equals(qualifiedName, "Gst.ValueArray", StringComparison.Ordinal))
        {
            lines.Add("<para>");
            lines.Add("Not to be confused with <see cref=\"Gst.GObject.ValueArray\"/>, which is the");
            lines.Add("boxed <c>GValueArray</c> of GLib and a wrapper of its own.");
            lines.Add("</para>");
        }

        return lines;
    }

    private GeneratedFile? Emit(ModuleInfo module, GirNamespace ns, GirClass declaration)
    {
        string qualifiedName = ns.Name + "." + declaration.Name;
        if (!declaration.IsIntrospectable || _overlays.IsSkipped(qualifiedName))
        {
            return null;
        }

        TypeKind kind = _classifier.Classify(declaration);
        if (kind == TypeKind.Fundamental)
        {
            return EmitFundamental(module, ns, declaration);
        }

        if (kind != TypeKind.GObjectClass)
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
        WriteHeader(writer, module, ns, surface.ParameterArrays.Count > 0);
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
