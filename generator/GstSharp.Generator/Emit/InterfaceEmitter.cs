using GstSharp.Generator.GirParsing.Model;
using GstSharp.Generator.Planning;
using GstSharp.Generator.Semantic;

namespace GstSharp.Generator.Emit;

/// <summary>
/// Emits the <c>&lt;interface&gt;</c> declarations of a gir namespace as a
/// marker interface plus a class of extension methods.
/// </summary>
/// <remarks>
/// <para>
/// The interface itself only exposes the native handle. Its methods are
/// extension methods on that interface rather than default interface methods,
/// for one reason: a default interface method is invisible on the implementing
/// class, so <c>bin.GetChildByName(...)</c> would not compile, while the
/// extension method resolves through the <c>Gst.IChildProxy</c> that
/// <c>Gst.Bin</c> declares.
/// </para>
/// <para>
/// The virtual methods of an interface are not bound: implementing a GObject
/// interface from C# needs subclassing support, which this milestone does not
/// have.
/// </para>
/// <para>
/// A signal of an interface lands in the extension class too, as the
/// <c>AddXHandler</c> and <c>RemoveXHandler</c> pair that
/// <see cref="SignalEmitter.WriteInterfaceSignal"/> writes: an event needs
/// accessors on the instance, which an interface cannot declare for its
/// implementors.
/// </para>
/// </remarks>
internal sealed class InterfaceEmitter
{
    private readonly NameMapper _names;
    private readonly SurfaceBuilder _surfaces;
    private readonly Overlays _overlays;
    private readonly EmissionCensus _census;
    private readonly List<InterfaceRegistryEntry> _registry;

    /// <summary>Initializes a new instance of the <see cref="InterfaceEmitter"/> class.</summary>
    /// <param name="names">The name mapper.</param>
    /// <param name="surfaces">The member builder.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="census">The census of the run.</param>
    /// <param name="registry">The interface table of the module being emitted.</param>
    internal InterfaceEmitter(
        NameMapper names,
        SurfaceBuilder surfaces,
        Overlays overlays,
        EmissionCensus census,
        List<InterfaceRegistryEntry> registry)
    {
        _names = names;
        _surfaces = surfaces;
        _overlays = overlays;
        _census = census;
        _registry = registry;
    }

    /// <summary>Emits every generated interface of one module.</summary>
    /// <param name="module">The module to emit.</param>
    /// <param name="ns">The gir namespace of the module.</param>
    /// <returns>The generated files, ordered by relative path.</returns>
    internal IReadOnlyList<GeneratedFile> Emit(ModuleInfo module, GirNamespace ns)
    {
        List<GeneratedFile> files = [];
        foreach (GirInterface declaration in ns.Interfaces)
        {
            if (Emit(module, ns, declaration) is { } file)
            {
                files.Add(file);
            }
        }

        files.Sort(static (left, right) => string.CompareOrdinal(left.RelativePath, right.RelativePath));
        return files;
    }

    private static string CTypeOf(GirInterface declaration) =>
        declaration.CType is { Length: > 0 } cType ? cType : declaration.Name;

    private GeneratedFile? Emit(ModuleInfo module, GirNamespace ns, GirInterface declaration)
    {
        string qualifiedName = ns.Name + "." + declaration.Name;
        if (!declaration.IsIntrospectable || _overlays.IsSkipped(qualifiedName))
        {
            return null;
        }

        GirSymbol symbol = new(ns, declaration.Name, GirSymbolKind.Interface, declaration);
        string typeName = _names.TypeName(symbol);
        string extensionsName = typeName[1..] + "Extensions";
        PlanningContext context = new(
            module,
            ns,
            TypeKind.Interface,
            module.ClrNamespace + "." + typeName,
            module.ClrNamespace + "." + extensionsName);
        TypeSurface surface = _surfaces.Build(
            declaration,
            context,
            CallableForm.ExtensionMethod,
            [typeName, extensionsName, .. SurfaceBuilder.WrapperNames],
            [],
            includeProperties: false,
            includeSignals: true);

        CodeWriter writer = new();
        ClassEmitter.WriteHeader(writer, module, ns, surface.ParameterArrays.Count > 0);
        writer.WriteLine();
        XmlDocWriter.Write(
            writer,
            declaration.Doc,
            "The <c>" + CTypeOf(declaration) + "</c> interface.",
            declaration);
        XmlDocWriter.WriteObsolete(writer, declaration);
        writer.WriteLine("public interface " + typeName);
        writer.OpenBlock();
        writer.WriteLine("/// <summary>Gets the native instance that implements the interface.</summary>");
        writer.WriteLine("nint Handle { get; }");
        writer.CloseBlock();

        string qualifiedTypeName = module.ClrNamespace + "." + typeName;
        bool hasTypeFunction = declaration.GlibGetType is { Length: > 0 };
        if (!surface.IsEmpty || hasTypeFunction)
        {
            writer.WriteLine();
            writer.WriteLine(
                surface.IsEmpty
                    ? "/// <summary>The type function and adapter of <c>" + CTypeOf(declaration) + "</c>.</summary>"
                    : "/// <summary>The methods of <c>" + CTypeOf(declaration) + "</c>.</summary>");

            // An interface without a single bound method still needs the class
            // to hold its type function and its adapter, and an empty class of
            // internal members adds nothing to the public surface.
            writer.WriteLine(
                (surface.IsEmpty ? "internal" : "public") + " static unsafe partial class " + extensionsName);
            writer.OpenBlock();
            ClassEmitter.WriteMembers(
                writer,
                surface,
                module,
                first: true,
                CTypeOf(declaration),
                qualifiedTypeName);
            if (hasTypeFunction)
            {
                if (!surface.IsEmpty)
                {
                    writer.WriteLine();
                }

                WriteCast(writer, module, declaration, qualifiedTypeName);
                _registry.Add(
                    new InterfaceRegistryEntry(
                        qualifiedTypeName,
                        module.ClrNamespace + "." + extensionsName));
            }

            writer.CloseBlock();
        }

        _census.Emitted(module.GirNamespace, "interface");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + typeName + ".cs", writer.ToSource());
    }

    /// <summary>
    /// Writes what <c>Gst.GObject.Object.As</c> needs of an interface: the
    /// import of its type function, and the adapter that presents a wrapper
    /// whose native instance implements the interface as that interface.
    /// </summary>
    /// <param name="writer">The target writer.</param>
    /// <param name="module">The module being emitted.</param>
    /// <param name="declaration">The gir interface.</param>
    /// <param name="qualifiedTypeName">The C# name of the generated interface.</param>
    private static void WriteCast(
        CodeWriter writer,
        ModuleInfo module,
        GirInterface declaration,
        string qualifiedTypeName)
    {
        ClassEmitter.WriteTypeFunction(
            writer,
            module,
            declaration.GlibGetType,
            CTypeOf(declaration),
            hidesBase: false,
            returns: "The type of the <c>" + CTypeOf(declaration) + "</c> interface.");
        writer.WriteLine();
        writer.WriteLine(
            "/// <summary>Presents a <see cref=\"Gst.GObject.Object\"/> as <see cref=\""
            + qualifiedTypeName + "\"/>, once the runtime has checked the type.</summary>");
        writer.WriteLine("internal sealed class Adapter : " + qualifiedTypeName);
        writer.OpenBlock();
        writer.WriteLine("private readonly Gst.GObject.Object _owner;");
        writer.WriteLine();
        writer.WriteLine("/// <summary>Initialises the view of an object.</summary>");
        writer.WriteLine("/// <param name=\"owner\">The wrapper that the view reads its handle from.</param>");
        writer.WriteLine("internal Adapter(Gst.GObject.Object owner) => _owner = owner;");
        writer.WriteLine();
        writer.WriteLine("/// <inheritdoc/>");
        writer.WriteLine("public nint Handle => _owner.Handle;");
        writer.CloseBlock();
        writer.WriteLine();
        writer.WriteLine("/// <summary>Creates the view of an object, for the type registry.</summary>");
        writer.WriteLine("/// <param name=\"owner\">The wrapper to present as the interface.</param>");
        writer.WriteLine("/// <returns>The new view.</returns>");
        writer.WriteLine("internal static object CreateAdapter(Gst.GObject.Object owner) => new Adapter(owner);");
    }
}
