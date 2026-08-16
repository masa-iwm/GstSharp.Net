using GstSharp.Generator.Emit;
using GstSharp.Generator.Semantic;
using Xunit;

namespace GstSharp.Generator.Tests;

/// <summary>
/// What the emitters make of the real <c>Gst</c> gir: the shape of the
/// hierarchy, a few members that have to be there, and the frozen counts of
/// what is emitted and what is left out.
/// </summary>
public sealed class ClassEmitterTests
{
    private static readonly Lazy<GenerationResult> LazyGenerated = new(
        static () => GenerationPipeline.Run(GirFixture.GirDirectory),
        isThreadSafe: true);

    private static GenerationResult Generated => LazyGenerated.Value;

    [Theory]
    [InlineData("Object.cs", "public abstract unsafe partial class Object : Gst.GObject.InitiallyUnowned")]
    [InlineData("Element.cs", "public abstract unsafe partial class Element : Gst.Object")]
    [InlineData("Bin.cs", "public unsafe partial class Bin : Gst.Element, Gst.IChildProxy")]
    [InlineData("Pipeline.cs", "public unsafe partial class Pipeline : Gst.Bin, Gst.IChildProxy")]
    [InlineData("SystemClock.cs", "public unsafe partial class SystemClock : Gst.Clock")]
    public void TheHierarchyFollowsTheGir(string fileName, string declaration)
    {
        Assert.Contains(declaration + "\n", Source(fileName), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Element.cs", "public Gst.StateChangeReturn SetState(Gst.State state)")]
    [InlineData("Element.cs", "public Gst.StateChangeReturn GetState(out Gst.State state, out Gst.State pending, Gst.ClockTime timeout)")]
    [InlineData("Object.cs", "public string? GetName()")]
    [InlineData("Bin.cs", "public bool Add(Gst.Element element)")]
    [InlineData("Buffer.cs", "public static Gst.Buffer New()")]
    [InlineData("ElementFactory.cs", "public static Gst.Element? Make(string factoryname, string? name)")]
    [InlineData("Caps.cs", "public static Gst.Caps NewEmpty()")]
    [InlineData("Pad.cs", "public Gst.PadLinkReturn Link(Gst.Pad sinkpad)")]
    [InlineData("DeviceProvider.cs", "public System.Collections.Generic.IReadOnlyList<Gst.Device> GetDevices()")]
    [InlineData("TypeFindFactory.cs", "public static System.Collections.Generic.IReadOnlyList<Gst.TypeFindFactory> GetList()")]
    [InlineData("Uri.cs", "public System.Collections.Generic.IReadOnlyList<string> GetQueryKeys()")]
    public void TheExpectedMembersAreEmitted(string fileName, string signature)
    {
        Assert.Contains("    " + signature + "\n", Source(fileName), StringComparison.Ordinal);
    }

    [Fact]
    public void GstObjectExposesItsNameAsAProperty()
    {
        // gst_object_set_name returns a gboolean, so the property is read only
        // and the setter stays a method of its own.
        string source = Source("Object.cs");

        Assert.Contains("public string? Name => GetName();", source, StringComparison.Ordinal);
        Assert.Contains("public bool SetName(string? name)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseLaunchThrowsTheErrorItIsGiven()
    {
        string source = Source("Global.cs");

        Assert.Contains("public static unsafe partial class Global\n", source, StringComparison.Ordinal);
        Assert.Contains("public static Gst.Element ParseLaunch(string pipelineDescription)", source, StringComparison.Ordinal);
        Assert.Contains("Gst.GLib.GException.ThrowIfSet(ref errorNative);", source, StringComparison.Ordinal);
        Assert.Contains(
            "[LibraryImport(\"Gst\", EntryPoint = \"gst_parse_launch\")]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFactoryThatABaseClassAlsoCarriesHidesItOnPurpose()
    {
        // gst_pipeline_new and gst_bin_new have the same signature, so the
        // derived one has to say that it hides the inherited one. Both are
        // narrowed onto the type that declares them by fixups.json.
        Assert.Contains(
            "public static new Gst.Pipeline New(string? name)",
            Source("Pipeline.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "public static Gst.Bin New(string? name)",
            Source("Bin.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInterfaceIsEmittedWithItsExtensions()
    {
        foreach (string name in new[] { "IChildProxy", "IPreset", "ITagSetter", "ITocSetter", "IURIHandler" })
        {
            string source = Source(name + ".cs");
            Assert.Contains("public interface " + name + "\n", source, StringComparison.Ordinal);
            Assert.Contains("public static unsafe partial class " + name[1..] + "Extensions\n", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheModuleRegistersTheWrappersOfTheModule()
    {
        string source = Source("_Module.cs");
        int entries = source.Split("new Gst.Interop.ModuleTypeEntry(").Length - 1;

        // Every class, mini object and boxed record of the module that GObject
        // knows a type for.
        Assert.Equal(57, entries);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Element.GetGType, &Gst.Element.CreateWrapper),",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "new Gst.Interop.ModuleTypeEntry(&Gst.Buffer.GetGType, &Gst.Buffer.CreateWrapper),",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", 35, 51, 5, 18, 18, 1206, 19, 23)]
    [InlineData("GstBase", 11, 8, 0, 5, 0, 166, 13, 2)]
    [InlineData("GstApp", 2, 2, 0, 8, 0, 61, 23, 8)]
    [InlineData("GstAudio", 14, 17, 1, 2, 2, 184, 15, 0)]
    [InlineData("GstVideo", 12, 42, 5, 0, 9, 293, 2, 2)]
    [InlineData("GstPbutils", 14, 1, 0, 0, 1, 169, 1, 3)]
    [InlineData("GstSdp", 1, 21, 0, 0, 0, 156, 0, 0)]
    [InlineData("GstWebRTC", 9, 4, 0, 1, 2, 37, 0, 6)]
    public void TheEmissionCensusIsStable(
        string module,
        int classes,
        int records,
        int interfaces,
        int callbacks,
        int enumHolders,
        int methods,
        int properties,
        int signals)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(classes, census.EmittedCount(module, "class"));
        Assert.Equal(records, census.EmittedCount(module, "record"));
        Assert.Equal(interfaces, census.EmittedCount(module, "interface"));
        Assert.Equal(callbacks, census.EmittedCount(module, "callback"));
        Assert.Equal(enumHolders, census.EmittedCount(module, "enum holder"));
        Assert.Equal(methods, census.EmittedCount(module, "method"));
        Assert.Equal(properties, census.EmittedCount(module, "property"));
        Assert.Equal(signals, census.EmittedCount(module, "signal"));
    }

    [Theory]
    [InlineData("Gst", 1, 94, 53, 118, 325, 10)]
    [InlineData("GstBase", 0, 11, 0, 20, 180, 0)]
    [InlineData("GstApp", 0, 0, 0, 2, 23, 0)]
    [InlineData("GstAudio", 0, 27, 0, 8, 51, 0)]
    [InlineData("GstVideo", 0, 102, 1, 6, 105, 0)]
    [InlineData("GstPbutils", 0, 1, 0, 0, 22, 0)]
    [InlineData("GstSdp", 0, 10, 0, 0, 12, 0)]
    [InlineData("GstWebRTC", 0, 2, 0, 0, 45, 0)]
    public void TheSkipCensusIsStable(
        string module,
        int shadowed,
        int movedTo,
        int varArgs,
        int notIntrospectable,
        int unsupported,
        int collisions)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(shadowed, census.SkippedCount(module, SkipReason.ShadowedBy));
        Assert.Equal(movedTo, census.SkippedCount(module, SkipReason.MovedTo));
        Assert.Equal(varArgs, census.SkippedCount(module, SkipReason.VarArgs));
        Assert.Equal(notIntrospectable, census.SkippedCount(module, SkipReason.NotIntrospectable));
        Assert.Equal(unsupported, census.SkippedCount(module, SkipReason.UnsupportedSignature));
        Assert.Equal(collisions, census.SkippedCount(module, SkipReason.NameCollision));
        Assert.Equal(0, census.SkippedCount(module, SkipReason.NoCIdentifier));
        Assert.Equal(0, census.SkippedCount(module, SkipReason.FieldSlotCallback));
    }

    /// <summary>
    /// The counts of the rules that this milestone added. They are frozen
    /// separately from the older ones, because each of them stands for a
    /// binding that used to be emitted and corrupt memory.
    /// </summary>
    /// <param name="module">The gir namespace to read.</param>
    /// <param name="overlaySkip">Callables that fixups.json lists.</param>
    /// <param name="callerAllocates">Callables with unusable caller allocated storage.</param>
    /// <param name="lifetime">Callables that release or reference their instance.</param>
    /// <param name="instanceTransfer">Callables that consume their instance and replace it.</param>
    /// <param name="actionSignals">Signals that are a call API rather than a notification.</param>
    [Theory]
    [InlineData("Gst", 10, 3, 21, 20, 0)]
    [InlineData("GstBase", 3, 3, 4, 0, 0)]
    [InlineData("GstApp", 0, 0, 4, 0, 9)]
    [InlineData("GstAudio", 1, 7, 4, 0, 0)]
    [InlineData("GstVideo", 0, 11, 10, 1, 0)]
    [InlineData("GstPbutils", 0, 0, 1, 0, 0)]
    [InlineData("GstSdp", 0, 4, 1, 0, 0)]
    [InlineData("GstWebRTC", 0, 0, 4, 0, 4)]
    public void TheRejectionCensusIsStable(
        string module,
        int overlaySkip,
        int callerAllocates,
        int lifetime,
        int instanceTransfer,
        int actionSignals)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(overlaySkip, census.SkippedCount(module, SkipReason.OverlaySkip));
        Assert.Equal(callerAllocates, census.SkippedCount(module, SkipReason.CallerAllocates));
        Assert.Equal(lifetime, census.SkippedCount(module, SkipReason.LifetimePrimitive));
        Assert.Equal(instanceTransfer, census.SkippedCount(module, SkipReason.InstanceTransferFull));
        Assert.Equal(actionSignals, census.SkippedCount(module, SkipReason.ActionSignal));
    }

    [Theory]
    [InlineData("GstSharp.Net.Base/Generated/BaseSink.cs", "public abstract unsafe partial class BaseSink : Gst.Element")]
    [InlineData("GstSharp.Net.Base/Generated/PushSrc.cs", "public unsafe partial class PushSrc : Gst.Base.BaseSrc")]
    [InlineData("GstSharp.Net.App/Generated/AppSink.cs", "public unsafe partial class AppSink : Gst.Base.BaseSink, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.App/Generated/AppSrc.cs", "public unsafe partial class AppSrc : Gst.Base.BaseSrc, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.Video/Generated/VideoSink.cs", "public unsafe partial class VideoSink : Gst.Base.BaseSink")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioClock.cs", "public unsafe partial class AudioClock : Gst.SystemClock")]
    [InlineData("GstSharp.Net.Pbutils/Generated/AudioVisualizer.cs", "public abstract unsafe partial class AudioVisualizer : Gst.Element")]
    public void AClassDerivesAcrossModuleBoundaries(string path, string declaration)
    {
        Assert.Contains(declaration + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Gst", "GstSharp.Net")]
    [InlineData("GstBase", "GstSharp.Net.Base")]
    [InlineData("GstApp", "GstSharp.Net.App")]
    [InlineData("GstAudio", "GstSharp.Net.Audio")]
    [InlineData("GstVideo", "GstSharp.Net.Video")]
    [InlineData("GstPbutils", "GstSharp.Net.Pbutils")]
    [InlineData("GstSdp", "GstSharp.Net.Sdp")]
    [InlineData("GstWebRTC", "GstSharp.Net.WebRTC")]
    public void EveryModuleEmitsItsOwnTypeTable(string module, string projectDirectory)
    {
        string source = SourceOf(projectDirectory + "/Generated/_Module.cs");

        Assert.Contains(
            "internal static unsafe partial class " + module + "Module\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains("internal static Gst.Interop.ModuleTypeEntry[] CreateEntries() =>", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/StateExtensions.cs", "public static string GetName(Gst.State state)")]
    [InlineData("GstSharp.Net/Generated/EventTypeExtensions.cs", "public static string GetName(Gst.EventType type)")]
    [InlineData("GstSharp.Net.Video/Generated/VideoFormatExtensions.cs", "public static string ToString(Gst.Video.VideoFormat format)")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioFormatExtensions.cs", "public static string ToString(Gst.Audio.AudioFormat format)")]
    [InlineData("GstSharp.Net.Pbutils/Generated/InstallPluginsReturnExtensions.cs", "public static string GetName(Gst.Pbutils.InstallPluginsReturn ret)")]
    public void TheFunctionsOfAnEnumerationLandOnAHolderNamedAfterIt(string path, string signature)
    {
        Assert.Contains(signature + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GstSharp.Net/Generated/Global.cs", "public static unsafe partial class Global")]
    [InlineData("GstSharp.Net.Base/Generated/BaseGlobal.cs", "public static unsafe partial class BaseGlobal")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioGlobal.cs", "public static unsafe partial class AudioGlobal")]
    [InlineData("GstSharp.Net.Video/Generated/VideoGlobal.cs", "public static unsafe partial class VideoGlobal")]
    [InlineData("GstSharp.Net.Pbutils/Generated/PbutilsGlobal.cs", "public static unsafe partial class PbutilsGlobal")]
    [InlineData("GstSharp.Net.Sdp/Generated/SdpGlobal.cs", "public static unsafe partial class SdpGlobal")]
    public void TheGlobalHolderOfAnExtensionModuleCarriesItsModuleName(string path, string declaration)
    {
        // Six types named Global, one per module, read as one type that keeps
        // changing shape once several modules are referenced together. Only the
        // core module keeps the plain name.
        Assert.Contains(declaration + "\n", SourceOf(path), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryInstanceMemberKeepsItsWrapperAliveAcrossTheCall()
    {
        // The call takes the raw handle out of the wrapper and nothing mentions
        // the wrapper afterwards, so without the barrier the finalizer may
        // release the instance while the call is still running.
        int calls = 0;
        int barriers = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            calls += file.Content.Split("(Handle, ").Length - 1;
            calls += file.Content.Split("(Handle)").Length - 1;
            barriers += file.Content.Split("System.GC.KeepAlive(").Length - 1;
        }

        Assert.True(calls > 0);
        Assert.True(
            barriers >= calls,
            $"{calls} call(s) take the raw handle of an instance but only {barriers} barrier(s) are emitted.");
    }

    [Fact]
    public void AnAdoptedWrapperSaysWhoOwnsIt()
    {
        // The gir documents the borrowed pointer the C function returns. The
        // wrapper is not borrowed: it references a mini object and it copies a
        // boxed value, so the caller has to dispose it.
        Assert.Contains(
            """
                /// The wrapper owns a reference of its own, which is a copy for a boxed type:
                /// dispose it when you are done, and note that changes made to a copy of a
                /// boxed value are not written back.
            """,
            Source("Buffer.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSkipReportListsEverySkippedSymbolAndIsDeterministic()
    {
        string report = Generated.SkipReport;

        Assert.StartsWith("<!-- Generated by GstSharp.Generator. Do not edit. -->\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", report, StringComparison.Ordinal);
        Assert.Contains("## Gst\n", report, StringComparison.Ordinal);
        Assert.Contains("### CallerAllocates (3)\n", report, StringComparison.Ordinal);
        Assert.Contains("- `gst_video_frame_map`\n", GenerationPipeline.Run(GirFixture.GirDirectory).SkipReport, StringComparison.Ordinal);
        Assert.Contains("- `GstApp.AppSrc::push-buffer`\n", report, StringComparison.Ordinal);

        Assert.Equal(report, GenerationPipeline.Run(GirFixture.GirDirectory).SkipReport, StringComparer.Ordinal);
    }

    [Fact]
    public void TheCommittedSkipReportIsUpToDate()
    {
        string committed = File.ReadAllText(
            Path.Combine(GirFixture.GirDirectory, GenerationPipeline.SkipReportFileName));

        Assert.Equal(
            Generated.SkipReport,
            committed.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void EveryGeneratedFileHasItsOwnPath()
    {
        HashSet<string> paths = new(StringComparer.Ordinal);

        foreach (GeneratedFile file in Generated.Files)
        {
            Assert.True(paths.Add(file.RelativePath), "Two emitters claim " + file.RelativePath + ".");
        }
    }

    [Fact]
    public void TheOutputIsDeterministic()
    {
        GenerationResult second = GenerationPipeline.Run(GirFixture.GirDirectory);

        Assert.Equal(Generated.Files.Count, second.Files.Count);
        for (int i = 0; i < second.Files.Count; i++)
        {
            Assert.Equal(Generated.Files[i].RelativePath, second.Files[i].RelativePath, StringComparer.Ordinal);
            Assert.Equal(Generated.Files[i].Content, second.Files[i].Content, StringComparer.Ordinal);
        }
    }

    [Fact]
    public void NothingIsEmittedForAFundamentalOrAVirtualMethod()
    {
        // The GType fundamentals of Gst (GstFraction, GstValueList, ...) are
        // hand written, and vfuncs need subclassing support that does not exist
        // yet.
        Assert.False(HasFile("Fraction.cs"));
        Assert.False(HasFile("ValueList.cs"));
        Assert.DoesNotContain("virtual", Source("Element.cs"), StringComparison.Ordinal);
    }

    private static bool HasFile(string fileName) =>
        Generated.Files.Any(file => file.RelativePath.EndsWith("/" + fileName, StringComparison.Ordinal));

    private static string SourceOf(string path)
    {
        foreach (GeneratedFile file in Generated.Files)
        {
            if (string.Equals(file.RelativePath, path, StringComparison.Ordinal))
            {
                return file.Content;
            }
        }

        throw new InvalidOperationException("The run produced no '" + path + "'.");
    }

    private static string Source(string fileName)
    {
        string path = "GstSharp.Net/Generated/" + fileName;
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
