using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the interface emitter adds for <c>Gst.GObject.Object.As</c>: the type
/// function of every generated interface, the adapter that presents an object
/// as it, and the interface table of the module.
/// </summary>
public sealed class InterfaceEmitterTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Fact]
    public void AnInterfaceCarriesItsTypeFunctionAndItsAdapter()
    {
        string source = Source("GstSharp.Net/Generated/IChildProxy.cs");

        Assert.Contains(
            "[LibraryImport(\"Gst\", EntryPoint = \"gst_child_proxy_get_type\")]\n"
            + "    internal static partial nuint GetGType();\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("    internal sealed class Adapter : Gst.IChildProxy\n", source, StringComparison.Ordinal);
        Assert.Contains("        public nint Handle => _owner.Handle;\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "    internal static object CreateAdapter(Gst.GObject.Object owner) => new Adapter(owner);\n",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnInterfaceWithoutBoundMethodsStillGetsItsAdapter()
    {
        // The extension class of such an interface only holds internal members,
        // so it is internal itself and adds nothing to the public surface.
        string source = Source("GstSharp.Net.Video/Generated/IVideoDirection.cs");

        Assert.Contains(
            "internal static unsafe partial class VideoDirectionExtensions\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "    internal sealed class Adapter : Gst.Video.IVideoDirection\n",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheModuleRegistersItsInterfaces()
    {
        Assert.Contains(
            "    internal static Gst.Interop.ModuleInterfaceEntry[] CreateInterfaceEntries() =>\n",
            Source("GstSharp.Net/Generated/_Module.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "        new Gst.Interop.ModuleInterfaceEntry(typeof(Gst.Audio.IStreamVolume), "
            + "&Gst.Audio.StreamVolumeExtensions.GetGType, &Gst.Audio.StreamVolumeExtensions.CreateAdapter),\n",
            Source("GstSharp.Net.Audio/Generated/_Module.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AModuleWithoutInterfacesRegistersAnEmptyTable()
    {
        Assert.Contains(
            "    internal static Gst.Interop.ModuleInterfaceEntry[] CreateInterfaceEntries() =>\n    [\n    ];\n",
            Source("GstSharp.Net.App/Generated/_Module.cs"),
            StringComparison.Ordinal);
    }

    private static string Source(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no " + path + ".");
    }
}
