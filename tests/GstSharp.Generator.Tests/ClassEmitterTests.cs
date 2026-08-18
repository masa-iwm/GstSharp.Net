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

    [Theory]
    [InlineData("GstSharp.Net/Generated/Stream.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net/Generated/Stream.cs", "Tags", "public Gst.TagList? GetTags()")]
    [InlineData("GstSharp.Net/Generated/Device.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net/Generated/PadTemplate.cs", "Caps", "public Gst.Caps GetCaps()")]
    [InlineData("GstSharp.Net.App/Generated/AppSrc.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net.App/Generated/AppSink.cs", "Caps", "public Gst.Caps? GetCaps()")]
    [InlineData("GstSharp.Net.Base/Generated/BaseSink.cs", "Stats", "public Gst.Structure GetStats()")]
    [InlineData("GstSharp.Net.GES/Generated/Track.cs", "Caps", "public Gst.Caps? GetCaps()")]
    public void AWrapperValuedPropertyIsAMethodInstead(string path, string propertyName, string getter)
    {
        // Reading one of these builds a mini object or a boxed wrapper that
        // owns a reference and has to be disposed, so a property would leak one
        // per evaluation, and GST0001 does not look at property reads. The
        // getter stays, under a name that says something is produced.
        string source = SourceOf(path);

        Assert.DoesNotContain("    public Gst.Caps? " + propertyName + "\n", source, StringComparison.Ordinal);
        Assert.DoesNotContain(" " + propertyName + " => Get" + propertyName + "();", source, StringComparison.Ordinal);
        Assert.Contains("    " + getter + "\n", source, StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyWhoseValueIsNotAWrapperStays()
    {
        // The rule keys on the wrapper flavour of the getter, not on the fact
        // that a getter exists: a blittable value like GstClockTime and an
        // interned GObject are still read as values.
        Assert.Contains(
            "    public Gst.ClockTime Delay\n",
            Source("Pipeline.cs"),
            StringComparison.Ordinal);
        Assert.Contains(
            "    public Gst.Object? Parent => GetParent();\n",
            Source("Object.cs"),
            StringComparison.Ordinal);
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
    [InlineData("Gst", 35, 51, 5, 17, 18, 1230, 14, 23)]
    [InlineData("GstBase", 11, 4, 0, 5, 0, 166, 11, 2)]
    [InlineData("GstApp", 2, 2, 0, 8, 0, 61, 21, 8)]
    [InlineData("GstAudio", 14, 17, 1, 2, 2, 191, 15, 0)]
    [InlineData("GstVideo", 12, 42, 5, 0, 9, 312, 2, 2)]
    [InlineData("GstPbutils", 14, 1, 0, 0, 1, 169, 0, 3)]
    [InlineData("GstSdp", 1, 21, 0, 0, 0, 156, 0, 0)]
    [InlineData("GstWebRTC", 9, 4, 0, 1, 2, 37, 0, 6)]
    [InlineData("GstNet", 5, 3, 0, 1, 0, 22, 0, 0)]
    [InlineData("GstRtsp", 1, 10, 1, 1, 2, 109, 0, 1)]
    [InlineData("GES", 56, 2, 2, 0, 3, 362, 49, 29)]
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
    [InlineData("Gst", 1, 94, 53, 119, 299, 10)]
    [InlineData("GstBase", 0, 11, 0, 20, 32, 0)]
    [InlineData("GstApp", 0, 0, 0, 2, 23, 0)]
    [InlineData("GstAudio", 0, 27, 0, 8, 43, 0)]
    [InlineData("GstVideo", 0, 102, 1, 6, 85, 0)]
    [InlineData("GstPbutils", 0, 1, 0, 0, 22, 0)]
    [InlineData("GstSdp", 0, 10, 0, 0, 12, 0)]
    [InlineData("GstWebRTC", 0, 2, 0, 0, 45, 0)]
    [InlineData("GstNet", 0, 3, 0, 0, 20, 0)]
    [InlineData("GstRtsp", 0, 17, 0, 0, 20, 0)]
    [InlineData("GES", 0, 3, 4, 10, 82, 0)]
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
    public void AGeneratedClassOpensItsConstructorToBindingModules()
    {
        // The constructor is the one thing about a generated class that is
        // open, and it is deliberate public surface: it is where a module
        // written against the package attaches its wrappers to the generated
        // hierarchy. Everything else stays internal.
        string source = Source("ControlSource.cs");

        Assert.Contains(
            "    protected ControlSource(nint handle, Gst.Interop.Transfer transfer)\n",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "/// This is where a binding module attaches its own wrappers: derive from",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "    internal static new object CreateWrapper(nint handle, Gst.Interop.Transfer transfer)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("    internal static new partial nuint GetGType();\n", source, StringComparison.Ordinal);

        // The private concrete subclass reaches the constructor by nesting, so
        // its own accessibility is unaffected.
        Assert.Contains("    private sealed class Concrete : ControlSource\n", source, StringComparison.Ordinal);
        Assert.Contains(
            "        internal Concrete(nint handle, Gst.Interop.Transfer transfer)\n",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGeneratedClassOpensItsConstructorAndEveryRecordDoesNot()
    {
        // A class is never sealed, so protected is always legal on it. A mini
        // object, a boxed value and an opaque record are sealed and keep the
        // internal constructor that only a generated factory calls.
        int classes = 0;
        int records = 0;

        foreach (GeneratedFile file in Generated.Files)
        {
            string content = file.Content;

            if (content.Contains("\npublic abstract unsafe partial class ", StringComparison.Ordinal)
                || content.Contains("\npublic unsafe partial class ", StringComparison.Ordinal))
            {
                classes++;
                Assert.Contains(
                    "\n    protected ",
                    content,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "\n    internal " + Path.GetFileNameWithoutExtension(file.RelativePath) + "(nint handle",
                    content,
                    StringComparison.Ordinal);
                continue;
            }

            if (!content.Contains("\npublic sealed unsafe partial class ", StringComparison.Ordinal))
            {
                continue;
            }

            records++;
            Assert.DoesNotContain("\n    protected ", content, StringComparison.Ordinal);
        }

        Assert.Equal(151, classes);
        Assert.Equal(104, records);
    }

    [Fact]
    public void AFailedCallReleasesTheResultItStillTransferred()
    {
        // gst_discoverer_discover_uri sets its GError whenever the run saw an
        // error message on the bus and returns the information object all the
        // same. The throw puts that object out of reach, so it is released
        // first; without this the call leaks one GObject per failed discovery.
        Assert.Contains(
            """
                    nint nativeResult = GstDiscovererDiscoverUri(Handle, uriScope.Pointer, &errorNative);
                    System.GC.KeepAlive(this);
                    if (errorNative != 0 && nativeResult != 0)
                    {
                        // The call failed and transferred a value all the same. The throw
                        // below puts it out of reach, so it is released rather than leaked.
                        Gst.Interop.GObjectNative.ObjectUnref(nativeResult);
                    }
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
            """,
            SourceOf("GstSharp.Net.Pbutils/Generated/Discoverer.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseOfAFailedResultFollowsItsKind()
    {
        // A mini object and a boxed value are not interned, so the wrapper is
        // the release: it adopts what the call transferred and hands it back.
        Assert.Contains(
            "Gst.Sdp.MIKEYMessage.FromNative(nativeResult, Gst.Interop.Transfer.Full)?.Dispose();",
            SourceOf("GstSharp.Net.Sdp/Generated/MIKEYMessage.cs"),
            StringComparison.Ordinal);

        // A transferred string is memory of GLib and nothing else.
        Assert.Contains(
            "Gst.Interop.GMarshal.Free(nativeResult);",
            Source("Global.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ABorrowedResultIsNotReleasedOnTheThrowPath()
    {
        // gst_parse_launch returns a floating reference the wrapper sinks, and
        // gst_element_make_from_uri returns transfer none. Releasing either
        // would be releasing something this call was never given.
        Assert.Contains(
            """
                    nint nativeResult = GstElementMakeFromUri((int)type, uriScope.Pointer, elementnameScope.Pointer, &errorNative);
                    Gst.GLib.GException.ThrowIfSet(ref errorNative);
            """,
            Source("Element.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryThrowingCallableThatOwnsItsResultReleasesIt()
    {
        // The guard is emitted for a transferred handle and a transferred
        // string, which is every owned return the bound surface throws with. A
        // new one of a kind the emitter does not cover — an opaque record, a
        // string vector, a list, an array — would leak silently, so the count
        // is frozen here and a change to it has to be looked at.
        int guards = 0;
        foreach (GeneratedFile file in Generated.Files)
        {
            guards += file.Content.Split("// The call failed and transferred a value all the same.").Length - 1;
        }

        Assert.Equal(12, guards);
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
    /// <param name="owningProperties">Properties whose value is a wrapper the reader would have to dispose.</param>
    [Theory]
    [InlineData("Gst", 11, 3, 21, 20, 0, 5)]
    [InlineData("GstBase", 3, 3, 4, 0, 0, 2)]
    [InlineData("GstApp", 0, 0, 4, 0, 9, 2)]
    [InlineData("GstAudio", 2, 7, 4, 0, 0, 0)]
    [InlineData("GstVideo", 1, 11, 10, 1, 0, 0)]
    [InlineData("GstPbutils", 0, 0, 1, 0, 0, 1)]
    [InlineData("GstSdp", 0, 4, 1, 0, 0, 0)]
    [InlineData("GstWebRTC", 0, 0, 4, 0, 4, 0)]
    [InlineData("GstNet", 0, 0, 1, 0, 0, 0)]
    [InlineData("GstRtsp", 1, 2, 3, 0, 0, 0)]
    [InlineData("GES", 1, 0, 1, 0, 0, 2)]
    public void TheRejectionCensusIsStable(
        string module,
        int overlaySkip,
        int callerAllocates,
        int lifetime,
        int instanceTransfer,
        int actionSignals,
        int owningProperties)
    {
        EmissionCensus census = Generated.Census;

        Assert.Equal(overlaySkip, census.SkippedCount(module, SkipReason.OverlaySkip));
        Assert.Equal(callerAllocates, census.SkippedCount(module, SkipReason.CallerAllocates));
        Assert.Equal(lifetime, census.SkippedCount(module, SkipReason.LifetimePrimitive));
        Assert.Equal(instanceTransfer, census.SkippedCount(module, SkipReason.InstanceTransferFull));
        Assert.Equal(actionSignals, census.SkippedCount(module, SkipReason.ActionSignal));
        Assert.Equal(owningProperties, census.SkippedCount(module, SkipReason.OwningProperty));
    }

    [Theory]
    [InlineData("GstSharp.Net.Base/Generated/BaseSink.cs", "public abstract unsafe partial class BaseSink : Gst.Element")]
    [InlineData("GstSharp.Net.Base/Generated/PushSrc.cs", "public unsafe partial class PushSrc : Gst.Base.BaseSrc")]
    [InlineData("GstSharp.Net.App/Generated/AppSink.cs", "public unsafe partial class AppSink : Gst.Base.BaseSink, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.App/Generated/AppSrc.cs", "public unsafe partial class AppSrc : Gst.Base.BaseSrc, Gst.IURIHandler")]
    [InlineData("GstSharp.Net.Video/Generated/VideoSink.cs", "public unsafe partial class VideoSink : Gst.Base.BaseSink")]
    [InlineData("GstSharp.Net.Audio/Generated/AudioClock.cs", "public unsafe partial class AudioClock : Gst.SystemClock")]
    [InlineData("GstSharp.Net.Pbutils/Generated/AudioVisualizer.cs", "public abstract unsafe partial class AudioVisualizer : Gst.Element")]
    [InlineData("GstSharp.Net.GES/Generated/Timeline.cs", "public unsafe partial class Timeline : Gst.Bin, GES.IExtractable, GES.IMetaContainer, Gst.IChildProxy")]
    [InlineData("GstSharp.Net.GES/Generated/Pipeline.cs", "public unsafe partial class Pipeline : Gst.Pipeline, Gst.IChildProxy, Gst.Video.IVideoOverlay")]
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
    [InlineData("GstNet", "GstSharp.Net.Net")]
    [InlineData("GstRtsp", "GstSharp.Net.Rtsp")]
    [InlineData("GES", "GstSharp.Net.GES")]
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
    [InlineData("GstSharp.Net.Net/Generated/NetGlobal.cs", "public static unsafe partial class NetGlobal")]
    [InlineData("GstSharp.Net.Rtsp/Generated/RtspGlobal.cs", "public static unsafe partial class RtspGlobal")]
    [InlineData("GstSharp.Net.GES/Generated/GESGlobal.cs", "public static unsafe partial class GESGlobal")]
    public void TheGlobalHolderOfAnExtensionModuleCarriesItsModuleName(string path, string declaration)
    {
        // Nine types named Global, one per module, read as one type that keeps
        // changing shape once several modules are referenced together. Only the
        // core module keeps the plain name. The editing services are the one
        // module whose C# namespace has no dot in it, and the last segment of
        // it is the whole of it, so the holder is GESGlobal.
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

    [Theory]
    [InlineData(
        "GstSharp.Net.App/Generated/AppSrcSimpleCallbacks.cs",
        "    public void SetNeedData(Gst.App.AppSrcNeedDataCallback needDataCb)\n"
        + "    {\n"
        + "        nint instanceHandle = Handle;\n"
        + "        ArgumentNullException.ThrowIfNull(needDataCb);\n"
        + "        Gst.Interop.CallbackHandle needDataCbState = Gst.Interop.CallbackHandle.Alloc(needDataCb);\n")]
    [InlineData(
        "GstSharp.Net.App/Generated/AppSinkSimpleCallbacks.cs",
        "    public void SetNewSample(Gst.App.AppSinkNewSampleCallback newSampleCb)\n"
        + "    {\n"
        + "        nint instanceHandle = Handle;\n"
        + "        ArgumentNullException.ThrowIfNull(newSampleCb);\n"
        + "        Gst.Interop.CallbackHandle newSampleCbState = Gst.Interop.CallbackHandle.Alloc(newSampleCb);\n")]
    [InlineData(
        "GstSharp.Net/Generated/Pad.cs",
        "        nint instanceHandle = Handle;\n"
        + "        ArgumentNullException.ThrowIfNull(callback);\n"
        + "        Gst.Interop.CallbackHandle callbackState = Gst.Interop.CallbackHandle.Alloc(callback);\n"
        + "        System.Runtime.InteropServices.CULong nativeResult = GstPadAddProbe(instanceHandle, ")]
    public void AMemberThatTakesACallbackReadsItsHandleBeforeItAllocatesTheState(string path, string body)
    {
        // The GCHandle of a callback is freed by the destroy notification of
        // the native call and by nothing else, so a read of Handle that throws
        // ObjectDisposedException after the allocation would pin the delegate
        // and everything its closure captured for the life of the process. The
        // read is hoisted ahead of the allocation for exactly that reason, and
        // the order is what this pins: the two lines are asserted adjacent.
        Assert.Contains(body, SourceOf(path), StringComparison.Ordinal);
    }
}
