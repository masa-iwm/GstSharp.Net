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

    /// <summary>Initializes a new instance of the <see cref="InterfaceEmitter"/> class.</summary>
    /// <param name="names">The name mapper.</param>
    /// <param name="surfaces">The member builder.</param>
    /// <param name="overlays">The overlay configuration.</param>
    /// <param name="census">The census of the run.</param>
    internal InterfaceEmitter(NameMapper names, SurfaceBuilder surfaces, Overlays overlays, EmissionCensus census)
    {
        _names = names;
        _surfaces = surfaces;
        _overlays = overlays;
        _census = census;
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

        if (!surface.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine(
                "/// <summary>The methods of <c>" + CTypeOf(declaration) + "</c>.</summary>");
            writer.WriteLine("public static unsafe partial class " + extensionsName);
            writer.OpenBlock();
            ClassEmitter.WriteMembers(
                writer,
                surface,
                module,
                first: true,
                CTypeOf(declaration),
                module.ClrNamespace + "." + typeName);
            writer.CloseBlock();
        }

        _census.Emitted(module.GirNamespace, "interface");
        return new GeneratedFile(module.ProjectDirectory + "/Generated/" + typeName + ".cs", writer.ToSource());
    }
}
